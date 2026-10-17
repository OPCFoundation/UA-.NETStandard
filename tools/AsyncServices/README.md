# Experimental RSE (Async Services) generator inputs

This directory maintains the **OPC 10000-27: Recoverable Service Execution
(RSE)** draft 0.2.0 inputs, dated 2026-10-16. The publication name does not rename
the Async Service APIs, source paths, ASE capability/conformance identifiers or
namespace-zero allocations. The proposal is not an adopted OPC UA standard.
Its identifiers are provisional, and the now-closed
[#4813](https://github.com/OPCFoundation/UA-.NETStandard/issues/4813) is not an
active approval record.

`..\Opc.Ua.SourceGeneration.Core\Design\AsyncServices.model-contract.json`
records field order, enumeration values, identifiers and protocol invariants.
The stack's existing source generators consume the synchronized
`UA Core Services.xml`, `StandardTypes.xml` and `StandardTypes.csv` inputs.
Do not edit generated C# to change the protocol.

## Source revision

The adopted source is `OPCF-Members/OPC10000-Core` commit
`8710615ed5765ff780fd54b7f58a6db884d315c8`. Its
`source\async-services\model-contract.json` supplies the canonical JSON;
`model\Opc.Ua.AsyncServices.NodeSet2.xml` supplies the runtime overlay.
`source\async-services\manifest.json` identifies the publication, and
`extras\async-services\revision-contract.json` records its semantic fingerprints.

| Fingerprint | SHA-256 |
| --- | --- |
| Schema semantics | `4a6a505de7fbb9085c9940cbf72c53f7269057acfba282b8e6f180dd8036e13b` |
| Model semantics | `2843d83052ca0a2b45c60e53845370a0807b90b71ef1d46c261d68e541f56387` |
| Runtime NodeSet bytes (Git blob) | `76be09ab2e25e459d22c0fbe6fea4c42f91a5b710505ca1ae98740918bbc7ed9` |

The JSON content and runtime NodeSet bytes match that committed source. Relative
to the original Core `70bf376` acceptance baseline, only the
`SubmitServiceRequest.ExecutionAllowance` description and the NodeSet's human
title changed. Field order, types, enums, encodings, namespaces, NodeIds and the
semantic fingerprints did not change. The older raw NodeSet fingerprint,
`5e675dd56cf8b296bc3739905777f93d5a3e0e68832e17d98b39ffe300dc4028`,
belongs to the earlier snapshot, not this adoption.

Git checkout line-ending conversion can change a local file's raw-byte hash;
the recorded fingerprint identifies the committed artifact.

Keep the publication-only `Opc.Ua.AsyncServices.Metadata.NodeSet2.xml` separate.
It is not an input to the runtime overlay or a Server dependency.

## Synchronize generator inputs

From the repository root:

```powershell
python tools\AsyncServices\sync_contract.py
python tools\AsyncServices\sync_contract.py --check
```

The synchronizer changes only its marked XML sections and the contract's CSV
allocations. It rejects identifier collisions and preserves the rest of the
standard model. Its check mode fails on drift without writing files.
Update the JSON and runtime overlay from the same reviewed source revision,
then run the synchronizer; it does not fetch or rewrite the runtime NodeSet.

## Generated surfaces

Two model categories keep the experiment separate from existing behavior:

- `RuntimeOptional` omits a definition from the generated default address-space
  initializer while retaining its typed state and factory surfaces. Runtime
  feature activation must install the optional model explicitly.
- `AsyncOnly` emits only TAP Client methods for a Service. Existing Services
  retain their compatibility methods; the new Services have no synchronous or
  Begin/End API.

The request and response messages remain normal generated `IServiceRequest` and
`IServiceResponse` types. JSON uses the standard DataType identifier for
`UaTypeId`; the explicitly allocated Default JSON encoding Objects remain
available in the model. The two well-known capability/diagnostic roots have
fixed proposal IDs. Their instance children use Server-assigned IDs in the
application namespace, not the IDs of the corresponding type declarations.

The three operations have their own `Asynchronous` Service Set category and
generated `IAsynchronousServiceSetClientMethods` interface. The Session client
and endpoint host that set because it requires a Session; it is not classified
as one of the four Session lifecycle Services.

The encoder tests independently check Binary field order, both Binary and JSON
round trips, the original response header and diagnostics, and exclusion of the
experimental definitions from the normal address-space initializer.

See the [runtime and typed-Client guide](../../docs/AsyncServices.md) and
[protocol reference](../../docs/AsyncServicesProtocol.md) for opt-in installation,
acceptance/ACK, lifetimes, ownership and evidence boundaries.
