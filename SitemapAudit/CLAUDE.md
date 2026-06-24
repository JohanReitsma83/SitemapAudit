# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```powershell
# Build
dotnet build

# Run (interactive — prompts for mode at startup)
dotnet run

# Publish self-contained
dotnet publish -c Release
```

No tests exist in this project.

## Architecture

Interactive .NET 8 console app using `Microsoft.Extensions.Hosting`. Startup in `Program.cs` wires DI, then `SitemapCheckerRunner` (an `IHostedService`) drives everything. App exits by calling `_lifetime.StopApplication()` when done.

### Flow

1. `SitemapCheckerRunner.StartAsync` loads all URLs from the sitemap (via `SitemapReader`)
2. Prompts user to choose an `OperationMode` (1/2/3)
3. Dispatches to one of three operations

### Three modes

| Mode | Class | Output |
|------|-------|--------|
| `StatusCheck` | `UrlChecker` | `sitemap-errors.csv` |
| `HtmlComparison` | `HtmlComparisonService` | `html-comparison-results.csv` |
| `SingleUrlComparison` | `HtmlDifferenceAnalyzer` | `single-url-comparison-<timestamp>.txt` |

### Key design points

- `SitemapReader` handles both `<urlset>` and `<sitemapindex>` (nested sitemaps), with `.gz` decompression
- All bulk URL checks run via `Parallel.ForEachAsync` capped by `Sitemap:Concurrency`
- `HtmlComparisonService` retries up to `MaxRetries` times when status is non-200 or similarity drops below `MinSimilarityThreshold`
- Similarity is length-based only (not diff-based): `(1 - |len1-len2| / max(len1,len2)) * 100`
- `HtmlDifferenceAnalyzer` does deeper line-by-line + element-count analysis, used only in `SingleUrlComparison` mode
- Named `HttpClient` (`"checker"`) configured once in DI with timeout, user-agent, and automatic gzip/deflate/br decompression

### Configuration (`appsettings.json` → `SitemapOptions`)

| Key | Purpose |
|-----|---------|
| `BaseUrl` | Source sitemap host |
| `SitemapPath` | Path to sitemap index (default `/sitemap`) |
| `ComparisonBaseUrl` | Target host for HTML comparison (e.g. production) |
| `Concurrency` | Max parallel HTTP requests |
| `MinSimilarityThreshold` | Retry threshold for HTML comparison (default 95.0%) |
| `MaxRetries` | Retry count per URL |
| `RetryDelayMs` | Delay between retries |

Supports `AddCommandLine(args)` so any option can be overridden at runtime:
```powershell
dotnet run -- Sitemap:Concurrency=4 Sitemap:BaseUrl=https://staging.example.com
