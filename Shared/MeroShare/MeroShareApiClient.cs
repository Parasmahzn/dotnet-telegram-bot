using System.Net.Http.Json;
using System.Text.Json;

namespace MeroShareBot.Shared.MeroShare;

// Stateless — no mutable session field. Every authenticated method takes MeroShareCredentials and
// resolves a (cached, reused) session via IMeroShareSessionCache internally, retrying once on a 401
// after invalidating the cached token — so this single registered instance is safe under concurrent
// chats/accounts, and callers never manage login/logout themselves.
public sealed class MeroShareApiClient(
    HttpClient http, IMeroShareDpCatalog dpCatalog, IMeroShareSessionCache sessions, ILogger<MeroShareApiClient> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<DepositoryParticipant>> GetDpListAsync(CancellationToken ct = default) =>
        dpCatalog.GetDpListAsync(ct);

    public Task<OwnDetail> GetOwnDetailAsync(MeroShareCredentials creds, CancellationToken ct = default) =>
        SendAsync<OwnDetail>(HttpMethod.Get, "meroShare/ownDetail/", creds, ct: ct);

    public Task<PortfolioResponse> GetPortfolioAsync(
        MeroShareCredentials creds, string demat, string? clientCode, CancellationToken ct = default) =>
        SendAsync<PortfolioResponse>(HttpMethod.Post, "meroShareView/myPortfolio/", creds,
            new PortfolioRequest("script", [demat], clientCode, 1, 200, true), ct);

    public Task<ApplicableIssueResponse> GetApplicableIssuesAsync(
        MeroShareCredentials creds, CancellationToken ct = default) =>
        SendAsync<ApplicableIssueResponse>(HttpMethod.Post, "meroShare/companyShare/applicableIssue/", creds,
            new FilteredSearchRequest(
                [
                    new("companyIssue.companyISIN.script", "Scrip"),
                    new("companyIssue.companyISIN.company.name", "Company Name"),
                    new("companyIssue.assignedToClient.name", "Issue Manager"),
                ],
                Page: 1, Size: 200, SearchRoleViewConstants: "VIEW_APPLICABLE_SHARE",
                FilterDateParams:
                [
                    new("minIssueOpenDate", "", "", ""),
                    new("maxIssueCloseDate", "", "", ""),
                ]),
            ct);

    public Task<IssueDetail> GetIssueDetailAsync(
        MeroShareCredentials creds, int companyShareId, CancellationToken ct = default) =>
        SendAsync<IssueDetail>(HttpMethod.Get, $"meroShare/active/{companyShareId}", creds, ct: ct);

    public Task<ApplicantFormSearchResponse> SearchApplicationsAsync(
        MeroShareCredentials creds, CancellationToken ct = default) =>
        SendAsync<ApplicantFormSearchResponse>(HttpMethod.Post, "meroShare/applicantForm/active/search/", creds,
            new FilteredSearchRequest(
                [], Page: 1, Size: 200, SearchRoleViewConstants: "VIEW_APPLICANT_FORM_COMPLETE",
                FilterDateParams: [new("appliedDate", "", "", "")]),
            ct);

    public Task<List<Bank>> GetBanksAsync(MeroShareCredentials creds, CancellationToken ct = default) =>
        SendAsync<List<Bank>>(HttpMethod.Get, "meroShare/bank/", creds, ct: ct);

    public Task<List<BankAccount>> GetBankAccountsAsync(
        MeroShareCredentials creds, int bankId, CancellationToken ct = default) =>
        SendAsync<List<BankAccount>>(HttpMethod.Get, $"meroShare/bank/{bankId}", creds, ct: ct);

    // Submits the share application — a non-idempotent, real-money-adjacent call. A 401 almost
    // always means the auth check rejected the request before any business logic ran, so retrying
    // once after a fresh login is accepted as safe here rather than adding special-case complexity.
    public async Task<ApplyResponseBody> ApplyAsync(
        MeroShareCredentials creds, ApplyRequest request, CancellationToken ct = default)
    {
        var session = await sessions.GetSessionAsync(creds, ct);
        try
        {
            return await ApplyWithTokenAsync(session.Token, request, ct);
        }
        catch (MeroShareApiException ex) when (ex.StatusCode == 401)
        {
            logger.LogInformation("Session for {Username} rejected (401) on apply — retrying after fresh login", creds.Username);
            await sessions.InvalidateAsync(creds, ct);
            session = await sessions.GetSessionAsync(creds, ct);
            return await ApplyWithTokenAsync(session.Token, request, ct);
        }
    }

    private async Task<ApplyResponseBody> ApplyWithTokenAsync(string token, ApplyRequest request, CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "meroShare/applicantForm/share/apply")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        httpRequest.Headers.TryAddWithoutValidation("Authorization", token);
        using var response = await http.SendAsync(httpRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var parsed = TryDeserialize<ApplyResponseBody>(body);
            throw new MeroShareApiException((int)response.StatusCode, body, parsed?.Message);
        }

        if (body.TrimStart().StartsWith('<'))
            throw new MeroShareUnavailableException();

        return TryDeserialize<ApplyResponseBody>(body) ?? new ApplyResponseBody(null, null, null);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method, string path, MeroShareCredentials creds, object? body = null, CancellationToken ct = default)
    {
        var session = await sessions.GetSessionAsync(creds, ct);
        try
        {
            return await SendWithTokenAsync<T>(method, path, session.Token, body, ct);
        }
        catch (MeroShareApiException ex) when (ex.StatusCode == 401)
        {
            logger.LogInformation("Session for {Username} rejected (401) — retrying after fresh login", creds.Username);
            await sessions.InvalidateAsync(creds, ct);
            session = await sessions.GetSessionAsync(creds, ct);
            return await SendWithTokenAsync<T>(method, path, session.Token, body, ct);
        }
    }

    private async Task<T> SendWithTokenAsync<T>(
        HttpMethod method, string path, string token, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        request.Headers.TryAddWithoutValidation("Authorization", token);

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new MeroShareApiException((int)response.StatusCode, text);

        try
        {
            return JsonSerializer.Deserialize<T>(text, Json)
                ?? throw new MeroShareApiException((int)response.StatusCode, text, "Empty response body");
        }
        catch (JsonException)
        {
            throw new MeroShareUnavailableException();
        }
    }

    private static T? TryDeserialize<T>(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, Json);
        }
        catch
        {
            return default;
        }
    }
}
