using System.Net.Http.Json;
using System.Text.Json;

namespace MeroShareBot.Shared.MeroShare;

// Stateless — no mutable session field. Every authenticated method takes an explicit
// MeroShareSession so this single registered instance is safe under concurrent chats/accounts.
public sealed class MeroShareApiClient(HttpClient http, IMeroShareDpCatalog dpCatalog)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<DepositoryParticipant>> GetDpListAsync(CancellationToken ct = default) =>
        dpCatalog.GetDpListAsync(ct);

    public async Task<MeroShareSession> LoginAsync(MeroShareCredentials creds, CancellationToken ct = default)
    {
        var clientId = await dpCatalog.ResolveClientIdAsync(creds.Dp, ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, "meroShare/auth/")
        {
            Content = JsonContent.Create(new LoginRequest(clientId, creds.Username, creds.Password), options: Json),
        };
        using var response = await http.SendAsync(request, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var parsed = TryDeserialize<LoginResponseBody>(body);
            throw new MeroShareApiException((int)response.StatusCode, body, parsed?.Message);
        }

        if (!response.Headers.TryGetValues("Authorization", out var values))
            throw new MeroShareLoginException("Login succeeded but no Authorization header was returned.");

        var token = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
            throw new MeroShareLoginException("Login succeeded but the Authorization header was empty.");

        return new MeroShareSession(token, clientId, creds.Username);
    }

    public Task LogoutAsync(MeroShareSession session, CancellationToken ct = default) =>
        SendBestEffortAsync(HttpMethod.Get, "meroShare/logout/", session, ct);

    public Task<OwnDetail> GetOwnDetailAsync(MeroShareSession session, CancellationToken ct = default) =>
        SendAsync<OwnDetail>(HttpMethod.Get, "meroShare/ownDetail/", session, ct: ct);

    public Task<PortfolioResponse> GetPortfolioAsync(
        MeroShareSession session, string demat, string? clientCode, CancellationToken ct = default) =>
        SendAsync<PortfolioResponse>(HttpMethod.Post, "meroShareView/myPortfolio/", session,
            new PortfolioRequest("script", [demat], clientCode, 1, 200, true), ct);

    public Task<ApplicableIssueResponse> GetApplicableIssuesAsync(
        MeroShareSession session, CancellationToken ct = default) =>
        SendAsync<ApplicableIssueResponse>(HttpMethod.Post, "meroShare/companyShare/applicableIssue/", session,
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
        MeroShareSession session, int companyShareId, CancellationToken ct = default) =>
        SendAsync<IssueDetail>(HttpMethod.Get, $"meroShare/active/{companyShareId}", session, ct: ct);

    public Task<ApplicantFormSearchResponse> SearchApplicationsAsync(
        MeroShareSession session, CancellationToken ct = default) =>
        SendAsync<ApplicantFormSearchResponse>(HttpMethod.Post, "meroShare/applicantForm/active/search/", session,
            new FilteredSearchRequest(
                [], Page: 1, Size: 200, SearchRoleViewConstants: "VIEW_APPLICANT_FORM_COMPLETE",
                FilterDateParams: [new("appliedDate", "", "", "")]),
            ct);

    public Task<List<Bank>> GetBanksAsync(MeroShareSession session, CancellationToken ct = default) =>
        SendAsync<List<Bank>>(HttpMethod.Get, "meroShare/bank/", session, ct: ct);

    public Task<List<BankAccount>> GetBankAccountsAsync(
        MeroShareSession session, int bankId, CancellationToken ct = default) =>
        SendAsync<List<BankAccount>>(HttpMethod.Get, $"meroShare/bank/{bankId}", session, ct: ct);

    public async Task<ApplyResponseBody> ApplyAsync(
        MeroShareSession session, ApplyRequest request, CancellationToken ct = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "meroShare/applicantForm/share/apply")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        httpRequest.Headers.TryAddWithoutValidation("Authorization", session.Token);
        using var response = await http.SendAsync(httpRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var parsed = TryDeserialize<ApplyResponseBody>(body);
            throw new MeroShareApiException((int)response.StatusCode, body, parsed?.Message);
        }

        return TryDeserialize<ApplyResponseBody>(body) ?? new ApplyResponseBody(null, null, null);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method, string path, MeroShareSession session, object? body = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        request.Headers.TryAddWithoutValidation("Authorization", session.Token);

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new MeroShareApiException((int)response.StatusCode, text);

        return JsonSerializer.Deserialize<T>(text, Json)
            ?? throw new MeroShareApiException((int)response.StatusCode, text, "Empty response body");
    }

    private async Task SendBestEffortAsync(HttpMethod method, string path, MeroShareSession session, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.TryAddWithoutValidation("Authorization", session.Token);
            using var _ = await http.SendAsync(request, ct);
        }
        catch
        {
            // logout is best-effort
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
