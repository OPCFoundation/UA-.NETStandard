# OpenUSD samples

Runnable samples that publish OpenUSD representations from OPC UA servers.

| Sample | What it is | What it shows |
|---|---|---|
| [`GeneratorServer`](GeneratorServer) | A server for simulated generating sets | Generators companion modelling, datasheet-driven simulation, and one independent OpenUSD twin per configured set |
| [`SiteCompositionServer`](SiteCompositionServer) | A supervisory server composing remote machines | Cross-server federation of the pump and generator twins into one live site |

Run the complete site demo with:

```powershell
pwsh samples/OpenUsd/SiteCompositionServer/run-site-composition-demo.ps1
```

See [OpenUSD](../../docs/OpenUsd.md) for the binding model, connector tool and viewport.
