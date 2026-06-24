using Microsoft.Extensions.Logging;

namespace SitemapAudit.Services;

public sealed class UrlChecker(IHttpClientFactory httpClientFactory, ILogger<UrlChecker> logger)
{
    public async Task<HttpResponseMessage> CheckAsync(string url, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("checker");

        // Prefer HEAD to reduce payload; fall back to GET if not allowed.
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, url);
            var headResp = await client.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct);
            if (headResp.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed ||
                headResp.StatusCode == System.Net.HttpStatusCode.NotFound && headResp.RequestMessage?.Method == HttpMethod.Head)
            {
                // Try GET when HEAD isn't supported or site behaves oddly on HEAD
                headResp.Dispose();
                using var get = new HttpRequestMessage(HttpMethod.Get, url);
                return await client.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            return headResp;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking {Url}", url);
            // Return a synthetic response to keep the pipeline moving
            return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
            {
                ReasonPhrase = "Check failed (exception)"
            };
        }
    }
}
