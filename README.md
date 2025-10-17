# AspireEstudo

## Visao Geral
AspireEstudo e uma solucao de estudo baseada no .NET Aspire que demonstra ingestao, armazenamento e distribuicao de dados de veiculos. O ecossistema e composto por um servico de API minimalista que processa telemetria, um front-end Razor Components para experimentacao e um AppHost que orquestra dependencias como Redis e RabbitMQ. A telemetria persiste em InfluxDB, e tambem e replicada em MySQL e MongoDB para cenarios de consulta relacional e historico append-only.

## Estrutura da Solucao
- `AspireEstudo.AppHost`: Projeto Aspire que provisiona os recursos (Redis, RabbitMQ, MongoDB, Seq) e injeta configuracoes compartilhadas nos microsservicos.
- `AspireEstudo.ApiService`: API minimalista que recebe eventos de veiculo, propaga-os para Redis, RabbitMQ, InfluxDB, MongoDB e MySQL, e expoe consultas.
- `AspireEstudo.ServiceDefaults`: Biblioteca compartilhada com opinioes de telemetria, health checks e service discovery usadas pelos demais projetos.
- `AspireEstudo.Web`: Aplicacao Razor Components (Server) com cache de saida Redis, pensada para consumir os dados provindos da API.

## Fluxo de Dados Principal
1. Um cliente envia um POST para `/veiculos` no `ApiService` com o payload de um `Veiculo`.
2. O `VehicleIngestionService` grava o objeto em Redis (`vehicle:{id}`), publica uma mensagem JSON na fila `vehicles` (RabbitMQ), armazena a linha no InfluxDB, anexa um documento no MongoDB com o payload completo e executa um upsert no MySQL.
3. Consultas subsequentes podem ser feitas por meio de `/influx/veiculos`, que retorna uma lista dos registros mais recentes filtrados por placa ou limitados por quantidade.

## Servicos e Componentes
### ApiService
- **Programacao** (`AspireEstudo.ApiService/Program.cs:9`): adiciona integracoes Aspire, configura Swagger, injeta `VehicleInfluxService`, `VehicleRedisWriter`, `VehicleRabbitPublisher`, `VehicleMongoRepository` e `VehicleIngestionService`.
- **Persistencia Influx** (`AspireEstudo.ApiService/Influxdb/VehicleInfluxService.cs:15`): suporta escrita e consulta usando Flux (bucket/org) ou SQL (database) com comutacao via `Influx:QueryStyle`. O measurement e configuravel via `Influx:Measurement`.
- **Persistencia historica (MongoDB)** (`AspireEstudo.ApiService/Mongo/VehicleMongoRepository.cs:1`): insere um novo documento para cada evento com `DataEvento`, `VehicleId`, `Placa`, coordenadas, velocidade e `PayloadJson` (serializacao integral do `Veiculo`). Mantem indice composto `(VehicleId asc, DataEvento desc)` para consultas eficientes por veiculo.
- **Persistencia relacional** (`AspireEstudo.ApiService/Services/VehicleMySqlRepository.cs:7`): usa MySqlConnector para executar upsert na tabela `veiculo` com base na chave primaria `Id`.
- **Mensageria** (`AspireEstudo.ApiService/RabbitMQ/VehicleRabbitPublisher.cs:8`): serializa `Veiculo` para JSON e publica na fila duravel `vehicles`.
- **Cache** (`AspireEstudo.ApiService/Redis/VehicleRedisWriter.cs:8`): grava snapshots de veiculos no Redis usando `StringSetAsync`.
- **Pipeline** (`AspireEstudo.ApiService/Services/VehicleIngestionService.cs:9`): coordena a escrita nos cinco destinos (Redis, RabbitMQ, InfluxDB, MongoDB, MySQL).

### AppHost
- Orquestra recursos Aspire: `builder.AddRedis("cache")` com Redis Commander, `builder.AddRabbitMQ("messaging")` com Management Plugin, `builder.AddMongoDB("mongo")` com DB Gate e `builder.AddSeq("seq")` com volume de dados (`AspireEstudo.AppHost/Program.cs:18`).
- Propaga `Serilog__Seq__ServerUrl` para ApiService e Web, permitindo que Serilog encaminhe logs ao Seq provisionado.
- Propaga secrets RabbitMQ via `AddParameter` e escreve variaveis de ambiente `Influx__*` para o ApiService.
- Expoe endpoints HTTP externos para ApiService e Web.
- Nao provisiona automaticamente InfluxDB nem MySQL; os endpoints devem estar acessiveis externamente e configurados via `appsettings` ou variaveis.

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
- `ConnectionStrings:mysql`: string de conexao MySQL usada pelo `VehicleMySqlRepository`; espera um schema com tabela `veiculo` e permissoes de insert/update.
- `ConnectionStrings:mongo`: URI de conexao MongoDB utilizada pelo `VehicleMongoRepository`. O AppHost usa o mesmo nome de recurso (`mongo`) para publicar a connection string Aspire.
- `Mongo:Database` e `Mongo:Collection`: nome do database e da colecao onde os documentos sao inseridos. A colecao padrao e `veiculos`.
- `infrastructure/mysql/init.sql`: script SQL opcional para provisionar o database `appdb` e a tabela `veiculo` em ambientes de desenvolvimento.
- `Influx:Url`, `Org`, `Bucket`, `Database`, `ApiKey`, `Measurement`, `QueryStyle`: credenciais e parametros para o InfluxDB. `Measurement` tem fallback para `vehicle` no codigo.
- `Serilog:Seq:ServerUrl`: URL base utilizada quando o ApiService roda isolado; ao executar via AppHost o valor e substituido pela variavel `Serilog__Seq__ServerUrl`.
- Pode ser sobrescrito por variaveis em `Influx__*` / `Mongo__*` (usadas pelo AppHost) ou por `appsettings.Development.json`.

