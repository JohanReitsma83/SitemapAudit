# SitemapChecker

Console tool that crawls a sitemap and either checks HTTP status codes or compares HTML content between two environments (e.g. staging vs production).

## Usage

```powershell
dotnet run
```

On startup the tool fetches the sitemap, then prompts for an operation mode.

---

## Operation Modes

### 1. Status Check

Fetches every URL from the sitemap and checks the HTTP status code. Any URL that does not return `200 OK` is recorded as an error.

**Output:** CSV file configured by `ErrorOutputFile` (default: `sitemap-errors.csv`).

| Column | Description |
|--------|-------------|
| Url | The URL that was checked |
| StatusCode | HTTP status code returned |
| ReasonPhrase | HTTP reason phrase (e.g. `Not Found`) |
| GeneratedAt | Timestamp of the run |

---

### 2. HTML Comparison

Fetches every URL from the sitemap and compares the HTML response against the same path on a second host (`ComparisonBaseUrl`). Useful for validating a migration or deployment by comparing staging against production.

After selecting this mode you are asked whether to process all URLs or a batch (skip N, take M).

Retries automatically when a URL returns a non-200 status or when HTML similarity drops below `MinSimilarityThreshold`. Similarity is calculated as:

```
similarity = (1 - |len_a - len_b| / max(len_a, len_b)) * 100
```

**Output:** CSV file configured by `ComparisonErrorOutputFile` (default: `html-comparison-results.csv`).

| Column | Description |
|--------|-------------|
| Url | Source URL |
| OriginalUrl | Full URL fetched from source environment |
| ComparisonUrl | Full URL fetched from comparison environment |
| HasDifferences | `true` if HTML content differs |
| OriginalStatusCode | HTTP status from source |
| ComparisonStatusCode | HTTP status from comparison host |
| DifferenceSummary | Brief description of difference (e.g. length delta) |
| SimilarityPercentage | 0–100, based on content length delta |
| ErrorMessage | Populated when a fetch error occurred |
| GeneratedAt | Timestamp of the run |

---

### 3. Single URL Comparison

Compares one specific URL you provide against the same path on `ComparisonBaseUrl`. Produces a detailed line-by-line and HTML element-count analysis.

**Output:** Text file `single-url-comparison-<timestamp>.txt` in the working directory.

---

## Configuration

All settings live under the `Sitemap` key in `appsettings.json`.

```json
{
  "Sitemap": {
    "BaseUrl": "https://staging.example.com",
    "SitemapPath": "/sitemap",
    "Concurrency": 16,
    "RequestTimeoutSeconds": 20,
    "UserAgent": "SitemapChecker/1.0",
    "ErrorOutputFile": "sitemap-errors.csv",
    "ComparisonBaseUrl": "https://www.example.com",
    "ComparisonErrorOutputFile": "html-comparison-results.csv",
    "MaxRetries": 3,
    "MinSimilarityThreshold": 95.0,
    "RetryDelayMs": 1000
  }
}
```

| Setting | Description | Default |
|---------|-------------|---------|
| `BaseUrl` | Host of the site whose sitemap is crawled | *(required)* |
| `SitemapPath` | Path to the sitemap or sitemap index | `/sitemap` |
| `Concurrency` | Max parallel HTTP requests | `8` |
| `RequestTimeoutSeconds` | Per-request timeout | `20` |
| `UserAgent` | User-Agent header sent with every request | `SitemapChecker/1.0` |
| `ErrorOutputFile` | CSV output path for Status Check errors | `sitemap-errors.csv` |
| `ComparisonBaseUrl` | Host to compare against (modes 2 & 3) | *(required for comparison modes)* |
| `ComparisonErrorOutputFile` | CSV output path for HTML Comparison results | `html-comparison-results.csv` |
| `MaxRetries` | How many times to retry a failed or low-similarity URL | `3` |
| `MinSimilarityThreshold` | Similarity % below which a retry is triggered | `95.0` |
| `RetryDelayMs` | Milliseconds to wait between retries | `1000` |

Any setting can be overridden from the command line:

```powershell
dotnet run -- Sitemap:BaseUrl=https://staging.example.com Sitemap:Concurrency=4
```
