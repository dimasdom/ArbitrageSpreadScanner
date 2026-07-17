using ccxt.pro;
using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Infrastructure.Services;

namespace ArbitrageScanner.Infrastructure.Services
{
    public abstract class ExchangePairService
    {
        public static (double bidLiquid, double askLiquid) GetLiquidity(ccxt.OrderBook orderBook, double currentPrice)
        {
            double minPrice = currentPrice * 0.995D;
            double maxPrice = currentPrice * 1.005D;
            double bidLiquid = 0;
            double askLiquid = 0;

            foreach (var bid in orderBook.bids ?? Enumerable.Empty<IList<double>>())
            {
                double bidPrice = bid[0];
                double bidAmount = bid[1];

                if (bidPrice >= minPrice)
                {
                    bidLiquid += bidPrice * bidAmount;
                }
            }

            foreach (var ask in orderBook.asks ?? Enumerable.Empty<IList<double>>())
            {
                double askPrice = ask[0];
                double askAmount = ask[1];

                if (askPrice <= maxPrice)
                {
                    askLiquid += askPrice * askAmount;
                }
            }

            return (bidLiquid, askLiquid);
        }

        public static (double bidLiquid, double askLiquid) GetLiquidity(IOrderBook orderBook, double currentPrice)
        {
            double minPrice = currentPrice * 0.995D;
            double maxPrice = currentPrice * 1.005D;
            double bidLiquid = 0;
            double askLiquid = 0;

            foreach (dynamic bid in orderBook.bids)
            {
                double bidPrice = Convert.ToDouble(bid[0]);
                double bidAmount = Convert.ToDouble(bid[1]);

                if (bidPrice >= minPrice)
                {
                    bidLiquid += bidPrice * bidAmount;
                }
            }

            foreach (dynamic ask in orderBook.asks)
            {
                double askPrice = Convert.ToDouble(ask[0]);
                double askAmount = Convert.ToDouble(ask[1]);

                if (askPrice <= maxPrice)
                {
                    askLiquid += askPrice * askAmount;
                }
            }

            return (bidLiquid, askLiquid);
        }

        public static double RoundToStep(double value, double step)
        {
            return Math.Floor(value / step) * step;
        }
    }
}
