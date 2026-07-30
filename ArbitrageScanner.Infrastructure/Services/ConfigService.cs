using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;

namespace ArbitrageScanner.Infrastructure.Services
{
    public class ConfigService
    {
        private readonly ConfigModel _config;

        public ConfigModel Current => _config;

        public ConfigService(IConfiguration configuration)
        {
            _config = configuration.GetArbitrageConfig();
        }
    }
}
