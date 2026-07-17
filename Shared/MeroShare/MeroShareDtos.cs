using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeroShareBot.Shared.MeroShare;

// ---- Capital / DP list ----
public sealed record DepositoryParticipant(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name);

// ---- Auth ----
public sealed record LoginRequest(
    [property: JsonPropertyName("clientId")] int ClientId,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password);

public sealed record LoginResponseBody(
    [property: JsonPropertyName("statusCode")] int? StatusCode,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("accountExpired")] bool? AccountExpired,
    [property: JsonPropertyName("dematExpired")] bool? DematExpired,
    [property: JsonPropertyName("passwordExpired")] bool? PasswordExpired);

// ---- Own detail / profile ----
public sealed record OwnDetail(
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("boid")] string Boid,
    [property: JsonPropertyName("clientCode")] string? ClientCode,
    [property: JsonPropertyName("contact")] string? Contact,
    [property: JsonPropertyName("demat")] string Demat,
    [property: JsonPropertyName("dematExpiryDate")] string? DematExpiryDate,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("expiredDateStr")] string? ExpiredDateStr,
    [property: JsonPropertyName("gender")] string? Gender,
    [property: JsonPropertyName("meroShareEmail")] string? MeroShareEmail,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("passwordExpiryDateStr")] string? PasswordExpiryDateStr,
    [property: JsonPropertyName("username")] string? Username);

// ---- Portfolio ----
public sealed record PortfolioRequest(
    [property: JsonPropertyName("sortBy")] string SortBy,
    [property: JsonPropertyName("demat")] string[] Demat,
    [property: JsonPropertyName("clientCode")] string? ClientCode,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("size")] int Size,
    [property: JsonPropertyName("sortAsc")] bool SortAsc);

public sealed record PortfolioItem(
    [property: JsonPropertyName("script")] string Script,
    [property: JsonPropertyName("scriptDesc")] string? ScriptDesc,
    [property: JsonPropertyName("currentBalance")] int CurrentBalance,
    [property: JsonPropertyName("lastTransactionPrice")] string? LastTransactionPrice,
    [property: JsonPropertyName("previousClosingPrice")] string? PreviousClosingPrice,
    [property: JsonPropertyName("valueOfLastTransPrice")] decimal ValueOfLastTransPrice,
    [property: JsonPropertyName("valueOfPrevClosingPrice")] decimal ValueOfPrevClosingPrice);

public sealed record PortfolioResponse(
    [property: JsonPropertyName("meroShareMyPortfolio")] List<PortfolioItem> Items,
    [property: JsonPropertyName("totalItems")] int TotalItems,
    [property: JsonPropertyName("totalValueOfLastTransPrice")] decimal TotalValueOfLastTransPrice,
    [property: JsonPropertyName("totalValueOfPrevClosingPrice")] decimal TotalValueOfPrevClosingPrice);

// ---- Generic "filtered search" request shape (reused by applicableIssue + applicantForm search) ----
public sealed record FilterFieldParam(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("alias")] string Alias,
    [property: JsonPropertyName("value")] string Value = "");

public sealed record FilterDateParam(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("condition")] string Condition,
    [property: JsonPropertyName("alias")] string Alias,
    [property: JsonPropertyName("value")] string Value);

public sealed record FilteredSearchRequest(
    [property: JsonPropertyName("filterFieldParams")] List<FilterFieldParam> FilterFieldParams,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("size")] int Size,
    [property: JsonPropertyName("searchRoleViewConstants")] string SearchRoleViewConstants,
    [property: JsonPropertyName("filterDateParams")] List<FilterDateParam> FilterDateParams);

// ---- Applicable issue (open IPO list) ----
public sealed record ApplicableIssue(
    [property: JsonPropertyName("companyShareId")] int CompanyShareId,
    [property: JsonPropertyName("scrip")] string Scrip,
    [property: JsonPropertyName("companyName")] string CompanyName,
    [property: JsonPropertyName("shareTypeName")] string ShareTypeName,
    [property: JsonPropertyName("shareGroupName")] string ShareGroupName,
    [property: JsonPropertyName("subGroup")] string SubGroup);

public sealed record ApplicableIssueResponse(
    [property: JsonPropertyName("object")] List<ApplicableIssue> Object,
    [property: JsonPropertyName("totalCount")] int TotalCount);

// ---- Issue detail ----
public sealed record IssueDetail(
    [property: JsonPropertyName("companyName")] string CompanyName,
    [property: JsonPropertyName("companyShareId")] int CompanyShareId,
    [property: JsonPropertyName("minUnit")] int MinUnit,
    [property: JsonPropertyName("maxUnit")] int MaxUnit,
    [property: JsonPropertyName("scrip")] string Scrip,
    [property: JsonPropertyName("shareTypeName")] string? ShareTypeName,
    [property: JsonPropertyName("shareGroupName")] string? ShareGroupName);

// ---- Application history / already-applied search ----
// Only CompanyShareId is a named property; everything else round-trips via extension data so a
// future application-status feature gets the full raw shape without another DTO revision.
public sealed record ApplicantFormEntry(
    [property: JsonPropertyName("companyShareId")] int CompanyShareId)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

public sealed record ApplicantFormSearchResponse(
    [property: JsonPropertyName("object")] List<ApplicantFormEntry> Object);

// ---- Bank / bank account ----
public sealed record Bank(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name);

public sealed record BankAccount(
    [property: JsonPropertyName("accountBranchId")] int AccountBranchId,
    [property: JsonPropertyName("accountNumber")] string AccountNumber,
    [property: JsonPropertyName("accountTypeId")] int AccountTypeId,
    [property: JsonPropertyName("accountTypeName")] string? AccountTypeName,
    [property: JsonPropertyName("branchName")] string? BranchName,
    [property: JsonPropertyName("id")] int Id); // this `id` becomes `customerId` in ApplyRequest

// ---- Apply ----
// NOTE the deliberate string/number type mix — preserved exactly per the MeroShare backend contract.
public sealed record ApplyRequest(
    [property: JsonPropertyName("demat")] string Demat,
    [property: JsonPropertyName("boid")] string Boid,
    [property: JsonPropertyName("accountNumber")] string AccountNumber,
    [property: JsonPropertyName("customerId")] int CustomerId,
    [property: JsonPropertyName("accountBranchId")] int AccountBranchId,
    [property: JsonPropertyName("accountTypeId")] int AccountTypeId,
    [property: JsonPropertyName("appliedKitta")] string AppliedKitta,
    [property: JsonPropertyName("crnNumber")] string CrnNumber,
    [property: JsonPropertyName("transactionPIN")] string TransactionPin,
    [property: JsonPropertyName("companyShareId")] string CompanyShareId,
    [property: JsonPropertyName("bankId")] string BankId);

public sealed record ApplyResponseBody(
    [property: JsonPropertyName("statusCode")] int? StatusCode,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("status")] string? Status);
