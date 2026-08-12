# MinimalRobotServer

A minimal, self-contained .NET console OPC UA server that demonstrates the **OPC 40010
Robotics**, **OPC 10000-210 Relative Spatial Location**, and **OPC 10000-211
Global Positioning** companion specifications bound to **OpenUSD** through the draft
[OpenUSD binding companion model](../../../docs/OpenUsd.md), so a **generic** connector renders a robot
cell live with **no robot-specific code**. It is built on the `Opc.Ua.Robotics` and `Opc.Ua.OpenUsd`
SDK libraries. See [`docs/Robotics.md`](../../../docs/Robotics.md) for the Robotics
developer guide and [`docs/OpenUsd.md`](../../../docs/OpenUsd.md) for the OpenUSD
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
  limits are those of the reference robot (see below). The axis values come from an arm
  solver, so the arm genuinely reaches the slot it is working rather than replaying a
  canned pose list.
- A cell **EmergencyStop** safety state driving a beacon and per-robot warning halo
  visibility (`UaAlarmToUsd`, `Visibility`).
- An opt-in **SpeedOverride** command (`UsdToUaCommand`, fail-closed).
- A **gripper tool** on each robot's flange, plus one mounted on R1 at runtime (`One` / `Reference`,
  `Dynamic = true`) via a model-change event. R1 carries a `NodeVersion` property so that
  mounting it is reportable at all — see [Dynamic components need a NodeVersion](#dynamic-components-need-a-nodeversion).
- A **`CellTwin` folder** under `Objects` carrying the state that is neither a device nor
  an axis: a `ThreeDCartesianCoordinates` position and a `ThreeDOrientation` heading per
  workpiece, driving `/Cell/Parts/PartNN` (`Translation` and `Rotation`), and a jaw
  position per robot driving `…/Flange/Tool/Jaw{Upper|Lower}`. Without these the
  choreography is invisible — the robots mime a transfer while the blocks stay wherever
  the asset authored them.
- One RSL spatial-object list with a world frame, R1/R2 SpatialObject AddIns,
  each robot's PositionFrame, and R1's ToolFlange AttachPoint.
- One GPOS Zone with ground-control points and one live GlobalLocation per robot. Both
  poses come from the shared CellChoreographer, so the robots agree on where each other
  is - see [The transfer cycle](#the-transfer-cycle).

All 15 representations (1 system + 2 robots + 12 axes) are discoverable through the
well-known `Server/OpenUSD/Representations` registry.

## The transfer cycle

R1 collects a part from the western station (`WorkTableA`) and delivers it to the eastern
one (`WorkTableB`); R2 collects from the east and returns it to the west. Three parts
circulate, so both robots usually have work.

### Keeping them apart

The cell is divided into reservable zones:

| Zone | Extent | Occupancy |
| --- | --- | --- |
| `EndZoneA` / `EndZoneB` | \|x\| > 1.3 m | one robot — a deployed arm needs the whole end |
| `CorridorEastbound` | \|x\| ≤ 1.3 m, northern lane (y = 0.35) | one robot |
| `CorridorWestbound` | \|x\| ≤ 1.3 m, southern lane (y = −1.15) | one robot |
| `DockA` / `DockB` | around each charging dock (y = −2.0) | one robot |

A robot reserves the zone *immediately ahead* before entering it and releases the one
behind once it is clear — block signalling, not a global schedule. Three rules follow:

- **Two robots travelling opposite ways pass**, because they are in different lanes 1.5 m
  apart. Two travelling the same way queue instead of overtaking.
- **An arm may only leave its transport envelope inside an end zone**, which the robot
  holds exclusively. This is the rule that fixes the original defect.
- **A robot with no work parks on its dock**, releasing the end zone so the other robot can
  deliver into the station it serves. Without this the cell deadlocks on the first
  handover — an idle robot standing by its table blocks the delivery it is waiting for.

The docks sit in a southern layby rather than beside the stations, because a robot parked
next to a station is close enough to foul a robot turning into it.

### What else is simulated

- **Trapezoidal travel** — accelerate, cruise, brake into the stop, with the heading
  following the path.
- **Speed and separation** — travel speed drops near the other robot, and the cell
  emergency stop now halts motion rather than only blinking the beacon.
- **Battery and charging** — charge drains with distance; below the threshold the robot
  finishes its delivery, docks, charges and resumes.
- **Grip faults** — a seeded slip occasionally drops a part; the robot re-picks it off the
  floor. Deterministic, so the recovery path is testable.
- **KPIs** — parts moved, cycle count, last and average cycle time, utilisation, fault
  count.

The invariants are asserted rather than assumed: `CellChoreographyTests` runs ten minutes
of simulated time and checks on every 50 ms step that the oriented footprints never
overlap, that a zone is never doubly occupied, that arms only deploy in an end zone, that
parts are conserved, and that a carried part sits exactly on the tool centre point.

### Where the part actually is

A carried part's pose is computed by forward kinematics from the same six axis values the
server publishes (`RobotKinematics`), not from the script that produced them. That is what
keeps it welded to the gripper: if the arm and the part were computed independently they
would drift apart. `RobotArmSolver` is checked against that same forward kinematics, so the
arm provably reaches each slot to within a micrometre.

It is computed from the platform pose that was **last published**, not the one the
simulation currently holds. The platform and the workpiece leave the server as separate
bindings, and publishing them from loops running at different rates floated the part a
quarter of a metre out in front of the jaws while the robot drove — three part widths, and
the first thing anyone notices in a viewport. `RobotOpenUsdE2eTests` rebuilds the tool
centre point from the values authored into the scene and asserts a carried part sits on it.

The buffers are worked **first-in-first-out**. Collecting the lowest free slot instead
starves any part that never lands in it: with two parts seeded on `WorkTableA`, the second
sat untouched for the life of the process while the robots shuttled the other one past it.

### Editing a live-bound prim

Every prim the server drives declares **exactly one** `matrix4d xformOp:transform` and
orders only that op — the parts and the gripper jaws included. A viewer composes the
translate and rotate ops it receives into that single matrix, and an op order declared in a
referenced asset sits in a weaker layer than the one a connector edits, so it could not be
cleared from there. `RobotAssetContractTests` asserts this for every bound prim.

The gripper jaws are one Xform each, owning their carrier *and* their finger. Driving the
carrier alone would slide it out from under the finger it is bolted to.

### Dynamic components need a NodeVersion

Part 5 §9.32.2 allows only a node that carries a **`NodeVersion`** property to trigger a
`ModelChangeEvent`, and `AsyncCustomNodeManager` drops entries for nodes that lack one. A
`Dynamic` component binding is reconciled from those events, so the node whose references
change — here the robot the tool is mounted on — has to expose `NodeVersion`, via
`EnableModelChangeTracking`. Without it the mount is filtered out and no client is ever told
the gripper appeared.

The failure this caused was quietly asymmetric, which is worth knowing about. **Removal**
kept reporting, because a deleted node is no longer in the manager's index and so takes the
"not mine, pass it through" branch of the filter; only **addition** was dropped. A connector
that happened to start while the tool was mounted therefore looked correct and could still
deactivate it, while one that started in the six-second gap never composed the tool at all
and never recovered.

`DynamicToolIsComposedAsync` asserts the transition — the prim observed both active and
inactive across more than one full cycle — rather than a momentary state, so it cannot pass
on a connector that only ever hears about one direction.

### The opening view

The cell authors `/Cell/OverviewCamera` and a connector opens on it, because framing the
bounds of an enclosed scene automatically puts the eye inside the fence, looking at whichever
robot happens to be nearest. The **first** camera in the served root layer wins, so the
establishing shot is authored before `/Cell/TopDownCamera`; `--camera` overrides the choice
outright. `RobotAssetContractTests` asserts the ordering.

## Ideas for more realism

Natural extensions that are deliberately *not* implemented, roughly in order of value:

- **Speed-and-separation monitoring** to ISO 10218 / TS 15066, with a modelled protective
  field, replacing the simple proximity speed scale.
- **Sensor cones** on the existing `ScannerFront` / `ScannerRear` prims, with detections
  driving the slowdown instead of omniscient positions.
- **A tool changer** — dock the gripper and pick up a different end-effector, which the
  dynamic-component binding already demonstrates the mechanics for.
- **An inspection station** that rejects a part, giving the routing a real branch.
- **Conveyor infeed and outfeed** instead of a fixed part population.
- **Operator gate interlock** — opening the front gate stops the cell.
- **Joint torque and current telemetry**, plus thermal drift, so axes carry more than
  position.
- **Maintenance counters** (distance travelled, grip cycles) feeding a service alarm.
- **Time-sampled pose history** so a viewer can scrub the last cycle rather than only
  following live values.
- **Traffic priority** — today a blocked robot simply waits; a real fleet manager would
  weigh deadlines and re-route.


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
x = ±3.4 m, y = ±2.2 m, plus a controller cabinet, two transfer stations, two charging
docks and a stack light.

### Mobile platform

Each arm rides on an omnidirectional AGV (`/Robot/MobileBase`, 1.10 × 0.72 m, 365 mm deck)
in the style of a KUKA KMP: orange chassis, dark skirt, four wheels, diagonally opposed
safety scanners, bumpers and status strips. The RSL scenario drives each robot's spatial
location across the cell, so the arm has to be carried rather than bolted to the floor.

> **Platform clearance is not enough on its own.** An earlier version of this sample ran
> the two robots on independent figure-eight paths and claimed the footprints cleared. They
> did — by 1.2 m at closest approach — but the *arms* reach 1.611 m, so they swept straight
> through each other. Clearance has to be argued about the deployed arm, not the chassis,
> which is why the cell is now zoned and the arm must be stowed to travel.

To floor-mount the arm instead, delete `/Robot/MobileBase` and clear the `xformOp:translate`
on `/Robot/Base`; nothing else depends on it.

### Editing the assets

The prim paths and rotate ops above are the **binding contract**: `RobotCell.cs` addresses
them by name, and the connector writes each joint rotate op as a scalar `double`. Link
offsets, geometry, materials and lighting are all free to change; the paths, op names,
`xformOpOrder` entries, `/Robot/Base/.../Flange`, `/Robot/Warning`, `/Cell/SafetyBeacon`,
`/Cell/Parts/PartNN`, `/Gripper/Jaw{Upper|Lower}`, `/Cell.inputs:speedOverride` and the
`R1`/`R2` mount attributes are not.

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
dotnet run --project samples/Robotics/MinimalRobotServer -- --host localhost --port 62830
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
USD assets, descriptor, writer, and a step-by-step guide live alongside the OpenUSD
binding specification, under its `examples/robotics/` directory.

To watch the cell animate without leaving the connector, install the optional
`Opc.Ua.OpenUsd.Connector.Viewer` assembly beside it and add `--view`:

```
dotnet run --project tools/Opc.Ua.OpenUsd.Connector -- \
    --server opc.tcp://localhost:62830/MinimalRobotServer --insecure --view
```

That fetches this server's served asset closure, composes `stage.usda`, opens a viewport, and streams
the same subscribed values into both the override layer and the rendered stage, so the joints move on
screen as the simulation runs. See [`docs/OpenUsd.md`](../../../docs/OpenUsd.md#rendering-the-twin-live).

The robot motion is configured through `RobotMobilityOptions`. RSL
Position/Orientation values author `xformOp:translate` and
`xformOp:rotateXYZ`; GPOS longitude, latitude, and elevation author the
`inputs:longitude`, `inputs:latitude`, and `inputs:elevation` attributes. See
[`docs/Positioning.md`](../../../docs/Positioning.md).
