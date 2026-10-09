# UaLens desktop tests

See [Testing UaLens](../../docs/DeveloperGuide.md#testing-ualens) for the three
test assemblies, standard commands, coverage and evidence boundaries.

## Native desktop prerequisites

Use .NET SDK 10 and PowerShell 7 on an interactive Windows desktop, or Linux with
a reachable X11 display and the Avalonia native dependencies. At the minimum
980 by 640 DIP client size, a 200% scale needs at least 1960 by 1280 physical
pixels plus window chrome. No headless rendering backend is substituted.

## Exact-count regression gate

Build the application-desktop assembly, then run the gate:

```powershell
dotnet build tests\Opc.Ua.Lens.Desktop.Tests\Opc.Ua.Lens.Desktop.Tests.csproj `
  -c Release -f net10.0 -p:CustomTestTarget=net10.0
pwsh .github\scripts\test-lens-desktop.ps1 -Configuration Release -NoBuild
```

The gate runs 24 named cases and checks exact identities, passing counters and
zero skips. Results go to `TestResults/lens-desktop/desktop.trx`. Use
`-ResultsDirectory` for a different repository-relative directory.

On Linux, wrap the PowerShell command with Xvfb:

```bash
xvfb-run -a -s '-screen 0 1280x1024x24 -nolisten tcp' \
  pwsh .github/scripts/test-lens-desktop.ps1 -Configuration Release -NoBuild
```

The complete `Opc.Ua.Lens.Desktop.Tests` project also runs the NodeSet and namespace
highlighting cases. The opt-in `NodeSetImportResponsivenessTests` case requires
`UALENS_NODESET_REPRO_FILE` naming a local file; it is not part of the count gate.

## Workflow tests

The separate workflow assembly exercises editing, administration, monitoring and
document lifetimes with injected protocol clients:

```powershell
dotnet test tests\Opc.Ua.Lens.Workflow.Tests\Opc.Ua.Lens.Workflow.Tests.csproj `
  -c Release -f net10.0 -p:CustomTestTarget=net10.0
```

The normal matrix schedules both native assemblies on their supported platforms.
The artifact workflow runs the count gate and workflow assembly separately.

## Captures and guard checks

The validation/contrast case writes nine verified PNGs and two CSV reports under
`TestResults/lens-desktop/ux06/<platform>-<native-scale>/` and attaches them to
the test result. Captures render the client visual after a dispatcher frame.
Color-vision simulations are diagnostics; only normal-source contrast is gated.

The Python guard tests exercise missing reports, incorrect counts, failures,
skips and duplicate or unrelated identities:

```powershell
python .github\scripts\test_desktop_result_guard.py
```

Historical DPI and platform results are recorded in
[`plans/UaLensReview.md`](../../plans/UaLensReview.md). They do not qualify a
new source revision or replace physical keyboard and screen-reader checks.
