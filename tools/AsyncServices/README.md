# Experimental RSE (Async Services) generator inputs

This directory maintains the pinned model inputs for **OPC 10000-29:
Recoverable Service Execution (RSE)** draft 0.4.0. The proposal is not an adopted
OPC UA standard. Its identifiers are provisional, and the now-closed
[#4813](https://github.com/OPCFoundation/UA-.NETStandard/issues/4813) is not an
active approval record.

Client and Server use the five manager Methods; the earlier dedicated Services
are removed. The [runtime guide](../../docs/AsyncServices.md#samples-and-validation-scope)
separates integrated recovery tests, native transports, process restarts and
NativeAOT evidence. Activation and Owner/Durable qualification remain explicit.
Importing or checking artifacts does not enable those modes or establish runtime
conformance.

The corrected NodeSet is consumed by the existing Core model source generator
under `Opc.Ua`, including typed structures, states and manager proxies.
Namespace-local helper containers use the `Rse` prefix, such as
`RseDataTypeIds`, so Core and RSE numeric identifiers do not share a lookup table.
The producer correction uses local SymbolicNames and composed CSV labels;
no naming workaround was added to the importer. Code generation and compilation
alone do not establish Server or Client runtime conformance.

## Source revision

The adopted source is `OPCF-Members/OPC10000-Core` commit
`533587684928669b0cd183fc9ab61b5b0730a576`. Its
`source\async-services\model-contract.json` supplies the canonical JSON;
`model\Opc.Ua.RSE.NodeSet2.xml` and
`model\Opc.Ua.RSE.NodeIds.csv` supply the primary model and identifiers.
`extras\async-services\revision-contract.json` records its semantic fingerprints.

| Fingerprint | SHA-256 |
| --- | --- |
| Schema semantics | `cf7adc567a97eef517456c66b7dc1bc55edc1f0b64376cb262a5494a113c7220` |
| Model semantics | `e080235527ba02f74fe9857878b6cbc76f539a9fcf51549e607b63775aaf3702` |
| Canonical JSON bytes | `f84345ba67b070723a37eec66c35cb2dcb7b22b698fa78d9b0f94f55ce52c75f` |
| NodeSet bytes | `36083b885c033206b22f754fb53b7491b868757a1de1df97c7b459345fa49904` |
| NodeIds CSV bytes | `6449c0f65179f91de1148ae5425ffd93f081270aa7d1b670fbc274e1f7dda14c` |

All 96 definitions use the namespace URI `http://opcfoundation.org/UA/RSE/`,
with contiguous local numeric IDs 1..96. Namespace index 1 belongs to the
supplied file; runtime consumers must resolve the URI, not hard-code that index.
Core dependencies remain in namespace zero. The primary model includes
NamespaceMetadata (89) and its properties (90..96); there is no separate
metadata-only model for this revision.

The manager is local ID 73, with Capabilities at 71 and its Mandatory type
declaration at 70. Submit, GetResult, Acknowledge, Cancel and Abandon declarations
and argument variables occupy 74..88. These are declarations, not the
Server-assigned Method instance IDs. Each Method has one scalar request and one
scalar response argument, without wrapper headers.
The CSV name of instance 71 is `RecoverableExecutionManager_Capabilities`; its
BrowseName remains the RSE-qualified `Capabilities`.

Conformance units use `RSE-*`, the capability identifier is `RSE`, and the
verification assertions are `RSE3-001` through `RSE3-070`. The experimental
Session, Owner and Durable profile suffixes remain `0.4`.

## Import and verify inputs

Export the four named files from the exact committed source into a local
directory: `model-contract.json`, `Opc.Ua.RSE.NodeSet2.xml`,
`Opc.Ua.RSE.NodeIds.csv` and `revision-contract.json`. Then, from the
repository root:

```powershell
python -B tools\AsyncServices\sync_contract.py --import-from <export-directory>
python -B tools\AsyncServices\sync_contract.py --check
python -B -m unittest discover -s tools\AsyncServices -p test_sync_contract.py -v
```

The importer verifies every pinned hash and the complete snapshot before writing
any destination. It checks namespace-local identities, the NodeSet/CSV inventory,
ordered structure fields, scalar Method arguments and conformance identifiers.
It does not fetch a moving branch. Check mode is read-only; repeated imports of
the same content do not rewrite the files. CRLF checkout conversion is normalized
to the committed LF bytes for hash comparison.

The JSON is kept at
`..\Opc.Ua.SourceGeneration.Core\Design\AsyncServices.model-contract.json`.
The NodeSet and CSV are kept together under
`..\..\src\Opc.Ua.Core\AsyncServices\Model`; the revision contract is in this
directory. Model C# must come from the existing source generator, not
handwritten replacement DTOs.

## Legacy implementation

The namespace-zero experimental definitions in `UA Core Services.xml`,
`StandardTypes.xml` and `StandardTypes.csv`, the dedicated ServiceSet and the
Server's old runtime overlay are removed. Their provisional identities are not
reassigned or registered as an alternate binding. Diagnostics type 49 and
instance 72 are `RecoverableExecutionDiagnosticsType` and
`RecoverableExecutionDiagnostics`.

See the [runtime guide](../../docs/AsyncServices.md),
[five-Method binding](../../docs/AsyncServices.md#five-manager-methods) and
[Client guide](../../docs/ClientRecoverableExecution.md) for current activation,
acceptance, lifetimes and evidence boundaries.
