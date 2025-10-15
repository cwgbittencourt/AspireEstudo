using Aspire.Hosting;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var influxSection = builder.Configuration.GetSection("Influx");
var influxUrl = influxSection["Url"] ?? "http://localhost:8181";
var influxOrg = influxSection["Org"] ?? string.Empty;
var influxBucket = influxSection["Bucket"] ?? string.Empty;
var influxDatabase = influxSection["Database"] ?? string.Empty;
var influxApiKey = influxSection["ApiKey"] ?? string.Empty;
var influxQueryStyle = influxSection["QueryStyle"] ?? "Sql";

var rabbitSection = builder.Configuration.GetSection("RabbitMQ");
var rabbitUsernameValue = rabbitSection["Username"] ?? throw new InvalidOperationException("RabbitMQ:Username configuration is missing.");
var rabbitPasswordValue = rabbitSection["Password"] ?? throw new InvalidOperationException("RabbitMQ:Password configuration is missing.");

var cache = builder.AddRedis("cache")
    .WithDataVolume("redis-data")
    .WithRedisCommander(commander =>
        commander.WithEndpoint("http", endpoint =>
        {
            endpoint.UriScheme = "http";
            endpoint.Port = 8081;
            endpoint.TargetPort = 8081;
            endpoint.IsExternal = true;
        }));

var rabbitUsername = builder.AddParameter("messaging-username", value: rabbitUsernameValue, secret: true);
var rabbitPassword = builder.AddParameter("messaging-password", value: rabbitPasswordValue, secret: true);

var rabbitmq = builder.AddRabbitMQ("messaging", rabbitUsername, rabbitPassword)
    .WithDataVolume("rabbitmq-data") 
    .WithManagementPlugin(port: 15672);

var apiService = builder.AddProject<Projects.AspireEstudo_ApiService>("apiservice")
    .WithExternalHttpEndpoints()
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq)
    .WithEnvironment("Influx__Url", influxUrl)
    .WithEnvironment("Influx__Org", influxOrg)
    .WithEnvironment("Influx__Bucket", influxBucket)
    .WithEnvironment("Influx__Database", influxDatabase)
    .WithEnvironment("Influx__ApiKey", influxApiKey)
    .WithEnvironment("Influx__QueryStyle", influxQueryStyle);

builder.AddProject<Projects.AspireEstudo_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();

  