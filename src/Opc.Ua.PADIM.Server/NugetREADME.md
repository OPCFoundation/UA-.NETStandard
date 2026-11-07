# OPC UA PA-DIM server

This package hosts OPC 30081 PA-DIM devices, signals and analysers through the
shared Device Integration node manager. `AddPadim()` registers a standalone
owner; `AddPadimModel()` composes with an existing DI owner. Machinery is not
required.

Applications provide acquisition, authorized configuration writes and physical
operations. Generated declarations retain typed values, engineering metadata
and IRDI references. The device handle serializes provider calls and joins
admitted work during shutdown. Providers must observe cancellation.

Use the [PA-DIM guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/PADIM.md)
for fluent/DI setup, direct hosting, simulation, damping and operation boundaries.
This package references the sole PA-DIM/IRDI model producer. Model availability
does not imply certification or support for every optional facet.
