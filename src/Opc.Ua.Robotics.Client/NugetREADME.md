# Opc.Ua.Robotics.Client

Client-side helpers for the **OPC 40010 Robotics** companion model.

Built on **Opc.Ua.Robotics** and **Opc.Ua.Di.Client** — Robotics types derive
from OPC 40001-1 IA, which derives from OPC 10000-100 DI, so `RoboticsClient`
composes `DiTopologyClient` instead of reimplementing device navigation. It lets
a generic OPC UA client (for example the OpenUSD connector or a viewer) work
with robot cells:

- `DiscoverMotionDeviceSystemsAsync` / `DiscoverMotionDevicesAsync` /
  `DiscoverControllersAsync` / `DiscoverAxesAsync` — continuation-safe,
  subtype-aware discovery of Robotics instances below a root node (defaulting to
  the DI `DeviceSet`);
- `GetRoboticsTypeNameAsync` — classify a discovered node against the server's
  type hierarchy, so vendor specialisations resolve to their closest standard
  Robotics type (`MotionDeviceSystem` / `MotionDevice` / `Axis` / `Controller`);
- `TryGetRoboticsTypeName` — the offline exact-match variant, with no server
  round-trip;
- `AddRoboticsClient()` — fluent registration on `IOpcUaClientBuilder`, which
  also registers the DI client services.

Pair it with **Opc.Ua.OpenUsd.Client** to render and live-update the cell.

See the [Robotics developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Robotics.md)
and the [OpenUSD binding guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/OpenUsd.md).
