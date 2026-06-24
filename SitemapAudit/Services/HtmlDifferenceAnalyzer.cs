using Microsoft.Extensions.Logging;

namespace SitemapAudit.Services;

public sealed class HtmlDifferenceAnalyzer(ILogger<HtmlDifferenceAnalyzer> logger)
{
    public DetailedHtmlComparisonResult AnalyzeDifferences(string originalUrl, string comparisonUrl, string originalHtml, string comparisonHtml)
    {
        var result = new DetailedHtmlComparisonResult
        {
            OriginalUrl = originalUrl,
            ComparisonUrl = comparisonUrl,
            HasDifferences = !AreHtmlContentsEqual(originalHtml, comparisonHtml)
        };

        if (result.HasDifferences)
        {
            var analysis = PerformDetailedAnalysis(originalHtml, comparisonHtml);
            result.SimilarityPercentage = analysis.SimilarityPercentage;
            result.LengthDifference = analysis.LengthDifference;
            result.OriginalLength = analysis.OriginalLength;
            result.ComparisonLength = analysis.ComparisonLength;
            result.DifferenceDetails = analysis.DifferenceDetails;
            result.Summary = analysis.Summary;
        }
        else
        {
            result.SimilarityPercentage = 100.0;
            result.LengthDifference = 0;
            result.OriginalLength = originalHtml.Length;
            result.ComparisonLength = comparisonHtml.Length;
            result.Summary = "No differences found - HTML content is identical";
        }

        return result;
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

    private DetailedAnalysisResult PerformDetailedAnalysis(string html1, string html2)
    {
        var normalized1 = NormalizeHtml(html1);
        var normalized2 = NormalizeHtml(html2);

        var length1 = normalized1.Length;
        var length2 = normalized2.Length;
        var lengthDiff = Math.Abs(length1 - length2);
        var maxLength = Math.Max(length1, length2);
        var similarityPercentage = maxLength > 0 ? (1.0 - (double)lengthDiff / maxLength) * 100 : 100;

        var differenceDetails = new List<string>();
        var summary = new List<string>();

        // Basic length analysis
        if (length1 != length2)
        {
            var lengthDiffText = length1 > length2 ? "longer" : "shorter";
            differenceDetails.Add($"Content length: Original is {Math.Abs(lengthDiff)} characters {lengthDiffText}");
            summary.Add($"Length difference: {lengthDiff} chars");
        }

        // Line-by-line comparison
        var lines1 = normalized1.Split('\n');
        var lines2 = normalized2.Split('\n');
        var maxLines = Math.Max(lines1.Length, lines2.Length);

        var differentLines = 0;
        var addedLines = 0;
        var removedLines = 0;

        for (int i = 0; i < maxLines; i++)
        {
            var line1 = i < lines1.Length ? lines1[i] : "";
            var line2 = i < lines2.Length ? lines2[i] : "";

            if (line1 != line2)
            {
                if (string.IsNullOrEmpty(line1))
                {
                    addedLines++;
                    differenceDetails.Add($"Line {i + 1}: Added content in comparison version");
                }
                else if (string.IsNullOrEmpty(line2))
                {
                    removedLines++;
                    differenceDetails.Add($"Line {i + 1}: Removed content in comparison version");
                }
                else
                {
                    differentLines++;
                    differenceDetails.Add($"Line {i + 1}: Content differs");
                }
            }
        }

        // Add line difference summary
        if (differentLines > 0 || addedLines > 0 || removedLines > 0)
        {
            var lineSummary = new List<string>();
            if (differentLines > 0) lineSummary.Add($"{differentLines} modified");
            if (addedLines > 0) lineSummary.Add($"{addedLines} added");
            if (removedLines > 0) lineSummary.Add($"{removedLines} removed");
            
            summary.Add($"Line differences: {string.Join(", ", lineSummary)}");
        }

        // Content type analysis
        var contentAnalysis = AnalyzeContentTypes(html1, html2);
        if (contentAnalysis.Any())
        {
            differenceDetails.AddRange(contentAnalysis);
            summary.Add($"Content type differences: {contentAnalysis.Count} categories affected");
        }

        return new DetailedAnalysisResult
        {
            SimilarityPercentage = similarityPercentage,
            LengthDifference = lengthDiff,
            OriginalLength = length1,
            ComparisonLength = length2,
            DifferenceDetails = differenceDetails,
            Summary = string.Join("; ", summary)
        };
    }

    private static List<string> AnalyzeContentTypes(string html1, string html2)
    {
        var differences = new List<string>();

        // Count different HTML elements
        var elements1 = CountHtmlElements(html1);
        var elements2 = CountHtmlElements(html2);

        foreach (var element in elements1.Keys.Union(elements2.Keys))
        {
            var count1 = elements1.GetValueOrDefault(element, 0);
            var count2 = elements2.GetValueOrDefault(element, 0);

            if (count1 != count2)
            {
                var diff = count1 - count2;
                var change = diff > 0 ? "more" : "fewer";
                differences.Add($"HTML element '{element}': {Math.Abs(diff)} {change} in original");
            }
        }

        // Check for script/style differences
        var scripts1 = CountOccurrences(html1, "<script");
        var scripts2 = CountOccurrences(html2, "<script");
        if (scripts1 != scripts2)
        {
            var diff = scripts1 - scripts2;
            var change = diff > 0 ? "more" : "fewer";
            differences.Add($"Script tags: {Math.Abs(diff)} {change} in original");
        }

        var styles1 = CountOccurrences(html1, "<style");
        var styles2 = CountOccurrences(html2, "<style");
        if (styles1 != styles2)
        {
            var diff = styles1 - styles2;
            var change = diff > 0 ? "more" : "fewer";
            differences.Add($"Style tags: {Math.Abs(diff)} {change} in original");
        }

        return differences;
    }

    private static Dictionary<string, int> CountHtmlElements(string html)
    {
        var elements = new Dictionary<string, int>();
        var pattern = @"<(\w+)[\s>]";
        var matches = System.Text.RegularExpressions.Regex.Matches(html, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var element = match.Groups[1].Value.ToLower();
            elements[element] = elements.GetValueOrDefault(element, 0) + 1;
        }

        return elements;
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    private sealed class DetailedAnalysisResult
    {
        public double SimilarityPercentage { get; set; }
        public int LengthDifference { get; set; }
        public int OriginalLength { get; set; }
        public int ComparisonLength { get; set; }
        public List<string> DifferenceDetails { get; set; } = new();
        public string Summary { get; set; } = "";
    }
}

public sealed class DetailedHtmlComparisonResult
{
    public string OriginalUrl { get; set; } = "";
    public string ComparisonUrl { get; set; } = "";
    public bool HasDifferences { get; set; }
    public double SimilarityPercentage { get; set; }
    public int LengthDifference { get; set; }
    public int OriginalLength { get; set; }
    public int ComparisonLength { get; set; }
    public List<string> DifferenceDetails { get; set; } = new();
    public string Summary { get; set; } = "";
}
