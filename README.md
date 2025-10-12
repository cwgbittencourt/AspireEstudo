# AspireEstudo

## Visao Geral
AspireEstudo e uma solucao de estudo baseada no .NET Aspire que demonstra ingestao, armazenamento e distribuicao de dados de veiculos. O ecossistema e composto por um servico de API minimalista que processa telemetria, um front-end Razor Components para experimentacao e um AppHost que orquestra dependencias como Redis e RabbitMQ. A telemetria persiste em InfluxDB, mantendo compatibilidade com Flux e com o modo SQL do InfluxDB 3.

## Estrutura da Solucao
- `AspireEstudo.AppHost`: Projeto Aspire que provisiona os recursos (Redis cache, RabbitMQ) e injeta configuracoes compartilhadas nos microsservicos.
- `AspireEstudo.ApiService`: API minimalista que recebe eventos de veiculo, propaga-os para Redis, RabbitMQ e InfluxDB, e expoe consultas.
- `AspireEstudo.ServiceDefaults`: Biblioteca compartilhada com opinioes de telemetria, health checks e service discovery usadas pelos demais projetos.
- `AspireEstudo.Web`: Aplicacao Razor Components (Server) com cache de saida Redis, pensada para consumir os dados provindos da API.

## Fluxo de Dados Principal
1. Um cliente envia um POST para `/veiculos` no `ApiService` com o payload de um `Veiculo`.
2. O `VehicleIngestionService` grava o objeto em Redis (`vehicle:{id}`), publica uma mensagem JSON na fila `vehicles` (RabbitMQ) e armazena a linha no InfluxDB.
3. Consultas subsequentes podem ser feitas por meio de `/influx/veiculos`, que retorna uma lista dos registros mais recentes filtrados por placa ou limitados por quantidade.

## Servicos e Componentes
### ApiService
- **Programacao** (`AspireEstudo.ApiService/Program.cs:9`): adiciona integracoes Aspire, configura Swagger, injeta `VehicleInfluxService`, `VehicleRedisWriter`, `VehicleRabbitPublisher` e `VehicleIngestionService`.
- **Persistencia Influx** (`AspireEstudo.ApiService/Influxdb/VehicleInfluxService.cs:15`): suporta escrita e consulta usando Flux (bucket/org) ou SQL (database) com comutacao via `Influx:QueryStyle`. O measurement e configuravel via `Influx:Measurement`.
- **Mensageria** (`AspireEstudo.ApiService/RabbitMQ/VehicleRabbitPublisher.cs:8`): serializa `Veiculo` para JSON e publica na fila duravel `vehicles`.
- **Cache** (`AspireEstudo.ApiService/Redis/VehicleRedisWriter.cs:8`): grava snapshots de veiculos no Redis usando `StringSetAsync`.
- **Pipeline** (`AspireEstudo.ApiService/Services/VehicleIngestionService.cs:9`): coordena a escrita nos tres destinos.

### AppHost
- Orquestra recursos Aspire: `builder.AddRedis("cache")` com Redis Commander e `builder.AddRabbitMQ("messaging")` com Management Plugin (`AspireEstudo.AppHost/Program.cs:18`).
- Propaga secrets RabbitMQ via `AddParameter` e escreve variaveis de ambiente `Influx__*` para o ApiService.
- Expoe endpoints HTTP externos para ApiService e Web.
- Nao provisiona automaticamente InfluxDB; o endpoint deve estar acessivel externamente e configurado via `appsettings` ou variaveis.

### Web (Razor Components)
- Habilita `builder.AddRedisOutputCache("cache")` reaproveitando o mesmo recurso Redis fornecido pelo Aspire.
- Usa `MapRazorComponents<App>()` para servir componentes interativos; atualmente a pagina `Home` e um placeholder para expor informacoes sobre as dependencias (Redis, RabbitMQ).

### ServiceDefaults
- Centraliza opinioes de observabilidade: inclui instrumentacao OpenTelemetry (ASP.NET Core, HttpClient, runtime) e health checks (`/health`, `/alive` em desenvolvimento).
- Configura resiliencia e service discovery para `HttpClient` por padrao (`AspireEstudo.ServiceDefaults/Extensions.cs:23`).

