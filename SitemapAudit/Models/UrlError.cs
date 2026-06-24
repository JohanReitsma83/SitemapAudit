namespace SitemapAudit.Models;

public sealed class UrlError
{
    public string Url { get; set; } = "";
    public int StatusCode { get; set; }
    public string? ReasonPhrase { get; set; }
}
