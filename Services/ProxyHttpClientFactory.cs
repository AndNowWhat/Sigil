using System.Net;
using System.Net.Http;
using Sigil.Models;

namespace Sigil.Services;

public static class ProxyHttpClientFactory
{
    public static HttpClient Create(ProxyConfig? proxy, bool useCookies = true)
    {
        var handler = new HttpClientHandler { UseCookies = useCookies };

        if (proxy is { Enabled: true } && !string.IsNullOrWhiteSpace(proxy.Host))
        {
            handler.Proxy = new WebProxy(proxy.ToUri());
            if (!string.IsNullOrWhiteSpace(proxy.Username))
                handler.Proxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password);
            handler.UseProxy = true;
        }

        return new HttpClient(handler);
    }
}
