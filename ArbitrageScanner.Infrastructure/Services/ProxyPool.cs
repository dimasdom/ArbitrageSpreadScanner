using ArbitrageScanner.Domain.Models;

namespace ArbitrageScanner.Infrastructure.Services
{
    public class ProxyPool
    {
        private List<ProxyModel> _proxies = new();

        public List<ProxyModel> Proxies => _proxies;

        public void ReplaceAll(List<ProxyModel> proxies)
        {
            _proxies = proxies ?? new List<ProxyModel>();
        }
    }
}
