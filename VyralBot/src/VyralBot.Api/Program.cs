using VyralBot.Api.Middleware;
using VyralBot.Api.Services;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.WebServer;
using VyralBot.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register data service
var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VyralBot", "vyralbot.db");
var connectionString = $"Data Source={dbPath}";
builder.Services.AddSingleton<IDataService>(sp => 
    new DapperDataService(connectionString, sp.GetRequiredService<ILogger<DapperDataService>>()));

// Register in-memory event bus (required by WebhooksController; ctor takes a logger)
builder.Services.AddSingleton<IEventBus>(sp =>
    new EventBus(sp.GetRequiredService<ILogger<EventBus>>()));

// Register custom services
builder.Services.AddSingleton<WebServerDependencyTracker>();
builder.Services.AddSingleton<WebServerService>();
builder.Services.AddSingleton<NgrokService>();
builder.Services.AddSingleton<KickWebhookSubscriptionService>();
builder.Services.AddSingleton<WebhookCoordinatorService>();

// Control API: TestEventPublisher publishes the canned test events; the status
// service resolves platform/OBS/restream state lazily (absent here → defaults).
builder.Services.AddSingleton<VyralBot.Integrations.Testing.TestEventPublisher>();
builder.Services.AddSingleton<ControlApiStatusService>();
// Song-request queue behind the /songs routes (songplayer.html browser source)
builder.Services.AddSingleton<VyralBot.Core.Services.Songs.ISongRequestService,
    VyralBot.Core.Services.Songs.SongRequestService>();
builder.Services.AddSingleton<VyralBot.Core.Services.Statistics.IStatisticsService,
    VyralBot.Core.Services.Statistics.StatisticsService>();

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

var app = builder.Build();

// Ensure database is created
var dataService = app.Services.GetRequiredService<IDataService>();
await dataService.EnsureDatabaseCreatedAsync();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Add static files and WebSockets
app.UseWebSockets();
app.UseStaticFiles();

// Add webhook validation middleware
app.UseMiddleware<KickWebhookValidationMiddleware>();

// Absolute gate for the control API (must run before MapControllers)
app.UseMiddleware<ControlApiAuthMiddleware>();

// Configure routing
app.UseRouting();
app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    service = "VyralBot.Api"
})
.WithName("GetHealth")
.WithOpenApi();

app.Run();
