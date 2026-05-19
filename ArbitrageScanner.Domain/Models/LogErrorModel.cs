namespace ArbitrageScanner.Domain.Models
{
    public class LogErrorModel
    {
        public string? Message { get; set; }
        public string? Method { get; set; }
        public string? Symbol { get; set; }
        public string? Exchange { get; set; }
        public DateTime DateTime { get; set; }
        public string? StackTrace { get; set; }
    }
}
