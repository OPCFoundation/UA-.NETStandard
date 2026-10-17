# UaLens desktop tests

See [Testing UaLens](../../docs/DeveloperGuide.md#testing-ualens) for the four
test assemblies, standard commands, coverage and evidence boundaries.

## Native desktop prerequisites

Use .NET SDK 10 and PowerShell 7 on an interactive Windows desktop, or Linux with
a reachable X11 display and the Avalonia native dependencies. At the minimum
980 by 640 DIP client size, a 200% scale needs at least 1960 by 1280 physical
pixels plus window chrome. No headless rendering backend is substituted.

## Desktop test assembly

Run the complete application-desktop assembly with NUnit:

```powershell
dotnet test tests\Opc.Ua.Lens.Desktop.Tests\Opc.Ua.Lens.Desktop.Tests.csproj `
  -c Release -f net10.0 -p:CustomTestTarget=net10.0 `
  --logger trx --results-directory TestResults\lens-desktop `
  -- NUnit.NumberOfTestWorkers=0 RunConfiguration.TreatNoTestsAsError=true
```

NUnit discovers the cases from the assembly; no second list of test methods is
maintained in a script. On Linux, provide a display with Xvfb:

```bash
xvfb-run -a -s '-screen 0 1280x1024x24 -nolisten tcp' \
  dotnet test tests/Opc.Ua.Lens.Desktop.Tests/Opc.Ua.Lens.Desktop.Tests.csproj \
    -c Release -f net10.0 -p:CustomTestTarget=net10.0 \
    --logger trx --results-directory TestResults/lens-desktop \
    -- NUnit.NumberOfTestWorkers=0 RunConfiguration.TreatNoTestsAsError=true
```

The complete `Opc.Ua.Lens.Desktop.Tests` project also runs the NodeSet and namespace
highlighting cases. The opt-in `NodeSetImportResponsivenessTests` case requires
`UALENS_NODESET_REPRO_FILE` naming a local file and is excluded from ordinary runs.

## Workflow tests

The separate workflow assembly exercises editing, administration, monitoring and
document lifetimes with injected protocol clients:

```powershell
dotnet test tests\Opc.Ua.Lens.Workflow.Tests\Opc.Ua.Lens.Workflow.Tests.csproj `
  -c Release -f net10.0 -p:CustomTestTarget=net10.0
```

The normal matrix schedules both native assemblies on their supported platforms.
The artifact workflow runs the desktop and workflow assemblies separately.

## Captures and guard checks

The validation/contrast case writes nine verified PNGs and two CSV reports under
`TestResults/lens-desktop/ux06/<platform>-<native-scale>/` and attaches them to
the test result. Captures render the client visual after a dispatcher frame.
Color-vision simulations are diagnostics; only normal-source contrast is gated.

Artifact acceptance tests cover markers, nonzero exits, timeouts, stale or corrupt
PNGs, uniform frames and unchanged decoded pixels. They use the standard NUnit
runner and do not start UaLens or a renderer:

```powershell
dotnet test tests\Opc.Ua.Lens.Artifacts.Tests\Opc.Ua.Lens.Artifacts.Tests.csproj `
  -c Release -f net10.0 -p:CustomTestTarget=net10.0
```

The real artifact fixture is `[Explicit]` and requires a selected published or
installed executable. See [artifact qualification](README.md#native-renderer-artifact-check).

Historical DPI and platform results are recorded in
[`plans/UaLensReview.md`](../../plans/UaLensReview.md). They do not qualify a
new source revision or replace physical keyboard and screen-reader checks.
