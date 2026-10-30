# Registry model generation

This directory contains the development-time scripts and manifests used by the
Endpoint Registry model package and its shared xRegistry and Schema Registry dependencies.
Normal builds consume the checked-in models, mapping tables and fixtures.

## Files

| File | Purpose |
| --- | --- |
| `ImportRegistryModels.ps1` | Copies the three registry NodeSets and NodeId ledgers from the companion specification checkout. |
| `registry-models.json` | Records the imported commit, model identities and SHA-256 content hashes. |
| `GenerateRegistryNativeCatalog.py` | Generates the Endpoint and shared xRegistry mapping tables, native field coverage and reference fixtures. |
| `registry-native-catalog.json` | Records the native catalog inputs, hashes and generated output inventory. |
| `GenerateRegistryRuleFixtures.py` | Records rule fixtures from the specification's Python validators. |
| `registry-conformance.json` | Maps implemented facets to runtime evidence and lists the configured validation targets. |

## Import and verification

Run from the stack repository root with PowerShell, Python and a local checkout of the
companion specification. Use the commit recorded in the manifests to verify checked-in output.

```powershell
$specification = 'D:\git\OPC30450-CloudIntegration'
$generation = 'src\Opc.Ua.EndpointRegistry\Generation'

& "$generation\ImportRegistryModels.ps1" -SpecificationRoot $specification
python "$generation\GenerateRegistryNativeCatalog.py" --spec-root $specification
python "$generation\GenerateRegistryRuleFixtures.py" --spec-root $specification
```

To verify output without importing or rewriting it:

```powershell
& "$generation\ImportRegistryModels.ps1" -SpecificationRoot $specification -Check
python "$generation\GenerateRegistryNativeCatalog.py" --spec-root $specification --check
python "$generation\GenerateRegistryRuleFixtures.py" --spec-root $specification --check
```

The import check compares all six model files and the expected provenance manifest.
The native catalog check compares its generated tables, fixtures and provenance.
The rule fixture check compares the recorded validator results.

Scripts require committed specification inputs by default. `-AllowUncommitted` and
`--allow-uncommitted` support local development and mark the resulting provenance accordingly.

See [xRegistry](../../../docs/XRegistry.md) for shared runtime contracts and
[Endpoint, Message and Schema registries](../../../docs/EndpointRegistry.md) for hosting and clients.
