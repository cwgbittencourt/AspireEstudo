using System.Text.Json;
using AspireEstudo.ApiService.Models;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace AspireEstudo.ApiService.RabbitMQ;

public sealed class VehicleRabbitPublisher : IDisposable
{
    private const string QueueName = "vehicles";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IModel _channel;
    private readonly ILogger<VehicleRabbitPublisher> _logger;

    public VehicleRabbitPublisher(IConnection connection, ILogger<VehicleRabbitPublisher> logger)
    {
        _logger = logger;
        _channel = connection.CreateModel();
        _channel.QueueDeclare(queue: QueueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
    }

    public Task PublishAsync(Veiculo veiculo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToUtf8Bytes(veiculo, SerializerOptions);
        var properties = _channel.CreateBasicProperties();
        properties.ContentType = "application/json";
        properties.DeliveryMode = 2;

        _channel.BasicPublish(exchange: string.Empty, routingKey: QueueName, mandatory: false, basicProperties: properties, body: payload);
        _logger.LogInformation("RabbitMQ published vehicle {VehicleId} to queue {Queue}", veiculo.Id, QueueName);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _channel.Dispose();
    }
}
