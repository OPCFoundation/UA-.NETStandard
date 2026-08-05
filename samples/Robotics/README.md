# Robotics samples

Three runnable samples covering the two robotics companion models this stack implements: **OPC 40010
Robotics**, which describes a robot, and the draft **[OPC UA — Robot Intent](../../docs/RobotIntent.md)**,
which commands one.

| Sample | What it is | What it shows |
|---|---|---|
| [`MinimalRobotServer`](MinimalRobotServer) | A server for a two-robot cell | OPC 40010 topology, RSL/GPOS positioning, and an OpenUSD representation of the whole cell |
| [`MinimalIntentRobotServer`](MinimalIntentRobotServer) | A server for one collaborative arm | The Robot Intent command surface end to end, plus an OpenUSD representation you can drive |
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

## `MinimalIntentRobotServer`

One stationary UR5e-style collaborative arm with an offset wrist — deliberately a different kinematic
family from the cell sample's arms — on a workbench with four target locations.

Where `MinimalRobotServer` shows you what a robot *is*, this shows you how to *ask it to do something*:
the server declares an `IntentControllerType` with its capabilities, frames, tool centre point,
locations, axes, outputs, kinematic description and safety state, and executes submitted intents against
a simulated motion kernel with real kinematics. Every intent is tracked on a Part 10 program instance, so
a motion that takes a minute outlives the `Call` that started it.

```powershell
dotnet run --project samples/Robotics/MinimalIntentRobotServer -- --host localhost --port 62840
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
    --server opc.tcp://localhost:62840/MinimalIntentRobotServer `
    --insecure --view
```

It also runs **headless**, which is how it is exercised in CI and how it works on a machine without the
native OpenUSD payload:

```powershell
dotnet run --project samples/Robotics/IntentViewerClient -- `
    --server opc.tcp://localhost:62840/MinimalIntentRobotServer --insecure
```

In headless mode the four targets are offered as a console menu instead of as prims to click, and
everything downstream of the pick — submission, tracking, cancellation, command authority — is identical.

### How a click becomes an intent

The viewport is rendered by the optional [`Opc.Ua.OpenUsd.Connector.Viewer`](../../tools/Opc.Ua.OpenUsd.Connector.Viewer)
assembly, which resolves a pointer press to a USD prim path and raises `UsdViewOptions.PrimPicked`. The
client maps that prim path to the `LocationType` node the server published under the controller's
`Locations` folder, builds an intent naming that location, and submits it.

`UsdViewOptions.PickMode` selects how the pick is resolved:

| Mode | Behaviour |
|---|---|
| `Auto` (default) | Renderer-backed pointer pick when the host can reach the picking backend, otherwise the command-prim fallback |
| `Renderer` | Renderer-backed pointer pick only |
| `CommandPrim` | Watch `UsdViewOptions.CommandPrimPath` (default `/World/IntentCommand`) and raise the callback when its `targetPrim` changes |

The fallback exists because the renderer's picking backend is not exposed through a supported accessor
on the viewer package, so the probe that finds it is best-effort by nature. `CommandPrim` also lets any
other USD tool drive the robot by editing one prim.

## Prerequisites

* .NET SDK 10.0.
* The `--view` option needs the optional viewer assembly and its native OpenUSD payload, which is
  **win-x64 only** today. Everything else, including both servers and the headless client, runs on every
  platform the stack supports.

## See also

* [Robotics developer guide](../../docs/Robotics.md) — the OPC 40010 model these servers expose.
* [Robot Intent](../../docs/RobotIntent.md) — the command surface, its lifecycle and its safety boundary.
* [OpenUSD](../../docs/OpenUsd.md) — the binding, the connector tool and the viewport.
* [Relative Spatial Location and Global Positioning](../../docs/Positioning.md) — how the cell sample
  places its robots.
