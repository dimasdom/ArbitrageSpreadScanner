using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;

namespace ArbitrageScanner.Worker.Worker
{
    public class ArbitrageWorker : BackgroundService
    {
        private readonly ArbitrageService _arbitrageService;
        private readonly DataService _dataService;
        private readonly ILogger<ArbitrageWorker> _logger;

        public ArbitrageWorker(ArbitrageService arbitrageService, DataService dataService, ILogger<ArbitrageWorker> logger)
        {
            _arbitrageService = arbitrageService;
            _dataService = dataService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("=== Starting ArbiScanner ===");

            try
            {
                ExcelPackage.License.SetNonCommercialOrganization("My Noncommercial organization");

                if (!stoppingToken.IsCancellationRequested)
                {
                    await _arbitrageService.StartOperation(true, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, method: "ArbitrageWorker.ExecuteAsync");
                _logger.LogError(ex, "ArbitrageWorker failed");
            }
        }
    }
}
