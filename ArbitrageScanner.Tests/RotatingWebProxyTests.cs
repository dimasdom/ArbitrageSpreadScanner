using System.Net;
using ArbitrageScanner.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests;

public class RotatingWebProxyTests
{
    [Fact]
    public void GetProxy_ReflectsTheMostRecentlyRotatedTarget()
    {
        var first = new WebProxy("http://10.0.0.1:8080");
        var second = new WebProxy("http://10.0.0.2:9090");
        var proxy = new RotatingWebProxy(first);
        var destination = new Uri("https://example.com/api");

        var beforeRotate = proxy.GetProxy(destination);
        proxy.Rotate(second);
        var afterRotate = proxy.GetProxy(destination);

        beforeRotate.Should().Be(first.GetProxy(destination));
        afterRotate.Should().Be(second.GetProxy(destination));
        afterRotate.Should().NotBe(beforeRotate);
    }

    [Fact]
    public void Credentials_ReflectsTheCurrentTarget_NotTheOneItWasConstructedWith()
    {
        var first = new WebProxy("http://10.0.0.1:8080") { Credentials = new NetworkCredential("user1", "pass1") };
        var second = new WebProxy("http://10.0.0.2:9090") { Credentials = new NetworkCredential("user2", "pass2") };
        var proxy = new RotatingWebProxy(first);

        proxy.Credentials.Should().Be(first.Credentials);

        proxy.Rotate(second);

        proxy.Credentials.Should().Be(second.Credentials);
    }

    [Fact]
    public void Credentials_Setter_IsNoOp()
    {
        var target = new WebProxy("http://10.0.0.1:8080") { Credentials = new NetworkCredential("user1", "pass1") };
        var proxy = new RotatingWebProxy(target);

        proxy.Credentials = new NetworkCredential("someone-else", "ignored");

        proxy.Credentials.Should().Be(target.Credentials);
    }

    [Fact]
    public void IsBypassed_DelegatesToTheCurrentTarget()
    {
        var bypassAll = new WebProxy("http://10.0.0.1:8080") { BypassProxyOnLocal = false };
        var proxy = new RotatingWebProxy(bypassAll);
        var destination = new Uri("https://example.com/api");

        proxy.IsBypassed(destination).Should().Be(bypassAll.IsBypassed(destination));
    }
}
