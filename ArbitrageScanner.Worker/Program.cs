using ArbitrageScanner.Worker;
using ArbitrageScanner.Worker.Controllers;
using ArbitrageScanner.Worker.Worker;
using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Infrastructure.Repositories;
using ArbitrageScanner.Futures.Services;
using ArbitrageScanner.Funding.Services;
using ArbitrageScanner.Spot.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<FileService>();
        services.AddSingleton<ITradeOpportunityRepository, TradeOpportunityRepositoryMongo>();
        services.AddSingleton<DataService>();
        services.AddHttpClient("Telegram")
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<ITelegramNotifierService, TelegramNotifierService>();
        services.AddSingleton<IProxyService, ProxyService>();
        services.AddSingleton<UserInterfaceService>();
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
    })
    .Build();

await host.RunAsync();