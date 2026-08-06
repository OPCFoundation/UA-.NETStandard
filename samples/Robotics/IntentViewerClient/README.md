<!--
Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.

OPC Foundation MIT License 1.00

The complete license agreement can be found here:
http://opcfoundation.org/License/MIT/1.00/
-->

# Robot Intent Viewer Client

This sample connects to `IntentEnabledRobot`, discovers its Robot Intent controller, prints the facets declared by its capabilities, obtains command authority, and maps OpenUSD target picks to Robot Intent linear moves. A submitted intent returns admission immediately; this client then waits on the returned operation and prints live `ExecutionState`, `Progress`, `CurrentPose`, and the terminal `IntentResultDataType`.

## Headless mode

Start the server, then run the client:

```powershell
dotnet run --project samples\Robotics\IntentEnabledRobot\IntentEnabledRobot.csproj -- --insecure
'1' | dotnet run --project samples\Robotics\IntentViewerClient\IntentViewerClient.csproj -- --server opc.tcp://localhost:62840/IntentEnabledRobot --insecure
```

The client lists the target pucks published by the server's OpenUSD bindings. Enter the number for Bin, Fixture, Inspect, or Handoff; the same pick handler used by the viewport reads the mapped `LocationType` pose and submits a linear move.

`--insecure` is for localhost demos only. It accepts any server certificate for localhost demos; do not use it for production systems.

## Viewport mode

On a machine with the optional viewer assembly and native OpenUSD renderer payload installed next to the sample output, run:

```powershell
dotnet run --project samples\Robotics\IntentViewerClient\IntentViewerClient.csproj -- --server opc.tcp://localhost:62840/IntentEnabledRobot --insecure --view --fetch-assets .\artifacts\intent-viewer-stage --pick-mode Auto
```

Click one of the four target pucks at `/World/Targets/Bin`, `/World/Targets/Fixture`, `/World/Targets/Inspect`, or `/World/Targets/Handoff`. Picks are resolved through the **command-prim fallback**: the host watches `/World/IntentCommand` and turns a written prim path into an intent. Renderer-backed pointer picking is not available with the current OpenUSD package — no picking backend is reachable through the viewport's object graph — so `--pick-mode Auto` degrades to the fallback immediately and says so, and `--pick-mode Renderer` will not produce picks. The gap is filed upstream at [`marcschier/openusd-dotnet`](https://github.com/marcschier/openusd-dotnet/issues). The renderer payload is currently win-x64-only; without it the sample falls back to headless mode and says why.

## Refusals and cancellation

Refusals are normal method outputs: `Accepted=false`, an `IntentFailureEnum`, and a `Message`. The sample switches on the failure to show retry, re-plan, or escalate decisions. Safety failures such as `SafetyStop` and `SafetyLimitExceeded` are reported as safety-system refusals observed by the client, not overridden by it.

Press `C` while an intent is moving to call `CancelIntent(ProcessStop)`. The output deliberately continues through `Cancelling` and waits for terminal `Cancelled`, demonstrating that cancel admission is not the end of robot motion.

## Mission demo

Add `--mission` to submit a tiny two-step mission with a released base and unreleased horizon before interactive picking:

```powershell
dotnet run --project samples\Robotics\IntentViewerClient\IntentViewerClient.csproj -- --server opc.tcp://localhost:62840/IntentEnabledRobot --insecure --mission
```
