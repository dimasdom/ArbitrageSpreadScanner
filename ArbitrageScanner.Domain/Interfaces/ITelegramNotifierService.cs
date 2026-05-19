namespace ArbitrageScanner.Domain.Interfaces
{
    public interface ITelegramNotifierService
    {
        Task SendMessageAsync(string message);
    }
}
