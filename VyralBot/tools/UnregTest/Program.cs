using VyralBot.Core.Services.EventBus;
using VyralBot.Data;
using VyralBot.Integrations.Discord;

// End-to-end check for DiscordService.UnregisterWorkflowSlashCommandAsync:
// connects the real bot, unregisters the named workflow slash commands from
// every guild, then prints what remains. Run only while VyralBot.UI is stopped
// (a second gateway session with the same token would kick the app's session).

const string dbPath = @"C:\Users\user\OneDrive\Desktop\VyralBot\VyralBot\appdata\profiles\Drip\data\vyralbot.db";

var bootstrap = new DapperDataService($"Data Source={dbPath}", Microsoft.Extensions.Logging.Abstractions.NullLogger<DapperDataService>.Instance);
var token = (await bootstrap.GetDiscordBotsAsync()).First(b => b.Name == "Primary").Token;

var data = new DapperDataService($"Data Source={dbPath}", Microsoft.Extensions.Logging.Abstractions.NullLogger<DapperDataService>.Instance);
var service = new DiscordService(
    Microsoft.Extensions.Logging.Abstractions.NullLogger<DiscordService>.Instance,
    data,
    new EventBus(Microsoft.Extensions.Logging.Abstractions.NullLogger<EventBus>.Instance));

Console.WriteLine("connecting...");
if (!await service.ConnectAsync(token))
{
    Console.WriteLine("FAILED to connect");
    return 1;
}

Console.WriteLine($"connected as {service.BotUsername} (connected={service.IsConnected})");

foreach (var name in new[] { "hello", "panel", "roll", "unreg-test" })
{
    var error = await service.UnregisterWorkflowSlashCommandAsync(name);
    Console.WriteLine($"unregister /{name}: {(error == null ? "OK" : $"ERROR {error}")}");
}

await service.DisconnectAsync();
Console.WriteLine("done");
return 0;
