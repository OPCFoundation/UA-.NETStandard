# AI Model Management sample

An OPC UA Server that exposes AI models — hosted somewhere else, or running on
the machine — through the draft companion specification *OPC UA — AI Model
Management and Inference*. Plus a client that exercises it, and a Helm chart
that runs it on Kubernetes.

The point of the sample is to demonstrate: **where inference runs should not
change how it is called.** A client discovers a deployment, calls `Invoke`, and
gets an answer together with the identity of the model that produced it —
whether that model was a hosted service in another jurisdiction or a quantized
copy on the same box.

> The specification is a **draft**. Namespace URIs and NodeIds are provisional.

## What is in here

The reusable parts live under `src/` as the `Opc.Ua.AI` package family, in the
same shape as the Robotics and Vision families: a model assembly, a server-side
assembly, a client-side assembly, and the inference backends. What remains here
is the two sample applications that compose them.

| | |
|---|---|
| `../../src/Opc.Ua.AI/` | The companion specification's NodeSet, source-generated at build time |
| `../../src/Opc.Ua.AI.Inference/` | Reaching a model: the `IInferenceBackend` contract, a `Microsoft.Extensions.AI` `IChatClient` backend, an OpenAI-compatible REST backend, credential resolution |
| `../../src/Opc.Ua.AI.Server/` | The node manager: publishes the address space and serves the specification's Methods |
| `../../src/Opc.Ua.AI.Client/` | Discovery, typed reads, Method calls and artefact transfer |
| `ModelManagementServer/` | The Server sample. Hosts the node manager and picks a backend |
| `ModelManagementClient/` | A console client that browses from the entry point and exercises what it finds |
| `deploy/helm/` | The chart, its tests, and a cluster smoke test |
| `../../tests/Opc.Ua.AI.Tests/` | Unit and integration tests against a fake backend |

**No cloud-vendor SDK is referenced anywhere.** The default backend goes
through `Microsoft.Extensions.AI`, which a hosted service and an on-device
runtime both implement, and workload identity is read from the token the
platform projects — the mechanism every platform implements underneath its own
SDK. A sample that only ran against one vendor would not be demonstrating a
platform-independent Server. `RestChatCompletionsBackend` remains available
when the endpoint only exposes the OpenAI-compatible REST contract and the host
cannot supply an `IChatClient`; set `InferenceBackend:Kind` (or
`FallbackInferenceBackend:Kind`) to `RestChatCompletions` for that wire
contract.

## Running it

The Server needs something to infer with. The quickest is the throwaway endpoint
in `verify_backend.py`, which speaks just enough of the OpenAI-compatible
contract to answer:

```powershell
python samples/AI/verify_backend.py 5273
```

Then, in two more terminals:

```powershell
dotnet run --project samples/AI/ModelManagementServer
dotnet run --project samples/AI/ModelManagementClient
```

The client browses to the AI root under the Server Object, walks every
deployment, follows `UsesModel` to the model and its digest, and then calls
`GetCapabilities`, `Invoke`, `BeginTransfer`, `InvokeAsync` and the source's
`TestConnection` and `ListModels`.

Point it at a real endpoint by configuration:

```powershell
$env:InferenceBackend__Kind = "ChatClient"
$env:InferenceBackend__EndpointUri = "https://<resource>.services.ai.azure.com/openai/"
$env:InferenceBackend__Authentication = "ApiKey"
$env:InferenceBackend__CredentialReference = "inference-api-key"
$env:InferenceBackend__Site = "Cloud"
$env:InferenceBackend__EgressPermitted = "true"
```

`ChatClient` is the default. This sample registers a small `IChatClient` over
the OpenAI-compatible endpoint so no vendor package is needed. A production
host can replace `IChatClientFactory` with one that creates the chat client
from its own SDK or local runtime. Use `RestChatCompletions` for deployments
whose contract is the REST shape itself rather than the
`Microsoft.Extensions.AI` abstraction.

## The control plane is OPC UA

There is no second management API, and that is a decision rather than an
omission. The specification defines Methods for the things an operator needs to
do — test a source, list what it offers, invoke a model, start a job, promote a
candidate — so adding an HTTP surface beside them would create two ways to do
the same thing that could disagree.

Everything else is startup configuration: which endpoint to reach, which
deployments to publish, and which credential to present.

## What the address space says, and why

A few members answer questions that are asked *before* a call rather than after
one.

**`ModelUsed`** names the model that actually produced a result. It exists for
the fallback case: a caller that cannot see which model answered cannot tell a
degraded answer from a good one, and a fallback that answers silently looks
exactly like a healthy primary. This is the single thing in the sample most
worth getting right, and the one the tests press hardest on.

