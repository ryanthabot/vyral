namespace VyralBot.Integrations.Kick;

public interface IKickService
{
    bool IsConnected { get; }
    string? Channel { get; }
    string? BotUsername { get; }
    
    Task ConnectAsync(string username, string accessToken, string channel, int broadcasterUserId);
    Task ConnectAsync(string username, string accessToken, string? refreshToken, string channel, int broadcasterUserId);
    Task DisconnectAsync();
    Task SendMessageAsync(string message);
}
