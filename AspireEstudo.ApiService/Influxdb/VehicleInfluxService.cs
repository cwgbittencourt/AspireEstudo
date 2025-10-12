using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using AspireEstudo.ApiService.Models;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Core.Flux.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlQueryType = InfluxDB3.Client.Query.QueryType;
using SqlWritePrecision = InfluxDB3.Client.Write.WritePrecision;

namespace AspireEstudo.ApiService.Influxdb;

public sealed class VehicleInfluxService : IAsyncDisposable
{
    private readonly bool _useSql;
    private readonly InfluxDB.Client.InfluxDBClient? _fluxClient;
    private readonly ILogger<VehicleInfluxService> _logger;
    private readonly InfluxOptions _options;
    private readonly string _measurement;
    private readonly InfluxDB3.Client.InfluxDBClient? _sqlClient;

    public VehicleInfluxService(IOptions<InfluxOptions> options, ILogger<VehicleInfluxService> logger)
    {
        _logger = logger;
        _options = options.Value;
        _measurement = string.IsNullOrWhiteSpace(_options.Measurement) ? "veiculo" : _options.Measurement;
        _useSql = string.Equals(_options.QueryStyle, "Sql", StringComparison.OrdinalIgnoreCase);

        if (_useSql)
        {
            if (string.IsNullOrWhiteSpace(_options.Database))
            {
                throw new InvalidOperationException("Influx database configuration is required when QueryStyle is set to Sql.");
            }

            _sqlClient = new InfluxDB3.Client.InfluxDBClient(
                host: _options.Url,
                token: _options.ApiKey,
                organization: _options.Org,
                database: _options.Database);
        }
        else
        {
            _fluxClient = new InfluxDB.Client.InfluxDBClient(new InfluxDB.Client.InfluxDBClientOptions(_options.Url)
            {
                Token = _options.ApiKey,
                Org = _options.Org,
                Bucket = _options.Bucket
            });
        }
    }

