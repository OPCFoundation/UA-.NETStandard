# Opc.Ua.Robotics

Shared foundation for the **OPC 40010 Robotics** companion specification
(`MotionDeviceSystem` → `MotionDevice` → `Axis`).

The Robotics NodeSet and its required Industrial Automation (IA) base model are
**source-generated** here (over the source-generated OPC UA DI base model),
exposing the typed model plus the `AddOpcUaRobotics` / `AddOpcUaIA` loaders. The
Robotics `Programs` facet uses the standard `FileDirectoryType`, whose
method-state classes a curated Core omits; the model generator degrades those
absent standard method-state references to the base `MethodState`, so the model
compiles. A consumer builds instances from the generated typed classes, or from
`BaseObjectState` plus the numeric type NodeIds in `RoboticsModel`
(`MotionDeviceSystemType`, `MotionDeviceType`, `AxisType`, `ControllerType`).

Pair it with **Opc.Ua.Robotics.Server** (address-space building) and
**Opc.Ua.Robotics.Client**, and with the draft **Opc.Ua.OpenUsd** model to render
a live robot-cell twin.
