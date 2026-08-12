# AI Model Management developer guide

This guide documents the `Opc.Ua.AI`, `Opc.Ua.AI.Server`,
`Opc.Ua.AI.Client` and `Opc.Ua.AI.Inference` package family — the .NET
implementation of the draft *OPC UA — AI Model Management and Inference*
companion specification.

> **Draft.** The namespace `http://opcfoundation.org/UA/AI/` and every NodeId
> in it are provisional. The model is neither official nor endorsed by the OPC
> Foundation until the working group publishes it.

AI Model Management publishes model sources, models, datasets, deployments and
inference methods through OPC UA. The control plane is OPC UA: clients discover
what is available, read the provenance and trust-boundary metadata, call
`Invoke` or `InvokeAsync`, and transfer large artefacts through the standard
file-transfer types.

## Packages

| Package | What it gives you | Depends on |
|---|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.AI` | Source-generated AI model — ObjectTypes, ReferenceTypes, DataTypes, enums, node states and model loader | `Opc.Ua.Core` |
| `OPCFoundation.NetStandard.Opc.Ua.AI.Server` | `AiNodeManager`, `AiOptions`, fallback reporting, transfer and job support, and `AddAi` hosting extensions | `Opc.Ua.AI`, `Opc.Ua.Server`, `Opc.Ua.AI.Inference` |
| `OPCFoundation.NetStandard.Opc.Ua.AI.Client` | `AiBrowseClient`, `AiBrowseClientFactory` and `AddAiClient()` DI registration | `Opc.Ua.AI`, `Opc.Ua.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.AI.Inference` | `IInferenceBackend`, `ChatClientInferenceBackend`, `RestChatCompletionsBackend`, credential resolvers and backend options | `Opc.Ua.AI`, `Microsoft.Extensions.AI` |

The AI libraries target modern .NET TFMs used by the sample (`net8.0`,
`net9.0` and `net10.0`). The inference assembly intentionally has no Azure,
OpenAI or other vendor SDK dependency.

## Model

The Server publishes one AI root below the Server object. Under it are model
sources, deployments, models, datasets and jobs. A deployment describes where
inference runs, whether egress is permitted, whether input may be retained, the
maximum inline payload size, and the model source it uses.

Two properties are especially important:

- `ModelUsed` is returned with an inference result so a fallback cannot answer
  silently. A caller can distinguish "the primary model answered" from "a
  degraded fallback answered".
- `CredentialReference` is a name only. The credential value is resolved inside
  the Server process by an `ICredentialResolver` and is never placed in the
  address space.

Large payloads use the standard Part 5 `FileType` transfer flow. Asynchronous
inference jobs use the Part 10 program lifecycle so clients can monitor state
instead of polling a private API.

## Minimal hosted server

`AddAi` registers the node manager, options and default backend composition.
The host supplies the `IChatClient` through `IAiChatClientFactory`; that keeps
vendor packages in the host and out of `Opc.Ua.AI.Inference`.

```csharp
using Microsoft.Extensions.Hosting;
using Opc.Ua.AI.Inference;
using Opc.Ua.AI.Server;
using Opc.Ua.AI.Server.Hosting;
using Opc.Ua.Server.Fluent;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddRestChatCompletionsAiChatClientFactory();

builder.Services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "AiServer";
        options.ApplicationUri = "urn:localhost:OPCFoundation:AiServer";
        options.AutoAcceptUntrustedCertificates = true;
        options.EndpointUrls.Add("opc.tcp://localhost:62640/AiServer");
    })
    .AddAi(
        ai => builder.Configuration.GetSection(AiOptions.SectionName).Bind(ai),
        backend => builder.Configuration
            .GetSection(InferenceBackendOptions.SectionName)
            .Bind(backend),
        fallback => builder.Configuration
            .GetSection(InferenceBackendOptions.FallbackSectionName)
            .Bind(fallback));

using IHost app = builder.Build();
await app.RunAsync().ConfigureAwait(false);
```

`AddRestChatCompletionsAiChatClientFactory()` is the sample-friendly factory:
it creates an `IChatClient` over the configured OpenAI-compatible endpoint
without adding a vendor SDK. A production host can instead register its own
`IAiChatClientFactory` that creates `IChatClient` instances from Azure, OpenAI,
Ollama or an on-device runtime package.

The direct construction path remains available for hosts that do not use the
generic hosting stack:

```csharp
var backends = new InferenceBackends(primaryBackend, fallbackBackend);
var factory = new AiNodeManagerFactory(
    backends,
    Options.Create(new AiOptions()),
    Options.Create(new InferenceBackendOptions()));
