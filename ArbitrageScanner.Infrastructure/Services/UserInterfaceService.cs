using EnumsNET;
using ArbitrageScanner.Infrastructure.Extensions;
using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using OfficeOpenXml;
using System.Text.Json;

namespace ArbitrageScanner.Infrastructure.Services
{
    public class UserInterfaceService
    {
        private readonly ConfigModel _config;
        private readonly DataService _dataService;
        private readonly ITelegramNotifierService _telegramNotifierService;

        public UserInterfaceService(
            IConfiguration configuration,
            DataService dataService,
            ITelegramNotifierService telegramNotifierService)
        {
            _config = configuration.GetArbitrageConfig();
            _dataService = dataService;
            _telegramNotifierService = telegramNotifierService;
        }

        public static void ShowPositionInfoInConsole(TradeOpportunityModel tradeOpportunity)
        {
            if (tradeOpportunity?.ExchangeRateA == null || tradeOpportunity.ExchangeRateB == null) return;
            Console.WriteLine($"Position found for {tradeOpportunity.ExchangeRateA.Symbol}:");
            Console.WriteLine($"Spread: {tradeOpportunity.Spread}%");
            Console.WriteLine($"{tradeOpportunity.ExchangeRateA.Exchange}: {tradeOpportunity.ExchangeRateA.ExchangeRate}");
            Console.WriteLine($"{tradeOpportunity.ExchangeRateB.Exchange}: {tradeOpportunity.ExchangeRateB.ExchangeRate}");
            Console.WriteLine($"Ask {tradeOpportunity.ExchangeRateA.VolumeAsk} | Bid {tradeOpportunity.ExchangeRateA.VolumeBid}");
            Console.WriteLine($"Ask {tradeOpportunity.ExchangeRateB.VolumeAsk} | Bid {tradeOpportunity.ExchangeRateB.VolumeBid}");
            Console.WriteLine($"Summary Slippage {tradeOpportunity.ExchangeRateA.Exchange} {tradeOpportunity.ExchangeRateA.SummarySlipage}% | Summary Slippage {tradeOpportunity.ExchangeRateB.Exchange} {tradeOpportunity.ExchangeRateB.SummarySlipage}%");
        }
       
        public async Task SaveJsonToFile<T>(T data, string fileName)
        {
            try
            {
            var json = JsonSerializer.Serialize(data);
            using (StreamWriter outputFile = new StreamWriter(fileName))
            {
                await outputFile.WriteAsync(json);
            }
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, fileName, "SaveJsonToFile");
                Console.WriteLine(ex.Message);
            }
        }

        public static T GetDataFromJsonFile<T>(string fileName)
        {
            try
            {
                var jsonContent = File.ReadAllText(fileName);
                return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(jsonContent)!;
            }
            catch
            {
                return default!;
            }
        }

