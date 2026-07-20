# Opc.Ua.Robotics.Server

Server-side reusable functionality for the **OPC 40010 Robotics** companion model.

Built on **Opc.Ua.Robotics**, it helps an OPC UA server expose a robot cell:

- `AddRoboticsTypeSystem` — loads the OPC UA DI base model plus the IA and
  Robotics companion models (all source-generated) into a node manager's
  predefined-node collection, in dependency order;
- `CreateTypedObject` — instantiates a Robotics-typed Object
  (`MotionDeviceSystem` / `MotionDevice` / `Axis` / `Controller`) from the numeric
  type NodeIds in `RoboticsModel`.

Combine it with **Opc.Ua.OpenUsd.Server** to bind the cell to a live USD twin.
