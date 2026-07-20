# Opc.Ua.Robotics.Client

Client-side helpers for the **OPC 40010 Robotics** companion model.

Built on **Opc.Ua.Robotics**, it lets a generic OPC UA client (for example the
OpenUSD connector or a viewer) work with robot cells:

- `DiscoverMotionDeviceSystemsAsync` — browse a root node (e.g. the DI
  `DeviceSet`) for `MotionDeviceSystem` instances over a session;
- `TryGetRoboticsTypeName` — identify the Robotics type
  (`MotionDeviceSystem` / `MotionDevice` / `Axis` / `Controller`) of a discovered
  node from its TypeDefinition, for labelling a robot-cell twin.

Pair it with **Opc.Ua.OpenUsd.Client** to render and live-update the cell.
