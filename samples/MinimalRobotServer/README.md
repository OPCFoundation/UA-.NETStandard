# MinimalRobotServer

A minimal, self-contained .NET console OPC UA server that demonstrates the **OPC 40010
Robotics**, **OPC 10000-210 Relative Spatial Location**, and **OPC 10000-211
Global Positioning** companion specifications bound to **OpenUSD** through the draft
[*OPC UA — OpenUSD Bindings*](../../../opcua-drafts/core-specs/openusd-binding/OPC-UA-OpenUSD-Bindings.md)
companion model, so a **generic** connector renders a robot cell live with **no
robot-specific code**. It is built on the `Opc.Ua.Robotics` and `Opc.Ua.OpenUsd`
SDK libraries. See [`docs/Robotics.md`](../../docs/Robotics.md) for the Robotics
developer guide and [`docs/OpenUsd.md`](../../docs/OpenUsd.md) for the OpenUSD
binding.

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
- One RSL spatial-object list with a world frame, R1/R2 SpatialObject AddIns,
  each robot's PositionFrame, and R1's ToolFlange AttachPoint.
- One GPOS Zone with ground-control points and one live GlobalLocation per
  robot. Each robot independently selects `Fixed`, `FigureEight`, `Circle`, or
  `Shuttle` motion; the default uses phase-shifted figure-eight paths.

All 15 representations (1 system + 2 robots + 12 axes) are discoverable through the
well-known `Server/OpenUSD/Representations` registry.

## Design note — stock Robotics hosting

The server uses the stock `Opc.Ua.Robotics.Server` hosting pipeline:

- `AddRobotics()` registers the sealed stock `RoboticsNodeManager`, DI/IA/Robotics
  model provider, and the shared Robotics configurator pipeline.
- `AddRoboticsModel<OpenUsdModelProvider>()`,
  `AddRoboticsModel<RslModelProvider>()`, and
  `AddRoboticsModel<GposModelProvider>()` contribute the additional compiled
  OpenUSD, RSL, and GPOS companion models to the same manager.
- `ConfigureRobotics<RobotCell>()` builds the cell with the validated fluent
  topology builders (`AddMotionDeviceSystemAsync`, `AddMotionDevice`,
  `AddAxis`, safety, controller, task-control, and operation builders).
- `AddPositioningFor<RoboticsNodeManager>()` and
  `ConfigurePositioningFor<RoboticsNodeManager>(...)` add the RSL/GPOS spatial
  objects, frames, attach point, zone, and live global-location bindings after
  the Robotics topology exists.

The OpenUSD facility still lives under the standard `Server/OpenUSD` entry point,
but it is now authored by the `RobotCell` configurator against the stock manager
instead of by subclassing a custom node manager.

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

The robot motion is configured through `RobotMobilityOptions`. RSL
Position/Orientation values author `xformOp:translate` and
`xformOp:rotateXYZ`; GPOS longitude, latitude, and elevation author the
`inputs:longitude`, `inputs:latitude`, and `inputs:elevation` attributes. See
[`docs/Positioning.md`](../../docs/Positioning.md).
