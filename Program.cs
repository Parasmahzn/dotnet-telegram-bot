var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

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

// Scheduler
builder.Services.AddScoped<IpoCheckerJob>();
builder.Services.AddHostedService<IpoCheckerService>();

var app = builder.Build();

app.UseSerilogRequestLogging();

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
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
