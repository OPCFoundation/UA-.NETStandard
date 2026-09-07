# Opc.Ua.Robotics

Server/client-independent foundation for the **OPC 40010 Robotics** companion
specification.

The Robotics NodeSet and its required Industrial Automation (IA) base model are
**source-generated** here (over the source-generated OPC UA DI base model),
exposing generated ObjectTypes, ReferenceTypes, enums, typed node states and
client proxies, plus the `AddOpcUaRobotics` / `AddOpcUaIA` model loaders. The
generated `ObjectTypeIds` and `ReferenceTypeIds` classes are the source of truth;
`RoboticsModel` provides namespace-safe resolution and classification helpers.

The package also provides `ArrayOf<T>`-based common contracts for a focused read
projection of systems, controllers, motion devices, axes, loads, power trains,
motors, gears, drives, safety states, task controls, task modules, engineering
values, telemetry, and semantic relationships. Containment identifiers remain
on owning instances, while `RoboticsRelationshipSnapshot` is the authoritative
projection of semantic Robotics references. These contracts can be shared
without taking a Server or Client dependency.

See the [Robotics developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Robotics.md).

## Robot Intent (draft)

The package additionally source-generates the **draft** *OPC UA — Robot Intent*
model, which supplies the task-level motion verbs OPC 40010 leaves undefined —
joint, linear and circular moves, trajectories, Cartesian paths, force-controlled
moves, six application processes, grasping, pick and place, tool change, output,
program call and wait — as a DataType hierarchy submitted against a Part 10
program lifecycle, so a motion that takes minutes outlives the `Call` that started
it. Its NodeSet declares only the base UA namespace as a `RequiredModel`, so it is
independent of OPC 40010 and of DI.

Alongside the generated model this package carries the executor contracts
(`IIntentExecutor`, `IntentExecution`, `IIntentProgress`, `IntentOutcome`) and the
specification's normative pose maths: `PoseMath` converts between the model's unit
quaternion and the core `ThreeDFrame`, and `FrameTree` composes transforms along a
frame tree.

> The namespace `http://opcfoundation.org/UA/RobotIntent/` and every NodeId in it
> are **provisional**. The model is a working-group draft.

See the [Robot Intent guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Robotics.md#robot-intent).
