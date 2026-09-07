# OPCFoundation.NetStandard.Opc.Ua.Vision.OpenUsd

Offscreen renderer for the draft OPC UA Vision companion server.

This package renders a USD stage camera's view through the OpenUSD Silk backend
(D3D12 hardware, D3D12 WARP software, or Vulkan headless) and returns encoded
PNG frames through a small `ISceneCameraCaptureProvider` abstraction. The
Vision server can depend on the abstraction without referencing OpenUSD, and
degrades to a non-rendering sensor when this optional package is not present.

## What it does

The `Opc.Ua.Vision.OpenUsd.OpenUsdSceneCameraCaptureProvider` fulfills a
capture request by:

1. Opening the USD stage from the supplied path or identifier.
2. Resolving the camera prim (view + off-centre projection built from
   `UsdGeomCamera.GetState(time)` and `GetTransform(time)`) - or rendering
   the automatic default framing when no prim path is supplied.
3. Creating a fresh `OpenUsdSilkSession` for the request (the SDK's
   reuse-with-different-camera path is known to silently render nothing).
4. Capturing an RGBA8 frame with `SilkFrameCapture.Capture` on a shared
   `ISilkGraphicsDevice` picked at construction time.
5. Encoding to PNG with an in-repo, dependency-free encoder.
6. Refusing to hand back an all-zero / no-mesh frame as if it succeeded -
   returning `SceneCameraCaptureStatus.BlankFrame` with a reason instead.

## Device selection

`OpenUsdSceneCameraCaptureProvider` tries backends in the order that makes
sense for the host:

- **Windows**: D3D12 hardware -> D3D12 WARP (software rasterizer, no GPU) -> Vulkan.
- **Linux / other**: Vulkan (which the OpenUSD runtime bundles the SwiftShader
  software ICD for, so CI without a GPU is still functional).

The provider reports the chosen backend and whether it is a software renderer
through `Backend`, and when no backend is available reports
`IsAvailable = false` with a reason. In that case every `CaptureAsync` returns
`SceneCameraCaptureStatus.NoRenderingBackend`.

## Registration

```csharp
using Opc.Ua.Vision.OpenUsd;
using Microsoft.Extensions.DependencyInjection;

services.AddOpenUsdSceneCameraCaptureProvider(o =>
{
    o.PluginPath = "/path/to/plugin/usd"; // optional; auto-probes AppContext.BaseDirectory
    o.PreferSoftware = false;
});
```

or without DI:

```csharp
using var provider = new OpenUsdSceneCameraCaptureProvider(
    new OpenUsdSceneCaptureOptions(), telemetry: null);
SceneCameraCaptureResult result = await provider.CaptureAsync(
    new SceneCameraCaptureRequest
    {
        StageIdentifier = "/path/to/scene.usda",
        PrimPath = "/World/Cam",
        Width = 640,
        Height = 360,
        TimeCode = 0.0,
        Format = SceneCameraImageFormat.Png,
    },
    cancellationToken);
```

## Native payload

The OpenUSD runtime packages ship RID-specific native assets (`win-x64`,
`linux-x64`, `osx-arm64`). Publish the *consuming application* with an
explicit `RuntimeIdentifier` (e.g. `dotnet publish -r linux-x64`) so the
`plugin/usd/` tree and the backend native libraries land alongside the
executable. The provider then auto-discovers them from `AppContext.BaseDirectory`.

When the payload is absent — the normal case on unadorned CI legs — the
provider still starts, `Backend.IsAvailable` reports `false` and every
capture returns `SceneCameraCaptureStatus.NoRenderingBackend`. This is the
degrade path the [Vision developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/main/docs/Vision.md#rendering-degrades-rather-than-throwing)
describes: the sensor stays visible in the address space, browses still
work, and only the pixel bytes are absent, so a client can distinguish "no
GPU" from a genuine rendering fault.

## Related packages

| Package | Adds |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Vision.Server` | The Vision server that consumes `ISceneCameraCaptureProvider` |
| `OPCFoundation.NetStandard.Opc.Ua.Vision.Client` | The client that reads the simulated sensor's `LatestClip` / `GetClip` |

## License

OPC Foundation MIT License 1.00 — <http://opcfoundation.org/License/MIT/1.00/>
