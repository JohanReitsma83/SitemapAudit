using SitemapAudit.Models;
using SitemapAudit.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SitemapAudit.HostedServices;

public sealed class BatchProcessingOptions
{
    public int Skip { get; set; }
    public int Take { get; set; }
    public bool ProcessAll { get; set; }
}

public sealed class SitemapCheckerRunner : IHostedService
{
    private readonly ILogger<SitemapCheckerRunner> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly SitemapReader _sitemapReader;
    private readonly UrlChecker _urlChecker;
    private readonly HtmlComparisonService _htmlComparisonService;
    private readonly HtmlDifferenceAnalyzer _htmlDifferenceAnalyzer;
    private readonly CsvOutputService _csvOutputService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SitemapOptions _options;

    public SitemapCheckerRunner(
        ILogger<SitemapCheckerRunner> logger,
        IHostApplicationLifetime lifetime,
        IOptions<SitemapOptions> options,
        SitemapReader sitemapReader,
        UrlChecker urlChecker,
        HtmlComparisonService htmlComparisonService,
        HtmlDifferenceAnalyzer htmlDifferenceAnalyzer,
        CsvOutputService csvOutputService,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _lifetime = lifetime;
        _sitemapReader = sitemapReader;
        _urlChecker = urlChecker;
        _htmlComparisonService = htmlComparisonService;
        _htmlDifferenceAnalyzer = htmlDifferenceAnalyzer;
        _csvOutputService = csvOutputService;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var start = DateTimeOffset.Now;
        try
        {
            if (string.IsNullOrWhiteSpace(_options.SitemapUrl))
            {
                _logger.LogError("SitemapUrl is missing in configuration.");
                return;
            }

            _logger.LogInformation("Loading sitemap: {Url}", _options.SitemapUrl);
            var urls = await _sitemapReader.GetAllUrlsAsync(_options.SitemapUrl, cancellationToken);
            _logger.LogInformation("Found {Count} URLs to process.", urls.Count);

            // Ask user for operation mode
            var operationMode = GetOperationModeFromUser(cancellationToken);
            if (operationMode == null)
            {
                _logger.LogInformation("Operation cancelled by user.");
                return;
            }

            _logger.LogInformation("Starting operation mode: {Mode}", operationMode);

            switch (operationMode)
            {
                case OperationMode.StatusCheck:
                    await PerformStatusCheckAsync(urls, cancellationToken);
                    break;
                case OperationMode.HtmlComparison:
                    var batchOptions = GetBatchProcessingOptions(urls.Count, cancellationToken);
                    if (batchOptions != null)
                    {
                        await PerformHtmlComparisonAsync(urls, batchOptions, cancellationToken);
                    }
                    break;
                case OperationMode.SingleUrlComparison:
                    await PerformSingleUrlComparisonAsync(cancellationToken);
                    break;
                default:
                    _logger.LogError("Unknown operation mode: {Mode}", operationMode);
                    break;
            }

            var duration = DateTimeOffset.Now - start;
            _logger.LogInformation("Operation completed in {Seconds:N1}s", duration.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error.");
        }
        finally
        {
            // Exit once finished
            _lifetime.StopApplication();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private OperationMode? GetOperationModeFromUser(CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine("Please select an operation mode:");
        Console.WriteLine("1. Status Check - Check HTTP status codes for all URLs");
        Console.WriteLine("2. HTML Comparison - Compare HTML content with staging environment");
        Console.WriteLine("3. Single URL Comparison - Compare a specific local URL with staging environment");
        Console.WriteLine();
        Console.Write("Enter your choice (1, 2, or 3): ");

        while (!cancellationToken.IsCancellationRequested)
        {
            var input = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(input))
            {
                Console.Write("Please enter 1, 2, or 3: ");
                continue;
            }

            if (int.TryParse(input.Trim(), out var choice))
            {
                switch (choice)
                {
                    case 1:
                        return OperationMode.StatusCheck;
                    case 2:
                        return OperationMode.HtmlComparison;
                    case 3:
                        return OperationMode.SingleUrlComparison;
                    default:
                        Console.Write("Invalid choice. Please enter 1, 2, or 3: ");
                        continue;
                }
            }
            else
            {
                Console.Write("Invalid input. Please enter 1, 2, or 3: ");
            }
        }

        return null; // Cancelled
    }

    private BatchProcessingOptions? GetBatchProcessingOptions(int totalUrls, CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine($"Found {totalUrls} URLs in the sitemap.");
        Console.WriteLine("How would you like to process the URLs?");
        Console.WriteLine("1. Process all URLs");
        Console.WriteLine("2. Process a batch (skip some, take a specific number)");
        Console.WriteLine();
        Console.Write("Enter your choice (1 or 2): ");

        while (!cancellationToken.IsCancellationRequested)
        {
            var input = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(input))
            {
                Console.Write("Please enter 1 or 2: ");
                continue;
            }

            if (int.TryParse(input.Trim(), out var choice))
            {
                switch (choice)
                {
                    case 1:
                        return new BatchProcessingOptions { ProcessAll = true, Skip = 0, Take = totalUrls };
                    case 2:
                        return GetBatchParameters(totalUrls, cancellationToken);
                    default:
                        Console.Write("Invalid choice. Please enter 1 or 2: ");
                        continue;
                }
            }
            else
            {
                Console.Write("Invalid input. Please enter 1 or 2: ");
            }
        }

        return null; // Cancelled
    }

    private BatchProcessingOptions? GetBatchParameters(int totalUrls, CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine($"Total URLs available: {totalUrls}");
        Console.WriteLine("Enter batch parameters:");
        
        // Get skip count
        int skip = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write($"How many URLs to skip (0-{totalUrls - 1}): ");
            var skipInput = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(skipInput))
            {
                Console.Write("Please enter a number: ");
                continue;
            }

            if (int.TryParse(skipInput.Trim(), out skip) && skip >= 0 && skip < totalUrls)
            {
                break;
            }
            else
            {
                Console.Write($"Invalid input. Please enter a number between 0 and {totalUrls - 1}: ");
            }
        }

