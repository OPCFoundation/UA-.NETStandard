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