### AppHost (`AspireEstudo.AppHost/appsettings.json`)
- Repete a secao `Influx` para carregar valores default e reenviar via variaveis de ambiente.
- `RabbitMQ:Username` e `RabbitMQ:Password` sao tratados como parametros secretos e reutilizados ao provisionar RabbitMQ.
- O AppHost injeta `Serilog__Seq__ServerUrl` nos servicos dependentes, apontando para o endpoint HTTP exposto pelo Seq.

### Web (`AspireEstudo.Web/appsettings.json`)
- Mantem configuracao basica de logging. Dependencias de cache sao resolvidas via Aspire/ServiceDefaults.

## Execucao Local
1. **Requisitos**: .NET 9 SDK, Docker Desktop (para Redis, RabbitMQ e possivel InfluxDB/MySQL/MongoDB), acesso a um servidor InfluxDB 3.x com token valido, a uma instancia MySQL acessivel pelas credenciais de `ConnectionStrings:mysql`, e a um MongoDB 6.x+.
2. **Prepare o MySQL** (opcional, para ambientes vazios): execute `mysql -u appuser -p < infrastructure/mysql/init.sql` (ajuste usuario/senha conforme necessario) para criar o schema `appdb` e a tabela `veiculo`.
3. **Prepare o MongoDB**: crie o database/colecao conforme `Mongo:Database`/`Mongo:Collection` ou deixe que o driver crie automaticamente ao inserir o primeiro documento.
4. **Restaure pacotes**: `dotnet restore` na raiz.
5. **Executar via Aspire** (recomendado): `dotnet run --project AspireEstudo.AppHost`. O AppHost iniciara Redis, RabbitMQ, MongoDB e Seq, aplicara configuracoes e abrira os endpoints HTTP externos; a interface do Seq ficara disponivel na porta publicada (padrao http://localhost:5341).
6. **Executar ApiService isoladamente**: `dotnet run --project AspireEstudo.ApiService`. Certifique-se de que Redis, RabbitMQ, InfluxDB, MongoDB e MySQL estejam acessiveis nos endpoints definidos em `ConnectionStrings` e `Influx`/`Mongo`; ajuste `Serilog:Seq:ServerUrl` caso utilize outra instancia de Seq.
7. **Executar Web**: `dotnet run --project AspireEstudo.Web`. Ele consumira o mesmo Redis se iniciado via AppHost ou se `Redis__ConnectionString` estiver presente.

## APIs Disponiveis (ApiService)
| Metodo | Rota | Descricao |
| --- | --- | --- |
| POST | `/veiculos` | Recebe um `Veiculo` (JSON) e aciona o pipeline de escrita (Redis, RabbitMQ, InfluxDB, MongoDB, MySQL). |
| POST | `/influx/veiculos` | Persiste um `Veiculo` apenas no InfluxDB (bypass Redis/RabbitMQ/MongoDB/MySQL). |
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

#### Documento MongoDB
```json
{
  "_id": "ObjectId",
  "dataEvento": "2025-10-12T12:00:00Z",
  "vehicleId": 123,
  "placa": "ABC1234",
  "lat": -23.56,
  "lon": -46.63,
  "velocidade": 80,
  "payloadJson": "{\"dataEvento\":\"2025-10-12T12:00:00Z\",...}"
}
```
Cada envio gera um novo documento, independente de `vehicleId`, preservando historico completo.

## Observabilidade e Resiliencia
- Health checks em desenvolvimento: `/health` (prontidao) e `/alive` (liveness).
- Logs de aplicacao sao registrados via Serilog e enviados ao Seq; a UI padrao fica disponivel na porta configurada (por padrao http://localhost:5341).
- Instrumentacao OpenTelemetry configurada para logs, metricas e traces; caso `OTEL_EXPORTER_OTLP_ENDPOINT` esteja definido, exporta via OTLP automaticamente.
- `HttpClient` configurados via `ServiceDefaults` suportam service discovery e handlers de resiliencia padrao.

## Extensoes e Proximos Passos
- Implementar paginas no front-end que consultem os endpoints e exibam dados em tempo real.
- Adicionar testes automatizados para o pipeline de ingestao e para o `VehicleInfluxService` (Flux e SQL).
- Provisionar InfluxDB via Aspire usando `builder.AddProject` ou `AddContainer` para simplificar o setup local.
- Expandir mensagens RabbitMQ para incluir headers e integracao com consumidores dedicados.
- Criar consultas especificas no MongoDB para relatorios historicos (por placa, faixa de datas ou comparativos de velocidade).














