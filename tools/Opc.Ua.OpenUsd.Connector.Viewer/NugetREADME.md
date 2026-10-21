# OPC UA — OpenUSD connector viewport

Optional viewport for [`OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Connector`](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Connector/).

The connector normally authors a `live.usda` override layer that some other tool renders. Install this package
alongside it and the connector's `--view` option instead opens a window on the composed stage and streams the same
subscribed OPC UA values straight into the stage being rendered, so the digital twin animates live in one process.

```
dotnet run --project tools/Opc.Ua.OpenUsd.Connector -- \
    --server opc.tcp://localhost:62830/MinimalRobotServer \
    --insecure --view --pick-command
```

## How it fits together

- The connector discovers the server's `Server/OpenUSD/Representations`, subscribes, and fetches the served USD
  asset closure.
- This package implements `IUsdViewHost`: it renders the composed stage with the OpenUSD Avalonia viewer and hands
  the connector an `IUsdSink` that authors into the scheduler-owned stage the renderer already owns.
- `CompositeUsdSink` fans every value into both that sink and the file sink, so the override layer on disk and the
  picture on screen never diverge.
- `UsdViewOptions.PrimPicked` is supported by the OpenUSD viewer's renderer-backed pick callback.
  `UsdViewOptions.PickMode` defaults to that path and automatically falls back to the command prim when renderer
  picking is unavailable or unsupported; the fallback watches `/World/IntentCommand` by default and raises the callback
  when its `targetPrim` relationship or attribute changes.

The connector loads this assembly by name from its own directory and never references it, which keeps the
connector package free of a UI framework and a native payload. Publish both projects into the same directory so the
assembly, its dependencies, and the USD plugin tree end up where the connector looks for them.

## Embedding in an existing desktop application

`EmbeddedUsdViewport` is a separate, typed composition API. It does **not** call the standalone viewer entry point,
create an Avalonia application, or use the Storm/OpenGL viewport. Its empty surface may be attached before startup;
only an explicit `StartAsync` opens a stage and enables presentation.

```csharp
var viewport = new EmbeddedUsdViewport(telemetry);
host.Content = viewport.Surface; // Inert until StartAsync.
await viewport.StartAsync(
    verifiedPrivateStagePath, "selected-stage", "/World/Camera", cancellationToken).ConfigureAwait(true);
await viewport.WaitForFirstFrameAsync(cancellationToken).ConfigureAwait(true);

// Reuse the connector's conversion and this sink for read-only live telemetry.
viewport.Sink.SetAttribute("/World/Robot", "xformOp:rotateZ", Variant.From(15d));

// On close: cancel and drain the caller-owned telemetry source first.
await viewport.DisposeAsync().ConfigureAwait(true);
```

The host must validate and retain its private asset closure before calling `StartAsync`; do not pass a server-supplied
URI or an unvalidated arbitrary stage. The viewport independently checks
physical frame dimensions, two/three-frame rings, at most two resize generations, and retained geometry before their
allocations. Initial, resized and newly selected-camera targets must contain nonuniform rendered pixels.

The stage scheduler, `IUsdSink`, native render source and retained mesh renderer share the same stage identity.
`PickAsync` returns a prim selection only: there is no command callback or command-prim watcher in this API.
Live transform targets must be active in `xformOpOrder`. Vector translate/rotate/scale bindings use the
connector asset contract: one active `xformOp:transform` matrix (an optional reset-stack marker is preserved).
The embedded sink refuses missing or inactive targets rather than silently counting invisible writes as updates.
`IRetainedSceneFrameSource` lets `RetainedSceneCameraCaptureProvider` capture the active camera through the same renderer
without another stage, file-export loop, or application lifetime. Capture must use the active camera, time and physical
dimensions; cancellation drains an in-progress readback before the owner releases its device.
The requested camera must match both the selected camera and the prepared frame's camera.
Preparation, rendering and device-loss failures fault queued captures. An already-started readback drains before
reporting failure or cancellation, so its caller cannot release the device while capture still uses it.

The typed backend branches use D3D12 on Windows, Vulkan on Linux, and Metal on macOS. Unsupported interop/device/scene
conditions remain explicit failures. NativeAOT, driver, and visual qualification are per RID; a Windows result does not
qualify Linux or macOS. The reflective `IUsdViewHost` shell has no assembly-wide AOT compatibility promise.

## Requirements

- .NET 8 or later.
- A supported OpenUSD native runtime payload: `win-x64`, `linux-x64`, or `osx-arm64`. A RID-less build or publish on a
  supported host uses that host's payload; use an explicit RID when publishing for another platform.
- The OpenUSD packages `OpenUsd`, `OpenUsd.Viewer`, and `OpenUsd.Runtime.Imaging`.

See [`docs/OpenUsd.md`](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/OpenUsd.md) for the full
binding guide and the local-feed bootstrap.

## License

MIT — see the OPC Foundation MIT license.
