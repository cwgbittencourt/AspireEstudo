using System.ComponentModel.DataAnnotations;

namespace AspireEstudo.ApiService.Influxdb;

public class InfluxOptions
{
    [Required]
    public string Url { get; init; } = string.Empty;

    [Required]
    public string Org { get; init; } = string.Empty;

    [Required]
    public string Bucket { get; init; } = string.Empty;

    public string? Database { get; init; }

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string Measurement { get; init; } = "vehicle";

    public string QueryStyle { get; init; } = "Flux";
}