    public async Task StoreAsync(Veiculo veiculo, CancellationToken cancellationToken)
    {
        if (veiculo is null)
        {
            throw new ArgumentNullException(nameof(veiculo));
        }

        if (_useSql)
        {
            await StoreSqlAsync(veiculo, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await StoreFluxAsync(veiculo, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<Veiculo>> QueryAsync(string? placa, int? limit, CancellationToken cancellationToken)
    {
        var effectiveLimit = limit is > 0 ? limit.Value : 100;

        return _useSql
            ? await QuerySqlAsync(placa, effectiveLimit, cancellationToken).ConfigureAwait(false)
            : await QueryFluxAsync(placa, effectiveLimit, cancellationToken).ConfigureAwait(false);
    }

    private async Task StoreFluxAsync(Veiculo veiculo, CancellationToken cancellationToken)
    {
        ValidateFluxConfiguration();

        var record = BuildLineProtocol(veiculo);
        var writeApi = _fluxClient!.GetWriteApiAsync();
        await writeApi.WriteRecordAsync(record, WritePrecision.Ns, _options.Bucket, _options.Org, cancellationToken).ConfigureAwait(false);
    }

    private async Task StoreSqlAsync(Veiculo veiculo, CancellationToken cancellationToken)
    {
        ValidateSqlConfiguration();

        var record = BuildLineProtocol(veiculo);
        await _sqlClient!.WriteRecordAsync(record, _options.Database!, SqlWritePrecision.Ns, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Veiculo>> QueryFluxAsync(string? placa, int limit, CancellationToken cancellationToken)
    {
        ValidateFluxConfiguration();

        var queryApi = _fluxClient!.GetQueryApi();
        var tables = await queryApi.QueryAsync(BuildFluxQuery(placa, limit), _options.Org, cancellationToken).ConfigureAwait(false);
        return ConvertFluxTablesToVehicles(tables);
    }

    private async Task<IReadOnlyList<Veiculo>> QuerySqlAsync(string? placa, int limit, CancellationToken cancellationToken)
    {
        ValidateSqlConfiguration();

        var query = BuildSqlQuery(placa, limit);
        var vehicles = new List<Veiculo>();

        await foreach (var row in _sqlClient!
                       .Query(query, SqlQueryType.SQL, _options.Database!)
                       .WithCancellation(cancellationToken)
                       .ConfigureAwait(false))
        {
            if (TryConvertSqlRow(row, out var veiculo))
            {
                vehicles.Add(veiculo);
            }
        }

        return vehicles;
    }

    private void ValidateFluxConfiguration()
    {
        if (_fluxClient is null)
        {
            throw new InvalidOperationException("Influx Flux client is not configured for the current query style.");
        }

        if (string.IsNullOrWhiteSpace(_options.Bucket))
        {
            throw new InvalidOperationException("Influx bucket configuration was not provided.");
        }

        if (string.IsNullOrWhiteSpace(_options.Org))
        {
            throw new InvalidOperationException("Influx organization configuration was not provided.");
        }
    }

    private void ValidateSqlConfiguration()
    {
        if (_sqlClient is null)
        {
            throw new InvalidOperationException("Influx SQL client is not configured for the current query style.");
        }

        if (string.IsNullOrWhiteSpace(_options.Database))
        {
            throw new InvalidOperationException("Influx database configuration was not provided.");
        }
    }

    private IReadOnlyList<Veiculo> ConvertFluxTablesToVehicles(IEnumerable<FluxTable> tables)
    {
        var vehicles = new List<Veiculo>();

        foreach (var table in tables)
        {
            foreach (var record in table.Records)
            {
                var date = record.GetTimeInDateTime() ?? DateTime.UtcNow;
                var id = Convert.ToInt32(record.GetValueByKey("id") ?? 0, CultureInfo.InvariantCulture);
                var placa = Convert.ToString(record.GetValueByKey("placa")) ?? string.Empty;
                var lat = Convert.ToDecimal(record.GetValueByKey("lat") ?? 0m, CultureInfo.InvariantCulture);
                var lon = Convert.ToDecimal(record.GetValueByKey("lon") ?? 0m, CultureInfo.InvariantCulture);
                var velocidade = Convert.ToInt32(record.GetValueByKey("velocidade") ?? 0, CultureInfo.InvariantCulture);

                vehicles.Add(new Veiculo(date, id, placa, lat, lon, velocidade));
            }
        }

        return vehicles;
    }

    private bool TryConvertSqlRow(object?[] row, out Veiculo veiculo)
    {
        veiculo = default!;

        if (row.Length < 6)
        {
            return false;
        }

        try
        {
            var timestamp = ConvertToDateTime(row[0]!);
            var id = Convert.ToInt32(row[1]!, CultureInfo.InvariantCulture);
            var placa = Convert.ToString(row[2]) ?? string.Empty;
            var lat = Convert.ToDecimal(row[3]!, CultureInfo.InvariantCulture);
            var lon = Convert.ToDecimal(row[4]!, CultureInfo.InvariantCulture);
            var velocidade = Convert.ToInt32(row[5]!, CultureInfo.InvariantCulture);

            veiculo = new Veiculo(timestamp, id, placa, lat, lon, velocidade);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert row returned from Influx SQL query.");
            return false;
        }
    }

    private static DateTime ConvertToDateTime(object value)
    {
        return value switch
        {
            DateTime dateTime => dateTime.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                : dateTime.ToUniversalTime(),
            DateTimeOffset offset => offset.UtcDateTime,
            long unixNs => DateTimeOffset.FromUnixTimeMilliseconds(unixNs / 1_000_000).UtcDateTime,
            double unixMs => DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(unixMs, CultureInfo.InvariantCulture)).UtcDateTime,
            string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
                => parsed,
            _ => DateTime.UtcNow
        };
    }

    private string BuildFluxQuery(string? placa, int limit)
    {
        var filterPlaca = string.IsNullOrWhiteSpace(placa)
            ? string.Empty
            : $"  |> filter(fn: (r) => r[\"placa\"] == \"{EscapeFluxString(placa)}\")\n";

        return $"from(bucket: \"{EscapeFluxString(_options.Bucket)}\")\n" +
               "  |> range(start: -30d)\n" +
               "  |> filter(fn: (r) => r[\"_measurement\"] == \"" + EscapeFluxString(_measurement) + "\")\n" +
               filterPlaca +
               "  |> pivot(rowKey:[\"_time\"], columnKey:[\"_field\"], valueColumn:\"_value\")\n" +
               "  |> keep(columns:[\"_time\",\"id\",\"placa\",\"lat\",\"lon\",\"velocidade\"])\n" +
               "  |> sort(columns:[\"_time\"], desc:true)\n" +
               $"  |> limit(n:{limit})";
    }

    private string BuildSqlQuery(string? placa, int limit)
    {
        var whereClause = string.IsNullOrWhiteSpace(placa)
            ? string.Empty
            : $" WHERE placa = '{placa.Replace("'", "''")}'";

        return $"SELECT time, id, placa, lat, lon, velocidade FROM {_measurement}{whereClause} ORDER BY time DESC LIMIT {limit}";
    }

    private string BuildLineProtocol(Veiculo veiculo)
    {
        var timestamp = veiculo.DataEvento.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(veiculo.DataEvento, DateTimeKind.Utc)
            : veiculo.DataEvento.ToUniversalTime();

        var unixTimeNs = (timestamp - DateTime.UnixEpoch).Ticks * 100L;

        var builder = new StringBuilder();
        builder.Append(_measurement)
            .Append(",placa=")
            .Append(EscapeLineProtocolTag(veiculo.Placa))
            .Append(' ')
            .Append("id=")
            .Append(veiculo.Id)
            .Append('i')
            .Append(",lat=")
            .Append(veiculo.Lat.ToString(CultureInfo.InvariantCulture))
            .Append(",lon=")
            .Append(veiculo.Lon.ToString(CultureInfo.InvariantCulture))
            .Append(",velocidade=")
            .Append(veiculo.Velocidade)
            .Append('i')
            .Append(' ')
            .Append(unixTimeNs);

        return builder.ToString();
    }

    private static string EscapeFluxString(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string EscapeLineProtocolTag(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace(" ", "\\ ", StringComparison.Ordinal)
        .Replace(",", "\\,", StringComparison.Ordinal)
        .Replace("=", "\\=", StringComparison.Ordinal);

    public ValueTask DisposeAsync()
    {
        _fluxClient?.Dispose();
        _sqlClient?.Dispose();
        return ValueTask.CompletedTask;
    }
}


