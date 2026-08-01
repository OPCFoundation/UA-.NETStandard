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
  kinematic chain — the live articulation (`RenderTargetKind = Rotation`). The axis
  limits are those of the reference robot (see below), and the simulation runs an eased
  pick-and-place cycle rather than a free sweep.
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

## The USD assets

`Assets/robot.usda`, `Assets/tool.usda` and `Assets/Cell.usda` are embedded in the server
and served over Part 5 `FileType` with SHA-256 digests, so a connector can fetch the whole
closure and render the twin without any external asset resolver.

### Reference robot — KUKA KR 16-2

`robot.usda` reproduces the published kinematics of a **KUKA KR 16-2**, taken from the
ROS-Industrial URDF (`ros-industrial/kuka_experimental`, `kuka_kr16_support`):

| Joint | Offset from the previous axis | Rotate op | Limit |
| --- | --- | --- | --- |
| A1 | 675 mm up from the mounting face | `xformOp:rotateZ` | ±185° |
| A2 | 260 mm forward | `xformOp:rotateY` | −155°…+35° |
| A3 | 680 mm along the upper arm | `xformOp:rotateY` | −130°…+154° |
| A4 | 670 mm along the forearm, −35 mm lateral | `xformOp:rotateX` | ±350° |
| A5 | coincident with A4 | `xformOp:rotateY` | ±130° |
| A6 | coincident with A4 | `xformOp:rotateX` | ±350° |
| Flange | 158 mm past the wrist centre | — | — |

`260 + 680 + 670 = 1611 mm`, the robot's published reach. A4, A5 and A6 are coincident,
which is the real spherical wrist. The livery is KUKA orange over dark cast housings, and
the cell is scaled around that reach: a 7.2 × 4.8 m floor with the guarding at
x = ±3.4 m, y = ±2.2 m, plus a controller cabinet, a shared work table and a stack light.

### Mobile platform

Each arm rides on an omnidirectional AGV (`/Robot/MobileBase`, 1.10 × 0.72 m, 365 mm deck)
in the style of a KUKA KMP: orange chassis, dark skirt, four wheels, diagonally opposed
safety scanners, bumpers and status strips. The RSL scenario drives each robot's spatial
location across the cell, so the arm has to be carried rather than bolted to the floor.
The platform footprint and the work-table position are chosen so the two AGVs clear each
other at their closest approach and never drive through the table.

To floor-mount the arm instead, delete `/Robot/MobileBase` and clear the `xformOp:translate`
on `/Robot/Base`; nothing else depends on it.

### Editing the assets

The prim paths and rotate ops above are the **binding contract**: `RobotCell.cs` addresses
them by name, and the connector writes each rotate op as a scalar `double`. Link offsets,
geometry, materials and lighting are all free to change; the paths, op names,
`xformOpOrder` entries, `/Robot/Base/.../Flange`, `/Robot/Warning`, `/Cell/SafetyBeacon`,
`/Cell.inputs:speedOverride` and the `R1`/`R2` mount attributes are not.

`RobotAssetContractTests` (in `tests/Opc.Ua.OpenUsd.Tests`) parses the shipped assets
and asserts that contract, so an edit that breaks it fails the build instead of silently
freezing the twin. Note that the `.usda` files under
`tests/Opc.Ua.OpenUsd.Tests/Assets` are a **frozen** reader/materializer fixture and
are deliberately *not* kept in sync with these assets.

Author with real USDA syntax: the managed `UsdaReader` is more forgiving than OpenUSD
itself, so a layer that parses in the tests can still fail to load in a viewer. The
end-to-end run below is the authoritative check.

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

To watch the cell animate without leaving the connector, install the optional
`Opc.Ua.OpenUsd.Connector.Viewer` assembly beside it and add `--view`:

```
dotnet run --project tools/Opc.Ua.OpenUsd.Connector -- \
    --server opc.tcp://localhost:62830/MinimalRobotServer --insecure --view
```

That fetches this server's served asset closure, composes `stage.usda`, opens a viewport, and streams
the same subscribed values into both the override layer and the rendered stage, so the joints move on
screen as the simulation runs. See [`docs/OpenUsd.md`](../../docs/OpenUsd.md#rendering-the-twin-live).

The robot motion is configured through `RobotMobilityOptions`. RSL
Position/Orientation values author `xformOp:translate` and
`xformOp:rotateXYZ`; GPOS longitude, latitude, and elevation author the
`inputs:longitude`, `inputs:latitude`, and `inputs:elevation` attributes. See
[`docs/Positioning.md`](../../docs/Positioning.md).
