# IntentEnabledRobot

This sample is a small OPC UA Robot Intent server for one stationary UR5e-style arm. It publishes
`Server/RobotIntent/Controllers/UR5eIntentController`, the controller's frames, tools, locations,
axes, outputs, programs, description, safety state and OpenUSD representation.

## Run

```powershell
dotnet run --project samples\Robotics\IntentEnabledRobot\IntentEnabledRobot.csproj -- --host localhost --port 62840
```

Default endpoint: `opc.tcp://{host}:{port}/IntentEnabledRobot` with port `62840`.
The default host is `localhost` so the generated server certificate matches local clients. Stop the
server before rebuilding the sample; the running process holds its assemblies open.

For demonstration only, the sample maps anonymous users to the well-known Operator role so the command
methods can be exercised without user-management setup. A production server should keep the Robot
Intent command methods role-restricted and grant Operator only to authenticated operator identities.

## Address space

The standalone Robot Intent node manager creates `Server/RobotIntent/Controllers`. The sample adds
one `IntentControllerType` named `UR5eIntentController` with:

- frames `World`, `Base`, `MechanicalInterface` and `GripperTcp` linked by `HasFrameParent`;
- six revolute axes `J1`..`J6`, indices `0`..`5`, with limits of +/-360 degrees except J3 at +/-180
  degrees to match the simulated kinematics file;
- one fitted tool `ParallelGripper` whose TCP frame is the `Tool` role frame;
- locations `Bin`, `Fixture`, `Inspect` and `Handoff`;
- outputs `GripperOpen` and `BenchLight`;
- programs `Home` and `PickAndPlace`;
- a robot description with a six-joint kinematic chain, reach radius 0.85 m and payload limit 5 kg.

## Capabilities

The server truthfully separates host capabilities from executor capabilities. `MissionsSupported`,
`MissionHorizonSupported` and `MissionBranchingSupported` are true because `IntentControllerHost`
implements mission submission, updates, cancellation, horizon release and branching step graphs; the
executor receives only the individual released intent steps. `TrajectorySupported` is true because the
simulated executor accepts a complete `TrajectoryIntentDataType`, follows its timed points and reports
trajectory deviation. `BlendingSupported` and `ForceControlSupported` are false because the simulation
neither blends motions nor regulates contact force.

The generated Robot Intent methods pass the `StopMode` argument to the executor. The simulation
differentiates it as application stop urgency only: `QuickStop` decelerates with the configured
acceleration bound, `ProcessStop` uses a moderate deceleration, `OnPath` uses a gentler deceleration
along the path, and `EndOfCycle` / `EndOfInstruction` finish the current simulated motion segment.
These modes do not select or imply IEC 60204-1 stop categories.

## OpenUSD mapping

The stage serves `Bench.usda` as the root layer and `arm.usda` / `gripper.usda` as referenced assets.
Viewer clients discover picked target prims through OpenUSD live bindings, not a hardcoded table.
Each `OpenUsdRepresentation` is mounted on its represented Object with `HasAddIn` through the shared
`CreateRepresentation` authoring helper. Browse `Server/OpenUSD/Representations`; for each representation, browse its live bindings
and read the binding with `SourceSemanticId = "RobotIntent.Location"`. Its `TargetPrimPath` is the
USD prim and its `SourceNodeId` is the corresponding `LocationType` NodeId.

| USD prim (`TargetPrimPath`) | `SourceNodeId` points to | `Pose` in frame `world` | Quaternion (x, y, z, w) |
| --- | --- | --- | --- |
| `/World/Targets/Bin` | `Bin` | `(0.41, -0.28, 0.829)` | `(0, 0, 0, 1)` |
| `/World/Targets/Fixture` | `Fixture` | `(0.48, 0.26, 0.829)`, rz 25� | `(0, 0, 0.2164396, 0.9762960)` |
| `/World/Targets/Inspect` | `Inspect` | `(-0.25, 0.30, 0.829)`, rz -20� | `(0, 0, -0.1736482, 0.9848078)` |
| `/World/Targets/Handoff` | `Handoff` | `(-0.46, -0.26, 0.829)`, rz 40� | `(0, 0, 0.3420201, 0.9396926)` |

The location poses are authored in the `World` Robot Intent frame and numerically match the USD target
coordinates; the arm `Base` frame is also a child of `World`, so clients can transform consistently.

## Target moves

The four published puck poses are reachable, but a straight Cartesian line between arbitrary pucks can
cross a joint-limit boundary in this compact UR5e-style simulation. To make the click-to-target sample
deterministic, the safety-aware sample executor handles a submitted `LinearMoveIntentDataType` as a
cell-level target move: it first performs an internal joint move to a short pre-target approach pose,
then executes the requested linear move into the published target pose. The externally visible command
is still the client's linear intent and the final `Result` reports the published target pose; the
internal joint segment demonstrates why Robot Intent exposes both joint and linear motion semantics.

## Safety demonstration

Type commands into the server console:

- `stop` trips a simulated protective stop; submitted intents fail with `NotPermittedInMode`.
- `limit 0.05` enables a safe speed limit; faster Cartesian motions fail with `SafetyLimitExceeded`.
- `reset` returns the simulated safety source to nominal.

The safety variables are read-only reports and there is no command method that clears a stop or
changes a safe-motion function.
