# MinimalRobotServer

A minimal, self-contained .NET console OPC UA server that demonstrates the **OPC 40010
Robotics** companion specification bound to **OpenUSD** through the draft
[*OPC UA — OpenUSD Bindings*](../../../opcua-drafts/core-specs/openusd-binding/OPC-UA-OpenUSD-Bindings.md)
companion model, so a **generic** connector renders a robot cell live with **no
robot-specific code**. It is built on the `Opc.Ua.Robotics` and `Opc.Ua.OpenUsd`
SDK libraries.

It is the Robotics counterpart of `PumpDeviceIntegrationServer` and is validated
end-to-end by `RobotOpenUsdE2eTests` (in `tests/Opc.Ua.Di.Tests`).

## What it exposes

A `MotionDeviceSystem` **"RobotCell"** (prim `/Cell`) composed recursively of:

- **Two 6-axis articulated robots** (`MotionDeviceType` `R1`, `R2` → `/Cell/Robots/R1`,
  `/Cell/Robots/R2`), aggregated 1..n with a `Many` / `Reference` `<Component>`
  binding (Reference — not Instance — so each robot articulates independently).
- **Six axes per robot** (`AxisType` `A1..A6`), aggregated with a nested `Many` /
  `Child` `<Component>` binding. Each Axis' `ParameterSet/ActualPosition` (degrees)
  drives one joint `xformOp:rotate{Z|Y|X}` on the (pre-authored) `robot.usda`
  kinematic chain — the live articulation (`RenderTargetKind = Rotation`).
- A cell **EmergencyStop** safety state driving a beacon and per-robot warning halo
  visibility (`UaAlarmToUsd`, `Visibility`).
- An opt-in **SpeedOverride** command (`UsdToUaCommand`, fail-closed).
- A **gripper tool** mounted on R1's flange at runtime (`One` / `Reference`,
  `Dynamic = true`) via a model-change event.

All 15 representations (1 system + 2 robots + 12 axes) are discoverable through the
well-known `Server/OpenUSD/Representations` registry.

## Design note — runtime NodeSet import

The OPC 40010 Robotics model (and its `IA` dependency) is loaded at **runtime** from
embedded `NodeSet2.xml` via `UANodeSet.Read` + `Import`, rather than source-generated.
The Robotics model uses base state-machine / method types whose generated NodeState
proxies are not all present in this repository's `Opc.Ua.Core`, so the source
generator's output does not compile for it. Runtime import loads the full, faithful
OPC 40010 type structure; the server builds its instances from `BaseObjectState` plus
the numeric type NodeIds, so no generated Robotics/IA typed classes are required. Only
the `OpenUsdBinding` NodeSet is source-generated (its typed helpers are used to author
the representations and bindings).

## Run it

```
dotnet run --project samples/MinimalRobotServer -- --host localhost --port 62830
```

Then drive it with the **same** generic connector used for pumps
(`Opc.Ua.OpenUsd.Connector`) to author a live USD override layer:

```
dotnet run --project tools/Opc.Ua.OpenUsd.Connector -- \
    --server opc.tcp://localhost:62830/MinimalRobotServer \
    --out <path>/live.usda --insecure --seconds 10
```

Compose `live.usda` over the base `Cell.usda` (see the example `stage.usda`) and open
it in `usdview` / NVIDIA Omniverse to see the two arms articulate live. The example
USD assets, descriptor, writer, and a step-by-step guide live in the `opcua-drafts`
repo under `core-specs/extras/openusd-binding/examples/robotics/`.
