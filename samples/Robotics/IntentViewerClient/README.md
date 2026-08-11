<!--
Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.

OPC Foundation MIT License 1.00

The complete license agreement can be found here:
http://opcfoundation.org/License/MIT/1.00/
-->

# Robot Intent Viewer Client

This sample connects to `IntentEnabledRobot`, discovers its Robot Intent controller, prints the facets declared by its capabilities, obtains command authority, and maps OpenUSD target picks to Robot Intent linear moves. With `--mcp` on a supported target framework, it also hosts the Robot Intent MCP tools over the same OPC UA session. A submitted intent returns admission immediately; this client then waits on the returned operation and prints live `ExecutionState`, `Progress`, `CurrentPose`, and the terminal `IntentResultDataType`.

## MCP hosting

With `--mcp`, the viewer client exposes the Robotics MCP profile so an LLM agent can discover controllers, read state, request authority, submit intents and missions, pause/resume/cancel/retry operations, and wait for operation completion while a human watches the same robot in the OpenUSD viewport. The MCP tools use the one `SampleSession` opened by the sample; the viewer, OpenUSD live bindings, and MCP tools therefore observe and command the same connected controller.

MCP hosting is compiled for `net8.0`, `net9.0`, and `net10.0`. It is unavailable on `net48`; running that leg with `--mcp` reports the limitation on stderr. Without `--mcp`, the sample continues to run on every target framework it builds for.

MCP options:

- `--mcp` enables MCP hosting.
- `--transport stdio|http|sse` selects the MCP transport. `sse` is accepted as an alias for `http`.
- `--port <number>` selects the Streamable HTTP port. The default is `5100`.

Transport selection is visible on stderr:

- No `--transport` and no `--view`: stdio is selected by default.
- No `--transport` with `--view`: HTTP is selected automatically and the client explains that MCP stdio uses stdout for protocol frames, which cannot safely coexist with the in-process OpenUSD viewer.
- `--transport http` with or without `--view`: HTTP is used.
- `--transport stdio` without `--view`: stdio is used.
- `--transport stdio --view`: the explicit request is honored, but the client warns plainly that the viewer may share stdout and corrupt the MCP stdio protocol.

Example MCP client configuration for viewport mode:

```json
{
  "servers": {
    "intent-viewer": {
      "url": "http://localhost:5100/mcp"
    }
  }
}
```

Run the sample with HTTP MCP and the viewport:

```powershell
dotnet run --project samples\Robotics\IntentViewerClient\IntentViewerClient.csproj --framework net10.0 -- --server opc.tcp://localhost:62840/IntentEnabledRobot --insecure --view --mcp --transport http --port 5100 --fetch-assets .\artifacts\intent-viewer-stage
```

For stdio MCP, point the MCP client at the command instead:

```json
{
  "servers": {
    "intent-viewer": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "samples\\Robotics\\IntentViewerClient\\IntentViewerClient.csproj",
        "--",
        "--server",
        "opc.tcp://localhost:62840/IntentEnabledRobot",
        "--insecure",
        "--mcp"
      ]
    }
  }
}
```

Agents can submit only what the server advertises and accepts. Refusals are returned to the agent as tool results and are never retried on the agent's behalf; the agent must decide whether to re-plan, ask for operator action, or stop.

## Headless mode

Start the server, then run the client:

```powershell
dotnet run --project samples\Robotics\IntentEnabledRobot\IntentEnabledRobot.csproj -- --insecure
'1' | dotnet run --project samples\Robotics\IntentViewerClient\IntentViewerClient.csproj -- --server opc.tcp://localhost:62840/IntentEnabledRobot --insecure
```

The client lists the target pucks published by the server's OpenUSD bindings. Enter the number for Bin, Fixture, Inspect, or Handoff; the same pick handler used by the viewport reads the mapped `LocationType` pose and submits a linear move.

Headless keyboard control is disabled when MCP stdio is active because both would read from standard input. Use `--mcp --transport http` if you want the headless menu and an MCP client at the same time.

`--insecure` is for localhost demos only. It accepts any server certificate for localhost demos; do not use it for production systems.

## Viewport mode

On a machine with the optional viewer assembly and native OpenUSD renderer payload installed next to the sample output, run:

```powershell
dotnet run --project samples\Robotics\IntentViewerClient\IntentViewerClient.csproj -- --server opc.tcp://localhost:62840/IntentEnabledRobot --insecure --view --fetch-assets .\artifacts\intent-viewer-stage --pick-mode Auto
```

Click one of the four target pucks at `/World/Targets/Bin`, `/World/Targets/Fixture`, `/World/Targets/Inspect`, or `/World/Targets/Handoff`. With `--pick-mode Auto`, the viewer uses renderer-backed pointer picking first: the OpenUSD viewer owns input handling, DPI scaling, physical-pixel conversion, stale-revision retry, and reports hits through the host callback. Misses do not submit intents. If renderer picking is unavailable or unsupported, Auto falls back to the command prim watcher: the host watches `/World/IntentCommand` and turns a written prim path into an intent. Use `--pick-mode Renderer` to require renderer-backed picks, or `--pick-mode CommandPrim` to use only the command-prim path. The renderer payload supports `win-x64`, `linux-x64` and `osx-arm64`; where it is unavailable the sample falls back to headless mode and says why.

