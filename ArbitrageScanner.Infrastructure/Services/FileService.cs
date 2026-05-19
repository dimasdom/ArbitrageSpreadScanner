using ArbitrageScanner.Infrastructure.Extensions;
using ArbitrageScanner.Domain.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArbitrageScanner.Infrastructure.Services
{
    public class FileService
    {
        private readonly ConfigModel _config;
        public readonly static string perspectivePairsFileName = "perspectivePairs_";

        public FileService(IConfiguration configuration)
        {
            _config = configuration.GetArbitrageConfig();
        }

        public ConfigModel LoadConfig()
        {
            return _config;
        }
        public List<string> LoadExchangeList()
        {
            return _config.ExchangeList;
        }
        public List<ProxyModel> LoadProxyList()
        {
            return _config.ProxyList;
        }

        public ConfigModel LoadCurrentConfig()
        {
            return _config;
        }

        public List<string> LoadCurrentExchangeList()
        {
            return _config.ExchangeList;
        }

        public List<ProxyModel> LoadCurrentProxyList()
        {
            return _config.ProxyList;
        }
    }
}
