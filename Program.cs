using Microsoft.AspNetCore.Hosting.Server;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(IsMeroShareApiLog)
        .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)));

static bool IsMeroShareApiLog(LogEvent e) =>
    e.Properties.TryGetValue("SourceContext", out var sc) &&
    sc is ScalarValue { Value: string s } &&
    s == "MeroShareBot.Shared.MeroShare.MeroShareLoggingHandler";

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Default is required (set via appsettings or the ConnectionStrings__Default env var).");

// Fixed ServerVersion, not AutoDetect — AutoDetect connects immediately during DI configuration,
// before the startup retry loop below gets a chance to run if MySQL isn't up yet.
builder.Services.AddDbContextFactory<BotDbContext>(o =>
    o.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36))));

// Config — validated at startup, not at first use
builder.Services.AddOptions<TelegramOptions>()
    .BindConfiguration("Telegram")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<MeroShareOptions>()
    .BindConfiguration("MeroShare")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<SecurityOptions>()
    .BindConfiguration("Security")
    .ValidateDataAnnotations()
    .Validate(o =>
    {
        try
        {
            var crypto = new CryptoService(Options.Create(o));
            return crypto.Decrypt(crypto.Encrypt("startup-check")) == "startup-check";
        }
        catch
        {
            return false;
        }
    }, "Security:DataEncryptionKey is not usable for AES-256-GCM")
    .ValidateOnStart();
builder.Services.AddOptions<SchedulerOptions>()
    .BindConfiguration("Scheduler")
    .Validate(o =>
    {
        try
        {
            Cronos.CronExpression.Parse(o.IpoCron);
            return true;
        }
        catch
        {
            return false;
        }
    }, "Scheduler:IpoCron is not a valid cron expression")
    .ValidateOnStart();

builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
    return new TelegramBotClient(new TelegramBotClientOptions(opts.BotToken, opts.ApiUrl));
});

// Deserialize Telegram Update payloads with Bot API JSON rules (snake_case, unix dates)
builder.Services.ConfigureTelegramBot<Microsoft.AspNetCore.Http.Json.JsonOptions>(opt => opt.SerializerOptions);

builder.Services.AddMemoryCache();
builder.Services.AddTransient<MeroShareLoggingHandler>();

builder.Services.AddHttpClient<MeroShareApiClient>(MeroShareHttpClientExtensions.ConfigureMeroShareClient)
    .AddMeroShareHandlers();
builder.Services.AddHttpClient<IMeroShareDpCatalog, MeroShareDpCatalog>(MeroShareHttpClientExtensions.ConfigureMeroShareClient)
    .AddMeroShareHandlers();
// Named (not typed) client — MeroShareSessionCache must be a true singleton for its per-account
// lock dictionary to work, and AddHttpClient<T> defaults typed clients to transient.
builder.Services.AddHttpClient(MeroShareSessionCache.HttpClientName, MeroShareHttpClientExtensions.ConfigureMeroShareClient)
    .AddMeroShareHandlers();

// Shared infrastructure (singleton — stateless or thread-safe)
builder.Services.AddSingleton<PendingApplyStore>();
builder.Services.AddSingleton<BotUpdateHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CryptoService>();
builder.Services.AddSingleton<AccountStore>();
builder.Services.AddSingleton<IMeroShareSessionStore>(sp => sp.GetRequiredService<AccountStore>());
builder.Services.AddSingleton<IMeroShareSessionCache, MeroShareSessionCache>();
builder.Services.AddSingleton<AccountResolver>();
builder.Services.AddSingleton<WatchlistStore>();
builder.Services.AddSingleton<UserStore>();
builder.Services.AddSingleton<LoginWizardState>();
builder.Services.AddSingleton<SettingsKittaPromptState>();
builder.Services.AddSingleton<PortfolioViewState>();
builder.Services.AddSingleton<BroadcastState>();

// Shared per-request (scoped)
builder.Services.AddScoped<TelegramSender>();
builder.Services.AddScoped<FeatureDispatcher>();

