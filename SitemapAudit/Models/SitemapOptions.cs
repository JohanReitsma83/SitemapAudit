namespace SitemapAudit.Models;

public sealed class SitemapOptions
{
    public string BaseUrl { get; set; } = "";
    public string SitemapPath { get; set; } = "/sitemap";
    /// <summary>Max number of concurrent URL checks.</summary>
    public int Concurrency { get; set; } = 8;
    public int RequestTimeoutSeconds { get; set; } = 20;
    public string? UserAgent { get; set; }
    
    // Status check options
    public string? ErrorOutputFile { get; set; }
    
    // HTML Comparison options
    public string? ComparisonBaseUrl { get; set; }
    public string? ComparisonErrorOutputFile { get; set; }
    
    // Retry options
    public int MaxRetries { get; set; } = 3;
    public double MinSimilarityThreshold { get; set; } = 95.0;
    public int RetryDelayMs { get; set; } = 1000;
    
    /// <summary>Gets the full sitemap URL by combining BaseUrl and SitemapPath</summary>
    public string SitemapUrl => $"{BaseUrl.TrimEnd('/')}{SitemapPath}";
}
