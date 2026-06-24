using SitemapAudit.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SitemapAudit.Services;

public sealed class HtmlComparisonService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HtmlComparisonService> _logger;
    private readonly SitemapOptions _options;

    public HtmlComparisonService(IHttpClientFactory httpClientFactory, ILogger<HtmlComparisonService> logger, IOptions<SitemapOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<HtmlComparisonResult> CompareUrlsAsync(string originalUrl, string comparisonBaseUrl, CancellationToken cancellationToken)
    {
        var result = new HtmlComparisonResult
        {
            Url = originalUrl,
            OriginalUrl = originalUrl,
            ComparisonUrl = BuildComparisonUrl(originalUrl, comparisonBaseUrl)
        };

        for (int attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            result.RetryAttempts = attempt;
            
            try
            {
                var client = _httpClientFactory.CreateClient("checker");
                
                // Fetch both URLs in parallel
                var originalTask = FetchHtmlContentAsync(client, originalUrl, cancellationToken);
                var comparisonTask = FetchHtmlContentAsync(client, result.ComparisonUrl, cancellationToken);

                var originalResponse = await originalTask;
                var comparisonResponse = await comparisonTask;

                result.OriginalStatusCode = (int)originalResponse.StatusCode;
                result.ComparisonStatusCode = (int)comparisonResponse.StatusCode;

                // Check if we need to retry due to non-200 status codes
                if (!originalResponse.IsSuccessStatusCode || !comparisonResponse.IsSuccessStatusCode)
                {
                    result.ErrorMessage = $"Original: {originalResponse.StatusCode}, Comparison: {comparisonResponse.StatusCode}";
                    
                    if (attempt < _options.MaxRetries)
                    {
                        _logger.LogWarning("Status code error for {Url} on attempt {Attempt}/{MaxRetries}: {Error}. Retrying...", 
                            originalUrl, attempt + 1, _options.MaxRetries + 1, result.ErrorMessage);
                        await Task.Delay(_options.RetryDelayMs, cancellationToken);
                        continue;
                    }
                    else
                    {
                        _logger.LogError("Final status code error for {Url} after {MaxRetries} retries: {Error}", 
                            originalUrl, _options.MaxRetries, result.ErrorMessage);
                        break;
                    }
                }

                // Both requests successful - compare HTML content
                var originalHtml = await originalResponse.Content.ReadAsStringAsync(cancellationToken);
                var comparisonHtml = await comparisonResponse.Content.ReadAsStringAsync(cancellationToken);

                result.HasDifferences = !AreHtmlContentsEqual(originalHtml, comparisonHtml);
                
                if (result.HasDifferences)
                {
                    var (summary, similarity) = GenerateDifferenceSummary(originalHtml, comparisonHtml);
                    result.DifferenceSummary = summary;
                    result.SimilarityPercentage = similarity;

                    // Check if we need to retry due to low similarity
                    if (similarity < _options.MinSimilarityThreshold && attempt < _options.MaxRetries)
                    {
                        _logger.LogWarning("Low similarity ({Similarity:F1}%) for {Url} on attempt {Attempt}/{MaxRetries}. Retrying...", 
                            similarity, originalUrl, attempt + 1, _options.MaxRetries + 1);
                        await Task.Delay(_options.RetryDelayMs, cancellationToken);
                        continue;
                    }
                    else if (similarity < _options.MinSimilarityThreshold)
                    {
                        _logger.LogWarning("Final low similarity ({Similarity:F1}%) for {Url} after {MaxRetries} retries", 
                            similarity, originalUrl, _options.MaxRetries);
                    }
                }

                // Success or acceptable result - exit retry loop
                if (attempt > 0)
                {
                    _logger.LogInformation("Successful comparison for {Url} after {Attempts} retries", originalUrl, attempt);
                }
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error comparing URLs on attempt {Attempt}/{MaxRetries}: {OriginalUrl} vs {ComparisonUrl}", 
                    attempt + 1, _options.MaxRetries + 1, originalUrl, result.ComparisonUrl);
                result.ErrorMessage = ex.Message;
                
                if (attempt < _options.MaxRetries)
                {
                    await Task.Delay(_options.RetryDelayMs, cancellationToken);
                }
            }
        }

        return result;
    }

    private static string BuildComparisonUrl(string originalUrl, string comparisonBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(comparisonBaseUrl))
            return originalUrl;

        try
        {
            var uri = new Uri(originalUrl);
            var pathAndQuery = uri.PathAndQuery;
            
            // Ensure comparison base URL doesn't end with slash
            var baseUrl = comparisonBaseUrl.TrimEnd('/');
            
            return $"{baseUrl}{pathAndQuery}";
        }
        catch
        {
            // If URL parsing fails, return original URL
            return originalUrl;
        }
    }

    private async Task<HttpResponseMessage> FetchHtmlContentAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            return await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching content from {Url}", url);
            // Return a synthetic response to keep the pipeline moving
            return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
            {
                ReasonPhrase = "Fetch failed (exception)"
            };
        }
    }

    private static bool AreHtmlContentsEqual(string html1, string html2)
    {
        // Normalize HTML for comparison by removing whitespace differences
        var normalized1 = NormalizeHtml(html1);
        var normalized2 = NormalizeHtml(html2);
        
        return string.Equals(normalized1, normalized2, StringComparison.Ordinal);
    }

    private static string NormalizeHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // Remove extra whitespace and normalize line endings
        return html
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .Aggregate((a, b) => $"{a}\n{b}");
    }

    private static (string summary, double similarityPercentage) GenerateDifferenceSummary(string html1, string html2)
    {
        var normalized1 = NormalizeHtml(html1);
        var normalized2 = NormalizeHtml(html2);

        var length1 = normalized1.Length;
        var length2 = normalized2.Length;

        // Calculate basic difference metrics
        var lengthDiff = Math.Abs(length1 - length2);
        var maxLength = Math.Max(length1, length2);
        var similarityPercentage = maxLength > 0 ? (1.0 - (double)lengthDiff / maxLength) * 100 : 100;

        var summary = $"Length difference: {lengthDiff} chars";
        return (summary, similarityPercentage);
    }
}
