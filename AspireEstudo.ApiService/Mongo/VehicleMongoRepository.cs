using System.Text.Json;
using AspireEstudo.ApiService.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AspireEstudo.ApiService.Mongo;

public class VehicleMongoRepository
{
    private readonly IMongoCollection<VehicleMongoDocument> _collection;
    private readonly ILogger<VehicleMongoRepository> _logger;

    public VehicleMongoRepository(IMongoClient client, IOptions<MongoOptions> options, ILogger<VehicleMongoRepository> logger)
    {
        _logger = logger;
        var mongoOptions = options.Value ?? throw new InvalidOperationException("Mongo configuration is missing.");

        var databaseName = string.IsNullOrWhiteSpace(mongoOptions.Database)
            ? throw new InvalidOperationException("Mongo:Database configuration is missing.")
            : mongoOptions.Database;

        var collectionName = string.IsNullOrWhiteSpace(mongoOptions.Collection)
            ? "veiculos"
            : mongoOptions.Collection;

        _collection = client.GetDatabase(databaseName).GetCollection<VehicleMongoDocument>(collectionName);

        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        try
        {
            var indexKeys = Builders<VehicleMongoDocument>
                .IndexKeys
                .Descending(document => document.DataEvento)
                .Ascending(document => document.VehicleId);

            var indexModel = new CreateIndexModel<VehicleMongoDocument>(indexKeys, new CreateIndexOptions
            {
                Name = "idx_vehicleid_dataevento"
            });

            _collection.Indexes.CreateOne(indexModel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to ensure Mongo indexes for vehicles.");
        }
    }

    public Task InsertAsync(Veiculo veiculo, CancellationToken cancellationToken)
    {
        if (veiculo is null)
        {
            throw new ArgumentNullException(nameof(veiculo));
        }

        var document = VehicleMongoDocument.FromVeiculo(veiculo);
        return _collection.InsertOneAsync(document, cancellationToken: cancellationToken);
    }

    private sealed class VehicleMongoDocument
    {
        [BsonId]
        public ObjectId Id { get; set; }

        public DateTime DataEvento { get; set; }

        public int VehicleId { get; set; }

        public string Placa { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Lat { get; set; }

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Lon { get; set; }

        public int Velocidade { get; set; }

        public string PayloadJson { get; set; } = string.Empty;

        public static VehicleMongoDocument FromVeiculo(Veiculo veiculo)
        {
            return new VehicleMongoDocument
            {
                DataEvento = veiculo.DataEvento,
                VehicleId = veiculo.Id,
                Placa = veiculo.Placa,
                Lat = veiculo.Lat,
                Lon = veiculo.Lon,
                Velocidade = veiculo.Velocidade,
                PayloadJson = JsonSerializer.Serialize(veiculo)
            };
        }
    }
}
