namespace Sigil.Models;

public sealed class ProxyConfig
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1080;
    public ProxyType Type { get; set; } = ProxyType.Socks5;
    public string? Username { get; set; }
    public string? Password { get; set; }

    public string ToUri() => Type switch
    {
        ProxyType.Http => $"http://{Host}:{Port}",
        ProxyType.Socks5 => $"socks5://{Host}:{Port}",
        _ => $"socks5://{Host}:{Port}"
    };

    public string ToDisplayString() =>
        Enabled && !string.IsNullOrWhiteSpace(Host)
            ? $"{Type} {Host}:{Port}"
            : "None";
}

public enum ProxyType { Http, Socks5 }
