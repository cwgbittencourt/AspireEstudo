using System.Text.Json;
using AspireEstudo.ApiService.Models;
using StackExchange.Redis;

namespace AspireEstudo.ApiService.Redis;

public class VehicleRedisWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IDatabase _database;

    public VehicleRedisWriter(IConnectionMultiplexer connection)
    {
        _database = connection.GetDatabase();
    }

    public async Task WriteAsync(Veiculo veiculo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var key = $"vehicle:{veiculo.Id}";
        var payload = JsonSerializer.Serialize(veiculo, SerializerOptions);

        await _database.StringSetAsync(key, payload).ConfigureAwait(false);
    }
}