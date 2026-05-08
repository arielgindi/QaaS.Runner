# CLAUDE.md — QaaS.Runner Solution

## Build & Test

```bash
# Build entire solution
dotnet build QaaS.Runner.sln

# Run all unit tests
dotnet test QaaS.Runner.sln

# Run a specific test project
dotnet test QaaS.Runner.Sessions.Tests

# Run tests matching a filter
dotnet test QaaS.Runner.Sessions.Tests --filter "TransactionTests"

# Build/test a single project
dotnet build QaaS.Runner.Sessions/QaaS.Runner.Sessions.csproj
dotnet test QaaS.Runner.Sessions.Tests/QaaS.Runner.Sessions.Tests.csproj
```

## Solution Overview

**QaaS.Runner** is the execution orchestration engine for QaaS test workflows. It reads YAML configuration (`test.qaas.yaml`) and executes sessions containing actions (publishers, consumers, transactions, probes, collectors, mocker commands) against various protocols (HTTP, gRPC, Kafka, RabbitMQ, SQL, Redis, Elastic, S3, SFTP, Socket, MongoDB).

## Project Structure

| Project | Purpose |
|---|---|
| `QaaS.Runner` | CLI entrypoint, bootstrap, YAML loading, execution builder & runner orchestration |
| `QaaS.Runner.Sessions` | Session runtime: staged actions (publishers, consumers, transactions, probes, collectors), builders, configuration objects |
| `QaaS.Runner.Assertions` | Assertion engine: builds assertion objects from hooks, executes against session outputs, writes Allure reports |
| `QaaS.Runner.Storage` | Storage abstraction for session data (filesystem, S3) |
| `QaaS.Runner.Infrastructure` | Shared helpers: template rendering, datetime/timezone, filesystem extensions |
| `QaaS.Runner.*.Tests` | Unit tests for each project above (NUnit + Moq) |
| `QaaS.Runner.E2ETests` | End-to-end integration tests with real YAML configs |

## Architecture — Session Action Pipeline

1. **YAML config** → deserialized into **Builder** objects (e.g., `TransactionBuilder`, `PublisherBuilder`)
2. Builders `.Build()` into **runtime action** objects (e.g., `Transaction`, `Publisher`)
3. Actions are grouped into **Stages** by stage number
4. Each **Session** runs its stages sequentially; within a stage, all actions run **concurrently** via `Task.Run`
5. Action results are collected into `SessionData` (inputs/outputs)

## Key Patterns

### Fluent Builder API
All action builders follow a fluent API pattern with `[Description]`, `[Required]`, `[DefaultValue]` attributes for both YAML deserialization and programmatic "Configuration as Code" usage. Example:
```csharp
new TransactionBuilder()
    .Named("my-transaction")
    .WithTimeout(5000)
    .AddDataSource("source-a")
    .Configure(new HttpTransactorConfig { ... })
    .WithParallelism(10)
    .Build(context, failures, sessionName);
```

### Parallelism (Publisher Pattern)
Publishers support concurrent data source processing via:
- `Parallel` config object with `Parallelism` property (YAML: `Parallel.Parallelism`)
- `SemaphoreSlim` for concurrency throttling
- `IterableSerializableDataIterator.ApplyToAll(data, action, parallel: true)` which calls `Parallel.ForEach`
- Thread-safe `LogData` with `lock` on shared lists

Transactions follow the same parallelism pattern.

### Data Flow
- **DataSources** → generators produce `IEnumerable<Data<object>>`
- **IterableSerializableDataIterator** wraps the enumerable, serializes items, and tracks original items
- Actions iterate using `IterateWithOriginal()` or `IterateEnumerable()` then process via protocol

### Running Communication Data (RCD)
Live action data is exported to `InternalContext` via `RunningCommunicationData<object>` so other actions and systems can observe data in real-time during session execution.

### Policies
Actions support policy chains (`Policy` base class) for flow control:
- `CountPolicy` — stop after N items
- `LoadBalancePolicy` — rate limiting
- Policies are chained via `.Add()` and evaluated with `RunChain()`

## Conventions

- **Target framework**: .NET 10.0
- **Test framework**: NUnit 4.x + Moq
- **Logging**: Microsoft.Extensions.Logging (Serilog at runtime)
- **Namespace ↔ folder**: Strict alignment (e.g., `QaaS.Runner.Sessions.Actions.Transactions`)
- **Builder partial classes**: Properties in `*Properties.cs`, logic/fluent API in `*Logic.cs`, validation in `*Validation.cs`
- **`internal` visibility**: Builders expose `.Build()` as `internal`; test projects access via `InternalsVisibleTo`
- **XML doc comments**: All public builder methods have `<summary>`, `<remarks>`, and `<qaas-docs>` tags
- **`StopActionException`**: Used inside `ApplyToAll` parallel loops to short-circuit on policy failure

## YAML Configuration Example

```yaml
Sessions:
  - Name: MySession
    Publishers:
      - Name: PublishData
        DataSourceNames: [Source1]
        Parallel:
          Parallelism: 10
        RabbitMq:
          Host: localhost
    Transactions:
      - Name: TransactData
        DataSourceNames: [Source1]
        TimeoutMs: 5000
        Parallel:
          Parallelism: 10
        Http:
          BaseAddress: https://api.example.com
          Method: Post
```