        if (cancellationToken.IsCancellationRequested)
            return null;

        // Get take count
        int take = 0;
        var maxTake = totalUrls - skip;
        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write($"How many URLs to process (1-{maxTake}): ");
            var takeInput = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(takeInput))
            {
                Console.Write("Please enter a number: ");
                continue;
            }

            if (int.TryParse(takeInput.Trim(), out take) && take > 0 && take <= maxTake)
            {
                break;
            }
            else
            {
                Console.Write($"Invalid input. Please enter a number between 1 and {maxTake}: ");
            }
        }

        if (cancellationToken.IsCancellationRequested)
            return null;

        Console.WriteLine();
        Console.WriteLine($"Batch configuration: Skip {skip} URLs, process {take} URLs");
        Console.WriteLine($"URLs to process: {skip + 1} to {skip + take} (out of {totalUrls} total)");
        Console.Write("Continue with this batch? (y/n): ");

        var confirmInput = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(confirmInput) || !confirmInput.Trim().ToLower().StartsWith("y"))
        {
            Console.WriteLine("Batch processing cancelled.");
            return null;
        }

        return new BatchProcessingOptions { ProcessAll = false, Skip = skip, Take = take };
    }

    private async Task PerformStatusCheckAsync(List<string> urls, CancellationToken cancellationToken)
    {
        int ok = 0, failed = 0;
        var errors = new List<UrlError>();

        var po = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, _options.Concurrency),
            CancellationToken = cancellationToken
        };

        _logger.LogInformation("Performing status checks on {Count} URLs...", urls.Count);

        await Parallel.ForEachAsync(urls, po, async (url, ct) =>
        {
            var result = await _urlChecker.CheckAsync(url, ct);
            if (result.IsSuccessStatusCode && (int)result.StatusCode == 200)
            {
                Interlocked.Increment(ref ok);
                _logger.LogDebug("200 OK: {Url}", url);
            }
            else
            {
                Interlocked.Increment(ref failed);
                _logger.LogWarning("{Status} for {Url}", (int)result.StatusCode, url);
                
                lock (errors)
                {
                    errors.Add(new UrlError
                    {
                        Url = url,
                        StatusCode = (int)result.StatusCode,
                        ReasonPhrase = result.ReasonPhrase
                    });
                }
            }
        });

        // Write errors to CSV file if configured and there are errors
        if (!string.IsNullOrWhiteSpace(_options.ErrorOutputFile) && errors.Count > 0)
        {
            await _csvOutputService.WriteUrlErrorsToCsvAsync(errors, _options.ErrorOutputFile, cancellationToken);
        }

        _logger.LogInformation("Status check completed — OK: {Ok} | Failed/Non-200: {Fail}", ok, failed);
    }

    private async Task PerformHtmlComparisonAsync(List<string> urls, BatchProcessingOptions batchOptions, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ComparisonBaseUrl))
        {
            _logger.LogError("ComparisonBaseUrl is required for HTML comparison mode.");
            return;
        }

        // Apply batch processing to URLs
        var urlsToProcess = batchOptions.ProcessAll 
            ? urls 
            : urls.Skip(batchOptions.Skip).Take(batchOptions.Take).ToList();

        var htmlComparisons = new List<HtmlComparisonResult>();

        var po = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, _options.Concurrency),
            CancellationToken = cancellationToken
        };

        // Log batch information
        if (batchOptions.ProcessAll)
        {
            _logger.LogInformation("Performing HTML comparison on all {Count} URLs against {BaseUrl}...", urlsToProcess.Count, _options.ComparisonBaseUrl);
        }
        else
        {
            _logger.LogInformation("Performing HTML comparison on batch: {Count} URLs (skipped {Skip}, taking {Take}) against {BaseUrl}...", 
                urlsToProcess.Count, batchOptions.Skip, batchOptions.Take, _options.ComparisonBaseUrl);
        }

        await Parallel.ForEachAsync(urlsToProcess, po, async (url, ct) =>
        {
            var comparisonResult = await _htmlComparisonService.CompareUrlsAsync(url, _options.ComparisonBaseUrl!, ct);
            
            lock (htmlComparisons)
            {
                htmlComparisons.Add(comparisonResult);
            }

            var retryInfo = comparisonResult.RetryAttempts > 0 ? $" (after {comparisonResult.RetryAttempts} retries)" : "";
            
            if (comparisonResult.HasDifferences)
            {
                var similarityInfo = comparisonResult.SimilarityPercentage.HasValue 
                    ? $" - Similarity: {comparisonResult.SimilarityPercentage.Value:F1}%"
                    : "";
                _logger.LogWarning("HTML differences found for {Url}: {Summary}{SimilarityInfo}{RetryInfo}", 
                    url, comparisonResult.DifferenceSummary, similarityInfo, retryInfo);
            }
            else if (comparisonResult.ErrorMessage != null)
            {
                _logger.LogWarning("HTML comparison error for {Url}: {Error}{RetryInfo}", url, comparisonResult.ErrorMessage, retryInfo);
            }
            else
            {
                _logger.LogDebug("HTML match for {Url}{RetryInfo}", url, retryInfo);
            }
        });

        // Write HTML comparison results to CSV file if configured
        if (!string.IsNullOrWhiteSpace(_options.ComparisonErrorOutputFile) && htmlComparisons.Count > 0)
        {
            await _csvOutputService.WriteHtmlComparisonResultsToCsvAsync(htmlComparisons, _options.ComparisonErrorOutputFile, cancellationToken);
            var differencesCount = htmlComparisons.Count(c => c.HasDifferences);
            _logger.LogInformation("Wrote {ComparisonCount} HTML comparisons ({DifferencesCount} with differences) to {FilePath}", 
                htmlComparisons.Count, differencesCount, _options.ComparisonErrorOutputFile);
        }

        var matches = htmlComparisons.Count(c => !c.HasDifferences && c.ErrorMessage == null);
        var differences = htmlComparisons.Count(c => c.HasDifferences);
        var errors = htmlComparisons.Count(c => c.ErrorMessage != null);
        var totalRetries = htmlComparisons.Sum(c => c.RetryAttempts);
        var urlsWithRetries = htmlComparisons.Count(c => c.RetryAttempts > 0);

        _logger.LogInformation("HTML comparison completed — Matches: {Matches} | Differences: {Differences} | Errors: {Errors} | URLs with retries: {RetriedUrls} | Total retries: {TotalRetries}", 
            matches, differences, errors, urlsWithRetries, totalRetries);
    }

    private async Task PerformSingleUrlComparisonAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ComparisonBaseUrl))
        {
            _logger.LogError("ComparisonBaseUrl is required for single URL comparison mode.");
            return;
        }

        // Get the local URL from user input
        var localUrl = GetLocalUrlFromUser(cancellationToken);
        if (string.IsNullOrWhiteSpace(localUrl))
        {
            _logger.LogInformation("No local URL provided. Operation cancelled.");
            return;
        }

        // Build the comparison URL
        var comparisonUrl = BuildComparisonUrl(localUrl, _options.ComparisonBaseUrl);
        
        _logger.LogInformation("Comparing local URL: {LocalUrl}", localUrl);
        _logger.LogInformation("With comparison URL: {ComparisonUrl}", comparisonUrl);

        try
        {
            var client = _httpClientFactory.CreateClient("checker");
            
            // Fetch both URLs
            var localTask = FetchHtmlContentAsync(client, localUrl, cancellationToken);
            var comparisonTask = FetchHtmlContentAsync(client, comparisonUrl, cancellationToken);

            var localResponse = await localTask;
            var comparisonResponse = await comparisonTask;

            _logger.LogInformation("Local URL status: {StatusCode}", (int)localResponse.StatusCode);
            _logger.LogInformation("Comparison URL status: {StatusCode}", (int)comparisonResponse.StatusCode);

            if (!localResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to fetch local URL. Status: {StatusCode}", (int)localResponse.StatusCode);
                return;
            }

            if (!comparisonResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to fetch comparison URL. Status: {StatusCode}", (int)comparisonResponse.StatusCode);
                return;
            }

            // Get HTML content
            var localHtml = await localResponse.Content.ReadAsStringAsync(cancellationToken);
            var comparisonHtml = await comparisonResponse.Content.ReadAsStringAsync(cancellationToken);

            // Perform detailed analysis
            var analysis = _htmlDifferenceAnalyzer.AnalyzeDifferences(localUrl, comparisonUrl, localHtml, comparisonHtml);

            // Display results
            DisplaySingleUrlComparisonResults(analysis);

            // Save detailed results to file
            await SaveSingleUrlComparisonResultsAsync(analysis, cancellationToken);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during single URL comparison");
        }
    }

    private string? GetLocalUrlFromUser(CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine("Enter the local URL you want to compare:");
        Console.WriteLine("Example: https://localhost:44368/some-page");
        Console.WriteLine("Example: http://localhost:5000/about");
        Console.WriteLine();
        Console.Write("Local URL: ");

        while (!cancellationToken.IsCancellationRequested)
        {
            var input = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(input))
            {
                Console.Write("Please enter a valid URL: ");
                continue;
            }

            var url = input.Trim();
            
            // Basic URL validation
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && 
                (uri.Scheme == "http" || uri.Scheme == "https"))
            {
                return url;
            }
            else
            {
                Console.Write("Invalid URL format. Please enter a valid URL (e.g., https://localhost:44368/page): ");
            }
        }

        return null;
    }

    private static string BuildComparisonUrl(string localUrl, string comparisonBaseUrl)
    {
        try
        {
            var uri = new Uri(localUrl);
            var pathAndQuery = uri.PathAndQuery;
            
            // Ensure comparison base URL doesn't end with slash
            var baseUrl = comparisonBaseUrl.TrimEnd('/');
            
            return $"{baseUrl}{pathAndQuery}";
        }
        catch
        {
            // If URL parsing fails, return original URL
            return localUrl;
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

    private void DisplaySingleUrlComparisonResults(DetailedHtmlComparisonResult analysis)
    {
        Console.WriteLine();
        Console.WriteLine("=== SINGLE URL COMPARISON RESULTS ===");
        Console.WriteLine($"Local URL: {analysis.OriginalUrl}");
        Console.WriteLine($"Comparison URL: {analysis.ComparisonUrl}");
        Console.WriteLine($"Has Differences: {analysis.HasDifferences}");
        Console.WriteLine($"Similarity: {analysis.SimilarityPercentage:F1}%");
        Console.WriteLine();

        if (analysis.HasDifferences)
        {
            Console.WriteLine("=== DIFFERENCE SUMMARY ===");
            Console.WriteLine($"Content Length: Local={analysis.OriginalLength}, Comparison={analysis.ComparisonLength}");
            Console.WriteLine($"Length Difference: {analysis.LengthDifference} characters");
            Console.WriteLine($"Summary: {analysis.Summary}");
            Console.WriteLine();

            Console.WriteLine("=== DETAILED DIFFERENCES ===");
            foreach (var detail in analysis.DifferenceDetails)
            {
                Console.WriteLine($"• {detail}");
            }
        }
        else
        {
            Console.WriteLine("✅ No differences found - HTML content is identical!");
        }
        Console.WriteLine();
    }

    private async Task SaveSingleUrlComparisonResultsAsync(DetailedHtmlComparisonResult analysis, CancellationToken cancellationToken)
    {
        var fileName = $"single-url-comparison-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);

        var lines = new List<string>
        {
            $"Single URL Comparison Results - Generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"Local URL: {analysis.OriginalUrl}",
            $"Comparison URL: {analysis.ComparisonUrl}",
            $"Has Differences: {analysis.HasDifferences}",
            $"Similarity: {analysis.SimilarityPercentage:F1}%",
            $"Content Length: Local={analysis.OriginalLength}, Comparison={analysis.ComparisonLength}",
            $"Length Difference: {analysis.LengthDifference} characters",
            $"Summary: {analysis.Summary}",
            ""
        };

        if (analysis.HasDifferences)
        {
            lines.Add("=== DETAILED DIFFERENCES ===");
            foreach (var detail in analysis.DifferenceDetails)
            {
                lines.Add($"• {detail}");
            }
        }
        else
        {
            lines.Add("✅ No differences found - HTML content is identical!");
        }

        await File.WriteAllLinesAsync(filePath, lines, cancellationToken);
        _logger.LogInformation("Detailed comparison results saved to: {FilePath}", filePath);
    }

}
