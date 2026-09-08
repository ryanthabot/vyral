namespace VyralBot.Integrations.Lights;

public sealed record LightInfo(string Id, string Name, bool On, bool Reachable, string? ProductName);
