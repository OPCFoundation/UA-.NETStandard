# Experimental Async Services generator inputs

This directory maintains the proposed 0.2.0 wire definitions tracked in
[#4813](https://github.com/OPCFoundation/UA-.NETStandard/issues/4813). The proposal
is not an adopted OPC UA standard. Its namespace-zero identifiers are
provisional and must not be presented as assigned standard identifiers.

`..\Opc.Ua.SourceGeneration.Core\Design\AsyncServices.model-contract.json`
records field order, enumeration values, identifiers and protocol invariants.
The stack's existing source generators consume the synchronized
`UA Core Services.xml`, `StandardTypes.xml` and `StandardTypes.csv` inputs.
Do not edit generated C# to change the protocol.

From the repository root:

```powershell
python tools\AsyncServices\sync_contract.py
python tools\AsyncServices\sync_contract.py --check
```

The synchronizer changes only its marked XML sections and the contract's CSV
allocations. It rejects identifier collisions and preserves the rest of the
standard model. Its check mode fails on drift without writing files.

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

The encoder tests independently check Binary field order, both Binary and JSON
round trips, the original response header and diagnostics, and exclusion of the
experimental definitions from the normal address-space initializer.