**`EgressPermitted`, `DataJurisdiction`, `RetainsInput`** say where the data
goes. Egress is not made false by encryption — that answers who can read data in
flight, not where the data went.

**`MaxInlinePayloadSize`** is published before a client calls, rather than
discovered from a rejection, because the real bound is the smallest of several
limits a client can see none of.

**`CredentialReference`** names the credential; it never carries one. A client
is entitled to know *which* credential is configured so it can tell whether the
right one is. A client that could read the value could use it.

**`Digest`** is empty when the backend declares none. A hosted endpoint that
will not say which weights answered cannot be made to say so by hashing its
name, and a digest that looks like an artefact digest but is not one is worse
than none, because something will eventually compare it.

## Implementation status

**Implemented.** The address space, the Methods, the provenance references, the
chunked transfer over Part 5 `FileType`, the asynchronous job on the Part 10
program lifecycle, the fallback and its reporting, the credential handling, and
the HTTP client that reaches an OpenAI-compatible endpoint. The learning job
node is also real: the Server publishes a `LearningJobType` instance, and
`SamplesCollected` is incremented only when host-level code records a distinct
ground-truth sample. Empty or retracted observations count the same way as
samples carrying geometry. All of the inference pieces have been run end to end
against a live endpoint, in a container, and in a Kubernetes cluster.

**Not implemented.** There is no retraining loop. A sample cannot retrain a
model, and `PromoteModel` is not wired to a simulated MLOps integration — faking
candidate generation or timed promotion would mislead a reader about the one
part of the specification a sample cannot honestly demonstrate.

> **Note:** `FakeInferenceBackend` lives in the test project and is not
> configurable from the Server. It exists so CI needs no inference service; if
> it shipped as a supported option the sample could look healthy while never
> having reached a model.

## Kubernetes

```powershell
docker build -f samples/AI/ModelManagementServer/Dockerfile `
             -t modelmanagementserver:local .

helm install ai samples/AI/deploy/helm/ai-model-management `
     --set image.repository=modelmanagementserver `
     --set image.tag=local --set image.pullPolicy=Never
```

The defaults describe an on-device runtime on loopback with no credential,
because that is the shape that installs and runs without anything else existing.
`deploy/helm/values-cloud.yaml` is the hosted shape: a remote endpoint with a
mounted credential, and a local fallback.

**Supply the credential as a Secret you manage**, not through Helm:

```powershell
kubectl create secret generic inference-credentials `
        --from-literal=inference-api-key=<key>

helm install ai ./ai-model-management -f values-cloud.yaml `
     --set credentials.existingSecret=inference-credentials
```

`credentials.create` exists for local clusters and puts the value in the release
history, where `helm get values` will show it. The chart says so when you use it.

### The chart refuses some configurations

Each refusal corresponds to a deployment that would come up green and describe
itself wrongly, which is worse than one that fails to start because nobody
investigates a healthy pod:

- `ApiKey` authentication with no credential mounted.
- A fallback deployment with no fallback endpoint — it would always fail.
- A fallback pointing at the primary's endpoint — that is a retry, and it fails
  for every reason the primary just failed for.
- `EgressPermitted: false` with a backend that is not on the machine — the
  Server would publish a promise it does not keep.

### Probes

The probes are TCP against the OPC UA port. That proves the listener accepts
connections; it does **not** prove the Server is serving the address space. An
exec probe running a real OPC UA client would prove it and costs a process
launch every period. The trade is deliberate, and it is the reason the smoke
test drives a real session rather than trusting readiness.

### Testing the chart

```powershell
helm lint samples/AI/deploy/helm/ai-model-management
python samples/AI/deploy/helm/chart_tests.py

# slow, needs Docker and kind; creates and deletes a cluster
pwsh samples/AI/deploy/helm/smoke-test.ps1
```

`chart_tests.py` renders the chart and asserts what the manifests must and must
not say — including that a credential passed inline appears exactly once, inside
the Secret it belongs in. Every refusal is asserted to fire, because a guardrail
that never triggers is indistinguishable from one that does not work.

`smoke-test.ps1` builds the image, creates a cluster, deploys a stub endpoint
alongside, installs the chart, and opens a real OPC UA session from outside. It
is the slowest and most fragile check here — image build, image load, chart
install and a live session — so run it deliberately rather than letting it gate
everything else.

## Why AOT is disabled

`PublishAot` is off for this sample. The workload-identity credential path
resolves tokens through a library that serialises by reflection, which the AOT
and trimming analyzers warn on, and this repository builds warnings as errors.
Nothing in the OPC UA surface requires AOT, and disabling it for one sample was
preferable to weakening the analyzer settings for the whole repository.
