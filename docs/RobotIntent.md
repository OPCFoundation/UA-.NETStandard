# OPC UA — Robot Intent

> **Status: draft companion model.** The namespace `http://opcfoundation.org/UA/RobotIntent/` and every
> NodeId in it are **provisional**. This implements the working-group draft
> [*OPC UA — Robot Intent*](https://github.com/marcschier/opcua-drafts/blob/robot-intent-spec/metaverse-specs/robot-intent/OPC-UA-Robot-Intent.md);
> nothing here is official or endorsed by the OPC Foundation. Do not deploy it on a production robot
> and expect the identifiers to survive.

OPC 40010 describes a robot in detail — its motion device system, its axes, its power trains, its
controller, its safety states — and defines **no motion verbs at all**. Its whole actuation surface is
`Start`, `Stop` and loading a named program. A conformant client can discover everything about a
robot's construction and cannot ask it to move anywhere.

Robot Intent supplies the verbs, and only the verbs, so the two compose rather than compete:

* [`Opc.Ua.Robotics`](../src/Opc.Ua.Robotics) carries the source-generated model, the executor
  contracts, and the Annex C pose maths.
* [`Opc.Ua.Robotics.Server`](../src/Opc.Ua.Robotics.Server) carries the execution engine, the address
  space builders and the hosting integration.
* [`Opc.Ua.Robotics.Client`](../src/Opc.Ua.Robotics.Client) carries discovery, the awaitable operation
  handle, command authority, missions and the fluent intent builders.

The NodeSet declares exactly one `RequiredModel` — the base OPC UA namespace — so a server can adopt
Robot Intent without pulling in OPC 40010, OPC 10000-100 DI, or anything else.

## Packages

* `OPCFoundation.NetStandard.Opc.Ua.Robotics` — source-generated OPC 40010/IA and draft Robot Intent
  models, generated NodeIds/DataTypes/ObjectType clients, the `IIntentExecutor` contract,
  `IntentExecution`, `IIntentProgress`, `IntentOutcome`, `PoseMath` and `FrameTree`.
* `OPCFoundation.NetStandard.Opc.Ua.Robotics.Server` — Robot Intent node manager, `AddRobotIntent` /
  `ConfigureRobotIntent` hosting extensions, the `IntentControllerHost`, fluent
  `IIntentControllerBuilder`, safety binding, real-time channel declarations and facet calculation.
* `OPCFoundation.NetStandard.Opc.Ua.Robotics.Client` — `RobotIntentClient`,
  `RobotIntentControllerClient`, command-authority and real-time-channel leases, awaitable
  `IntentOperationHandle`, missions and `RobotIntentBuilder`.

The generated types live in `Opc.Ua.RobotIntent`. The hand-written server APIs live in
`Opc.Ua.Robotics.Server` and `Opc.Ua.Robotics.Server.Builders`; the hand-written client APIs live in
`Opc.Ua.Robotics.Client.Intent`.

## Hosting a controller

The hosted path is the normal path for an application server. `AddRobotIntent` registers the standalone
node manager, the draft model provider and a rejecting executor. `AddRobotIntentExecutor<T>` replaces
that rejecting executor with the application implementation, and `ConfigureRobotIntent` runs after the
model and the `Server/RobotIntent/Controllers` root exist:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.RobotIntent;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;

HostApplicationBuilder host = Host.CreateApplicationBuilder(args);

host.Services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "IntentRobot";
        options.EndpointUrls.Add("opc.tcp://localhost:4840/IntentRobot");
    })
    .AddRobotIntent(options =>
    {
        options.InstanceNamespaceUri = "urn:example:intent-robot";
    })
    .AddRobotIntentExecutor<MyIntentExecutor>()
    .ConfigureRobotIntent(async (context, ct) =>
    {
        await context.AddIntentControllerAsync(
            "Arm1",
            ConfigureArmController,
            ct);
    });

await host.Build().RunAsync();
```

The direct-construction fallback is the same address space and the same builder, just without the
Generic Host pipeline. A server that constructs node managers itself can register
`RobotIntentNodeManagerFactory` directly, or construct `RobotIntentNodeManager` with explicit
`IRobotIntentModelProvider` and `RobotIntentServerOptions` services. Once the manager has created its
address space, call `CreateRobotIntentBuildContext` and use the same `AddIntentControllerAsync`
extension:

```csharp
using Opc.Ua;
using Opc.Ua.Robotics.Server;

var factory = new RobotIntentNodeManagerFactory(
    new IRobotIntentModelProvider[] { new RobotIntentModelProvider() }.ToArrayOf(),
    new RobotIntentServerOptions
    {
        InstanceNamespaceUri = "urn:example:intent-robot"
    });

