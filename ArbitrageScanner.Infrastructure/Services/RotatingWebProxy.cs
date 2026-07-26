using System.Net;

namespace ArbitrageScanner.Infrastructure.Services
{
    /// <summary>
    /// An IWebProxy whose target/credentials can be swapped after an HttpClientHandler has
    /// already sent requests. HttpClientHandler itself locks properties like Proxy/UseProxy
    /// after first use, but it only ever reads through this indirection, so the handler's own
    /// configuration never changes and the lock never trips.
    /// </summary>
    public sealed class RotatingWebProxy : IWebProxy
    {
        private volatile WebProxy _current;

        public RotatingWebProxy(WebProxy initial)
        {
            _current = initial;
        }

        public void Rotate(WebProxy next) => _current = next;

        public ICredentials? Credentials
        {
            get => _current.Credentials;
            set
            {
                // No-op: credentials travel with the WebProxy swapped in via Rotate(), not through this setter.
            }
        }

        public Uri GetProxy(Uri destination) => _current.GetProxy(destination) ?? destination;

        public bool IsBypassed(Uri host) => _current.IsBypassed(host);
    }
}
