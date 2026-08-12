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

The connector loads this assembly by name from its own directory and never references it, which is what keeps the
connector package free of a UI framework and a native payload. Publish both projects into the same directory so the
assembly, its dependencies, and the USD plugin tree end up where the connector looks for them.

## Requirements

- .NET 8 or later.
- A supported OpenUSD native runtime payload: `win-x64`, `linux-x64`, or `osx-arm64`. A RID-less build or publish on a
  supported host uses that host's payload; use an explicit RID when publishing for another platform.
- The OpenUSD packages `OpenUsd`, `OpenUsd.Viewer`, and `OpenUsd.Runtime.Imaging`.

See [`docs/OpenUsd.md`](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/OpenUsd.md) for the full
binding guide and the local-feed bootstrap.

## License

MIT — see the OPC Foundation MIT license.
