using System;
using System.Net;
using System.Net.Http;
using System.Threading;
namespace MusicRpc.Utils;
internal static class HttpClientManager
{
    private static readonly Lazy<HttpClient> SharedClientLazy = new(() =>
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        var client = new HttpClient(handler)
        {
            Timeout = PerformanceConfig.HttpClientTimeout,
        };
        return client;
    }, LazyThreadSafetyMode.ExecutionAndPublication);
    public static HttpClient SharedClient => SharedClientLazy.Value;
}