```

## Hosting API

The extension method on `IOpcUaServerBuilder` is:

| Method | Purpose |
|---|---|
| `AddAi(Action<AiOptions>?, Action<InferenceBackendOptions>?, Action<InferenceBackendOptions>?)` | Registers `AiNodeManagerFactory`, `AiOptions`, primary and fallback `InferenceBackendOptions`, an `InferenceBackends` singleton, and the OPC UA node-manager registration |

`AddAi` composes the backend from `InferenceBackendOptions.Kind`:

- `ChatClient` (default) creates `ChatClientInferenceBackend` from
  `IAiChatClientFactory`.
- `RestChatCompletions` creates `RestChatCompletionsBackend` directly for
  endpoints whose wire contract is the OpenAI-compatible REST shape.

### `AiOptions`

| Property | Purpose |
|---|---|
| `PrimaryDeploymentId` / `FallbackDeploymentId` | Deployment identifiers published in the address space |
| `EnableFallback` | Publishes the fallback deployment and `FallsBackTo` reference |
| `EnableCatalogue` | Publishes catalogue and import-job nodes |
| `EnableLearningLoop` | Publishes a `LearningJobType` node for ground-truth sample accounting |
| `TransferExpiry`, `MaxTransferSize`, `MaxConcurrentTransfers`, `TransferInferenceTimeout` | Bounds for chunked transfers |
| `AsyncInferenceDelay`, `MaxRetainedJobs` | Bounds and timing for asynchronous inference jobs |
| `SourceId` | Identifier of the model source |

When `EnableLearningLoop` is true, `AiNodeManager` publishes one
`LearningJobType` under `LearningJobs`. Host-level coordinators report
ground-truth corrections through `RecordLearningSampleAsync(sampleId,
sampleKind)`. The stable `sampleId` makes retries idempotent, and
`AiLearningSampleKind.Negative` counts empty or retracted observations exactly
as positive examples count.

### `InferenceBackendOptions`

| Property | Purpose |
|---|---|
| `Enabled` | Enables the backend; most useful for disabling fallback |
| `Kind` | `ChatClient` or `RestChatCompletions` |
| `EndpointUri`, `ChatCompletionsPath`, `ProbePath` | Endpoint and paths for REST-shaped clients |
| `Authentication`, `CredentialReference`, `ApiKeyHeader`, `CredentialDirectory`, `TokenAudience` | Server-to-backend authentication |
| `Site`, `DataJurisdiction`, `EgressPermitted`, `RetainsInput` | Trust-boundary metadata published to clients |
| `MaxInlinePayloadSize` | Inline `Invoke` payload limit |
| `Models` | Configured model catalogue entries |

## Client surface

`AddAiClient()` mirrors the other companion-family client extensions. It
registers an `AiBrowseClientFactory` and a
`Func<CancellationToken, Task<AiBrowseClient?>>` over the managed session.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.AI.Client;
using Opc.Ua.Client;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOpcUa()
    .AddClient(options =>
    {
        options.ApplicationName = "AiClient";
        options.ApplicationUri = "urn:localhost:OPCFoundation:AiClient";
        options.AutoAcceptUntrustedCertificates = true;
    })
    .AddDiscoveryAndConnect(options =>
    {
        options.DiscoveryUrl = "opc.tcp://localhost:62640/ModelManagementServer";
        options.SecurityMode = MessageSecurityMode.SignAndEncrypt;
        options.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
    })
    .AddAiClient();

using IHost app = builder.Build();
await app.StartAsync().ConfigureAwait(false);

var createClient = app.Services
    .GetRequiredService<Func<CancellationToken, Task<AiBrowseClient?>>>();
AiBrowseClient? client = await createClient(CancellationToken.None)
    .ConfigureAwait(false);

if (client is null)
{
    Console.WriteLine("The Server does not implement AI Model Management.");
}
```

For one-off code, `AiBrowseClient.TryCreate(session)` is the direct fallback
when you already own an `ISession`.

## Inference backends

`IInferenceBackend` is the server-side contract: list models, invoke a model
and probe reachability. Two implementations ship:

- `ChatClientInferenceBackend` wraps `Microsoft.Extensions.AI.IChatClient`.
  This is the default because it lets the host choose any SDK or local runtime
  that implements the abstraction without changing the OPC UA address space.
- `RestChatCompletionsBackend` speaks the OpenAI-compatible REST
  chat-completions contract directly. Use it when that REST shape is the actual
  wire contract and no `IChatClient` is available.

Both hosted and on-device deployments use the same OPC UA nodes. The difference
is configuration: endpoint, credentials, data jurisdiction and egress.

## Example

The sample in
[`samples/AI/ModelManagementServer`](../samples/AI/ModelManagementServer) hosts
the node manager with `AddAi`. By default it uses the `ChatClient` path and the
sample composition root supplies a small `IChatClient` over the
OpenAI-compatible endpoint. `verify_backend.py` is a throwaway endpoint that
speaks enough of that contract for local validation:

```powershell
python samples/AI/verify_backend.py 5273
dotnet run --project samples/AI/ModelManagementServer
dotnet run --project samples/AI/ModelManagementClient
```

Set `InferenceBackend__Kind=RestChatCompletions` when testing an endpoint that
must be reached through the REST backend directly. Configure
`FallbackInferenceBackend__Kind` independently when the fallback uses a
different wire contract from the primary.

## Limitations

- The companion specification is a draft, so namespace URIs and NodeIds can
  change.
- The sample publishes a real `LearningJobType` instance and a real
  `SamplesCollected` counter. Host-level code can call the server-side
  accounting API when ground-truth corrections arrive, including empty or
  retracted observations. Retraining, candidate generation and promotion are
  deliberately not simulated.
- `IChatClient` has no standard model-enumeration method, so hosts that need a
  catalogue should configure `InferenceBackendOptions.Models`.
- The libraries do not reference vendor SDKs. If a provider package is needed,
  add it in the hosting application and expose it through `IAiChatClientFactory`.
- Native AOT is disabled for the AI inference/sample projects because the
  `Microsoft.Extensions.AI` ecosystem uses reflection in areas this repository
  builds with warnings as errors.

## See also

- [AI sample README](../samples/AI/README.md)
- [Developer guide](DeveloperGuide.md)
- [Vision developer guide](Vision.md)
- [Robotics developer guide](Robotics.md)
