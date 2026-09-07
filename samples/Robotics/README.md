# Robotics samples

Three runnable samples covering the two robotics companion models this stack implements: **OPC 40010
Robotics**, which describes a robot, and the draft **[OPC UA — Robot Intent](../../docs/Robotics.md#robot-intent)**,
which commands one.

| Sample | What it is | What it shows |
|---|---|---|
| [`MinimalRobotServer`](MinimalRobotServer) | A server for a two-robot cell | OPC 40010 topology, RSL/GPOS positioning, and an OpenUSD representation of the whole cell |
| [`IntentEnabledRobot`](IntentEnabledRobot) | A server for one collaborative arm | The Robot Intent command surface end to end, plus an OpenUSD representation you can drive |
| [`IntentViewerClient`](IntentViewerClient) | A client with a 3-D viewport | Clicking a target in the OpenUSD viewport and watching the arm execute the intent |

All three are non-production demonstration code. `--insecure` in the command lines below means an
unsecured endpoint with blanket certificate acceptance; it exists so the samples start without a PKI
dance and must never be used anywhere real.

## `MinimalRobotServer`

A self-contained server exposing a robot **cell**: two KUKA-style six-axis arms on mobile platforms,
their axes, power trains and safety states modelled per OPC 40010, their positions modelled per
OPC 10000-210 RSL and OPC 10000-211 GPOS, and the whole thing published as an OpenUSD representation
whose assets the server serves over the Part 5 `FileType`.

```powershell
dotnet run --project samples/Robotics/MinimalRobotServer -- --host localhost --port 62830
```

Then render it live with the connector tool:

```powershell
dotnet run --project tools/Opc.Ua.OpenUsd.Connector -- `
    --server opc.tcp://localhost:62830/MinimalRobotServer `
    --fetch-assets ./stage --insecure --view
```

See [`MinimalRobotServer/README.md`](MinimalRobotServer/README.md) for the full walkthrough, the
address-space layout and the USD binding contract.

## `IntentEnabledRobot`

One stationary UR5e-style collaborative arm with an offset wrist — deliberately a different kinematic
family from the cell sample's arms — on a workbench with four target locations.

Where `MinimalRobotServer` shows you what a robot *is*, this shows you how to *ask it to do something*:
the server declares an `IntentControllerType` with its capabilities, frames, tool centre point,
locations, axes, outputs, kinematic description and safety state, and executes submitted intents against
a simulated motion kernel with real kinematics. Every intent is tracked on a Part 10 program instance, so
a motion that takes a minute outlives the `Call` that started it.

```powershell
dotnet run --project samples/Robotics/IntentEnabledRobot -- --host localhost --port 62840
```

## `IntentViewerClient`

The reason the intent surface exists, made visible. The client connects, discovers
`Server/RobotIntent/Controllers`, reads the controller's capabilities, opens the OpenUSD viewport on the
served stage, and streams live joint and tool-centre-point values into the picture. **Click one of the
four target pucks on the bench** and the client submits a linear-move intent to that location, then
tracks the resulting `IntentOperation` to a terminal state while the arm animates on screen.

Start the server first, then:

```powershell
dotnet run --project samples/Robotics/IntentViewerClient -- `
    --server opc.tcp://localhost:62840/IntentEnabledRobot `
    --insecure --view
```

It also runs **headless**, which is how it is exercised in CI and how it works on a machine without the
native OpenUSD payload:

```powershell
dotnet run --project samples/Robotics/IntentViewerClient -- `
    --server opc.tcp://localhost:62840/IntentEnabledRobot --insecure
```

In headless mode the four targets are offered as a console menu instead of as prims to click, and
everything downstream of the pick — submission, tracking, cancellation, command authority — is identical.

### Agent-driven pallet stacking

`IntentViewerClient --mcp` is the agent entry point for the intent sample. Start `IntentEnabledRobot`,
then run the viewer with MCP over stdio for a headless agent or over HTTP when the OpenUSD viewport is
open. The agent and the human viewer share the same OPC UA `SampleSession`, so MCP calls, operation
monitoring, payload outputs and OpenUSD live bindings all describe the same robot.

A well-behaved agent starts with discovery, not motion:

1. `robotics_list_controllers`
2. `robotics_read_controller` to inspect `SupportedIntents`, `SupportedFacets`, frames, tools,
   locations and outputs
3. `robotics_read_state` to verify `Ready`, mode, safety and current command owner
4. `robotics_request_control` before any command

The pallet demonstration deliberately proves refusal handling before the successful plan. Another
session holds command authority, the agent submits a move, receives `Accepted=false` with
`Failure=ControlNotOwned`, reads the enum and message, releases or waits for the owner, requests
authority itself, and only then replans. It must not retry the refused submission blindly.

The stacking plan uses the published model seam. Parts begin under `/World/Payloads/BinParts`, the
held part is driven by `HeldPartPosition` and `HeldPartVisible`, and completed slots are driven by
`PayloadSlotNNFilled`. The viewer and connector do not contain pallet logic; they just render the
server-published bindings. The agent can pick from `Bin`, move the held part to row/layer coordinates
near `Fixture`, place it, then submit a mission that repeats the sequence for the next slot.

Agents must not assume:

* command authority is implicit;
* a supported tool means the controller supports every intent kind;
* `SupportedIntents`, `SupportedFacets`, safety state or queue depth can be skipped;
* a refusal should be retried without changing the plan;
* a submitted operation is done until `robotics_wait_operation` or controller state says so.

### How a click becomes an intent

The viewport is rendered by the optional [`Opc.Ua.OpenUsd.Connector.Viewer`](../../tools/Opc.Ua.OpenUsd.Connector.Viewer)
assembly. A pick becomes an intent the same way whichever path resolves it: the viewer raises
`UsdViewOptions.PrimPicked`, and the client maps that prim path to the `LocationType` node the server
published under the controller's `Locations` folder, builds an intent naming that location, and submits it.

`UsdViewOptions.PickMode` selects how the pick is resolved:

| Mode | Behaviour |
|---|---|
| `Auto` (default) | Renderer-backed pointer picking first, falling back to the command prim only when renderer picking is unavailable |
| `Renderer` | Renderer-backed pointer picking only |
| `CommandPrim` | Watch `UsdViewOptions.CommandPrimPath` (default `/World/IntentCommand`) and raise the callback when its `targetPrim` changes |

Renderer-backed picking works: the OpenUSD viewer owns input handling, DPI scaling, physical-pixel
conversion and stale-revision retry, and reports hits through the host callback. Misses do not submit
intents. The gaps this used to work around were filed as `marcschier/openusd-dotnet` issues #1, #5, #8,
#9, #10 and #11, and all are fixed in the package version this sample ships against. The command-prim
path remains supported, and is what makes picking work headlessly — which is also how an
[MCP agent](IntentViewerClient/README.md) drives the same robot.

## Prerequisites

* .NET SDK 10.0.
* The `--view` option needs the optional viewer assembly and its native OpenUSD payload, which is
  supported on `win-x64`, `linux-x64`, and `osx-arm64`. A RID-less build or publish on a supported host
  uses that host's payload; publish with an explicit RID for another platform. Everything else,
  including both servers and the headless client, runs on every platform the stack supports.

## See also

* [Robotics developer guide](../../docs/Robotics.md) — the OPC 40010 model these servers expose.
* [Robot Intent](../../docs/Robotics.md#robot-intent) — the command surface, its lifecycle and its safety boundary.
* [OpenUSD](../../docs/OpenUsd.md) — the binding, the connector tool and the viewport.
* [Relative Spatial Location and Global Positioning](../../docs/Positioning.md) — how the cell sample
  places its robots.
