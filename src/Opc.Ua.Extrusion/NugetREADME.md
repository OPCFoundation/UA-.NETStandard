# Opc.Ua.Extrusion

All twelve **OPC 40084 version 2.00** information models in one package:
source-generated typed node states, data types, identifiers, client proxies,
factory registrations and compiled model loaders.

| Part | Source folder | CLR namespace |
| --- | --- | --- |
| 40084-1 | `GeneralTypes` | `Opc.Ua.Extrusion` |
| 40084-2 | `Line` | `Opc.Ua.Extrusion.Line` |
| 40084-3 | `Extruder` | `Opc.Ua.Extrusion.Extruder` |
| 40084-4 | `HaulOff` | `Opc.Ua.Extrusion.HaulOff` |
| 40084-5 | `MeltPump` | `Opc.Ua.Extrusion.MeltPump` |
| 40084-6 | `Filter` | `Opc.Ua.Extrusion.Filter` |
| 40084-7 | `Die` | `Opc.Ua.Extrusion.Die` |
| 40084-8 | `Pelletizer` | `Opc.Ua.Extrusion.Pelletizer` |
| 40084-9 | `Cutter` | `Opc.Ua.Extrusion.Cutter` |
| 40084-10 | `Calibrator` | `Opc.Ua.Extrusion.Calibrator` |
| 40084-11 | `Corrugator` | `Opc.Ua.Extrusion.Corrugator` |
| 40084-12 | `Calender` | `Opc.Ua.Extrusion.Calender` |

There are no separate per-device model packages. Each wire model URI has one
producer, this assembly; each part retains its own namespace, model identity,
compiled loader and factory registrations. Existing Core, DI, Machinery and
plastics/rubber dependencies are reused rather than regenerated.

## Server and client use

Use `Opc.Ua.Extrusion.Server` for provider-backed device behavior and
`Opc.Ua.Extrusion.Client` for managed-session discovery, typed workflows and
events. The three extrusion packages cover the complete family.

The server's `ExtrusionModelProvider` loads the models in dependency order.
Direct users can call the per-part compiled loaders, including
`AddOpcUaExtrusion`, `AddOpcUaExtrusionLine` and `AddOpcUaExtrusionExtruder`.
Load dependencies and register their data types first. Model loading alone
does not implement device behavior.

## Provenance

The twelve NodeSets and upstream identifier tables remain byte-for-byte copies
of their individually MIT-licensed publications at UA-Nodeset revision
`96a08945425d0295edb3607d2a38f6841ef44046`. Each source folder includes its
model-specific provenance and loading instructions. The package contains
`licenses/CompanionModels.NOTICE.txt`; hashes, identifier discrepancies and
dependency headers are recorded in
`src/Opc.Ua.PlasticsRubber/Model/companion-models.json`.

The shared common model is the independently authored 1.03-compatible
implementation on the MIT-licensed 1.02 basis in `Opc.Ua.PlasticsRubber`, not a
redistributed official 1.03 NodeSet. No formal certification is claimed.

## Platforms

The package follows the stack's library target-framework matrix. Generated
registrations and compiled loaders do not require reflection-based XML loading
and support NativeAOT applications.
