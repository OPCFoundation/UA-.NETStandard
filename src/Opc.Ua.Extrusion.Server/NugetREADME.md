# OPC UA Extrusion Server

Reusable server behavior for OPC 40084 parts 1–12, version 2.00.

All twelve wire models are supplied by the single `Opc.Ua.Extrusion` model
package. Device constructors and part-specific controllers live in per-part
source folders; the shared runtime still owns the complete family.

- Creates the actual generated Line, Extruder, HaulOff, MeltPump, Filter, Die,
  Pelletizer, Cutter, Calibrator, Corrugator and Calender types.
- Selects optional features and real repeated equipment from the unchanged model
  declarations; never instantiates abstract `ExtrusionDeviceType`.
- Composes into one DI/Machinery owner, with standalone/fluent DI and direct
  construction paths and existing AMB registration.
- Publishes absolute committed values, qualities and timestamps without
  independently simulating production or incrementing counters.
- Routes authorized hardware commands with their original caller context.
- Implements extrusion state projection, line job groups/jobs, exact completion
  events, quality alarms and cutter completion while reusing common OPC 40083
  controllers and existing file infrastructure.

Start with `AddExtrusion().ConfigureExtrusion(...)`, or use
`AddMachinery(...).AddExtrusionToMachinery()` for an existing owner.
`ExtrusionRuntime.Create(existingManager)` is the direct fallback.
Hardware and persistence authorities are explicitly supplied; they are not
implicitly permitted or replaced with successful no-op implementations.

See the repository's `docs/Extrusion.md` for complete usage, member/feature
evidence, provider contracts and validation commands.

The actual Filter model is implemented. The disputed OPC 40084-6 profile whose
published table references OPC 40084-5 is deliberately **not advertised**. No
formal certification is claimed.

The package includes `licenses/CompanionModels.NOTICE.txt`. Its model
dependencies retain their distinct, documented import/implementation provenance.
