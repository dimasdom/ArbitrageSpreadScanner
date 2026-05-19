namespace ArbitrageScanner.Domain.Models
{
    public class ProxyModel
    {
        public object? id { get; set; }
        public string? ip { get; set; }
        public int port { get; set; }
        public string? country_code { get; set; }
        public string? username { get; set; }
        public string? password { get; set; }
    }
}
