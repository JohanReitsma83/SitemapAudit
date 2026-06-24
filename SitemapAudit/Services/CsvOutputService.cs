using SitemapAudit.Models;
using Microsoft.Extensions.Logging;

namespace SitemapAudit.Services;

public sealed class CsvOutputService
{
    private readonly ILogger<CsvOutputService> _logger;

    public CsvOutputService(ILogger<CsvOutputService> logger)
    {
        _logger = logger;
    }

    public async Task WriteUrlErrorsToCsvAsync(List<UrlError> errors, string filePath, CancellationToken cancellationToken)
    {
        if (errors.Count == 0)
        {
            _logger.LogInformation("No errors to write to CSV file.");
            return;
        }

        // Sort errors by URL
        var sortedErrors = errors.OrderBy(e => e.Url).ToList();
        
        var csvLines = new List<string>
        {
            // CSV Header
            "Url,StatusCode,ReasonPhrase,GeneratedAt"
        };

        var generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        foreach (var error in sortedErrors)
        {
            // Escape CSV values that contain commas, quotes, or newlines
            var url = EscapeCsvValue(error.Url);
            var reasonPhrase = EscapeCsvValue(error.ReasonPhrase ?? "N/A");
            
            csvLines.Add($"{url},{error.StatusCode},{reasonPhrase},{generatedAt}");
        }

        await File.WriteAllLinesAsync(filePath, csvLines, cancellationToken);
        _logger.LogInformation("Wrote {ErrorCount} URL errors to CSV file: {FilePath}", errors.Count, filePath);
    }

    public async Task WriteHtmlComparisonResultsToCsvAsync(List<HtmlComparisonResult> comparisons, string filePath, CancellationToken cancellationToken)
    {
        if (comparisons.Count == 0)
        {
            _logger.LogInformation("No HTML comparison results to write to CSV file.");
            return;
        }

        // Sort comparisons by URL
        var sortedComparisons = comparisons.OrderBy(c => c.Url).ToList();
        
        var csvLines = new List<string>
        {
            // CSV Header
            "Url,OriginalUrl,ComparisonUrl,HasDifferences,OriginalStatusCode,ComparisonStatusCode,DifferenceSummary,SimilarityPercentage,ErrorMessage,GeneratedAt"
        };

        var generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        foreach (var comparison in sortedComparisons)
        {
            // Escape CSV values that contain commas, quotes, or newlines
            var url = EscapeCsvValue(comparison.Url);
            var originalUrl = EscapeCsvValue(comparison.OriginalUrl);
            var comparisonUrl = EscapeCsvValue(comparison.ComparisonUrl);
            var differenceSummary = EscapeCsvValue(comparison.DifferenceSummary ?? "N/A");
            var similarityPercentage = comparison.SimilarityPercentage?.ToString("F1") ?? "N/A";
            var errorMessage = EscapeCsvValue(comparison.ErrorMessage ?? "N/A");
            
            csvLines.Add($"{url},{originalUrl},{comparisonUrl},{comparison.HasDifferences},{comparison.OriginalStatusCode},{comparison.ComparisonStatusCode},{differenceSummary},{similarityPercentage},{errorMessage},{generatedAt}");
        }

        await File.WriteAllLinesAsync(filePath, csvLines, cancellationToken);
        _logger.LogInformation("Wrote {ComparisonCount} HTML comparison results to CSV file: {FilePath}", comparisons.Count, filePath);
    }

    private static string EscapeCsvValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // If the value contains comma, quote, or newline, wrap it in quotes and escape internal quotes
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}
