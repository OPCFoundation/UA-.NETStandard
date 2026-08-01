# OPC UA — OpenUSD connector viewport

Optional viewport for [`OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Connector`](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Connector/).

The connector normally authors a `live.usda` override layer that some other tool renders. Install this package
alongside it and the connector's `--view` option instead opens a window on the composed stage and streams the same
subscribed OPC UA values straight into the stage being rendered, so the digital twin animates live in one process.

```
dotnet run --project tools/Opc.Ua.OpenUsd.Connector -- \
    --server opc.tcp://localhost:62830/MinimalRobotServer \
    --insecure --view
```

## How it fits together

- The connector discovers the server's `Server/OpenUSD/Representations`, subscribes, and fetches the served USD
  asset closure.
- This package implements `IUsdViewHost`: it renders the composed stage with the OpenUSD Avalonia viewer and hands
  the connector an `IUsdSink` that authors into the scheduler-owned stage the renderer already owns.
- `CompositeUsdSink` fans every value into both that sink and the file sink, so the override layer on disk and the
  picture on screen never diverge.

The connector loads this assembly by name from its own directory and never references it, which is what keeps the
connector package free of a UI framework and a native payload. Publish both projects into the same directory so the
assembly, its dependencies, and the USD plugin tree end up where the connector looks for them.

## Requirements

- .NET 10 on `win-x64`.
- The OpenUSD packages `OpenUsd`, `OpenUsd.Viewer`, and `OpenUsd.Runtime.Imaging.win-x64`.

See [`docs/OpenUsd.md`](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/OpenUsd.md) for the full
binding guide and the local-feed bootstrap.

## License

MIT — see the OPC Foundation MIT license.
