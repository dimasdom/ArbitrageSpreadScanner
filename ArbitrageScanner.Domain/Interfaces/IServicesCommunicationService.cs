using ArbitrageScanner.Domain.Models;

namespace ArbitrageScanner.Domain.Interfaces
{
    public interface IServicesCommunicationService
    {
        Task PostPossiblePosition(TradeOpportunityModel tradeOpportunity);
    }
}
