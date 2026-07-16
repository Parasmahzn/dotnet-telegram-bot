var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

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

builder.Services.AddTransient<MeroShareLoggingHandler>();
builder.Services.AddHttpClient<MeroShareApiClient>((sp, http) =>
{
    http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<MeroShareOptions>>().Value.BaseUrl + "/");
    http.Timeout = TimeSpan.FromSeconds(30);
    http.DefaultRequestHeaders.Accept.Add(new("application/json"));
}).AddHttpMessageHandler<MeroShareLoggingHandler>();
builder.Services.AddHttpClient<IMeroShareDpCatalog, MeroShareDpCatalog>((sp, http) =>
{
    http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<MeroShareOptions>>().Value.BaseUrl + "/");
    http.Timeout = TimeSpan.FromSeconds(30);
    http.DefaultRequestHeaders.Accept.Add(new("application/json"));
}).AddHttpMessageHandler<MeroShareLoggingHandler>();

// Shared infrastructure (singleton — stateless or thread-safe)
builder.Services.AddSingleton<PendingApplyStore>();
builder.Services.AddSingleton<BotUpdateHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CryptoService>();
builder.Services.AddSingleton<AccountStore>();
builder.Services.AddSingleton<AccountResolver>();
builder.Services.AddSingleton<WatchlistStore>();
builder.Services.AddSingleton<UserStore>();
builder.Services.AddSingleton<LoginWizardState>();
builder.Services.AddSingleton<SettingsKittaPromptState>();
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

app.MapGet("/", () => Results.Ok(new
{
    status = "ok",
    service = "MeroShareBot",
    timestamp = DateTimeOffset.UtcNow,
    uptimeSeconds = Environment.TickCount64 / 1000.0,
}));

// Respond 200 immediately; process the update fire-and-forget (Telegram's 5-second webhook timeout)
app.MapPost("/telegram/webhook", (Update update, BotUpdateHandler handler) =>
{
    handler.Dispatch(update);
    return Results.Ok();
});

app.Run();
