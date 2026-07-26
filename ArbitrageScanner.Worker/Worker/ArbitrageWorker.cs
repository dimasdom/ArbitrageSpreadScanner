using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Worker;
using Microsoft.Extensions.Hosting;
using OfficeOpenXml;

namespace ArbitrageScanner.Worker.Worker
{
    public class ArbitrageWorker : BackgroundService
    {
        private readonly ArbitrageService _arbitrageService;
        private readonly DataService _dataService;

        public ArbitrageWorker(ArbitrageService arbitrageService, DataService dataService)
        {
            _arbitrageService = arbitrageService;
            _dataService = dataService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("=== Starting ArbiScanner ===");

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
                Console.WriteLine($"Error: {ex.Message} Stack Trace:{ex.StackTrace}");
            }
        }
    }
}
