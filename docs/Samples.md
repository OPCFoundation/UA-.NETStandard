# Sample applications

This repository contains a large collection of platform-independent samples
that demonstrate the stack's client, server, PubSub, companion-model, and
developer-tooling APIs. Each sample has its own `README.md` with build and run
instructions. If you are new to the SDK, start with the
[minimal samples](#minimal-samples).

## Contents

- [Minimal samples](#minimal-samples)
- [Reference applications](#reference-applications)
- [PubSub samples](#pubsub-samples)
- [Experimental Data Channel samples](#experimental-data-channel-samples)
- [Companion-model samples](#companion-model-samples)
- [OpenUSD site composition](#openusd-site-composition)

## Minimal samples

[Getting started](GettingStarted.md) runs the Minimal Boiler Server and the
Minimal Client together.

- [Minimal Boiler Server](../samples/MinimalApi/MinimalBoilerServer) — minimal
  Boiler-model server with the fluent state-machine builder; Native-AOT
  publishable.
- [Minimal Client](../samples/MinimalApi/MinimalClient/README.md) — hosted
  client that discovers a secure endpoint of the Minimal Boiler Server, creates
  a subscription, browses, and reads; Native-AOT publishable.
- [Minimal Calc Server](../samples/MinimalApi/MinimalCalcServer) — minimal
  server built on the source-generated NodeManager pipeline and Calc model.

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
- [SecureChannel PubSub](../samples/PubSub/ConsoleSecureChannelPubSub/README.md)
  (experimental) - brokerless UADP/JSON over dedicated or shared UASC, in one
  or two processes, with byte/value validation, live revision and optional
  Session authorization.

## Experimental Data Channel samples

These .NET 10 applications use the rewritten [Data Channels](DataChannels.md)
protocol. Configured dedicated/native modes create no Session or optional
address-space model. Native modes require the reviewed assets described in
their READMEs; there is no alternate-carrier fallback.

- [Streaming](../samples/Core/ConsoleDataChannelStreaming/README.md) - finite
  complete-message runs over all four carriers, payload role reversal, live
  revision and a shared-channel Read/Publish benchmark.
- [Audio](../samples/Core/ConsoleDataChannelAudio/README.md) - complete opaque
  PCM messages, finite headless WAV output and explicit Windows playback.

## Companion-model samples

- [Generic recycling simulation](../samples/Recycling/README.md) — one durable causal coordinator
  and ten process views with typed OPC UA coordination, native companion equipment,
  material genealogy and read-only committed measurements. Its optional
  [operator console walkthrough](../samples/Recycling/README.md#operator-console-walkthrough)
  covers browser inspection and role-limited controls with screenshots and an animated tour.
  The guide distinguishes synthetic simulation from calibration, original-source parity
  and formal companion certification.
- [Pump Device Integration Server](../samples/DI/PumpDeviceIntegrationServer/README.md) —
  Device Integration Part 100 server using `Opc.Ua.Di.Server`'s fluent
  builder.
- [AMB Server](../samples/AMB/README.md) — OPC 10000-110 Asset Management
  Basics with `AddAssetManagement` next to `AddMachinery`: a cooling pump and
  a flow sensor (Device Integration devices) and a hydraulic press (a
  Machinery machine) as assets with configurable AssetIds, health alarms with
  root causes, maintenance activities, documentation links, locations and
  persistence, and a simulation that raises alarms and moves the maintenance
  on.
- [AMB Client](../samples/AMB/README.md) — discovers every asset with
  `AmbClient`, prints snapshots and the location hierarchies, adds and removes
  a documentation link, and streams health alarms and maintenance activities.
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
- [Machine Vision server](../samples/Vision/MachineVisionServer/README.md) and
  [client](../samples/Vision/MachineVisionClient/README.md) — released OPC 40100-1/-2
  in Machinery, with recipe/file workflows and a shared draft/native inspection engine.
- [MachineTool and WMTP](../samples/MachineTool/) — separate or combined
  MachineTool/peripheral hosting, independent simulated instances, retained and
  modern production, remote files, measurement configuration and record workflows.
- [GMS server and client](../samples/GMS/) — native systems, qualification,
  environmental conditions and typed result/characteristic/correction workflows.
- [AutoID server and client](../samples/AutoID/) — a simulated OPC 30010 RFID
  reader with scans, tag memory access and scan/presence events.
- [Cranes and Hoists](../samples/CranesHoists/) — Robotics-derived topology,
  acquisition quality and authorized simulated limit requests.
- [Combined companion suite](../samples/CompanionSuite/) — MachineTool, WMTP,
  GMS, CranesHoists and MachineVision on one DI/Machinery owner, with isolated
  instances and a bounded real-session self-check.
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
