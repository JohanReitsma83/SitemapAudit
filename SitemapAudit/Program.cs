using SitemapAudit.HostedServices;
using SitemapAudit.Models;
using SitemapAudit.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(cfg =>
    {
        cfg.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
           .AddEnvironmentVariables()
           .AddCommandLine(args);
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });
    })
    .ConfigureServices((ctx, services) =>
    {
        var section = ctx.Configuration.GetSection("Sitemap");
        services.Configure<SitemapOptions>(section);

        var options = section.Get<SitemapOptions>() ?? new SitemapOptions();

        services.AddHttpClient("checker", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                string.IsNullOrWhiteSpace(options.UserAgent)
                    ? "SitemapChecker/1.0"
                    : options.UserAgent);
            client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br");
        })
        // Enable automatic decompression
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        });
        
        services.AddTransient<SitemapReader>();
        services.AddTransient<UrlChecker>();
        services.AddTransient<HtmlComparisonService>();
        services.AddTransient<HtmlDifferenceAnalyzer>();
        services.AddTransient<CsvOutputService>();
        services.AddHostedService<SitemapCheckerRunner>();
    })
    .Build();

await host.RunAsync();
