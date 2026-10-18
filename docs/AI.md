# AI Model Management developer guide

The `Opc.Ua.AI`, `Opc.Ua.AI.Server`, `Opc.Ua.AI.Client`, and
`Opc.Ua.AI.Inference` packages implement the draft
*OPC UA — AI Model Management and Inference* companion specification in .NET.

> **Draft.** The namespace `http://opcfoundation.org/UA/AI/` and every NodeId
> in it are provisional until the working group publishes the specification.

AI Model Management exposes model sources, models, datasets, deployments, and
inference methods through OPC UA. Clients can discover available resources and
read provenance and trust-boundary metadata. They can also invoke `Invoke` or
`InvokeAsync` and transfer large artefacts with standard file-transfer types.

## Contents

- [Packages](#packages)
- [Model](#model)
- [Minimal hosted server](#minimal-hosted-server)
- [Hosting API](#hosting-api)
  - [`AIOptions`](#aioptions)
  - [`InferenceBackendOptions`](#inferencebackendoptions)
- [Client surface](#client-surface)
- [Inference backends](#inference-backends)
- [Invocation inputs and parameters](#invocation-inputs-and-parameters)
- [Example](#example)
- [Limitations](#limitations)
- [See also](#see-also)

## Packages

| Package | What it gives you | Depends on |
|---|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.AI` | Source-generated AI model — ObjectTypes, ReferenceTypes, DataTypes, enums, node states and model loader | `Opc.Ua.Core` |
| `OPCFoundation.NetStandard.Opc.Ua.AI.Server` | `AINodeManager`, `AIOptions`, fallback reporting, transfer and job support, and `AddAI` hosting extensions | `Opc.Ua.AI`, `Opc.Ua.Server`, `Opc.Ua.AI.Inference` |
| `OPCFoundation.NetStandard.Opc.Ua.AI.Client` | `AIClient`, `AIClientFactory` and `AddAIClient()` DI registration | `Opc.Ua.AI`, `Opc.Ua.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.AI.Inference` | `IInferenceBackend`, `ChatClientInferenceBackend`, `RestChatCompletionsBackend`, credential resolvers and backend options | `Opc.Ua.AI`, `Microsoft.Extensions.AI` |

The AI libraries target modern .NET TFMs used by the sample (`net8.0`,
`net9.0` and `net10.0`). The inference assembly intentionally has no Azure,
OpenAI or other vendor SDK dependency.

## Model

The Server publishes one AI root below the Server object. The root contains:

- Model sources
- Deployments
- Models
- Datasets
- Jobs

Each deployment describes where inference runs and identifies its model
source. It also records egress permission, input-retention status, and the
maximum inline payload size.

Two properties are especially important:

- An inference result includes `ModelUsed`, so callers can tell whether the
  primary model or a degraded fallback answered.
- `CredentialReference` is a name only. An `ICredentialResolver` resolves the
  credential value inside the Server process. The address space contains only
  the reference name, never the credential value.

Large payloads use the standard Part 5 `FileType` transfer flow. Asynchronous
inference jobs use the Part 10 program lifecycle so clients can monitor state
instead of polling a private API.

## Minimal hosted server

`AddAI` registers the node manager, options and default backend composition.
The host supplies the `IChatClient` through `IChatClientFactory`; that keeps
vendor packages in the host and out of `Opc.Ua.AI.Inference`.

```csharp
using Microsoft.Extensions.Hosting;
using Opc.Ua.AI.Inference;
using Opc.Ua.AI.Server;
using Opc.Ua.AI.Server.Hosting;
using Opc.Ua.Server.Fluent;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddRestChatCompletionsAIChatClientFactory();

builder.Services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "AIServer";
        options.ApplicationUri = "urn:localhost:OPCFoundation:AIServer";
        options.AutoAcceptUntrustedCertificates = true;
        options.EndpointUrls.Add("opc.tcp://localhost:62640/AIServer");
    })
    .AddAI(
        ai => builder.Configuration.GetSection(AIOptions.SectionName).Bind(ai),
        backend => builder.Configuration
            .GetSection(InferenceBackendOptions.SectionName)
            .Bind(backend),
        fallback => builder.Configuration
            .GetSection(InferenceBackendOptions.FallbackSectionName)
            .Bind(fallback));

using IHost app = builder.Build();
await app.RunAsync().ConfigureAwait(false);
```

`AddRestChatCompletionsAIChatClientFactory()` is the sample-friendly factory:
it creates an `IChatClient` over the configured OpenAI-compatible endpoint
without adding a vendor SDK. A production host can instead register its own
`IChatClientFactory`. That factory can create `IChatClient` instances for
Azure, OpenAI, Ollama, or an on-device runtime.

The direct construction path remains available for hosts that do not use the
generic hosting stack:

```csharp
var backends = new InferenceBackends(primaryBackend, fallbackBackend);
var factory = new AINodeManagerFactory(
    backends,
    Options.Create(new AIOptions()),
    Options.Create(new InferenceBackendOptions()));
```

## Hosting API

The extension method on `IOpcUaServerBuilder` is:

| Method | Purpose |
|---|---|
| `AddAI(Action<AIOptions>?, Action<InferenceBackendOptions>?, Action<InferenceBackendOptions>?)` | Registers `AINodeManagerFactory`, `AIOptions`, primary and fallback `InferenceBackendOptions`, an `InferenceBackends` singleton, and the OPC UA node-manager registration |

`AddAI` composes the backend from `InferenceBackendOptions.Kind`:

- `ChatClient` (default) creates `ChatClientInferenceBackend` from
  `IChatClientFactory`.
- `RestChatCompletions` creates `RestChatCompletionsBackend` directly for
  endpoints whose wire contract is the OpenAI-compatible REST shape.

### `AIOptions`

| Property | Purpose |
|---|---|
| `PrimaryDeploymentId` / `FallbackDeploymentId` | Deployment identifiers published in the address space |
| `EnableFallback` | Publishes the fallback deployment and `FallsBackTo` reference |
| `EnableCatalogue` | Publishes catalogue and import-job nodes |
| `EnableLearningLoop` | Publishes a `LearningJobType` node for ground-truth sample accounting |
| `TransferExpiry`, `MaxTransferSize`, `MaxConcurrentTransfers`, `TransferInferenceTimeout` | Bounds for chunked transfers |
| `AsyncInferenceDelay`, `MaxRetainedJobs` | Bounds and timing for asynchronous inference jobs |
| `SourceId` | Identifier of the model source |

When `EnableLearningLoop` is true, `AINodeManager` publishes one
`LearningJobType` under `LearningJobs`. Host-level coordinators report
ground-truth corrections through `RecordLearningSampleAsync(sampleId,
sampleKind)`. The stable `sampleId` makes retries idempotent, and
`AILearningSampleKind.Negative` counts empty or retracted observations exactly
as positive examples count.

### `InferenceBackendOptions`

| Property | Purpose |
|---|---|
| `Enabled` | Enables the backend; most useful for disabling fallback |
| `Kind` | `ChatClient` or `RestChatCompletions` |
| `EndpointUri`, `ChatCompletionsPath`, `ProbePath` | Endpoint and paths for REST-shaped clients |
| `Authentication`, `CredentialReference`, `ApiKeyHeader`, `CredentialDirectory`, `TokenAudience` | Server-to-backend authentication |
| `Site`, `DataJurisdiction`, `EgressPermitted`, `RetainsInput` | Trust-boundary metadata published to clients |
| `MaxInlinePayloadSize` | UInt32 inline `Invoke` payload limit; zero permits only an explicitly chosen transfer |
| `Models` | Configured model catalogue entries |

## Client surface

`AddAIClient()` mirrors the other companion-family client extensions. It
registers an `AIClientFactory` and a
`Func<CancellationToken, Task<AIClient?>>` over the managed session.

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
        options.ApplicationName = "AIClient";
        options.ApplicationUri = "urn:localhost:OPCFoundation:AIClient";
        options.AutoAcceptUntrustedCertificates = true;
    })
    .AddDiscoveryAndConnect(options =>
    {
        options.DiscoveryUrl = "opc.tcp://localhost:62640/ModelManagementServer";
        options.SecurityMode = MessageSecurityMode.SignAndEncrypt;
        options.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
    })
    .AddAIClient();

using IHost app = builder.Build();
await app.StartAsync().ConfigureAwait(false);

var createClient = app.Services
    .GetRequiredService<Func<CancellationToken, Task<AIClient?>>>();
AIClient? client = await createClient(CancellationToken.None)
    .ConfigureAwait(false);

if (client is null)
{
    Console.WriteLine("The Server does not implement AI Model Management.");
}
```

For one-off code, `AIClient.TryCreate(session)` is the direct fallback
when you already own an `ISession`.

## Inference backends

`IInferenceBackend` is the server-side contract: list models, invoke a model
and probe reachability. Two implementations ship:

- `ChatClientInferenceBackend` wraps `Microsoft.Extensions.AI.IChatClient`.
  This is the default. Hosts can pair it with any SDK or local runtime that
  implements the abstraction. The OPC UA address space stays unchanged.
- `RestChatCompletionsBackend` speaks the OpenAI-compatible REST
  chat-completions contract directly. Use it when that REST shape is the actual
  wire contract and no `IChatClient` is available.

Both hosted and on-device deployments use the same OPC UA nodes. Their
configuration identifies the endpoint, credentials, data jurisdiction, and
egress.

## Invocation inputs and parameters

`Invoke` and `InvokeAsync` accept an inline `Payload` or a `PayloadUri`, but the
current server accepts inline payloads only. It returns `BadNotSupported` for
URI-only input rather than accepting it and running a backend with no request
body.

Both built-in backends accept these parameters:

- `temperature`: 0 through 2
- `max_tokens`: positive integer
- `top_p`: 0 through 1

Clients can provide values as strings, built-in integer or floating-point
values, or booleans. Both backends convert values using the invariant culture
and forward them through these execution paths:

- Synchronous and asynchronous calls
- Fallback execution
- Oversized-transfer execution

Both backends reject malformed or unsupported parameters. In the REST
chat-completions backend, an explicit OPC UA parameter overrides the matching
field in the JSON request body. The backend preserves body fields without a
matching parameter.

The server sets `SpecificationVersion` from the source-generated
`Opc.Ua.AI.ModelVersions.Target` constant. The AI NodeSet's target model
version determines this constant, so updating the NodeSet also updates the
published version without a separately maintained literal.

## Example

The sample in
[`samples/AI/ModelManagementServer`](../samples/AI/ModelManagementServer) hosts
the node manager with `AddAI`. By default it uses the `ChatClient` path and the
sample composition root supplies a small `IChatClient` over the
OpenAI-compatible endpoint. `verify_backend.py` is a throwaway endpoint that
speaks enough of that contract for local validation:

```powershell
python samples/AI/verify_backend.py 5273
dotnet run --project samples/AI/ModelManagementServer
dotnet run --project samples/AI/ModelManagementClient
```

Set `InferenceBackend__Kind=RestChatCompletions` to select the REST backend for
an endpoint that requires direct REST access. Configure
`FallbackInferenceBackend__Kind` independently when the fallback uses a
different wire contract from the primary.

## Limitations

- The companion specification is a draft, so namespace URIs and NodeIds can
  change.
- The sample publishes a real `LearningJobType` instance and a real
  `SamplesCollected` counter. Host-level code can call the server-side
  accounting API when ground-truth corrections arrive, including empty or
  retracted observations. The sample does not simulate retraining, candidate
  generation, or promotion.
- `IChatClient` has no standard model-enumeration method, so hosts that need a
  catalogue should configure `InferenceBackendOptions.Models`.
- The libraries do not reference vendor SDKs. If your hosting application needs
  a provider package, add it there and expose it through `IChatClientFactory`.
- The AI inference and sample projects disable Native AOT because the
  `Microsoft.Extensions.AI` ecosystem uses reflection in areas this repository
  builds with warnings as errors.

## See also

- [AI sample README](../samples/AI/README.md)
- [Developer guide](DeveloperGuide.md)
- [Vision developer guide](Vision.md)
- [Robotics developer guide](Robotics.md)