        public async Task PostFoundSpreadToTelegram(TradeOpportunityModel tradeOpportunity)
        {
            try
            {
                string riskLevel = "";
                if (tradeOpportunity.Volatility > 0)
                {
                    riskLevel = $"Volatility(30m): {tradeOpportunity.Volatility.ToString("0.00")}%\nRisk Level - ";
                    double volatilityRatio = Math.Abs(tradeOpportunity.Volatility / tradeOpportunity.Spread * 100);

                    if (volatilityRatio <= 15)
                        riskLevel += "Safe";
                    else if (volatilityRatio <= 30)
                        riskLevel += "Medium";
                    else if (volatilityRatio <= 50)
                        riskLevel += "Risky";
                    else
                        riskLevel += "Dangerous";
                }
                string fundingForLong = "";
                string fundingForShort = "";
                try
                {
                    if (tradeOpportunity.ExchangeLong?.FundingRate.HasValue == true && tradeOpportunity.ExchangeLong.FundingRate.Value.fundingRate.HasValue)
                    {
                        double longFunding = tradeOpportunity.ExchangeLong.FundingRate.Value.fundingRate.Value * 100;
                        fundingForLong = $"\nFunding {tradeOpportunity.ExchangeLong.Exchange}:{longFunding.ToString("0.00")}%";
                    }
                    if (tradeOpportunity.ExchangeShort?.FundingRate.HasValue == true && tradeOpportunity.ExchangeShort.FundingRate.Value.fundingRate.HasValue)
                    {
                        double shortFunding = tradeOpportunity.ExchangeShort.FundingRate.Value.fundingRate.Value * 100;
                        fundingForShort = $"\nFunding {tradeOpportunity.ExchangeShort.Exchange}:{shortFunding.ToString("0.00")}%";
                    }
                }
                catch (Exception ex)
                {
                    _dataService.LogErrorEntry(ex, tradeOpportunity.ExchangeLong?.Symbol ?? "", "PostFoundSpreadToTelegram");
                }
                await _telegramNotifierService.SendMessageAsync($"Coin: {tradeOpportunity.ExchangeRateA?.Symbol}\nSpread: {tradeOpportunity.Spread.ToString("0.00")}%\nLong: {tradeOpportunity.ExchangeLong?.Exchange}({tradeOpportunity.ExchangeLong?.ExchangeRate}$)\nShort: {tradeOpportunity.ExchangeShort?.Exchange}({tradeOpportunity.ExchangeShort?.ExchangeRate}$)\nPosition volume: {_config.PositionSize}$\nSlippage {tradeOpportunity.ExchangeLong?.Exchange}: {tradeOpportunity.ExchangeLong?.SlippageShort.ToString("0.00")}%\nSlippage {tradeOpportunity.ExchangeShort?.Exchange}: {tradeOpportunity.ExchangeShort?.SlippageLong.ToString("0.00")}%\nVolumeAskLong:{tradeOpportunity.ExchangeLong?.VolumeAsk}\nVolumeBidLong:{tradeOpportunity.ExchangeLong?.VolumeBid}\n\nVolumeAskShort:{tradeOpportunity.ExchangeShort?.VolumeAsk}\nVolumeBidShort:{tradeOpportunity.ExchangeShort?.VolumeBid}\n{riskLevel}{fundingForLong}{fundingForShort}");
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, tradeOpportunity.ExchangeLong?.Symbol ?? "", "PostFoundSpreadToTelegram");
            }
        }
        public async Task PostFoundFundingSpreadToTelegram(TradeOpportunityModel tradeOpportunity)
        {
            try
            {
                string riskLevel = "";
                if (tradeOpportunity.Volatility > 0)
                {
                    riskLevel = $"\nVolatility(30m): {tradeOpportunity.Volatility.ToString("0.00")}%\nRisk Level - ";
                    double volatilityRatio = Math.Abs(tradeOpportunity.Volatility / tradeOpportunity.Spread * 100);

                    if (volatilityRatio <= 15)
                        riskLevel += "Safe";
                    else if (volatilityRatio <= 30)
                        riskLevel += "Medium";
                    else if (volatilityRatio <= 50)
                        riskLevel += "Risky";
                    else
                        riskLevel += "Dangerous";
                }
                string fundingForLong = "";
                string fundingForShort = "";
                //string nextPayout = "";
                try
                {
                    if (tradeOpportunity.ExchangeLong?.FundingRate.HasValue == true && tradeOpportunity.ExchangeLong.FundingRate.Value.fundingRate.HasValue)
                    {
                        double longFunding = tradeOpportunity.ExchangeLong.FundingRate.Value.fundingRate.Value * 100;
                        fundingForLong = $"\nFunding {tradeOpportunity.ExchangeLong.Exchange}:{longFunding.ToString("0.00")}%";
                    }
                    if (tradeOpportunity.ExchangeShort?.FundingRate.HasValue == true && tradeOpportunity.ExchangeShort.FundingRate.Value.fundingRate.HasValue)
                    {
                        double shortFunding = tradeOpportunity.ExchangeShort.FundingRate.Value.fundingRate.Value * 100;
                        fundingForShort = $"\nFunding {tradeOpportunity.ExchangeShort.Exchange}:{shortFunding.ToString("0.00")}%";
                    }
                    //if (tradeOpportunity.FundingPayoutExchangeA.HasValue && tradeOpportunity.FundingPayoutExchangeB.HasValue)
                    //{
                    //    nextPayout = $"Next Payout\n{tradeOpportunity.ExchangeRateA.Exchange} : {tradeOpportunity.FundingPayoutExchangeA?.ToString("MM/dd HH:mm")} UTC\n{tradeOpportunity.ExchangeRateB.Exchange} : {tradeOpportunity.FundingPayoutExchangeB?.ToString("MM/dd HH:mm")} UTC";
                    //}
                }
                catch (Exception ex)
                {
                    _dataService.LogErrorEntry(ex, tradeOpportunity.ExchangeLong?.Symbol ?? "", "PostFoundFundingSpreadToTelegram");
                }
                await _telegramNotifierService.SendMessageAsync($"Coin: {tradeOpportunity.ExchangeRateA?.Symbol}\nFunding Spread: {tradeOpportunity.TotalFunding.ToString("0.00")}%\nLong: {tradeOpportunity.ExchangeLong?.Exchange}({tradeOpportunity.ExchangeLong?.ExchangeRate}$)\nShort: {tradeOpportunity.ExchangeShort?.Exchange}({tradeOpportunity.ExchangeShort?.ExchangeRate}$)\nPosition volume: {_config.PositionSize}$\nSlippage {tradeOpportunity.ExchangeLong?.Exchange}: {tradeOpportunity.ExchangeLong?.SlippageShort.ToString("0.00")}%\nSlippage {tradeOpportunity.ExchangeShort?.Exchange}: {tradeOpportunity.ExchangeShort?.SlippageLong.ToString("0.00")}%{riskLevel}{fundingForLong}{fundingForShort}\nVolumeAskLong:{tradeOpportunity.ExchangeLong?.VolumeAsk}\nVolumeBidLong:{tradeOpportunity.ExchangeLong?.VolumeBid}\nVolumeAskShort:{tradeOpportunity.ExchangeShort?.VolumeAsk}\nVolumeBidShort:{tradeOpportunity.ExchangeShort?.VolumeBid}\nPossible Profit:{tradeOpportunity.PossibleProfit.ToString("0.00")}%");
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, tradeOpportunity.ExchangeLong?.Symbol ?? "", "PostFoundFundingSpreadToTelegram");
            }
        }
        public async Task PostFoundSpotSpreadToTelegram(TradeOpportunityModel tradeOpportunity)
        {
            try
            {
                string riskLevel = "";
                if (tradeOpportunity.Volatility > 0)
                {
                    riskLevel = $"\nVolatility(30m): {tradeOpportunity.Volatility.ToString("0.00")}%\nRisk Level - ";
                    double volatilityRatio = Math.Abs(tradeOpportunity.Volatility / tradeOpportunity.Spread * 100);

                    if (volatilityRatio <= 15)
                        riskLevel += "Safe";
                    else if (volatilityRatio <= 30)
                        riskLevel += "Medium";
                    else if (volatilityRatio <= 50)
                        riskLevel += "Risky";
                    else
                        riskLevel += "Dangerous";
                }
                string fundingForShort = "";
                try
                {
                    if (tradeOpportunity.ExchangeShort?.FundingRate.HasValue == true && tradeOpportunity.ExchangeShort.FundingRate.Value.fundingRate.HasValue)
                    {
                        double shortFunding = tradeOpportunity.ExchangeShort.FundingRate.Value.fundingRate.Value * 100;
                        fundingForShort = $"\nFunding:{shortFunding.ToString("0.00")}%";
                    }
                }
                catch (Exception ex)
                {
                    _dataService.LogErrorEntry(ex, tradeOpportunity.ExchangeLong?.Symbol ?? "", "PostFoundFundingSpreadToTelegram");
                }
                await _telegramNotifierService.SendMessageAsync($"Coin: {tradeOpportunity.ExchangeRateA?.Symbol}\nSpot Spread: {tradeOpportunity.Spread.ToString("0.00")}%\n{tradeOpportunity.ExchangeLong?.Exchange} Spot: ({tradeOpportunity.ExchangeLong?.ExchangeRate}$)\n{tradeOpportunity.ExchangeShort?.Exchange} Futures: ({tradeOpportunity.ExchangeShort?.ExchangeRate}$)\nPosition volume: {_config.PositionSize}$\nSlippage Spot: {tradeOpportunity.ExchangeLong?.SlippageShort.ToString("0.00")}%\nSlippage Futures: {tradeOpportunity.ExchangeShort?.SlippageLong.ToString("0.00")}%{riskLevel}{fundingForShort}\nVolumeAskLong:{tradeOpportunity.ExchangeLong?.VolumeAsk}\nVolumeBidLong:{tradeOpportunity.ExchangeLong?.VolumeBid}\n\nVolumeAskShort:{tradeOpportunity.ExchangeShort?.VolumeAsk}\nVolumeBidShort:{tradeOpportunity.ExchangeShort?.VolumeBid}\nPossible Profit:{tradeOpportunity.PossibleProfit.ToString("0.00")}%");
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, tradeOpportunity.ExchangeLong?.Symbol ?? "", "PostFoundSpotSpreadToTelegram");
            }
        }
        public async Task PostInvalidatingSpreadToTelegram(TradeOpportunityModel tradeOpportunity)
        {
            try
            {
                if (tradeOpportunity?.ExchangeRateA == null || tradeOpportunity.ExchangeRateB == null) return;
                await _telegramNotifierService.SendMessageAsync($"Spread closed: {tradeOpportunity.ExchangeRateA.Symbol}\n{tradeOpportunity.ExchangeRateA.Exchange} ({tradeOpportunity.ExchangeRateA.ExchangeRate})\n{tradeOpportunity.ExchangeRateB.Exchange} ({tradeOpportunity.ExchangeRateB.ExchangeRate})");
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, tradeOpportunity?.ExchangeRateA?.Symbol ?? "", "PostInvalidatingSpreadToTelegram");
                Console.WriteLine(ex.Message);
            }
        }
        public async Task PostInvalidatingSpotSpreadToTelegram(TradeOpportunityModel tradeOpportunity)
        {
            try
            {
                if (tradeOpportunity?.ExchangeRateA == null || tradeOpportunity.ExchangeLong == null) return;
                await _telegramNotifierService.SendMessageAsync($"Spot Spread closed: {tradeOpportunity.ExchangeRateA.Symbol}\n{tradeOpportunity.ExchangeLong.Exchange}\nSpot ({tradeOpportunity.ExchangeLong.ExchangeRate})\nFutures ({tradeOpportunity.ExchangeRateB?.ExchangeRate})");
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, tradeOpportunity?.ExchangeRateA?.Symbol ?? "", "PostInvalidatingSpotSpreadToTelegram");
                Console.WriteLine(ex.Message);
            }
        }
    }
}
