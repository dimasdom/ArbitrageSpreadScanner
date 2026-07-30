using ArbitrageScanner.Worker.Controllers;
using ArbitrageScanner.Infrastructure.Common;
using ArbitrageScanner.Infrastructure.Extensions;
using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Futures.Services;
using ArbitrageScanner.Funding.Services;
using ArbitrageScanner.Spot.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ArbitrageScanner.Worker
{
    public class ArbitrageService
    {
        private List<string> symbols = new List<string>();
        private readonly ArbitrageStrategyOrchestrator _arbitrageStrategyOrchestrator;
        private readonly FuturesObserverService _futuresObserverService;
        private readonly FundingObserverService _fundingObserverService;
        private readonly SpotObserverService _spotObserverService;
        private readonly IProxyService _proxyService;
        private readonly DataService _dataService;
        private readonly ConfigModel _config;
        private readonly ILogger<ArbitrageService> _logger;

        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(30);
        public static readonly SemaphoreSlim semaphoreWatching = new SemaphoreSlim(20);

        private const int SymbolProcessingTimeoutSeconds = 60;
        private const int RetryDelaySeconds = 2;
        private const int SymbolBatchIntervalSeconds = 5;

        public ArbitrageService(
            ArbitrageStrategyOrchestrator arbitrageStrategyOrchestrator,
            FuturesObserverService futuresObserverService,
            FundingObserverService fundingObserverService,
            SpotObserverService spotObserverService,
            IProxyService proxyService,
            DataService dataService,
            IConfiguration configuration,
            ILogger<ArbitrageService> logger)
        {
            _arbitrageStrategyOrchestrator = arbitrageStrategyOrchestrator;
            _futuresObserverService = futuresObserverService;
            _fundingObserverService = fundingObserverService;
            _spotObserverService = spotObserverService;
            _proxyService = proxyService;
            _dataService = dataService;
            _config = configuration.GetArbitrageConfig();
            _logger = logger;

            List<string> exchangeList = _dataService.ExchangeInitList;
            var tasks = exchangeList.Select((exc) =>
            {
                return Task.Run(async () =>
                {
                    _logger.LogInformation("Start to init {Exchange}", exc);
                    var exchangeService = new ExchangeService(_dataService, configuration);
                    var inst = ccxt.Exchange.DynamicallyCreateInstance(exc.ToLower());
                    inst.enableRateLimit = true;
                    await exchangeService.Init(inst);
                    _dataService.ExchangeServices[exchangeService.GetExchangeName()] = exchangeService;
                    _dataService.Exchanges[exchangeService.GetExchangeName()] = _dataService.ExchangeServices[exchangeService.GetExchangeName()].GetExchange();
                    //init services to observer found spreads
                    var exchangeObserverService = new ExchangeService(_dataService, configuration);
                    var observerInst = ccxt.Exchange.DynamicallyCreateInstance(exc.ToLower());
                    observerInst.enableRateLimit = true;
                    await exchangeObserverService.Init(observerInst);
                    _dataService.ExchangeObserverServices[exchangeService.GetExchangeName()] = exchangeObserverService;

                    _logger.LogInformation("Finished to init {Exchange}", exc);
                });
            });
            Task.WaitAll(Task.WhenAll(tasks));
        }
        public async Task StartOperation(bool parallel, CancellationToken cancellationToken)
        {
            await _dataService.LoadProxiesAsync();
            await _proxyService.SetNextProxy();

            foreach (var svc in _dataService.ExchangeServices.Select(x => x.Value))
            {
                _logger.LogInformation("Start to load swap markets {Exchange}", svc.GetExchangeName());
                await svc.LoadSwapMarkets();
                await svc.LoadSpotMarkets();
                _logger.LogInformation("Finished to load swap markets {Exchange}", svc.GetExchangeName());
            }

            foreach (var svc in _dataService.ExchangeObserverServices.Select(x => x.Value))
            {
                _logger.LogInformation("Start to load swap markets order {Exchange}", svc.GetExchangeName());
                await svc.LoadSwapMarkets();
                await svc.LoadSpotMarkets();
                _logger.LogInformation("Finished to load swap markets order {Exchange}", svc.GetExchangeName());
            }
            await _dataService.LoadActivePossiblePositionsAsync();
            _futuresObserverService.StartToWatchPositionsWithCombineKeys(cancellationToken).FireAndForgetWithLogging(_dataService, "StartToWatchPositionsWithCombineKeys", exchange: "Futures");
            _fundingObserverService.StartToWatchPositionsWithCombineKeys(cancellationToken).FireAndForgetWithLogging(_dataService, "StartToWatchPositionsWithCombineKeys", exchange: "Funding");
            _spotObserverService.StartToWatchPositionsWithCombineKeys(cancellationToken).FireAndForgetWithLogging(_dataService, "StartToWatchPositionsWithCombineKeys", exchange: "Spot");
            if (!symbols.Any())
            {
                symbols = (await _dataService.GetUniqueCommonFuturesPairsFromApiAsync()).ToList();
            }
            if (parallel)
            {
                await StartOperationParallel(cancellationToken: cancellationToken);
            }
            else
            {
                await StartOperations(cancellationToken);
            }
        }
        public async Task StartOperations(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    foreach (var symbol in symbols)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(SymbolProcessingTimeoutSeconds));
                        await ProcessSymbol(symbol, cts.Token);
                    }
                }
                catch (Exception ex)
                {
                    _dataService.LogErrorEntry(ex, method: "StartOperations");
                    _logger.LogError(ex, "StartOperations failed");
                    await ArbitrageScanner.Infrastructure.Common.TaskExtensions.DelayRetry(TimeSpan.FromSeconds(RetryDelaySeconds), cancellationToken);
                }
            }
        }

        public async Task StartOperationParallel(int timesUpdated = 0, CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (timesUpdated > 0 && timesUpdated % 10 == 0)
                    {
                        var updateTasks = _dataService.ExchangeServices.Values.Select(x => x.UpdatePairs());
                        await Task.WhenAll(updateTasks);
                    }

                    var tasks = new List<Task>();
                    int symbolCount = 0;
                    foreach (var symbol in symbols)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(SymbolProcessingTimeoutSeconds));
                        var task = ProcessSymbol(symbol, cts.Token);
                        tasks.Add(task);
                        symbolCount++;

                        if (symbolCount % 200 == 0)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(SymbolBatchIntervalSeconds), cancellationToken);
                            await _proxyService.SetNextProxy();
                        }

                        if (tasks.Count >= _config.ThreadCount)
                        {
                            var finishedTask = await Task.WhenAny(tasks);
                            tasks.Remove(finishedTask);
                        }
                    }

                    if (tasks.Count > 0)
                    {
                        await Task.WhenAll(tasks);
                    }

                    _logger.LogInformation("READY");
                    symbols.Shuffle();
                    timesUpdated++;
                }
                catch (Exception ex)
                {
                    _dataService.LogErrorEntry(ex, method: "StartOperationParallel");
                    _logger.LogError(ex, "StartOperationParallel failed");
                    await ArbitrageScanner.Infrastructure.Common.TaskExtensions.DelayRetry(TimeSpan.FromSeconds(RetryDelaySeconds), cancellationToken);
                }
            }
        }
        public async Task ProcessSymbol(string symbol, CancellationToken cancellationToken)
        {
            try
            {
                await semaphore.WaitAsync(cancellationToken);
                _logger.LogDebug("Processing {Symbol}...", symbol);
                CoinDataModel coinData = new CoinDataModel
                {
                    Symbol = symbol,
                    ExchangeRates = new List<ExchangeRateModel>()
                };
                IEnumerable<Task<ExchangeRateModel?>> tasks = _dataService.ExchangeServices.Values.Select(x => x.GetDataForCoin(symbol));

                var processingTask = Task.WhenAll(tasks);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(SymbolProcessingTimeoutSeconds), cancellationToken);

                var completedTask = await Task.WhenAny(processingTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _logger.LogWarning("Timeout: {Symbol} took too long and was cancelled.", symbol);
                    return;
                }
                coinData.ExchangeRates.AddRange(processingTask.Result.Where(x => x != null).Select(x => x!));
                await _arbitrageStrategyOrchestrator.General(coinData);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "Timeout: {Symbol} took too long and was cancelled.", symbol);
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, symbol, method: "ProcessSymbol");
                _logger.LogError(ex, "Exception while processing {Symbol}", symbol);
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}
