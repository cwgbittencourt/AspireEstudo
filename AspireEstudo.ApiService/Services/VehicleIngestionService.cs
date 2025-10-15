using AspireEstudo.ApiService.Influxdb;
using AspireEstudo.ApiService.Models;
using AspireEstudo.ApiService.RabbitMQ;
using AspireEstudo.ApiService.Redis;

namespace AspireEstudo.ApiService.Services;

public class VehicleIngestionService
{
    private readonly VehicleInfluxService _influxService;
    private readonly VehicleRedisWriter _redisWriter;
    private readonly VehicleRabbitPublisher _rabbitPublisher;
    private readonly VehicleMySqlRepository _mysqlRepository;

    public VehicleIngestionService(
        VehicleInfluxService influxService,
        VehicleRedisWriter redisWriter,
        VehicleRabbitPublisher rabbitPublisher,
        VehicleMySqlRepository mysqlRepository)
    {
        _influxService = influxService;
        _redisWriter = redisWriter;
        _rabbitPublisher = rabbitPublisher;
        _mysqlRepository = mysqlRepository;
    }

    public async Task StoreAsync(Veiculo veiculo, CancellationToken cancellationToken)
    {
        await _redisWriter.WriteAsync(veiculo, cancellationToken).ConfigureAwait(false);
        await _rabbitPublisher.PublishAsync(veiculo, cancellationToken).ConfigureAwait(false);
        await _influxService.StoreAsync(veiculo, cancellationToken).ConfigureAwait(false);
        await _mysqlRepository.UpsertAsync(veiculo, cancellationToken).ConfigureAwait(false);
    }
}
