using AspireEstudo.ApiService.Influxdb;
using AspireEstudo.ApiService.Models;
using AspireEstudo.ApiService.RabbitMQ;
using AspireEstudo.ApiService.Redis;
using AspireEstudo.ApiService.Services;
using AspireEstudo.ApiService.Mongo;
using MongoDB.Driver;
using RabbitMQ.Client;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddOptions<InfluxOptions>()
    .Bind(builder.Configuration.GetSection("Influx"))
    .ValidateDataAnnotations();

builder.Services.AddOptions<MongoOptions>()
    .Bind(builder.Configuration.GetSection("Mongo"))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Database), "Mongo:Database configuration is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Collection), "Mongo:Collection configuration is required.")
    .ValidateOnStart();

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var redisConnectionString = builder.Configuration.GetConnectionString("cache")
        ?? throw new InvalidOperationException("ConnectionStrings:cache was not provided.");
    return ConnectionMultiplexer.Connect(redisConnectionString);
});

builder.Services.AddSingleton<IConnection>(_ =>
{
    var rabbitConnectionString = builder.Configuration.GetConnectionString("messaging")
        ?? throw new InvalidOperationException("ConnectionStrings:messaging was not provided.");
    var factory = new ConnectionFactory
    {
        Uri = new Uri(rabbitConnectionString),
        DispatchConsumersAsync = true
    };
    return factory.CreateConnection();
});

builder.Services.AddSingleton<IMongoClient>(_ =>
{
    var mongoConnectionString = builder.Configuration.GetConnectionString("mongo")
        ?? throw new InvalidOperationException("ConnectionStrings:mongo was not provided.");
    return new MongoClient(mongoConnectionString);
});

builder.Services.AddSingleton<VehicleMongoRepository>();
builder.Services.AddSingleton<VehicleRedisWriter>();
builder.Services.AddSingleton<VehicleRabbitPublisher>();
builder.Services.AddSingleton<VehicleMySqlRepository>();
builder.Services.AddScoped<VehicleIngestionService>();
builder.Services.AddSingleton<VehicleInfluxService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost("/veiculos", async (Veiculo veiculo, VehicleIngestionService service, CancellationToken cancellationToken) =>
{
    await service.StoreAsync(veiculo, cancellationToken);
    return Results.Accepted($"/veiculos/{veiculo.Id}", veiculo);
})
.WithName("PostVeiculo");

app.MapPost("/influx/veiculos", async (Veiculo veiculo, VehicleInfluxService service, CancellationToken cancellationToken) =>
{
    await service.StoreAsync(veiculo, cancellationToken);
    return Results.Accepted($"/influx/veiculos/{veiculo.Id}", veiculo);
})
.WithName("PostVeiculoInflux");

app.MapGet("/influx/veiculos", async (string? placa, int? limit, VehicleInfluxService service, CancellationToken cancellationToken) =>
{
    var vehicles = await service.QueryAsync(placa, limit, cancellationToken);
    return Results.Ok(vehicles);
})
.WithName("GetVeiculosInflux");

app.MapDefaultEndpoints();

app.Run();




