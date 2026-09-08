using System.Security.Cryptography;
using System.Text;

namespace VyralBot.Integrations.Obs.WebSocket;

public static class ObsWebSocketAuth
{
    /// <summary>base64(SHA256(base64(SHA256(password+salt)) + challenge)) — obs-websocket 5 auth (UTF-8).</summary>
    public static string ComputeAuthentication(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }
}
