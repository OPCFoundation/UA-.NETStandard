# Sample applications

This repository contains a large collection of platform-independent samples
that demonstrate the stack's client, server, PubSub, companion-model, and
developer-tooling APIs. Each sample has its own `README.md` with build and run
instructions.

## Reference applications

- [Console Reference Server](../samples/Reference/ConsoleReferenceServer/README.md) —
  the certified reference server with Quickstarts, CTT, and Mono
  configurations. It also ships as a
  [Docker container](ContainerReferenceServer.md).
- [Console Reference Client](../samples/Reference/ConsoleReferenceClient/README.md) —
  cross-platform reference client demonstrating sessions, subscriptions,
  browsing, and method calls.
- [Console LDS Server](../samples/Lds/ConsoleLdsServer) — a standalone
  Local Discovery Server built on `Opc.Ua.Lds.Server`.

## PubSub samples

- [Console Reference PubSub Client](../samples/PubSub/ConsoleReferencePubSubClient/README.md) —
  one executable with `publisher`, `subscriber`, and `external`
  external-server-adapter modes across the supported transport profiles.

## Minimal and companion-model samples

- [Minimal Calc Server](../samples/MinimalApi/MinimalCalcServer) — minimal
  server built on the source-generated NodeManager pipeline and Calc model.
- [Minimal Boiler Server](../samples/MinimalApi/MinimalBoilerServer) — minimal
  Boiler-model server with the fluent state-machine builder; Native-AOT
  publishable.
- [Pump Device Integration Server](../samples/DI/PumpDeviceIntegrationServer/README.md) —
  Device Integration Part 100 server using `Opc.Ua.Di.Server`'s fluent
  builder.
- [Minimal Robot Server](../samples/Robotics/MinimalRobotServer/README.md) —
  OPC 40010 Robotics with independently configurable RSL/GPOS motion and
  live OpenUSD transforms.
- [Intent Enabled Robot](../samples/Robotics/IntentEnabledRobot/README.md) —
  collaborative arm exposing task-level Robot Intent motion verbs, Part 10
  program lifecycle, missions, command authority, and safety-aware refusal.
- [Intent Viewer Client](../samples/Robotics/IntentViewerClient/README.md) —
  click a target in an OpenUSD viewport and watch the arm execute the
  resulting intent; it also runs headless.
- [Bin Picking Cell](../samples/Robotics/BinPickingCell/README.md) — Robot
  Intent, Vision, an eye-in-hand camera, and an in-address-space OpenUSD scene
  in one reference cell.
- [Bin Picking Client](../samples/Robotics/BinPickingClient/README.md) — closes
  the Vision-to-Robot-Intent loop, with optional MCP hosting and an OpenUSD
  viewport.
- [Visual Inspection Cell](../samples/Vision/VisualInspectionCell/README.md) —
  hosts Vision, AI Model Management, ISA-95 Job Control V2, and an operator
  dialog for deterministic machined-bracket inspection.
- [Visual Inspection Agent](../samples/Vision/VisualInspectionAgent/README.md) —
  drives the inspection loop with typed clients, routes inference through
  deployment `Invoke`, applies recipe verdicts, schedules allowlisted jobs,
  and records operator ground truth.
- [AI Model Management sample](../samples/AI/README.md) —
  `ModelManagementServer` publishes the draft AI Model Management catalogue
  and routes inference; `ModelManagementClient` discovers deployments and
  exercises the Methods.
- [Minimal ISA-95 Server](../samples/Isa95/MinimalIsa95Server/README.md) —
  hosts the OPC-10030 ISA-95 Common Model with OPC-10031-4 Job Control V1 and
  V2, using the typed common-model builder and in-memory Job Control provider.

## OpenUSD site composition

- [Generator Server](../samples/OpenUsd/GeneratorServer/README.md) — simulated
  generating sets with datasheet-driven behavior and independent OpenUSD
  twins.
- [Site Composition Server](../samples/OpenUsd/SiteCompositionServer/README.md) —
  federates the pump and generator servers into one live OpenUSD site.

Run the complete site demo from the repository root:

```powershell
pwsh samples/OpenUsd/SiteCompositionServer/run-site-composition-demo.ps1
```
