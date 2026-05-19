using System.Runtime.Serialization.Formatters.Binary;
using Newtonsoft.Json;
namespace ArbitrageScanner.Domain.Models
{
    public class CoinDataModel
    {
        public required string Symbol { get; set; }
        public required List<ExchangeRateModel> ExchangeRates { get; set; }
        public static T DeepClone<T>(T obj)
        {
            var json = JsonConvert.SerializeObject(obj, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                PreserveReferencesHandling = PreserveReferencesHandling.Objects
            });

            return JsonConvert.DeserializeObject<T>(json, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            })!;
        }
    }
}
