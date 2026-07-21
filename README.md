# ArbitrageScanner

The core arbitrage detection engine of the ArbiScanner platform. It runs as a .NET 9 background worker service that continuously scans 12+ cryptocurrency exchanges for price discrepancies, stores discovered opportunities in MongoDB, and publishes them to RabbitMQ for consumption by the Web API and Telegram Notifier.

---

## Table of Contents

- [Overview](#overview)
- [How Arbitrage Detection Works](#how-arbitrage-detection-works)
- [Arbitrage Strategies](#arbitrage-strategies)
  - [Futures Arbitrage](#futures-arbitrage)
  - [Funding Rate Arbitrage](#funding-rate-arbitrage)
  - [Spot-Futures Arbitrage](#spot-futures-arbitrage)
- [Architecture](#architecture)
  - [Project Layers](#project-layers)
- [Supported Exchanges](#supported-exchanges)
- [Technologies](#technologies)
- [Configuration Reference](#configuration-reference)
- [Environment Variables](#environment-variables)
- [Multi-Node Sharding](#multi-node-sharding)
- [Docker](#docker)
- [Local Development](#local-development)
- [Testing](#testing)
- [Project Structure](#project-structure)

---

## Overview

ArbitrageScanner continuously monitors cryptocurrency markets across multiple exchanges, looking for situations where the same asset trades at meaningfully different prices. When a discrepancy exceeds the configured threshold, the engine records the opportunity, tracks its evolution, and publishes a structured event when the spread closes so downstream consumers can act on it.

The engine implements three distinct arbitrage strategies: cross-exchange futures price divergence, funding rate basis trades, and spot-versus-futures price spreads. All three run concurrently in a single background worker process.

---

## How Arbitrage Detection Works

The detection loop follows these steps on every cycle:

1. **Exchange initialisation.** All exchanges in `ExchangeList` are initialised via ccxt. Swap (perpetual) and spot markets are loaded for each exchange and stored in `ExchangeRegistry`.

2. **Symbol universe construction.** The engine finds all trading pairs that appear on at least five exchanges simultaneously. This ensures every candidate has enough competing venues to produce meaningful spread comparisons. The resulting list is the shared symbol universe for this cycle.

3. **Parallel per-symbol processing.** The symbol list is divided across `ThreadCount` threads (and optionally across multiple container instances — see [Multi-Node Sharding](#multi-node-sharding)). For every (exchange, symbol) pair the engine fetches:
   - The current ticker (best bid and best ask)
   - The Level 2 order book (to verify liquidity)
   - The funding rate (for perpetual contracts)

   This data is assembled into a `CoinDataModel` and passed to the three strategy calculators.

4. **Strategy calculation.** Each calculator inspects the `CoinDataModel` for its exchange and compares prices, funding rates, or spot-futures relationships across all available venues. When the spread exceeds the configured minimum, a `TradeOpportunityModel` is created and stored in the in-memory watch list (`StrategyWatchListService`).

5. **Observer monitoring.** Three observer services run concurrently and watch open positions. Each tick, every open position's combine key is dispatched to its own fire-and-forget re-check task (bounded by a `SemaphoreSlim(5, 5)`) instead of the loop awaiting the whole watchlist as one batch; a `ConcurrentDictionary<string, byte>` of in-flight keys prevents a position from being scheduled twice while its previous re-check is still running. This means one slow or stuck (exchange, symbol) lookup no longer stalls re-checks of the rest of the watchlist on the next tick. When a spread falls below `KeepWatchingSpread`%, the position is considered closed: the final result is persisted to the MongoDB `SpreadsTicker` collection and a protobuf-serialised `TradeOpportunityModel` is published to the RabbitMQ fanout exchange. If the re-check can't produce a valid result (e.g. a funding rate or ticker fetch failed), the observer leaves the position in the watch list rather than publishing a partially-populated update.

6. **Downstream consumption.** The Web API and Telegram Notifier subscribe to RabbitMQ and receive completed opportunity events in real time.

---

## Arbitrage Strategies

### Futures Arbitrage

**What it detects:** The same perpetual swap contract (e.g. BTC/USDT:USDT) trading at a price difference of at least `SpreadSize`% across two or more exchanges simultaneously.

**How it works:**
- `FuturesPositionCalculatorService` iterates over every exchange pair for a given symbol and computes the relative price spread between the best ask on the cheaper venue and the best bid on the more expensive venue.
- Before recording an opportunity, it checks that the order book on both sides has enough liquidity to fill a position of `PositionSize` USD (with a 5× safety buffer, i.e. `PositionSize × 5`).
- Valid opportunities are stored in the futures watch list.

**How it closes:** `FuturesObserverService` re-evaluates the spread on each cycle. When the spread drops below `KeepWatchingSpread`%, the position is marked closed and published downstream.

---

### Funding Rate Arbitrage

**What it detects:** Perpetual contracts where the funding rate spread between two exchanges is large enough to make a delta-neutral basis trade attractive.

**How it works:**
- `FundingPositionCalculatorService` fetches the funding rate for each (exchange, symbol) pair.
- It computes the rate differential between exchanges. If the differential exceeds `FundingThresholdRatio`, one side pays a significant periodic funding fee while the other collects it — creating a yield without net directional exposure.
- Valid opportunities are stored in the funding watch list.

**How it closes:** `FundingObserverService` monitors the rate differential. When it falls below the threshold, the opportunity is finalised and published.

---

### Spot-Futures Arbitrage

**What it detects:** A situation where the spot price of an asset on one or more exchanges differs significantly from the perpetual futures price on the same or a different exchange.

**How it works:**
- `SpotPositionCalculatorService` compares the best spot ask with the best futures bid (and vice versa) across all venue combinations for a given symbol.
- A premium of at least `SpreadSize`% on either side triggers recording an opportunity.
- Liquidity constraints from the order books on both the spot and futures legs are verified before accepting the opportunity.

**How it closes:** `SpotObserverService` tracks the spot-futures basis. When it collapses, the position is finalised.

---

## Architecture

ArbitrageScanner is built on Clean Architecture principles. Business logic and domain types live in layers that have no dependency on infrastructure. Infrastructure implementations depend only on domain interfaces. The worker host wires everything together through dependency injection.

### Project Layers

#### ArbitrageScanner.Domain

The pure domain layer. Contains all models and interfaces; has no dependency on databases, message brokers, or any external service.

**Models:**
- `CoinDataModel` — ticker and order book data for a single (exchange, symbol) snapshot
- `ConfigModel` — strongly typed representation of the `Arbitrage` configuration section
- `ExchangeRateModel` — wraps ccxt exchange and market types
- `TradeOpportunityModel` — the central result type; protobuf-serialisable for RabbitMQ transport
- `TradeOpportunityTickerModel`, `FundingTradeOpportunityTickerModel` — strategy-specific ticker variants
- `LogErrorModel` — structured error record persisted to MongoDB
- `ProxyModel` — proxy endpoint descriptor

**Interfaces:**
- `IProxyService` — abstracts the rotating proxy pool
- `IServicesCommunicationService` — abstracts RabbitMQ publishing
- `ITelegramNotifierService` — abstracts direct Telegram alert delivery
- `ITradeOpportunityRepository` — abstracts persistence of trade opportunity records

**NuGet packages:** ccxt (exchange market types), protobuf-net, MongoDB.Bson, Google.Protobuf

---

#### ArbitrageScanner.Infrastructure

Concrete implementations of all domain interfaces plus supporting services.

| Class | Responsibility |
|---|---|
| `ExchangeService` | Wraps a ccxt exchange instance; loads swap/spot markets; fetches tickers, order books, and funding rates; calculates available liquidity |
| `ExchangePairService` | Abstract base for cross-exchange pair analysis; provides static liquidity calculation and `RoundToStep` |
| `ExchangeRegistry` | Thread-safe concurrent registry of all initialised exchange instances and their loaded markets |
| `DataService` | Central singleton data hub: exchange maps, per-strategy watch lists, proxy pool, MongoDB queries |
| `MongoService` | MongoDB CRUD operations for spreads, tickers, errors, proxies, and active positions |
| `TradeOpportunityRepositoryMongo` | `ITradeOpportunityRepository` backed by MongoDB |
| `ConfigService` | Loads `ConfigModel` from `appsettings.json` with env var overrides |
| `ProxyService` / `ProxyPool` | Rotating HTTP proxy pool; distributes outbound exchange requests across configured proxies |
| `TelegramNotifierService` | Sends alert messages directly via the Telegram Bot HTTP API |
| `ServicesCommunicationService` | Serialises `TradeOpportunityModel` with protobuf and publishes to a RabbitMQ fanout exchange |
| `UserInterfaceService` | Console logging helper with structured output |
| `StrategyWatchListService` | Thread-safe `ConcurrentDictionary<string, TradeOpportunityModel>` per strategy type |
| `ConfigurationExtensions` | `GetArbitrageConfig()` extension on `IConfiguration`; reads env var overrides |
| `TaskExtensions` | `FireAndForgetWithLogging()` extension for background tasks |
| `RateLimiter`, `Utils` | Per-exchange rate limiting and collection utilities (shuffle) |

**NuGet packages:** Azure.Storage.Blobs, Enums.NET, EPPlus, Microsoft.Extensions.Configuration.Binder, MongoDB.Driver 3.3, RabbitMQ.Client 7.1

---

#### ArbitrageScanner.Futures

Implements the futures cross-exchange arbitrage strategy.

- `FuturesPositionCalculatorService` — detects spreads between perpetual swap prices across exchanges
- `FuturesObserverService` — monitors open futures positions and closes them when the spread collapses

Namespace: `ArbitrageScanner.Futures.Services`

---

#### ArbitrageScanner.Funding

Implements the funding rate basis arbitrage strategy.

- `FundingPositionCalculatorService` — detects funding rate differentials across exchanges
- `FundingObserverService` — monitors funding positions and closes them when the differential narrows

Namespace: `ArbitrageScanner.Funding.Services`

---

#### ArbitrageScanner.Spot

Implements the spot-futures arbitrage strategy.

- `SpotPositionCalculatorService` — detects spot/futures price premiums across venues
- `SpotObserverService` — monitors spot positions and closes them when the basis collapses

Namespace: `ArbitrageScanner.Spot.Services`

---

#### ArbitrageScanner.Worker

The host entry point and main orchestration layer.

- **`Program.cs`** — registers all services in the DI container and sets up the `IHostedService`
- **`ArbitrageWorker`** — `BackgroundService` that calls `ArbitrageService.StartOperation()` on startup; the container is restarted every 4 hours (see [Docker](#docker))
- **`ArbitrageService`** — main orchestration loop: initialises exchanges, loads markets and proxies, restores active positions from MongoDB, starts all three observer services, then enters the parallel processing loop over the shared symbol universe
- **`ArbitrageStrategyOrchestrator`** — per-symbol dispatcher; runs `FindAndComputeFuturesPositions`, `FindAndComputeFundingPositions`, and `FindAndComputeSpotPositions` concurrently for each `CoinDataModel`

---

## Supported Exchanges

The following exchanges are supported and configurable via `ExchangeList`:

| Exchange | ccxt ID |
|---|---|
| Binance | `Binance` |
| Bybit | `Bybit` |
| MEXC | `MEXC` |
| OKX | `OKX` |
| HTX | `HTX` |
| CoinEX | `CoinEX` |
| KuCoin Futures | `kucoinfutures` |
| BingX | `BingX` |
| Gate.io | `Gateio` |
| XT | `XT` |
| LBank | `LBank` |
| WhiteBit | `WhiteBit` |

Exchanges in `WeakExchangeList` are treated as less reliable venues. The engine may apply stricter liquidity requirements or lower trust weighting for opportunities that include only weak exchanges on one side.

---

## Technologies

### ccxt

[ccxt](https://github.com/ccxt/ccxt) (CryptoCurrency eXchange Trading Library) is the unified exchange abstraction layer. It provides a single consistent API for loading markets, fetching tickers, retrieving order books, and reading funding rates across all supported exchanges. Without ccxt, each exchange would require a bespoke API client. The .NET port of ccxt is used here.

### Other key technologies

| Technology | Role |
|---|---|
| .NET 9 | Runtime and worker host |
| MongoDB / MongoDB.Driver 3.3 | Persistence for spread results, tickers, errors, positions, and proxies |
| RabbitMQ / RabbitMQ.Client 7.1 | Message bus for publishing closed trade opportunities to downstream consumers |
| protobuf-net / Google.Protobuf | Binary serialisation of `TradeOpportunityModel` for efficient RabbitMQ transport |
| Microsoft.Extensions.Hosting | `BackgroundService` host, dependency injection, configuration |
| EPPlus | Excel export support for opportunity data |
| Azure.Storage.Blobs | Optional blob storage for export artefacts |
| OpenTelemetry SDK | Distributed tracing (HTTP client, RabbitMQ, MongoDB spans) |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | OTLP gRPC export of traces to Grafana Tempo |
| OpenTelemetry.Exporter.Prometheus.HttpListener | Standalone `/metrics` HTTP server on port 8085 for Prometheus scraping |

---

## Configuration Reference

Configuration lives in `ArbitrageScanner.Worker/appsettings.json` under the `Arbitrage` key. Every value can be overridden with an environment variable using the `Arbitrage__<Key>` naming convention (double underscore as section separator).

```json
{
  "OpenTelemetry": {
    "Endpoint": "http://localhost:4317"
  },
  "Arbitrage": {
    "SpreadSize": 1.0,
    "PositionSize": 600,
    "KeepWatchingSpread": 0.2,
    "ThreadCount": 3,
    "FundingThresholdRatio": 0.2,
    "TelegramToken": "...",
    "ChatId": "...",
    "Futures": true,
    "Funding": true,
    "Spot": true,
    "ExchangeList": ["Binance", "Bybit", "MEXC", ...],
    "WeakExchangeList": ["KuCoin Futures", "MEXC Global", "LBank", "BingX"],
    "ProxyList": [...]
  }
}
```

| Key | Type | Default | Description |
|---|---|---|---|
| `SpreadSize` | `double` | `1.0` | Minimum spread percentage to record a futures or spot opportunity. An opportunity is created only when the price difference between two venues is at least this percentage. |
| `PositionSize` | `double` | `600` | Notional USD position size used for liquidity verification. The order book on each leg must support at least `PositionSize × 5` USD of volume before an opportunity is accepted. |
| `KeepWatchingSpread` | `double` | `0.2` | An open position is closed and published when the spread falls below this percentage. Setting it close to zero means the engine waits for full convergence; higher values exit earlier. |
| `ThreadCount` | `int` | `3` | Number of parallel threads used to process the symbol universe. Increase for faster cycles on machines with more cores. |
| `FundingThresholdRatio` | `double` | `0.2` | Minimum funding rate differential (expressed as a ratio, not a percentage) required to record a funding rate opportunity. |
| `TelegramToken` | `string` | — | Telegram Bot API token for direct alert delivery. Used by `TelegramNotifierService`. |
| `ChatId` | `string` | — | Telegram chat or group ID where direct alerts are sent. |
| `Futures` | `bool` | `true` | Enable or disable the futures cross-exchange arbitrage strategy entirely. |
| `Funding` | `bool` | `true` | Enable or disable the funding rate arbitrage strategy entirely. |
| `Spot` | `bool` | `true` | Enable or disable the spot-futures arbitrage strategy entirely. |
| `ExchangeList` | `string[]` | see above | List of exchange identifiers to initialise and scan. Must match the ccxt exchange class name used in the infrastructure layer. |
| `WeakExchangeList` | `string[]` | see above | Subset of exchanges treated as less reliable. Opportunities where a weak exchange is the only counterparty may be filtered or flagged accordingly. |
| `ProxyList` | `ProxyModel[]` | — | List of HTTP proxies for outbound exchange requests. Each entry requires `ip`, `port`, `username`, `password`, and optionally `country_code`. The proxy pool rotates across entries to avoid rate limiting. |

---

## Environment Variables

Environment variables override the corresponding `appsettings.json` values. Use these in Docker and CI environments to avoid embedding secrets in configuration files.

| Variable | Required | Description |
|---|---|---|
| `MongoDb_ConnectionString` | Yes | Full MongoDB connection URI (e.g. `mongodb://user:pass@host:27017`). |
| `MongoDb_DatabaseName` | No | MongoDB database name. Defaults to `SwapArbitrage`. |
| `RABBITMQ_HOST` | Yes | Hostname or IP of the RabbitMQ broker. Used when constructing the AMQP connection. |
| `TELEGRAM_TOKEN` | No | Overrides `Arbitrage.TelegramToken` from appsettings. |
| `TELEGRAM_CHAT_ID` | No | Overrides `Arbitrage.ChatId` from appsettings. |
| `OpenTelemetry__Endpoint` | No | OTLP gRPC endpoint for Grafana Tempo (e.g. `http://tempo:4317`). Defaults to `http://localhost:4317` from `appsettings.json`. |
| `NODE_TOTAL` | No | Total number of scanner instances in a sharded deployment. Defaults to `1` (no sharding). See [Multi-Node Sharding](#multi-node-sharding). |
| `NODE_INDEX` | No | Zero-based index of this instance in a sharded deployment. Must be in the range `0..NODE_TOTAL-1`. |
| `Arbitrage__SpreadSize` | No | Overrides `Arbitrage.SpreadSize`. |
| `Arbitrage__PositionSize` | No | Overrides `Arbitrage.PositionSize`. |
| `Arbitrage__KeepWatchingSpread` | No | Overrides `Arbitrage.KeepWatchingSpread`. |
| `Arbitrage__ThreadCount` | No | Overrides `Arbitrage.ThreadCount`. |
| `Arbitrage__FundingThresholdRatio` | No | Overrides `Arbitrage.FundingThresholdRatio`. |
| `Arbitrage__Futures` | No | Overrides `Arbitrage.Futures`. |
| `Arbitrage__Funding` | No | Overrides `Arbitrage.Funding`. |
| `Arbitrage__Spot` | No | Overrides `Arbitrage.Spot`. |

Any key in the `Arbitrage` configuration section can be overridden using the `Arbitrage__<Key>` pattern (ASP.NET Core double-underscore convention for nested sections).

---

## Multi-Node Sharding

The engine supports horizontal scaling by partitioning the symbol universe across multiple container instances. This reduces the per-instance workload and allows more frequent scan cycles when the symbol list is large.

**How it works:**

When `NODE_TOTAL` is set to a value greater than `1`, `ArbitrageService` partitions the full symbol list so that each node receives a non-overlapping slice:

- Node 0 processes symbols at indices `0, N, 2N, ...`
- Node 1 processes symbols at indices `1, N+1, 2N+1, ...`
- Node k processes symbols at indices `k, N+k, 2N+k, ...`

Each node independently initialises all exchanges and fetches data only for its assigned symbols. All nodes write to the same MongoDB database and publish to the same RabbitMQ exchange, so the downstream consumers see the complete opportunity stream regardless of how many nodes are running.

**Example docker-compose configuration for three nodes:**

```yaml
services:
  scanner-0:
    image: arbitrage-scanner:latest
    environment:
      - NODE_TOTAL=3
      - NODE_INDEX=0
      - MongoDb_ConnectionString=${MONGO_DB_CONNECTION_STRING}
      - RABBITMQ_HOST=rabbitmq

  scanner-1:
    image: arbitrage-scanner:latest
    environment:
      - NODE_TOTAL=3
      - NODE_INDEX=1
      - MongoDb_ConnectionString=${MONGO_DB_CONNECTION_STRING}
      - RABBITMQ_HOST=rabbitmq

  scanner-2:
    image: arbitrage-scanner:latest
    environment:
      - NODE_TOTAL=3
      - NODE_INDEX=2
      - MongoDb_ConnectionString=${MONGO_DB_CONNECTION_STRING}
      - RABBITMQ_HOST=rabbitmq
```

---

## Docker

### Build

The Dockerfile is a multi-stage build located at `ArbitrageScanner.Worker/Dockerfile`. The build context is the `ArbitrageScanner/` directory.

```bash
docker build -f ArbitrageScanner.Worker/Dockerfile -t arbitrage-scanner:latest .
```

From the repository root:

```bash
docker build -f ArbitrageScanner/ArbitrageScanner.Worker/Dockerfile \
  -t arbitrage-scanner:latest \
  ArbitrageScanner/
```

Base runtime image: `mcr.microsoft.com/dotnet/aspnet:9.0` (upgraded from the plain `runtime:9.0` image so the `/health` endpoint, which needs the ASP.NET Core shared framework, actually has something to run on)

### Retired: the 4-hour forced restart

Earlier revisions ran the worker under a hard 4-hour wall-clock `timeout`, paired with `restart: unless-stopped`, to force-recycle the process — undocumented as a workaround, but effectively one. See `docs/investigations/scanner-memory-leak.md` for the investigation: a concrete leak candidate in `ProxyService`'s per-rotation `HttpClient` churn was found and fixed (it now reuses one handler per exchange instead of creating a new one on every proxy rotation — see `RotatingWebProxy`), but that specific mechanism did **not** reproduce as an unbounded leak when tested in isolation. The restart hack was removed on the basis of that fix rather than a confirmed root cause; if the scanner's memory/handle usage still grows unbounded over a multi-hour run, that investigation doc is the place to pick the thread back up — the candidates it lists as not yet ruled out are the next things to check.

### docker-compose

```yaml
services:
  arbitragebusiness:
    image: arbitrage-scanner:latest
    build:
      context: ArbitrageScanner/
      dockerfile: ArbitrageScanner.Worker/Dockerfile
    restart: unless-stopped
    ports:
      - "8086:8085"   # Prometheus metrics scrape endpoint
    environment:
      - MongoDb_ConnectionString=${MONGO_DB_CONNECTION_STRING}
      - RABBITMQ_HOST=rabbitmq
      - OpenTelemetry__Endpoint=http://tempo:4317
    depends_on:
      rabbitmq:
        condition: service_healthy

  rabbitmq:
    image: rabbitmq:3-management-alpine
    restart: unless-stopped
    ports:
      - "5672:5672"
      - "15672:15672"
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5
```

---

## Local Development

### Prerequisites

- .NET 9 SDK
- MongoDB instance (local or remote)
- RabbitMQ instance (optional; only required if you need the publishing pipeline; the engine logs errors and continues if RabbitMQ is unavailable)

### Running

```bash
cd ArbitrageScanner/ArbitrageScanner.Worker
dotnet run
```

Configuration is loaded from `appsettings.json`. Override any value with environment variables before running:

```bash
export MongoDb_ConnectionString="mongodb://localhost:27017"
export RABBITMQ_HOST="localhost"
dotnet run
```

### Building

```bash
cd ArbitrageScanner
dotnet build ArbitrageScanner.sln
```

### Running tests

```bash
dotnet test ArbitrageScanner.sln
```

See [Testing](#testing) for what each test project covers and how to run them in isolation.

---

## Testing

Two dedicated test projects were added alongside the strategy/observer refactor described above.

### ArbitrageScanner.Tests (unit)

Pure unit tests (xUnit + FluentAssertions) covering the calculation and formatting logic shared by the three strategies — no MongoDB, RabbitMQ, or network calls:

| File | Coverage |
|---|---|
| `SpreadCalculationTests` | Futures/spot spread percentage math, basis (mark vs. index) calculation |
| `SlippageCalculationTests` | Order-book walking to fill a target size, weighted-average fill price, empty/insufficient-liquidity error paths |
| `FundingCalculationTests` | Funding rate differential and long/short side selection |
| `FormatOrdersToSendTests` | Mapping raw order book levels into the ask/bid lists attached to a `TradeOpportunityModel` |
| `IntervalParsingTests` | `FundingObserverService.ParseInterval` (funding interval strings like `8h`, `4h30m`) and `GetNextPayoutUtc` boundary calculation |
| `DeepCloneTests` | Deep-clone correctness for `TradeOpportunityModel`, including independence of nested `ExchangeRateModel` lists |

`FundingObserverService.ParseInterval` is `internal`; `ArbitrageScanner.Funding.csproj` grants `ArbitrageScanner.Tests` access via `InternalsVisibleTo` so it can be unit tested without making it part of the public API.

```bash
dotnet test ArbitrageScanner.Tests/ArbitrageScanner.Tests.csproj
```

### ArbitrageScanner.IntegrationTests

Testcontainers-backed tests that exercise the real MongoDB and RabbitMQ integration paths. **Docker must be running locally** — each test class spins up disposable containers via a shared fixture.

| File | Coverage |
|---|---|
| `Mongo/TradeOpportunityRepositoryMongoTests` | Round-trips found spreads, spread tickers, spot spreads/tickers, funding spreads, error logs, and proxy documents through the real Mongo driver against a `Testcontainers.MongoDb` instance |
| `RabbitMq/SpreadFanoutContractTests` | Publishes a `TradeOpportunityModel` to the fanout exchange and asserts it is delivered to both the `spread_api` and `spread_telegram` queues, and that all protobuf fields round-trip faithfully — guards the exact contract the Web API and Telegram Notifier depend on |

```bash
dotnet test ArbitrageScanner.IntegrationTests/ArbitrageScanner.IntegrationTests.csproj
```

---

## Project Structure

```
ArbitrageScanner/
├── ArbitrageScanner.Domain/
│   ├── Models/
│   │   ├── CoinDataModel.cs
│   │   ├── ConfigModel.cs
│   │   ├── ExchangeRateModel.cs
│   │   ├── TradeOpportunityModel.cs
│   │   ├── TradeOpportunityTickerModel.cs
│   │   ├── FundingTradeOpportunityTickerModel.cs
│   │   ├── LogErrorModel.cs
│   │   └── ProxyModel.cs
│   └── Interfaces/
│       ├── IProxyService.cs
│       ├── IServicesCommunicationService.cs
│       ├── ITelegramNotifierService.cs
│       └── ITradeOpportunityRepository.cs
├── ArbitrageScanner.Infrastructure/
│   ├── Services/
│   │   ├── ExchangeService.cs
│   │   ├── ExchangePairService.cs
│   │   ├── ExchangeRegistry.cs
│   │   ├── DataService.cs
│   │   ├── MongoService.cs
│   │   ├── ConfigService.cs
│   │   ├── ProxyService.cs
│   │   ├── ProxyPool.cs
│   │   ├── TelegramNotifierService.cs
│   │   ├── ServicesCommunicationService.cs
│   │   ├── UserInterfaceService.cs
│   │   └── StrategyWatchListService.cs
│   ├── Repositories/
│   │   └── TradeOpportunityRepositoryMongo.cs
│   ├── Extensions/
│   │   ├── ConfigurationExtensions.cs
│   │   └── TaskExtensions.cs
│   └── Common/
│       ├── RateLimiter.cs
│       └── Utils.cs
├── ArbitrageScanner.Futures/
│   └── Services/
│       ├── FuturesPositionCalculatorService.cs
│       └── FuturesObserverService.cs
├── ArbitrageScanner.Funding/
│   └── Services/
│       ├── FundingPositionCalculatorService.cs
│       └── FundingObserverService.cs
├── ArbitrageScanner.Spot/
│   └── Services/
│       ├── SpotPositionCalculatorService.cs
│       └── SpotObserverService.cs
├── ArbitrageScanner.Worker/
│   ├── Controllers/
│   │   └── ArbitrageStrategyOrchestrator.cs
│   ├── Worker/
│   │   └── ArbitrageWorker.cs
│   ├── ArbitrageService.cs
│   ├── Program.cs
│   ├── appsettings.json
│   ├── launchSettings.json     # Docker Compose launch profile (Visual Studio)
│   └── Dockerfile
├── ArbitrageScanner.Tests/              # Unit tests — see Testing
│   ├── DeepCloneTests.cs
│   ├── FormatOrdersToSendTests.cs
│   ├── FundingCalculationTests.cs
│   ├── IntervalParsingTests.cs
│   ├── SlippageCalculationTests.cs
│   ├── SpreadCalculationTests.cs
│   └── Helpers/
│       ├── OrderBookBuilder.cs
│       └── ServiceFactory.cs
├── ArbitrageScanner.IntegrationTests/    # Testcontainers-backed tests — see Testing
│   ├── Fixtures/
│   │   ├── MongoTestFixture.cs
│   │   └── RabbitMqTestFixture.cs
│   ├── Mongo/
│   │   └── TradeOpportunityRepositoryMongoTests.cs
│   ├── RabbitMq/
│   │   └── SpreadFanoutContractTests.cs
│   └── Support/
│       ├── Images.cs
│       └── TradeOpportunityModelBuilder.cs
├── skills/
│   └── create-docker-compose.md
└── ArbitrageScanner.sln
```
