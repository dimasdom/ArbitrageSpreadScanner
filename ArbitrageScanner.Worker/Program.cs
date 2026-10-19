using ArbitrageScanner.Worker;
using ArbitrageScanner.Worker.Controllers;
using ArbitrageScanner.Worker.Worker;
using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Infrastructure.Extensions;
using ArbitrageScanner.Infrastructure.HealthChecks;
using ArbitrageScanner.Infrastructure.Messaging.Kafka;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Infrastructure.Repositories;
using ArbitrageScanner.Futures.Services;
using ArbitrageScanner.Funding.Services;
using ArbitrageScanner.Spot.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Serilog;
using Serilog.Enrichers.Span;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog((context, cfg) =>
        {
            cfg.ReadFrom.Configuration(context.Configuration)
               .Enrich.WithSpan();

            // VerboseLogging:Enabled (ARBITRAGE_VERBOSE_CONSOLE_LOGS in .env) raises the
            // minimum level to Debug so per-symbol processing details also reach the
            // console (and Loki) instead of only the per-exchange init/load milestones.
            if (context.Configuration.GetValue("VerboseLogging:Enabled", false))
            {
                cfg.MinimumLevel.Debug();
            }
        })
        .ConfigureServices((context, services) =>
        {
            services.AddSingleton<FileService>();
            services.AddSingleton(sp =>
            {
                var mongoConfig = context.Configuration.GetArbitrageConfig().MongoDb;
                return new MongoService(mongoConfig.ConnectionString ?? string.Empty, mongoConfig.DatabaseName ?? string.Empty, sp.GetRequiredService<FileService>());
            });
            services.AddSingleton<ITradeOpportunityRepository>(sp => new TradeOpportunityRepositoryMongo(sp.GetRequiredService<MongoService>()));
            services.AddSingleton<DataService>();
            services.AddHttpClient("Telegram")
                .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(10));
            services.AddSingleton<ITelegramNotifierService, TelegramNotifierService>();
            services.AddSingleton<IProxyService, ProxyService>();
            services.AddSingleton<UserInterfaceService>();
            services.AddOptions<KafkaOptions>()
                .Bind(context.Configuration.GetSection(KafkaOptions.SectionName))
                .Validate(o => !string.IsNullOrWhiteSpace(o.BootstrapServers), "Kafka:BootstrapServers is required.")
                .ValidateOnStart();
            services.AddSingleton<IKafkaMessageProducer, KafkaMessageProducer>();
            services.AddSingleton<IServicesCommunicationService, ServicesCommunicationService>();
            services.AddTransient<FuturesPositionCalculatorService>();
            services.AddTransient<FundingPositionCalculatorService>();
            services.AddTransient<SpotPositionCalculatorService>();
            services.AddSingleton<FuturesObserverService>();
            services.AddSingleton<FundingObserverService>();
            services.AddSingleton<SpotObserverService>();
            services.AddSingleton<ArbitrageStrategyOrchestrator>();
            services.AddSingleton<ArbitrageService>();
            services.AddHostedService<ArbitrageWorker>();

            services.AddHealthChecks()
                .AddCheck<MongoHealthCheck>("mongo")
                .AddCheck<KafkaHealthCheck>("kafka");

            if (context.Configuration.GetValue("Observability:Enabled", true))
            {
                services.AddOpenTelemetry()
                    .ConfigureResource(r => r.AddService("arbiscanner-arbitrage-scanner", serviceVersion: "1.0.0"))
                    .WithTracing(tracing => tracing
                        .AddHttpClientInstrumentation(o => o.RecordException = true)
                        .AddSource(KafkaTelemetry.SourceName)
                        .AddSource("MongoDB.Driver.Core.Extensions.DiagnosticSources")
                        .AddOtlpExporter())
                    .WithMetrics(metrics => metrics
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation()
                        .AddMeter(KafkaPublishMetrics.MeterName)
                        .AddPrometheusHttpListener(o =>
                        {
                            o.UriPrefixes = ["http://+:8085/"];
                        }));
            }
        })
        .ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.UseUrls("http://+:8090");
            webBuilder.Configure(app => app.UseHealthChecks("/health"));
        })
        .Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