## Refusals and cancellation

Refusals are normal method outputs: `Accepted=false`, an `IntentFailureEnum`, and a `Message`. The sample switches on the failure to show retry, re-plan, or escalate decisions. Safety failures such as `SafetyStop` and `SafetyLimitExceeded` are reported as safety-system refusals observed by the client, not overridden by it.

Press `C` while an intent is moving to call `CancelIntent(ProcessStop)`. The output deliberately continues through `Cancelling` and waits for terminal `Cancelled`, demonstrating that cancel admission is not the end of robot motion.

## Mission demo

Add `--mission` to submit a tiny two-step mission with a released base and unreleased horizon before interactive picking:

```powershell
dotnet run --project samples\Robotics\IntentViewerClient\IntentViewerClient.csproj -- --server opc.tcp://localhost:62840/IntentEnabledRobot --insecure --mission
```

## Agent workflow for pallet stacking

Use MCP when an LLM agent should drive the same robot a human watches:

```powershell
dotnet run --project samples\Robotics\IntentEnabledRobot\IntentEnabledRobot.csproj -- --insecure
dotnet run --project samples\Robotics\IntentViewerClient\IntentViewerClient.csproj --framework net10.0 -- --server opc.tcp://localhost:62840/IntentEnabledRobot --insecure --view --mcp --transport http --port 5100
```

Configure the MCP client with `http://localhost:5100/mcp`, or omit `--view --transport http` and
register the `dotnet run ... -- --mcp` command as a stdio MCP server.

Before commanding anything, the agent should read:

* `robotics_list_controllers`
* `robotics_read_controller` for `SupportedIntents`, `SupportedFacets`, `Bin`, `Fixture`,
  `ParallelGripper`, `HeldPartPosition`, `HeldPartVisible` and `PayloadSlotNNFilled`
* `robotics_read_state` for `Ready`, mode, safety and command owner
* `robotics_request_control`

The stacking scenario then exercises the full surface:

1. provoke and handle a refusal, for example by submitting while another session owns control and
   observing `IntentFailureEnum.ControlNotOwned`;
2. take command authority explicitly;
3. submit direct pick, move and place intents;
4. call `robotics_wait_operation` and `robotics_list_operations` while motion is active;
5. pause, resume, cancel and retry as explicit tools, never as hidden client behaviour;
6. compile a multi-step mission with `robotics_submit_mission`;
7. inspect `robotics_list_missions`, update a mission horizon, and cancel a mission;
8. verify payload outputs: `HeldPartVisible`, changed `HeldPartPosition`, and filled stack slots.

Agents must not retry a refusal blindly, assume authority, ignore `SupportedIntents`, or keep issuing
commands after safety/mode/authority refusals without re-reading state and changing the plan.

Transcript-style sequence for one part:

```text
agent -> robotics_list_controllers()
server -> [{ name: "UR5eIntentController", nodeId: "ns=...;s=..." }]

agent -> robotics_read_controller(controllerId)
server -> SupportedIntents includes Pick, Place, LinearMove, JointMove and Wait;
          SupportedFacets includes RI-Mission and RI-Mission-Horizon;
          locations include Bin and Fixture; outputs include PayloadSlot01Filled.

agent -> robotics_read_state(controllerId)
server -> Ready=true, OperationalMode=AutomaticExternal, ControlOwner=<other session>

agent -> robotics_submit_linear_move(controllerId, { intentId: "probe", target: ... })
server -> { accepted: false, failure: ControlNotOwned, message: "..." }

agent decision: this is an authority refusal, not a motion problem; do not retry the same call.

agent -> robotics_request_control(controllerId)
server -> { granted: true }

agent -> robotics_submit_pick(controllerId,
    { intentId: "pick-slot-01", source: "<Bin NodeId>", tool: "<ParallelGripper NodeId>" })
server -> { accepted: true, operation: "<IntentOperation NodeId>" }

agent -> robotics_wait_operation(controllerId, "pick-slot-01", operation, 2000)
server -> completed=true

agent -> robotics_submit_joint_move(controllerId,
    { intentId: "carry-slot-01", jointTargets: [0.1, -1.0, 1.5, -0.9, 0.75, 0.0] },
    axisCount: 6)
server -> { accepted: true, operation: "<IntentOperation NodeId>" }

agent -> robotics_wait_operation(controllerId, "carry-slot-01", operation, 2000)
server -> completed=true; HeldPartPosition has changed and HeldPartVisible is true.

agent -> robotics_submit_place(controllerId,
    { intentId: "place-slot-01", destination: "<Fixture NodeId>", tool: "<ParallelGripper NodeId>" })
server -> { accepted: true, operation: "<IntentOperation NodeId>" }

agent -> robotics_wait_operation(controllerId, "place-slot-01", operation, 2000)
server -> completed=true; PayloadSlot01Filled is true.
```
