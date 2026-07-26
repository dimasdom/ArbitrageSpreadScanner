using RabbitMQ.Client;

namespace ArbitrageScanner.Infrastructure.Common
{
    public static class RabbitMqConnectionFactory
    {
        public static ConnectionFactory FromEnvironment()
        {
            var factory = new ConnectionFactory
            {
                HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost"
            };

            var port = Environment.GetEnvironmentVariable("RABBITMQ_PORT");
            if (!string.IsNullOrWhiteSpace(port) && int.TryParse(port, out var parsedPort))
            {
                factory.Port = parsedPort;
            }

            var username = Environment.GetEnvironmentVariable("RABBITMQ_USERNAME");
            if (!string.IsNullOrWhiteSpace(username))
            {
                factory.UserName = username;
            }

            var password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD");
            if (!string.IsNullOrWhiteSpace(password))
            {
                factory.Password = password;
            }

            return factory;
        }
    }
}
