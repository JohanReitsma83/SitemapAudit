using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace SitemapAudit.Services;

public sealed class SitemapReader
{
    private static readonly XNamespace Ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SitemapReader> _logger;

    public SitemapReader(IHttpClientFactory httpClientFactory, ILogger<SitemapReader> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<List<string>> GetAllUrlsAsync(string sitemapUrl, CancellationToken ct)
    {
        var urls = new List<string>();

        try
        {
            var xml = await DownloadXmlAsync(sitemapUrl, ct);
            var doc = XDocument.Parse(xml);

            // Two possibilities: <urlset> or <sitemapindex>
            if (doc.Root is null)
                return urls;

            if (doc.Root.Name.LocalName.Equals("urlset", StringComparison.OrdinalIgnoreCase))
            {
                urls.AddRange(ParseUrlSet(doc));
            }
            else if (doc.Root.Name.LocalName.Equals("sitemapindex", StringComparison.OrdinalIgnoreCase))
            {
                var childMaps = ParseSitemapIndex(doc);
                // Load each child sitemap (in parallel but capped to avoid hammering)
                var throttler = new SemaphoreSlim(8);
                var tasks = childMaps.Select(async mapUrl =>
                {
                    await throttler.WaitAsync(ct);
                    try
                    {
                        var childXml = await DownloadXmlAsync(mapUrl, ct);
                        var childDoc = XDocument.Parse(childXml);
                        urls.AddRange(ParseUrlSet(childDoc));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to load child sitemap: {Url}", mapUrl);
                    }
                    finally
                    {
                        throttler.Release();
                    }
                }).ToArray();

                await Task.WhenAll(tasks);
            }
            else
            {
                _logger.LogWarning("Unknown sitemap root element: {Root}", doc.Root.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read sitemap {Url}", sitemapUrl);
        }

        return urls.Distinct().ToList();
    }

    private static List<string> ParseUrlSet(XDocument doc)
        => doc.Descendants(Ns + "url")
              .Select(u => (string?)u.Element(Ns + "loc"))
              .Where(loc => !string.IsNullOrWhiteSpace(loc))
              .Select(loc => loc!.Trim())
              .ToList();

    private static List<string> ParseSitemapIndex(XDocument doc)
        => doc.Descendants(Ns + "sitemap")
              .Select(s => (string?)s.Element(Ns + "loc"))
              .Where(loc => !string.IsNullOrWhiteSpace(loc))
              .Select(loc => loc!.Trim())
              .ToList();

    private async Task<string> DownloadXmlAsync(string url, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("checker");

        try
        {
            using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            // Handle .gz explicitly if needed (some servers misreport content-encoding)
            if (url.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var gzip = new GZipStream(stream, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip);
                return await reader.ReadToEndAsync(ct);
            }

            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading sitemap: {Url}", url);
            throw;
        }
    }
}