## Configuracao
### ApiService (`AspireEstudo.ApiService/appsettings.json`)
- `ConnectionStrings:cache`: conexao StackExchange.Redis (`localhost:6379` quando executado isoladamente).
- `ConnectionStrings:messaging`: URI RabbitMQ (`amqp://guest:guest@localhost:5672/`).
- `Influx:Url`, `Org`, `Bucket`, `Database`, `ApiKey`, `Measurement`, `QueryStyle`: credenciais e parametros para o InfluxDB. `Measurement` tem fallback para `vehicle` no codigo.
- Pode ser sobrescrito por variaveis em `Influx__*` (usadas pelo AppHost) ou por `appsettings.Development.json`.

### AppHost (`AspireEstudo.AppHost/appsettings.json`)
- Repete a secao `Influx` para carregar valores default e reenviar via variaveis de ambiente.
- `RabbitMQ:Username` e `RabbitMQ:Password` sao tratados como parametros secretos e reutilizados ao provisionar RabbitMQ.

### Web (`AspireEstudo.Web/appsettings.json`)
- Mantem configuracao basica de logging. Dependencias de cache sao resolvidas via Aspire/ServiceDefaults.

## Execucao Local
1. **Requisitos**: .NET 9 SDK, Docker Desktop (para Redis, RabbitMQ e possivel InfluxDB), acesso a um servidor InfluxDB 3.x com token valido.
2. **Restaure pacotes**: `dotnet restore` na raiz.
3. **Executar via Aspire** (recomendado): `dotnet run --project AspireEstudo.AppHost`. O AppHost iniciara Redis e RabbitMQ em contenedores, aplicara configuracoes e abrira os endpoints HTTP externos.
4. **Executar ApiService isoladamente**: `dotnet run --project AspireEstudo.ApiService`. Certifique-se de que Redis, RabbitMQ e InfluxDB estejam acessiveis nos endpoints definidos em `ConnectionStrings` e `Influx`.
5. **Executar Web**: `dotnet run --project AspireEstudo.Web`. Ele consumira o mesmo Redis se iniciado via AppHost ou se `Redis__ConnectionString` estiver presente.

## APIs Disponiveis (ApiService)
| Metodo | Rota | Descricao |
| --- | --- | --- |
| POST | `/veiculos` | Recebe um `Veiculo` (JSON) e aciona o pipeline de escrita (Redis, RabbitMQ, Influx). |
| POST | `/influx/veiculos` | Persiste um `Veiculo` apenas no InfluxDB (bypass Redis/RabbitMQ). |
| GET | `/influx/veiculos?placa={string}&limit={int}` | Consulta telemetria recente no InfluxDB, filtrando por placa opcional e limitando resultados (default 100). |

Swagger e OpenAPI estao habilitados em desenvolvimento (`/swagger`).

### Modelo de Dados
```json
{
  "dataEvento": "2025-10-12T12:00:00Z",
  "id": 123,
  "placa": "ABC1234",
  "lat": -23.56,
  "lon": -46.63,
  "velocidade": 80
}
```

## Observabilidade e Resiliencia
- Health checks em desenvolvimento: `/health` (prontidao) e `/alive` (liveness).
- Instrumentacao OpenTelemetry configurada para logs, metricas e traces; caso `OTEL_EXPORTER_OTLP_ENDPOINT` esteja definido, exporta via OTLP automaticamente.
- `HttpClient` configurados via `ServiceDefaults` suportam service discovery e handlers de resiliencia padrao.

## Extensoes e Proximos Passos
- Implementar paginas no front-end que consultem os endpoints e exibam dados em tempo real.
- Adicionar testes automatizados para o pipeline de ingestao e para o `VehicleInfluxService` (Flux e SQL).
- Provisionar InfluxDB via Aspire usando `builder.AddProject` ou `AddContainer` para simplificar o setup local.
- Expandir mensagens RabbitMQ para incluir headers e integracao com consumidores dedicados.

