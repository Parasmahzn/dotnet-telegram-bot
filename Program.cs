using MeroShareBot.Features.Help;
using MeroShareBot.Features.Ipo.ApplyIpo;
using MeroShareBot.Features.Ipo.GetOpenIpos;
using MeroShareBot.Features.Profile;
using MeroShareBot.Features.Scheduler;
using MeroShareBot.Shared.Browser;
using MeroShareBot.Shared.Config;
using MeroShareBot.Shared.MeroShare;
using MeroShareBot.Shared.Telegram;
using Microsoft.Extensions.Options;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

// Config — validated at startup, not at first use
builder.Services.AddOptions<TelegramOptions>()
    .BindConfiguration("Telegram")
    .ValidateDataAnnotations()
    .Validate(o => o.WebhookUrl.Length == 0 || Uri.IsWellFormedUriString(o.WebhookUrl, UriKind.Absolute),
        "Telegram:WebhookUrl must be a valid absolute URL when set")
    .ValidateOnStart();
builder.Services.AddOptions<MeroShareOptions>()
    .BindConfiguration("MeroShare")
    .ValidateDataAnnotations()
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
    new TelegramBotClient(sp.GetRequiredService<IOptions<TelegramOptions>>().Value.BotToken));

// Deserialize Telegram Update payloads with Bot API JSON rules (snake_case, unix dates)
builder.Services.ConfigureTelegramBot<Microsoft.AspNetCore.Http.Json.JsonOptions>(opt => opt.SerializerOptions);

// Shared infrastructure (singleton — stateless or thread-safe)
builder.Services.AddSingleton<BrowserFactory>();
builder.Services.AddSingleton<PendingApplyStore>();
builder.Services.AddSingleton<BotUpdateHandler>();
builder.Services.AddSingleton(TimeProvider.System);

// Shared per-request (scoped)
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<TelegramSender>();
builder.Services.AddScoped<FeatureDispatcher>();

// Feature endpoints — one registration per slice
builder.Services.AddScoped<HelpEndpoint>();
builder.Services.AddScoped<GetOpenIposEndpoint>();
builder.Services.AddScoped<GetOpenIposHandler>();
builder.Services.AddScoped<ApplyIpoEndpoint>();
builder.Services.AddScoped<ApplyIpoHandler>();
builder.Services.AddScoped<GetProfileEndpoint>();
builder.Services.AddScoped<GetProfileHandler>();

// Scheduler + optional webhook registration
builder.Services.AddScoped<IpoCheckerJob>();
builder.Services.AddHostedService<IpoCheckerService>();
builder.Services.AddHostedService<WebhookRegistrationService>();

var app = builder.Build();

app.UseSerilogRequestLogging();

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    timestamp = DateTimeOffset.UtcNow,
    uptimeSeconds = Environment.TickCount64 / 1000.0,
}));

// Respond 200 immediately; process the update fire-and-forget (mirrors the Node webhook)
app.MapPost("/telegram/webhook", (Update update, BotUpdateHandler handler) =>
{
    handler.Dispatch(update);
    return Results.Ok();
});

app.Run();
