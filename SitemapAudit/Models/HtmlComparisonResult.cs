namespace SitemapAudit.Models;

public sealed class HtmlComparisonResult
{
    public string Url { get; set; } = "";
    public string OriginalUrl { get; set; } = "";
    public string ComparisonUrl { get; set; } = "";
    public bool HasDifferences { get; set; }
    public string? DifferenceSummary { get; set; }
    public double? SimilarityPercentage { get; set; }
    public int OriginalStatusCode { get; set; }
    public int ComparisonStatusCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryAttempts { get; set; }
}
