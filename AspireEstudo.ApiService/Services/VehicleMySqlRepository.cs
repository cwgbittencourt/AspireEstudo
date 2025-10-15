using AspireEstudo.ApiService.Models;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace AspireEstudo.ApiService.Services;

public class VehicleMySqlRepository
{
    private readonly string _connectionString;

    public VehicleMySqlRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("mysql")
            ?? throw new InvalidOperationException("ConnectionStrings:mysql was not provided.");
    }

    public async Task UpsertAsync(Veiculo veiculo, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string commandText = @"INSERT INTO veiculo (Id, Placa, DataEvento, Lat, Lon, Velocidade)
VALUES (@Id, @Placa, @DataEvento, @Lat, @Lon, @Velocidade)
ON DUPLICATE KEY UPDATE
    Placa = VALUES(Placa),
    DataEvento = VALUES(DataEvento),
    Lat = VALUES(Lat),
    Lon = VALUES(Lon),
    Velocidade = VALUES(Velocidade);";

        await using var command = new MySqlCommand(commandText, connection);
        command.Parameters.Add("@Id", MySqlDbType.Int32).Value = veiculo.Id;
        command.Parameters.Add("@Placa", MySqlDbType.VarChar).Value = veiculo.Placa;
        command.Parameters.Add("@DataEvento", MySqlDbType.DateTime).Value = veiculo.DataEvento;
        command.Parameters.Add("@Lat", MySqlDbType.Decimal).Value = veiculo.Lat;
        command.Parameters.Add("@Lon", MySqlDbType.Decimal).Value = veiculo.Lon;
        command.Parameters.Add("@Velocidade", MySqlDbType.Int32).Value = veiculo.Velocidade;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