// Register the factory with the server's node-manager registration mechanism.
// If you already have a RobotIntentNodeManager instance after address-space creation:
IRobotIntentBuildContext context = manager.CreateRobotIntentBuildContext(ct);
await context.AddIntentControllerAsync("Arm1", ConfigureArmController, ct);
```

That standalone path is intentionally useful: because the NodeSet requires only the base OPC UA
namespace, a machine can expose Robot Intent without OPC 40010, DI or the Robotics topology model. A
server that already owns an OPC 40010 node manager can instead use `ConfigureRobotIntentFor<TNodeManager>`
and then link a `MotionDeviceSystem` to the intent controller with `HasIntentController`. That inverse
reference is the structural evidence used to derive **RI-Interop-40010**.

## Declaring a robot

The controller builder makes the model declaration and the host declaration one thing. What the server
publishes under `Capabilities` is the contract `IntentControllerHost` enforces at submission time.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.RobotIntent;
using Opc.Ua.Robotics.Server.Builders;

static void ConfigureArmController(IIntentControllerBuilder controller)
{
    controller
        .WithOperationalMode(OperationalModeEnum.AutomaticExternal)
        .WithReady(true)
        .WithMaxQueueDepth(8)
        .Accepts<JointMoveIntentDataType>()
        .Accepts<LinearMoveIntentDataType>()
        .Accepts<CircularMoveIntentDataType>()
        .Accepts<GraspIntentDataType>(cancelSupported: false)
        .Accepts<ReleaseIntentDataType>()
        .Accepts<PickIntentDataType>(cancelSupported: false)
        .Accepts<PlaceIntentDataType>()
        .Accepts<ToolChangeIntentDataType>(cancelSupported: false)
        .Accepts<SetOutputIntentDataType>()
        .Accepts<CallProgramIntentDataType>()
        .Accepts<WaitIntentDataType>()
        .WithSafetyState(new MySafetySource());

    IIntentFrameBuilder world = controller.AddFrame(
        "World",
        "world",
        FrameRoleEnum.World,
        Pose("world", 0.0, 0.0, 0.0));
    IIntentFrameBuilder @base = controller.AddFrame(
        "Base",
        "robot-base",
        FrameRoleEnum.Base,
        Pose("world", 0.0, 0.0, 0.82),
        frame => frame.WithParent(world));
    IIntentFrameBuilder flange = controller.AddFrame(
        "Flange",
        "robot-flange",
        FrameRoleEnum.MechanicalInterface,
        Pose("robot-base", 0.0, 0.0, 0.18),
        frame => frame.WithParent(@base));
    IIntentFrameBuilder tcp = controller.AddFrame(
        "GripperTcp",
        "gripper-tcp",
        FrameRoleEnum.Tool,
        Pose("robot-flange", 0.0, 0.0, 0.12),
        frame => frame.WithParent(flange));

    controller.AddTool("ParallelGripper", tcp, fitted: true);

    for (uint index = 0; index < 6; index++)
    {
        controller.AddAxis($"J{index + 1}", index, AxisKindEnum.Revolute);
    }

    controller.AddLocation(
        "Bin",
        Pose("world", 0.45, -0.30, 0.82),
        location => location.WithOccupancy(false, capacity: 1));
    controller.AddLocation("Fixture", Pose("world", 0.50, 0.25, 0.82));

    controller.AddOutput("GripperOpen", DataTypeIds.Boolean, new Variant(true));
    controller.AddOutput("BenchLight", DataTypeIds.Boolean, new Variant(false));
    controller.AddProgram("Home", "home");
    controller.AddProgram("PickAndPlace", "pick-and-place");
    controller.AddRealTimeChannel(
        "JointTelemetry",
        "joint-telemetry",
        RealTimeTransportEnum.Udp,
        "udp://239.0.0.40:4840");

    controller.WithDescription(description => description
        .WithKinematicChain(CreateKinematicChain())
        .WithLimits(
            reachRadius: 0.85,
            payloadLimit: 5.0,
            maxCartesianSpeed: 0.25,
            maxCartesianAcceleration: 0.7));
}

static Pose3DDataType Pose(string frameId, double x, double y, double z)
{
    return new Pose3DDataType
    {
        FrameId = frameId,
        Position = new[] { x, y, z }.ToArrayOf(),
        Orientation = new[] { 0.0, 0.0, 0.0, 1.0 }.ToArrayOf()
    };
}

static ArrayOf<KinematicJointDataType> CreateKinematicChain()
{
    var joints = new KinematicJointDataType[6];
    for (int ii = 0; ii < joints.Length; ii++)
    {
        joints[ii] = new KinematicJointDataType
        {
            AxisId = $"J{ii + 1}",
            Kind = AxisKindEnum.Revolute,
            OriginTransform = Pose(ii == 0 ? "robot-base" : $"J{ii}", 0.0, 0.0, 0.12),
            AxisVector = new[] { 0.0, 0.0, 1.0 }.ToArrayOf()
        };
    }
    return joints.ToArrayOf();
}

public sealed class MySafetySource : IRobotIntentSafetySource
{
    public ValueTask<RobotIntentSafetySnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        return new ValueTask<RobotIntentSafetySnapshot>(new RobotIntentSafetySnapshot(
            SafeMotionFunctionEnum.None,
            EmergencyStopActive: false,
            ProtectiveStopActive: false,
            SafeSpeedLimitActive: false,
            SafeSpeedLimit: 0.0,
            SafetyControllerOk: true,
            LocalizedText.Null));
    }
}
```

The builder enforces the invariants that make the address space dependable. A tool can only point at a
TCP frame whose role is `Tool`, and at most one tool below a controller can be `Fitted=true`, because a
motion intent otherwise has no unambiguous active tool centre point. Axis indices must be unique and
contiguous from zero, because `JointMoveIntentDataType.JointTargets` is an array and the index is the
array coordinate. `Capabilities.AxisCount` is written from the number of axes, so a client can validate
a joint target vector before submitting. Every capability must include `BufferModeEnum.Aborting`, which
is the fail-safe "replace what is running" mode every controller must understand.

Declare only what the executor can really do. `Accepts<TIntent>()` is not documentation; it is the
admission rule. If a robot cannot abandon a tool change safely, declare
`Accepts<ToolChangeIntentDataType>(cancelSupported: false)` and implement `CanCancel` accordingly.

## Writing an executor

`IntentControllerHost` owns admission, queueing, the Part 10 state machine, cancellation acceptance and
the final result node. `IIntentExecutor` owns the doing: turning a typed intent into controller-specific
motion, reporting progress and returning an outcome.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.RobotIntent;

public sealed class MyIntentExecutor : IIntentExecutor
{
    public async ValueTask<IntentOutcome> ExecuteAsync(
        IntentExecution execution,
        CancellationToken cancellationToken)
    {
        switch (execution.Intent)
        {
            case LinearMoveIntentDataType linear:
                await MoveLinearAsync(linear.Target, execution.Progress, cancellationToken);
                return IntentOutcome.SucceededAt(linear.Target);

            case JointMoveIntentDataType joint when joint.HasJointTargets:
                await MoveJointsAsync(joint.JointTargets, execution.Progress, cancellationToken);
                return IntentOutcome.Success;

            case WaitIntentDataType wait:
                await Task.Delay(TimeSpan.FromMilliseconds(wait.Duration), cancellationToken);
                return IntentOutcome.Success;

            default:
                return IntentOutcome.Fail(
                    IntentFailureEnum.CapabilityNotSupported,
                    "The executor does not implement this intent.");
        }
    }

