using System.Text.Json;
using AspireEstudo.ApiService.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AspireEstudo.ApiService.Redis;

public class VehicleRedisWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IDatabase _database;
    private readonly ILogger<VehicleRedisWriter> _logger;

    public VehicleRedisWriter(IConnectionMultiplexer connection, ILogger<VehicleRedisWriter> logger)
    {
        _database = connection.GetDatabase();
        _logger = logger;
    }

    public async Task WriteAsync(Veiculo veiculo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var key = $"vehicle:{veiculo.Id}";
        var payload = JsonSerializer.Serialize(veiculo, SerializerOptions);

        await _database.StringSetAsync(key, payload).ConfigureAwait(false);

        _logger.LogInformation("Redis stored vehicle {VehicleId} at key {Key}: {Payload}", veiculo.Id, key, payload);
    }
}
