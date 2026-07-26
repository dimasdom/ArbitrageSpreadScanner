using System.Net;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace ArbitrageScanner.Tests.Services;

public class TelegramNotifierServiceTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri;
        public string? LastRequestBody;
        public Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            // Capture the body now — the caller disposes its FormUrlEncodedContent right after the
            // request completes, before test assertions get a chance to read it.
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return Respond(request);
        }
    }

    private static IConfiguration BuildConfig(string? token, string? chatId) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Arbitrage:TelegramToken"] = token,
            ["Arbitrage:ChatId"] = chatId,
        })
        .Build();

    [Fact]
    public async Task SendMessageAsync_MissingToken_DoesNotCallHttpClientFactory()
    {
        var dataService = ServiceFactory.BuildDataService();
        var factory = new Mock<IHttpClientFactory>();
        var service = new TelegramNotifierService(BuildConfig(null, "123"), dataService, factory.Object);

        await service.SendMessageAsync("hello");

        factory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SendMessageAsync_MissingChatId_DoesNotCallHttpClientFactory()
    {
        var dataService = ServiceFactory.BuildDataService();
        var factory = new Mock<IHttpClientFactory>();
        var service = new TelegramNotifierService(BuildConfig("token", null), dataService, factory.Object);

        await service.SendMessageAsync("hello");

        factory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SendMessageAsync_ValidConfig_PostsToTelegramApi()
    {
        var handler = new StubHttpMessageHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Telegram")).Returns(new HttpClient(handler));
        var dataService = ServiceFactory.BuildDataService();
        var service = new TelegramNotifierService(BuildConfig("bot-token", "chat-1"), dataService, factory.Object);

        await service.SendMessageAsync("hello world");

        handler.LastRequestUri.Should().NotBeNull();
        handler.LastRequestUri!.ToString().Should().Be("https://api.telegram.org/botbot-token/sendMessage");
        handler.LastRequestBody.Should().Contain("chat_id=chat-1");
    }

    [Fact]
    public async Task SendMessageAsync_NonSuccessStatusCode_DoesNotThrow()
    {
        var handler = new StubHttpMessageHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("bad request"),
            },
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Telegram")).Returns(new HttpClient(handler));
        var dataService = ServiceFactory.BuildDataService();
        var service = new TelegramNotifierService(BuildConfig("bot-token", "chat-1"), dataService, factory.Object);

        var act = async () => await service.SendMessageAsync("hello");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendMessageAsync_HttpClientThrows_LogsAndDoesNotPropagate()
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Telegram")).Returns(new HttpClient(new ThrowingHandler()));
        var dataService = ServiceFactory.BuildDataService();
        var service = new TelegramNotifierService(BuildConfig("bot-token", "chat-1"), dataService, factory.Object);

        var act = async () => await service.SendMessageAsync("hello");

        await act.Should().NotThrowAsync();
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("network down");
    }
}