    public bool CanCancel(IntentExecution execution)
    {
        return execution.Intent is not ToolChangeIntentDataType;
    }

    private static async Task MoveLinearAsync(
        Pose3DDataType target,
        IIntentProgress progress,
        CancellationToken cancellationToken)
    {
        for (int step = 1; step <= 20; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.ReportProgress(step / 20.0);
            progress.ReportPose(target);
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
    }

    private static async Task MoveJointsAsync(
        ArrayOf<double> joints,
        IIntentProgress progress,
        CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        progress.ReportProgress(1.0);
    }
}
```

The cancellation token is signalled after a cancel has been accepted and the operation has entered
`Cancelling`. The executor then brings motion to a controlled end and returns; it does not need to
manufacture a `Cancelled` result, because the host records that terminal state. `CanCancel` is the
per-operation hook for refusing a cancel that would leave the cell in a worse state, such as a tool
change mid-exchange.

## Client: submit and await

The high-level client discovers controllers under `Server/RobotIntent/Controllers`, reads their
capabilities, takes command authority, builds a typed intent and returns an awaitable operation handle.

```csharp
using System;
using Opc.Ua;
using Opc.Ua.RobotIntent;
using Opc.Ua.Robotics.Client.Intent;

RobotIntentClient discovery = session.RobotIntent(telemetry);
ArrayOf<RobotIntentNodeLookupEntry> controllers =
    await discovery.DiscoverControllersAsync(ct);

RobotIntentControllerClient controller = discovery.Controller(controllers[0].NodeId);
RobotIntentControllerInfo info = await controller.ReadAsync(ct);

await using CommandAuthorityLease authority =
    await controller.RequestAuthorityAsync(ct);
if (!authority.Granted)
{
    throw new InvalidOperationException($"Command authority is held by {authority.CurrentOwner}.");
}

Pose3DDataType target = RobotIntentBuilder.Pose(
    x: 0.45,
    y: -0.30,
    z: 0.82,
    qx: 0.0,
    qy: 0.0,
    qz: 0.0,
    qw: 1.0,
    frameId: "world");

LinearMoveIntentDataType intent = RobotIntentBuilder
    .LinearMove(target, speed: 0.2)
    .WithIntentId("move-to-bin")
    .WithBufferMode(BufferModeEnum.Aborting)
    .Build();

await using IntentOperationHandle handle =
    await controller.SubmitIntentAsync(intent, ct);
IntentResultDataType result = await handle.Completion;
Console.WriteLine($"{handle.IntentId} ended with {handle.Current.ExecutionState}: {result.Failure}");
```

Use `TrySubmitIntentAsync` when refusal is part of the normal control flow and you want the fixed
`IntentFailureEnum` rather than an exception:

```csharp
IntentSubmissionResult submission = await controller.TrySubmitIntentAsync(intent, ct);
if (!submission.Accepted)
{
    Console.WriteLine($"Refused: {submission.Failure} {submission.Message.Text}");
    return;
}

await using IntentOperationHandle handle =
    await controller.TrackOperationAsync(submission.IntentId, submission.Operation, ct);
```

`IntentOperationHandle.StartAsync` subscribes to `ExecutionState`, `Progress`, `CurrentPose` and
`Result`. The `Changed` event is the convenient way to update a UI:

```csharp
handle.Changed += snapshot =>
{
    Console.WriteLine($"{snapshot.ExecutionState} {snapshot.Progress:P0}");
    Pose3DDataType current = snapshot.CurrentPose;
    _ = current.FrameId;
};
```

`CurrentPose` is a status report at the Subscription's sampling and publishing rate. It is deliberately
not a servo channel; using it to close a motion-control loop is outside the Robot Intent model and
outside OPC UA client/server timing guarantees.

## Client: missions

A mission builder emits the same `IntentDataType` structures used for single submissions. Released
steps form the base; unreleased steps form the horizon. A horizon update replaces the unreleased
suffix while preserving the released prefix.

```csharp
MissionDataType mission = RobotIntentBuilder.Mission("tray-42")
    .WithMissionUpdateId(1)
    .ReleasedStep("approach", RobotIntentBuilder
        .LinearMove(RobotIntentBuilder.Pose(0.40, -0.20, 0.90, 0, 0, 0, 1, "world"), 0.2)
        .Build())
    .ReleasedStep("pick", RobotIntentBuilder.Pick(
        info.Lookups.Locations[0].NodeId,
        info.Lookups.Tools[0].NodeId).Build())
    .HorizonStep("place", RobotIntentBuilder.Place(
        info.Lookups.Locations[1].NodeId,
        info.Lookups.Tools[0].NodeId).Build())
    .Build();

MissionSubmissionResult submitted = await controller.SubmitMissionAsync(mission, ct);
if (!submitted.Accepted)
{
    Console.WriteLine($"Mission refused: {submitted.Failure} {submitted.Message.Text}");
    return;
}

ArrayOf<MissionStepDataType> revisedHorizon = new[]
{
    new MissionStepDataType
    {
        StepId = "place",
        SequenceId = 3,
        Released = false,
        Intent = RobotIntentBuilder.Place(
            info.Lookups.Locations[1].NodeId,
            info.Lookups.Tools[0].NodeId).Build()
    }
}.ToArrayOf();

MissionUpdateOutcome update =
    await controller.UpdateMissionAsync("tray-42", 2, revisedHorizon, ct);
switch (update.Result)
{
    case MissionUpdateResultEnum.Accepted:
        break;
    case MissionUpdateResultEnum.Outdated:
        await controller.ReadAsync(ct);
        break;
    case MissionUpdateResultEnum.BaseConflict:
        Console.WriteLine("The update changed a released step; rebuild from the current base.");
        break;
    case MissionUpdateResultEnum.UnknownMission:
        Console.WriteLine("The mission has already ended or was never admitted.");
        break;
    case MissionUpdateResultEnum.Rejected:
        Console.WriteLine(update.Message.Text);
        break;
}
```

Transitions turn the flat list into a step graph. `MissionCondition.Always()` creates the empty
`ContentFilter` that OPC UA defines as true; an empty `Transitions` array leaves the mission as a flat
sequence.

```csharp
MissionDataType branched = RobotIntentBuilder.Mission("inspect-or-rework")
    .WithMissionUpdateId(1)
    .ReleasedStep("inspect", RobotIntentBuilder.CallProgram(info.Lookups.Programs[0].NodeId).Build())
    .HorizonStep("accept", RobotIntentBuilder.Wait(100).Build())
    .HorizonStep("rework", RobotIntentBuilder.CallProgram(info.Lookups.Programs[1].NodeId).Build())
    .Transition("inspect", "accept", DivergenceKindEnum.Alternative, MissionCondition.Always())
    .Transition("inspect", "rework", DivergenceKindEnum.Alternative)
    .ErrorPolicy("rework", ErrorPolicyEnum.Retry)
    .Build();
```

## Client: cancellation, pause and retry

Cancellation is refusal-aware. A server may legitimately refuse because the executor's `CanCancel`
returned false or because the session does not hold command authority. `Cancelling` is not terminal;
wait for the operation handle to reach `Cancelled`, `Succeeded`, `Failed` or `Retriable`.

```csharp
IntentCommandOutcome cancel =
    await handle.CancelAsync(StopModeEnum.QuickStop, ct);
if (!cancel.Accepted)
{
    Console.WriteLine("The server refused this cancel request.");
}

IntentCommandOutcome pause = await handle.PauseAsync(ct);
if (pause.Accepted)
{
    await handle.ResumeAsync(ct);
}

IntentResultDataType final = await handle.Completion;
if (handle.Current.ExecutionState == ExecutionStateEnum.Retriable)
{
    IntentSubmissionResult retry = await handle.RetryAsync(ct);
    if (retry.Accepted)
    {
        await using IntentOperationHandle retryHandle =
            await controller.TrackOperationAsync(retry.IntentId, retry.Operation, ct);
        final = await retryHandle.Completion;
    }
}
```

## Pose maths

`PoseMath` implements Annex C conversion between Robot Intent's `(x, y, z, w)` quaternion and the core
OPC UA `ThreeDFrame` A/B/C orientation:

```csharp
Pose3DDataType pose = RobotIntentBuilder.Pose(
    0.4, 0.2, 0.8,
    0.0, 0.0, 0.3826834323650898, 0.9238795325112867,
    "world");

ThreeDFrame frame = PoseMath.ToThreeDFrame(pose);
Pose3DDataType roundTripped = PoseMath.FromThreeDFrame(frame, "world");

if (!PoseMath.TryValidate(roundTripped, 1e-6, out string? error))
{
    throw new InvalidOperationException(error);
}
```

`FrameTree` is the corresponding helper for re-expressing poses through a declared frame graph:

```csharp
var frames = new FrameTree();
frames.TryAdd(
    "world",
    "",
    RobotIntentBuilder.Pose(0, 0, 0, 0, 0, 0, 1, "world"),
    FrameRoleEnum.World,
    out _);
frames.TryAdd(
    "robot-base",
    "world",
    RobotIntentBuilder.Pose(0.5, 0.0, 0.0, 0, 0, 0, 1, "world"),
    FrameRoleEnum.Base,
    out _);

Pose3DDataType inBase = RobotIntentBuilder.Pose(
    0.1, 0.0, 0.2,
    0.0, 0.0, 0.0, 1.0,
    "robot-base");

if (frames.TryExpress(inBase, "world", out Pose3DDataType inWorld, out string? frameError))
{
    Console.WriteLine(inWorld.Position[0]);
}
```

## Handling refusal

A refusal is an ordinary method outcome: the Method call returns `Good`, `Accepted` is false and the
failure is in the output arguments. A Bad `StatusCode` still means the transport, Session or Service
layer failed. The point of the small failure set is that the client can choose a policy without parsing
human text:

```csharp
IntentSubmissionResult submission = await controller.TrySubmitIntentAsync(intent, ct);
if (!submission.Accepted)
{
    switch (submission.Failure)
    {
        case IntentFailureEnum.QueueFull:
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            break;
        case IntentFailureEnum.ParameterInvalid:
        case IntentFailureEnum.JointLimit:
        case IntentFailureEnum.WorkspaceLimit:
            Console.WriteLine("Re-plan with a reachable target.");
            break;
        case IntentFailureEnum.ControlNotOwned:
        case IntentFailureEnum.NotPermittedInMode:
        case IntentFailureEnum.SafetyLimitExceeded:
            Console.WriteLine($"Operator action required: {submission.Message.Text}");
            break;
        default:
            Console.WriteLine(submission.Message.Text);
            break;
    }
}
```

## Facets in code

A controller publishes the facets it claims in the read-only `Capabilities.SupportedFacets`, so a
client reads the claim rather than reconstructing it. The server binds that variable to the facet
calculator, so the list is recomputed on every read and tracks the address space instead of being a
registration-time snapshot:

```csharp
ArrayOf<string> facets = controller.ComputeFacets();
ArrayOf<string> published = RobotIntentFacetCalculator.Compute(controller.State);
```

On the client, `ReadAsync` returns what the server published:

```csharp
RobotIntentControllerInfo info = await controller.ReadAsync(ct);
if (info.SupportedFacets.Contains("RI-Mission-Horizon"))
{
    Console.WriteLine("The controller accepts missions with horizon updates.");
}
if (!info.Facets.EveryCapabilitySupportsAborting)
{
    throw new InvalidOperationException("The server published an invalid capability set.");
}
```

`info.Facets` remains available as a convenience projection of the individual capability variables,
and against a server that predates `SupportedFacets` it is all there is. Prefer `SupportedFacets`
where the server publishes it: the projection can only see the flags, so it necessarily disagrees with
the server about any facet whose requirements go beyond a single flag.

That disagreement is the reason `SupportedFacets` exists. A facet is not a restatement of the
declaration a client has already read. Some of what the table below requires — that blending modes are
honoured, that the refusal rules are followed, that a mission base is immutable — cannot be settled by
reading the address space at all, so a client deriving facets locally is guessing at precisely the
rows that matter most. A published claim that could drift from the model would reintroduce the same
defect with the server's authority behind the wrong answer; the live read binding makes that drift
impossible by construction.

## Limitations

The current stack implements the draft information model, admission rules, Part 10 operation lifecycle,
missions, command authority, cancellation, safety observation, capability/facet reporting, real-time
channel leasing and the client handles shown above. It does not provide a safety-rated interface, a
servo-level real-time channel, or a vendor robot driver. The facet calculator checks every structural
requirement in clause 12.2, but behavioural requirements remain the server's attestation and require
interop or acceptance testing to verify. The sample executor is a simulator, transition conditions are
only as powerful as the server-supplied `ConditionEvaluator`, and real-time channels are brokered as
leases rather than implemented as a cyclic transport in this package. The namespace
`http://opcfoundation.org/UA/RobotIntent/` and all NodeIds remain provisional until the companion
specification is ratified.

## Why a submission is not a method call

An OPC UA `Call` cannot stay open for the length of a real motion. Session timeouts, SecureChannel
re-keying and transport timeouts all bound it, and OPC 10000-4 §5.12.2 is explicit that when the
Session ends the method result is discarded *"independent of the task actually performed at the
Server"*. A synchronous method that commands a robot is therefore not merely inelegant: it loses the
outcome of work that has **already physically happened**. The robot keeps moving after the answer has
been thrown away.

OPC 10000-10 gives the OPC Foundation's own resolution — a Method performs a calculation, a
**Program** runs a batch process or a machine-tool part program. So `SubmitIntent` returns as soon as
the intent is **admitted**, and what it returns is a NodeId: an `IntentOperationType` instance, a
Part 10 program instance created for that submission, which the client subscribes to for progress and
reads for the result.

Building on `ProgramStateMachineType` buys four things this model then does not have to invent:
transition events, a terminal result object that survives the operation, invocation diagnostics
recording which Session commanded what, and a lifetime model for the instance itself. Two of those —
`FinalResultData` and `ProgramDiagnostic` — are Optional in Part 10, and Robot Intent promotes both to
**Mandatory**, because a `shall` that rests on a member a conformant server may omit is not a
requirement.

## The intent hierarchy

Intents are a **DataType hierarchy**, not one Method per verb.

```
IntentDataType (abstract)
├── MotionIntentDataType (abstract)
│   ├── JointMoveIntentDataType        movej / MoveJ / PTP / J / MOVJ
│   ├── LinearMoveIntentDataType       movel / MoveL / LIN / L / MOVL
│   ├── CircularMoveIntentDataType     movec / MoveC / CIRC / C / MOVC
│   ├── TrajectoryIntentDataType       a time-parameterised path, handed over whole
│   ├── CartesianPathIntentDataType    a taught path with per-waypoint blending
│   ├── ForceIntentDataType            move until contact
│   └── ProcessIntentDataType (abstract)
│       ├── ArcWeldIntentDataType          SpotWeldIntentDataType
│       ├── DispenseIntentDataType         FastenIntentDataType
│       └── PalletiseIntentDataType        SurfaceFinishIntentDataType
├── GraspIntentDataType / ReleaseIntentDataType
├── PickIntentDataType / PlaceIntentDataType
├── ToolChangeIntentDataType
├── SetOutputIntentDataType
├── CallProgramIntentDataType
└── WaitIntentDataType
```

Three consequences follow, and each is why the shape was chosen:

* **A single intent and a mission step are the same thing.** `MissionStepDataType.Intent` is an
  `IntentDataType`, so nothing has to be expressed twice.
* **Extension is subtyping.** A vendor adds an intent by deriving from `IntentDataType`. It is then
  carried, queued, cancelled and reported by the existing machinery without a new Method.
* **Discovery is a read, not a probe.** `IntentCapabilitiesType.SupportedIntents` names each accepted
  DataType, so a client learns what a robot accepts by reading one Variable rather than by browsing
  for BrowseNames and inferring support from their presence.

## Poses, frames and units

`Pose3DDataType` carries a `FrameId`, a `Position` of three doubles in **metres**, and an
`Orientation` of four doubles forming a **unit quaternion ordered (x, y, z, w)**.

Four rules make that unambiguous, and the server enforces all of them:

1. Every frame is **right-handed**.
2. Units are fixed by the specification and are **not** negotiable per instance: position in metres,
   joint targets in radians for a `Revolute` axis and metres for a `Prismatic` one, force in newtons,
   durations in milliseconds. `Pose3DDataType` appears as a Method argument, where no `EUInformation`
   property can reach it, so a per-instance unit would be undeliverable.
3. `Orientation` must be normalised. A quaternion whose norm differs from 1 by more than `1e-6` is
   rejected with `ParameterInvalid`.
4. `FrameId` names a `CoordinateFrameType` instance under the controller's `Frames` folder. An empty
   `FrameId` means the server's default work frame.

Quaternions are used because OPC UA defines no quaternion DataType anywhere, and because the `A`, `B`
and `C` fields of the core `ThreeDOrientation` carry no convention of their own. `PoseMath` implements
the specification's Annex C conversion in both directions, including the two properties that are
normative and easy to get wrong:

* the `asin` argument is **clamped** to `[-1, +1]`, because floating-point error at a pole otherwise
  turns a legal orientation into a domain error;
* `q` and `-q` denote the same orientation, and the conversion emits the representative whose `w` is
  non-negative, so two servers describing one orientation produce the same four numbers.

`FrameTree` composes the transforms along the path between two frames, so a pose given in one frame can
be re-expressed in another.

## The lifecycle

The Part 10 state machine carries the coarse state and generates the events. `ExecutionState` refines
it, because `Queued`, `Cancelling` and the three distinct terminal outcomes cannot be told apart from
`CurrentState` alone. The pairing is exhaustive — a combination not in this table is not legal:

| `ExecutionState` | Part 10 state | Meaning |
|---|---|---|
| `Accepted` | `Ready` | Admitted and validated; not yet queued or executing. |
| `Queued` | `Ready` | Waiting behind another intent. `QueuePosition` is non-zero. |
| `Executing` | `Running` | Commanding the robot now. |
| `Suspended` | `Suspended` | Paused; position retained. |
| `Cancelling` | `Running` | A cancel was accepted; motion is being brought to a controlled end. |
| `Succeeded` | `Halted` | Terminal. Completed as requested. |
| `Failed` | `Halted` | Terminal. `Result.Failure` carries the reason. |
| `Cancelled` | `Halted` | Terminal. Ended early because a cancel was accepted. |
| `Retriable` | `Halted` | Terminal for now; `Retry` may re-attempt it. |

`Cancelling` is **not** terminal. A client that treats acceptance of a cancel as the end of motion acts
too early.

### Refusal is an ordinary outcome

`SubmitIntent` refuses in a fixed order, and the order matters — a caller that lacks authority must be
told *that*, not that its parameters are wrong:

1. `ControlNotOwned` — the calling Session does not hold command authority.
2. `NotPermittedInMode` — `OperationalMode` is not `Automatic` or `AutomaticExternal`.
3. `CapabilityNotSupported` — the intent's DataType is not among `SupportedIntents`, or its
   `BufferMode`/`BlockingMode` is not among those the capability entry permits.
4. `ParameterInvalid` — a parameter is missing, malformed or out of range.
5. `QueueFull` — admitting it would exceed `MaxQueueDepth`.

A refusal creates no operation instance and moves nothing. It is reported in the **output arguments** —
`Accepted` false with a `Failure` and a `Message` — and the call returns `Good`. A Bad `StatusCode`
still means what it always meant: the transport, the Session or the Service layer failed. The
distinction is normative, and it is what makes the failure set diagnosable: a client decides whether to
retry, re-plan or escalate from the `IntentFailureEnum` value alone.

### Queueing and blending

`BufferMode` decides how a new submission relates to what is already executing. The values are
PLCopen's `MC_BufferMode`, adopted unchanged:

| Value | Meaning |
|---|---|
| `Aborting` | Abort what is executing and start immediately. The default, and always accepted. |
| `Buffered` | Queue; start when the predecessor succeeds. |
| `BlendingLow` / `BlendingPrevious` / `BlendingNext` / `BlendingHigh` | Queue, and do not decelerate to a stop at the boundary. |

Where blending occurs, the predecessor reaches `Succeeded` **when blending begins**, not when its
target is exactly attained, and its `Result.AchievedPose` records where the tool centre point was at
that moment. That is what PLCopen defines, and reporting it any other way would tell a client the robot
stopped somewhere it never was. A server that accepts a blending mode but executes it as `Buffered`
reports `BlendingSupported` false, so a client can tell a robot that blends from one that merely
tolerates being asked to.

`BlockingMode` is orthogonal and constrains concurrency rather than ordering — it is the VDA 5050
`blockingType` matrix. A server does not begin an intent whose `BlockingMode` is `Single` or `Hard`
while any other intent is executing.

### Cancellation is not the `Cancel` Service

The OPC UA `Cancel` Service in OPC 10000-4 §5.7.5 cancels an **outstanding service request**. It does
not stop the robot: it returns `Bad_RequestCancelledByClient` for that request and leaves the motion
running. Stopping a robot is `CancelIntent`, `CancelMission` or `CancelAll`.

A server **may refuse** a cancel and says so in the `Accepted` output. Some motions cannot be abandoned
part-way without leaving the cell worse than completing them would — a tool change mid-exchange, a
placement mid-release.

## Missions

A mission is an ordered sequence of intents submitted and tracked as a unit, so a supervisor can commit
work in advance and still change what has not yet been committed.

Every step carries `Released`. The released steps form a prefix called the **base**; the rest form the
**horizon**.

```
Step 0        Step 1        Step 2   │   Step 3        Step 4
released      released      released │   horizon       horizon
└────────── base: committed ─────────┘   └── revisable ──┘
```

The base is committed and immutable: the server assumes every released step is executing or already
executed and refuses any update that would alter, remove or reorder one. `UpdateMission` replaces the
horizon wholly and may release some of it, extending the base. `MissionUpdateId` must be strictly
greater than the mission's current value, which is what makes two updates that crossed in flight safe —
the later one wins and the earlier is rejected with `Outdated` rather than applied out of order. An
update is applied **atomically**.

Where a mission carries `Transitions`, it becomes the step-and-transition form of an IEC 61131-3
sequential function chart. Conditions are OPC UA `ContentFilter`s — the base specification's own filter
grammar, reused rather than invented — and `DivergenceKind` says whether exactly one transition is taken
(`Alternative`, evaluated in array order so two clients predict the same branch) or all of them are
(`Parallel`). Per-step `ErrorPolicy` covers `Abort`, `Retry`, `Skip`, `Fallback` and `Compensate`. An
empty `Transitions` array leaves the mission the flat sequence it was, which is what makes the graph an
addition rather than a replacement.

## Command authority

At most one Session at a time holds command authority over a controller, and only that Session may
submit. Authority is released automatically when the holding Session closes, so a crashed client does
not lock a robot permanently. Reading, browsing and subscribing require no authority: observation is
always permitted.

> Command authority arbitrates between OPC UA clients. It is **not** the single point of control that
> ISO 10218-2 requires — that concerns mutual exclusion of remote command and local manual control and
> is enforced by safety-rated means outside this interface. It is also **not authorisation**: a Session
> that holds authority but lacks the necessary Role is still refused.

## Safety, and the boundary that is never crossed

**This is a non-safety-rated application interface.** The Methods here are application-level requests.
They do not constitute, and must not be used as, safety functions as defined in IEC 61508, nor safety
communication as defined in IEC 61784-3 or IEC 62541-15.

This is a property of the technology, not a scoping preference. OPC 10000-15 carries cyclic safety data
from a SafetyProvider to a SafetyConsumer, and the consumer's request carries an identifier, a
monitoring number and one octet of explicitly **non-safety** flags — so a caller has no channel through
which to supply safety-rated arguments. Every safety fieldbus expresses a safety command as a
**continuously asserted cyclic signal**, because the integrity argument rests on the fail-safe state
that follows when assertion stops. A Method call has no defined behaviour when it stops being called,
and therefore cannot be a safety function however it is labelled.

What the model *can* do is observe and refuse. `SafetyStateType` reports what the safety system is
enforcing, and the server refuses a submission:

* with `SafetyLimitExceeded`, when `SafeSpeedLimitActive` is true and the intent's
  `Constraints.CartesianSpeed` exceeds `SafeSpeedLimit`;
* with `NotPermittedInMode`, when `EmergencyStopActive` or `ProtectiveStopActive` is true, when
  `SafetyControllerOk` is false, or when `OperationalMode` is not `Automatic` or `AutomaticExternal`.

Each of those is observable against a running server: assert a protective stop and a conformant server
refuses; lower `SafeSpeedLimit` below a submitted speed and it refuses.

**What none of that makes true.** These refusals are an application-layer courtesy performed by
non-safety-rated software. They reduce the number of requests the safety system has to reject; they are
not a protective measure.

* A client must not treat acceptance of an intent as evidence that the motion is safe.
* A client must not treat `SafeSpeedLimit` as a limit *this interface* enforces — the safety system
  enforces it, and would enforce it identically if this model did not exist.
* `StopMode` expresses urgency and selects **no** IEC 60204-1 stop category. A client that requires a
  category-rated stop obtains it from the safety system; it cannot be obtained here.
* The model may **observe** the safety system and **refuse** on what it sees. It may never
  **instruct** it: no Method commands a safe motion function, changes an operational mode, or clears a
  stop. `OperationalMode` is read-only, because mode selection is a safety function performed by a key
  switch or an interlock, and an interface that could change it from the network would defeat the
  arrangement it is reporting.

## What this interface carries, and what it brokers

OPC UA method invocation is not deterministic and completes in tens of milliseconds. Vendor real-time
channels run two to four orders of magnitude faster on dedicated transports. The model divides the work
rather than pretending the gap is not there.

**Carried here.** A trajectory, a Cartesian path or a force-controlled move is handed over *whole* and
run by the robot's own motion kernel. The round trip happens once, at submission, so transport latency
bounds how quickly work can be *started* and never how accurately it is *executed*. This is the shape of
`FollowJointTrajectory` in ROS and of the PLCopen buffered path function blocks, and it is why
trajectory execution belongs here while trajectory streaming does not.

**Brokered.** Where a client genuinely needs a high-rate channel — visual servoing, force tracking,
conveyor following — `RealTimeChannelType` describes one and `OpenRealTimeChannel` leases it. The
samples travel on that channel and never through this interface. Of the transports named (`Rtde`,
`Egm`, `Fri`, `Rsi`, `MotoRos2`, `OpcUaFx`, `Other`) only `OpcUaFx` is an OPC Foundation specification;
the rest are vendor channels the model describes without defining.

A lease lapses at `LeaseExpiry` unless renewed, and is released when the holding Session closes — the
same reasoning as command authority: a client that dies must not hold a resource for good. While a lease
is held, the server refuses motion intents with `CapabilityNotSupported` unless it can genuinely
arbitrate between the two sources, because two things commanding one robot with no arbitration is
exactly the failure that rule exists to prevent.

`IntentOperationType.CurrentPose` exists so a client can *watch* a motion. It is a status report
delivered at whatever rate the client's Subscription asks for, and using it to close a control loop is
outside this model.

## NodeIds in intents are untrusted input

Every NodeId-valued member of an intent is chosen by the client, so the server validates that each
resolves to a node of the expected type **under the controller being commanded**, and refuses with
`ParameterInvalid` otherwise. A NodeId that resolves to a node belonging to a different controller, or
to no node at all, is never acted on.

| Member | Resolves to |
|---|---|
| `PickIntentDataType.Source`, `PlaceIntentDataType.Destination`, `PalletiseIntentDataType.Pattern` | a `LocationType` under the controller |
| `MotionIntentDataType.ToolFrame`, `ForceIntentDataType.FrameId` | a `CoordinateFrameType` under the controller; `ToolFrame` additionally of `Role` `Tool` |
| `ToolChangeIntentDataType.Tool` | a `ToolType` under the controller, or null to release the fitted tool |
| `SetOutputIntentDataType.Output` | an `OutputSignalType` under the controller; `Value` must match that signal's own DataType |
| `CallProgramIntentDataType.Program`, `ProcessIntentDataType.ProcessProgram` | a `ProgramType` under the controller |
| `WaitIntentDataType.Signal` | an `OutputSignalType` under the controller, or a Boolean Variable under it |
| `FastenIntentDataType.Joint` | a joint in an OPC 40450/40451 model where one is implemented |

`CallProgramIntentDataType` deserves particular care because it runs code the server holds: it is
restricted to programs published as `ProgramType` instances, and a program identifier naming anything
else is refused.

Commanding is a privileged operation. Every Method here moves a machine that can injure people and
destroy property, so the server requires an authenticated Session and restricts the Methods of
`IntentControllerType` by Role, distinctly from read access to the same address space. Observing a robot
and commanding one are different privileges and are not conflated. `UserExecutable` is applied so a
client discovers what it may invoke before invoking it.

## Interoperating with OPC 40010

OPC 40010 describes the robot; Robot Intent commands it. The two are joined by one reference and are
otherwise independent — this model takes no dependency on the Robotics NodeSet, and a server
implementing only Robot Intent is fully conformant.

A server claiming the interop profile exposes a `HasIntentController` reference from the
`MotionDeviceSystemType` instance describing the robot to the `IntentControllerType` instance that
commands it, reports the same operational mode as the OPC 40010 model, publishes as `ProgramType`
instances exactly those programs the OPC 40010 task control can load, and expresses its poses in frames
consistent with the mounting and geometry OPC 40010 describes. The published **RI-Interop-40010** facet
is derived from the inverse of that reference on the intent controller, and because `SupportedFacets`
is a live read binding, the claim tracks the address space whether the reference is attached before or
after the controller is registered.

It does **not** duplicate OPC 40010's topology. `AxisType` exists here only to fix the order, kind and
limits a joint target needs; where OPC 40010 is also implemented its axis description is the fuller one
and **OPC 40010 decides**. `RobotDescriptionType.KinematicChain` is additive, because OPC 40010 defines
no kinematic chain an inverse-kinematics solver could use — and no tool centre point at all, which is
why `ToolType.TcpFrame` supplies the concept and has nothing in OPC 40010 to contradict.

## Facets

Only **RI-Base** is mandatory. A server implements the facets it can honour and declares the rest false;
a facet other than RI-Base is claimed only where every intent type it names appears in
`SupportedIntents`. Each controller lists what it claims in `Capabilities.SupportedFacets`, which
RI-Base requires — a conformance claim that cannot be read is not a claim.

Requirements below are of two kinds, and the difference decides what a tool can check. **Structural**
requirements are settled by reading the address space and the capability declaration; the facet
calculator checks every one of them, and a server shall not list a facet whose structural requirements
are unmet. **Attested** requirements — accepting, honouring, maintaining or observing a rule — cannot
be settled by reading, only by exercising the server, and are the server's own statement under the
honesty rules. Listing **RI-Blending** while treating the blending modes as `Buffered` is a false
statement in exactly the sense the honesty rules forbid, whatever `BlendingSupported` says.

| Facet | Requires |
|---|---|
| **RI-Base** (mandatory) | `RobotIntentRootType`; at least one `IntentControllerType` with `Capabilities`, `Frames`, `Tools`, `Locations`, `Axes` and `Intents`; `SupportedFacets`; `SubmitIntent`, `CancelIntent`, `CancelAll`, `RequestControl`, `ReleaseControl`; `IntentOperationType` instances with the state model above; the refusal rules *(attested)* |
| **RI-Motion-Joint** / **-Linear** / **-Circular** | the corresponding move intent; joint additionally needs `AxisType` instances covering `0`..`AxisCount − 1` |
| **RI-Trajectory** | `TrajectoryIntentDataType`, `TrajectorySupported` true, and the tolerance rules *(attested)* |
| **RI-Path** | `CartesianPathIntentDataType` and `TrajectorySupported` true |
| **RI-Force** | `ForceIntentDataType` and `ForceControlSupported` true — the robot genuinely regulates force *(attested)* |
| **RI-RealTimeChannel** | `RealTimeChannelsSupported` true, the `RealTimeChannels` folder, and the lease rules *(attested)* |
| **RI-Safety** | `SafetyState` present; populated from the safety system, and the safety refusals *(attested)* |
| **RI-Description** | `Description` with a `KinematicChain` covering every axis, `ReachRadius`, `PayloadLimit`, `MaxCartesianSpeed` |
| **RI-Process-ArcWeld / -SpotWeld / -Dispense / -Fasten / -Palletise / -SurfaceFinish** | the corresponding process intent; palletise also needs a `LocationType` pattern, surface finish also needs **RI-Force** |
| **RI-Grasp** | `GraspIntentDataType`, `ReleaseIntentDataType`, and a `ToolType` with a `TcpFrame` |
| **RI-PickPlace** | `PickIntentDataType`, `PlaceIntentDataType`, and a `LocationType` |
| **RI-ToolChange** | `ToolChangeIntentDataType` and more than one `ToolType` |
| **RI-Output** / **RI-Program** / **RI-Wait** | `SetOutputIntentDataType` + `Outputs`; `CallProgramIntentDataType` + `Programs`; `WaitIntentDataType` |
| **RI-Queue** | `MaxQueueDepth` greater than zero and `Buffered` accepted; `QueuePosition` maintained *(attested)* |
| **RI-Blending** | `BlendingSupported` true and the four blending modes accepted; the modes honoured and `Result.AchievedPose` at the blend point *(attested)* |
| **RI-Pause** / **RI-Retry** | `Pause` and `Resume`; `Retry` with `Retriable` reachable |
| **RI-Mission** | `MissionsSupported` true, `SubmitMission`, `CancelMission`, `MissionType` instances |
| **RI-Mission-Horizon** | RI-Mission plus `MissionHorizonSupported` and `UpdateMission`; base immutability *(attested)* |
| **RI-Mission-Branching** | RI-Mission plus `MissionBranchingSupported`; transitions evaluated and error policies honoured *(attested)* |
| **RI-Interop-40010** | inverse `HasIntentController` from the `MotionDeviceSystemType` instance to the `IntentControllerType` instance; operational-mode agreement with OPC 40010, `ProgramType` instances exactly matching the programs the OPC 40010 task control can load, pose/frame consistency and safety consistency *(attested)* |

## See also

* [Robotics developer guide](Robotics.md) — the OPC 40010 topology model this commands.
* [OpenUSD](OpenUsd.md) — rendering a robot's live state, and driving intents from a viewport pick.
* [State machines](StateMachines.md) — the Part 10 program lifecycle this builds on.
* [Subscriptions](Subscriptions.md) — how the client tracks an operation.
* [Robotics samples](../samples/Robotics/README.md) — runnable servers and clients.