// Feature endpoints — one registration per slice
builder.Services.AddScoped<HelpEndpoint>();
builder.Services.AddScoped<LoginEndpoint>();
builder.Services.AddScoped<AccountsListEndpoint>();
builder.Services.AddScoped<SwitchAccountEndpoint>();
builder.Services.AddScoped<RemoveAccountEndpoint>();
builder.Services.AddScoped<GetOpenIposEndpoint>();
builder.Services.AddScoped<GetOpenIposHandler>();
builder.Services.AddScoped<ApplyIpoEndpoint>();
builder.Services.AddScoped<ApplyIpoHandler>();
builder.Services.AddScoped<GetProfileEndpoint>();
builder.Services.AddScoped<GetProfileHandler>();
builder.Services.AddScoped<GetPortfolioEndpoint>();
builder.Services.AddScoped<GetPortfolioHandler>();
builder.Services.AddScoped<MarketEndpoint>();
builder.Services.AddScoped<WatchlistEndpoint>();
builder.Services.AddScoped<NotifyEndpoint>();
builder.Services.AddScoped<AutoApplyEndpoint>();
builder.Services.AddScoped<AutoApplyCallbackEndpoint>();
builder.Services.AddScoped<AutoApplyScheduler>();
builder.Services.AddScoped<SettingsEndpoint>();
builder.Services.AddScoped<UsersListEndpoint>();
builder.Services.AddScoped<ManageUserEndpoint>();
builder.Services.AddScoped<BroadcastEndpoint>();

// Scheduler
builder.Services.AddScoped<IpoCheckerJob>();
builder.Services.AddHostedService<IpoCheckerService>();

var app = builder.Build();

// Safety net for container/orchestrator startup races where the app starts slightly before
// MySQL accepts connections — retries instead of crashing the whole process on first failure.
{
    var dbFactory = app.Services.GetRequiredService<IDbContextFactory<BotDbContext>>();
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    const int maxAttempts = 10;
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            using var db = dbFactory.CreateDbContext();
            db.Database.Migrate();
            break;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            startupLogger.LogWarning(ex, "Database migration attempt {Attempt}/{MaxAttempts} failed, retrying...", attempt, maxAttempts);
            Thread.Sleep(TimeSpan.FromSeconds(3));
        }
    }
}

app.UseSerilogRequestLogging();

app.MapGet("/", (IServer server) =>
{
    var addresses = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()?.Addresses;
    var url = addresses is { Count: > 0 } ? string.Join(", ", addresses) : "unknown address";
    return Results.Text($"Server is listening on {url}", "text/plain");
});

app.MapGet("/healthcheck", async (
    IDbContextFactory<BotDbContext> dbFactory,
    IOptions<SchedulerOptions> schedulerOpts,
    TimeProvider timeProvider,
    IWebHostEnvironment env) =>
{
    bool dbOk;
    try
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        dbOk = await db.Database.CanConnectAsync();
    }
    catch
    {
        dbOk = false;
    }

    var cron = Cronos.CronExpression.Parse(schedulerOpts.Value.IpoCron);
    var nextIpoCheckUtc = cron.GetNextOccurrence(timeProvider.GetUtcNow().UtcDateTime, inclusive: false);

    static string Pill(string text, string bg) =>
        $"""<span style="display:inline-block;padding:2px 10px;border-radius:9999px;background:{bg};color:#fff;font-size:12px;font-weight:600;">{text}</span>""";

    var statusPill = dbOk ? Pill("ok", "#22c55e") : Pill("degraded", "#ef4444");
    var envPill = env.IsDevelopment() ? Pill(env.EnvironmentName, "#22c55e")
        : env.IsProduction() ? Pill(env.EnvironmentName, "#ef4444")
        : Pill(env.EnvironmentName, "#6b7280");
    var dbDot = $"""<span style="display:inline-block;width:9px;height:9px;border-radius:50%;background:{(dbOk ? "#22c55e" : "#ef4444")};margin-right:6px;"></span>{(dbOk ? "Live" : "Offline")}""";

    var html = $"""
        <!DOCTYPE html>
        <html>
        <head><title>MeroShare Bot Health</title></head>
        <body style="display:flex;justify-content:center;align-items:center;min-height:100vh;margin:0;font-family:sans-serif;">
        <div>
            <h1>MeroShareBot</h1>
            <p>Status: {statusPill}</p>
            <p>Environment: {envPill}</p>
            <p>Version: {typeof(Program).Assembly.GetName().Version}</p>
            <p>Timestamp: {DateTimeOffset.UtcNow:u}</p>
            <p>Uptime (seconds): {Environment.TickCount64 / 1000.0}</p>
            <p>Database: {dbDot}</p>
            <p>Next IPO check (UTC): {nextIpoCheckUtc:u}</p>
        </div>
        </body>
        </html>
        """;

    return Results.Content(html, "text/html", statusCode: dbOk ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

// Respond 200 immediately; process the update fire-and-forget (Telegram's 5-second webhook timeout)
app.MapPost("/telegram/webhook", (Update update, BotUpdateHandler handler) =>
{
    handler.Dispatch(update);
    return Results.Ok();
});

await app.RunAsync();