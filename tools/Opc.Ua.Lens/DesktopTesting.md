# Desktop regression tests

The desktop lane runs **24 explicit NUnit cases** against the real Avalonia
desktop backend and UaLens application resources. An ordinary Lens unit-test
run does not execute these cases. The runner rejects a missing display, missing
TRX, zero tests, an unexpected count, duplicate test identities, skipped tests,
and any failed case.

## Prerequisites and command

- .NET SDK 10 and PowerShell 7.
- Windows with an interactive, unlocked desktop, or Linux with a reachable
  X11 `DISPLAY` and the native libraries required by Avalonia Desktop.
- The Lens net10.0 dependency graph already restored. No additional test package,
  headless backend, test-only rendering backend, or GUI automation driver is used.
- A display large enough for the 980 × 640 DIP client area plus window chrome.
  At 200% scale, allow at least 1960 × 1280 physical pixels plus chrome.

From the repository root:

```powershell
dotnet restore tests/Opc.Ua.Lens.Tests/Opc.Ua.Lens.Tests.csproj -p:CustomTestTarget=net10.0
pwsh .github/scripts/test-lens-desktop.ps1
```

After a matching Debug build, pass `-NoBuild`; for a Release build also pass
`-Configuration Release`. Build and run serially when sharing a checkout.
Results are written to `TestResults/lens-desktop/desktop.trx`. The script removes
only the previous TRX before running so a stale result cannot satisfy the gate.

On Linux, an isolated Xvfb server also exercises Avalonia's real X11 backend.
With `Xvfb`, `xvfb-run` and `xauth` already available on `PATH`:

```bash
mkdir -p TestResults/lens-desktop
xvfb-run -a -f "$PWD/TestResults/lens-desktop/xauthority" \
  -e "$PWD/TestResults/lens-desktop/xvfb.log" \
  -s '-screen 0 1280x1024x24 -nolisten tcp' \
  pwsh .github/scripts/test-lens-desktop.ps1 -NoBuild
```

The explicit authorization path keeps runner files inside the checkout.
`xvfb-run` stops its server when the command exits. This verifies X11 windows,
rendering and dispatcher behavior, not a physical display, window-manager
integration, or assistive technology.

The existing explicit endpoint fixture can also be selected directly:

```powershell
dotnet test tests/Opc.Ua.Lens.Tests/Opc.Ua.Lens.Tests.csproj `
  --no-restore -p:CustomTestTarget=net10.0 -f net10.0 `
  --filter "FullyQualifiedName~UaLens.Tests.Connection.EndpointCredentialsPickerDialogTests" `
  -- NUnit.ExplicitMode=Relaxed NUnit.NumberOfTestWorkers=0
```

This selection must execute **two** tests, including
`SecureEndpointAcceptReturnsAnonymousAndCancelReturnsNull`. Use the script for
the CI count gate, not the direct command. All desktop fixtures remain explicit, but
no longer require NUnit to discover an STA fixture on Linux. The harness owns
one UI thread, uses STA on Windows, installs Avalonia's synchronization context,
and runs the native dispatcher message loop. NUnit awaits each submitted task;
it does not block a task with `Wait`, `Result`, or a nested modal test pump.
Namespace teardown awaits termination of the dispatcher.

## NodeSet2 explorer lane

The six additional `NodeSetDesktopTests` cases run separately from the
24-case count gate above. They exercise the File menu, multiple-file import,
the existing explorer and inspector, global offline search, AllNodes,
download consent followed by the nonstandard-model file picker, and dependency
cancellation without replacing the current graph. Invalid-file cases verify
that errors reach the banner and log, Diagnostics and Log remain usable, the
current graph survives, and a corrected file can be opened. An invalid
download also has to allow local dependency selection.

After building the matching Release graph, run them on the same real desktop
backend:

```bash
dotnet test tests/Opc.Ua.Lens.Tests/Opc.Ua.Lens.Tests.csproj \
  -c Release -f net10.0 -p:CustomTestTarget=net10.0 --no-build --no-restore \
  --filter FullyQualifiedName~UaLens.Tests.NodeSets.NodeSetDesktopTests \
  -- NUnit.ExplicitMode=Relaxed NUnit.NumberOfTestWorkers=0
```

The OS file picker and remote repository are injected. These cases exercise
native Lens windows and bindings, not a real GitHub download or the operating
system's file-dialog implementation. Loader/repository tests use deterministic
files and HTTP responses; graph tests cover namespace remapping, cross-file
references, unknown targets and static attributes.

The separate, opt-in `NodeSetNetworkTests` fixture downloads DI using the
production repository adapter and resolves it into the offline graph. Run its
fully qualified fixture name with the same `NUnit.ExplicitMode=Relaxed` setting
only when access to the public OPC Foundation repository is intended.

To investigate a particular local file, run the dispatcher-watchdog case:

```bash
UALENS_NODESET_REPRO_FILE=/absolute/path/model.NodeSet2.xml \
dotnet test tests/Opc.Ua.Lens.Tests/Opc.Ua.Lens.Tests.csproj \
  -c Release -f net10.0 -p:CustomTestTarget=net10.0 --no-build --no-restore \
  --filter FullyQualifiedName~NodeSetImportResponsivenessTests \
  -- NUnit.ExplicitMode=Relaxed NUnit.NumberOfTestWorkers=0
```

The NUnit worker checks dispatcher heartbeats with a three-second timeout
while the file imports. A dependency prompt must render and allow cancellation;
an invalid file may report an error. This case checks responsiveness and cleanup,
not successful import of an arbitrary model. It leaves the supplied file unchanged.

## Namespace highlighting lane

`NamespaceHighlightDesktopTests` adds six explicit cases outside the 24-case
gate: live-session and imported-XML sources in Light, DarkStandard and DarkNavy.
They open the namespace picker through routed keyboard events and check the
selected label, rendered row backgrounds, markers and text styles. The cases
also cover later-expanded children, namespace zero, clearing the highlight,
source removal and preservation of tree selection and expansion.

```bash
dotnet test tests/Opc.Ua.Lens.Tests/Opc.Ua.Lens.Tests.csproj \
  -c Release -f net10.0 -p:CustomTestTarget=net10.0 --no-build --no-restore \
  --filter FullyQualifiedName~NamespaceHighlightDesktopTests \
  -- NUnit.ExplicitMode=Relaxed NUnit.NumberOfTestWorkers=0
```

The live-session namespace table and browse responses are injected; these
cases do not connect to a remote server. Offline cases import isolated XML
files through the production loader. The ordinary `NamespaceHighlightTests`
cover namespace remapping, appended namespaces, source replacement, refresh,
collapsed branches and placeholders without a desktop.

## Recorded execution evidence

The final four runs on Ubuntu 26.04.1 LTS with Xvfb/X11 each recorded
**24 passed, zero failed and zero skipped**: 96 executions in total.
They include pending-Write shell shutdown, palette captures, and all six
Light/Dark/System minimum-size cases. The shell client area was 980 × 640 DIP
at every native render scale.

| Logical DPI | Native render scale | Passed / executed | TRX |
| --- | --- | --- | --- |
| 96 | 1.0 / 100% | 24 / 24 | `TestResults/lens-desktop-final-100/desktop.trx` |
| 120 | 1.25 / 125% | 24 / 24 | `TestResults/lens-desktop-final-125/desktop.trx` |
| 144 | 1.5 / 150% | 24 / 24 | `TestResults/lens-desktop-final-150/desktop.trx` |
| 192 | 2.0 / 200% | 24 / 24 | `TestResults/lens-desktop-final-200/desktop.trx` |

Each result directory contains nine verified PNGs and two CSV reports, covering
error/warning states and all 12 series colors in Light, DarkStandard and
DarkNavy. See the [capture and color-vision evaluation notes](../../docs/UaLens.md)
for rendering details and simulation limits. The 13 result-guard regression
tests also pass, including the missing-display preflight.

This virtual-display evidence does not validate Windows, physical displays,
physical keyboard or pointer input, native file choosers, screen readers, or
mixed-monitor changes. Those checks remain separate from the Linux/X11 result.

## Automated coverage and boundaries

| Cases | Checks |
| --- | --- |
| Endpoint selection, accept and cancel | Show the actual owned endpoint modal; select the secure endpoint rather than the unsecured default; preserve policy and anonymous identity; cancel without a selection. |
| Endpoint cancellation | An endpoint without Anonymous cannot be accepted at its root; cancellation closes the owned modal and reaches its awaiting caller without choosing another identity. |
| Five certificate dismissal cases | An injected empty store loads the actual certificate modal. Close click, Space, Enter and Escape each complete the modal task, remove the owned window and restore the owner's prior focus. A fifth case verifies the parameterless designer constructor's Close click. No system certificate store is accessed. |
| Three write outcome and shutdown cases | The real write dialog accepts one request, disables editing and requires acknowledgement of success. Closing a pending write cancels and drains its local task while retaining the uncertain-outcome warning for acknowledgement. Shell shutdown cancels the owned write, keeps both native windows alive until its deferred response completes, and only then disposes the workspace and closes the windows. The session and structured-value lookup are injected. |
| Monitored-item validation | Invalid sampling and percent deadband values keep the modal open, focus the responsible input and expose format/range guidance through the automation peer. Corrected inputs produce the expected configuration. |
| Tool open/close | Open the actual searchable catalog from the shell, select a Monitor, verify its document view and tab bindings, close through the tab button, and recover the welcome view. Cancelled catalogs create no document. |
| Document keyboard navigation | Route Ctrl+Tab and Ctrl+Shift+Tab through the visible tab strip, including wraparound. F2 focuses the selected inline editor. Enter commits its draft and Escape discards it. Text input does not trigger Ctrl+W; the tab-strip route closes the selected document. |
| Workspace save/load | Ctrl+S/Ctrl+O reach the shell menus and connection controller. The real JSON store writes and reads an isolated file; reopening restores two actual tool views, titles, selection and explorer state offline. Only the OS file chooser is replaced by an injected `IStorageProvider`. |
| Shutdown with an owned task | Workspace work shows an actual owned endpoint modal. Closing the shell cancels that work and dismisses the modal. A controlled cleanup gate proves that the native shell remains open until the task drains, then releases its window handle and documents. |
| Inspector worker completion | Attribute and reference reads complete on a real worker thread. Every row collection notification must run on the native Avalonia dispatcher thread, and both rendered inspector views must receive the completed rows. |
| Six minimum-size cases | Light, Dark and System themes, each with diagnostics hidden and shown. Check actual client size, native handle, display, shell-action bounds, focus, and monitor layout after the explorer is collapsed. |
| Monitor lane controls and names | The monitor agent's desktop cases select every named timing-line style through keyboard routes and the rendered pointer target, preserve configuration and records, and inspect automation-peer names independently of control values. The name-only case does not claim screen-reader execution. |

Tests wait for property changes, task completion and dispatcher animation frames.
They do not use sleeps to guess when an operation finished. Each case disposes
its workspace and owned windows and removes its isolated files.

Owner-only closing lets the shell start shutdown even when a pending child would
veto closure. It awaits each owned `IAsyncDisposable` before closing that child,
then disposes the workspace. The shutdown tests exercise the shell's close
request, not a direct call to the child dialog's disposal method.

The keyboard tests inject **Avalonia routed key events**, not physical OS input.
They test shell routing, rendered focus and bindings, not keyboard drivers,
Windows UI Automation, AT-SPI, Narrator or NVDA. The storage-provider test does
not validate the native file chooser's keyboard behavior or permission prompts.
These distinctions apply even though the windows and layout are native.

## Guard regression tests

The count and display gates have executable negative-path tests:

```bash
python3 .github/scripts/test_desktop_result_guard.py
```

These Python standard-library tests invoke the actual PowerShell validator with
generated TRX reports. They accept the expected passing report and reject zero
tests, skips, forged passing counters, failures, wrong counts, missing results,
duplicate identities, unrelated replacement tests, failed run summaries and
missing counters/reports. On Linux, a thirteenth case removes `DISPLAY` and
proves that a stale passing report cannot bypass the display preflight.

PowerShell 7 is required. No NUnit build, display server or package installation
is needed for these guard tests. A `dotnet` stub prevents a regression in the
script from starting a real build or test host. Generated reports and the stub
stay under `TestResults/lens-desktop-guard-tests` and are removed after each case.
These checks validate the gate, not the desktop application.

## DPI and theme matrix

The automated fixture runs all three supported appearance choices at the
runner's **actual** platform scale and prints that scale in the test log. It
does not emulate DPI by changing control dimensions or overriding a scale
property. The Windows CI lane runs the six cases at its desktop's configured
scale; it does not claim to cover all rows below.

For release qualification, repeat the full 24-case command in a fresh process
for each scale. Configure scaling using the OS display settings, keep the
desktop unlocked, and set the expected scale so a misconfigured run fails:

```powershell
$env:UALENS_DESKTOP_EXPECTED_SCALE = '1.5'
pwsh .github/scripts/test-lens-desktop.ps1 -NoBuild
```

For the native X11 virtual-display matrix, configure `Xft.dpi` through `xrdb`
and keep the X server from resetting between `xrdb` and the test host:

```bash
mkdir -p TestResults/lens-desktop-150-xft
xvfb-run -a \
  -f "$PWD/TestResults/lens-desktop-150-xft/xauthority" \
  -e "$PWD/TestResults/lens-desktop-150-xft/xvfb.log" \
  -s '-screen 0 2560x1800x24 -dpi 144 -noreset -nolisten tcp' \
  bash -c 'printf "Xft.dpi: 144\n" | xrdb -merge &&
    UALENS_DESKTOP_EXPECTED_SCALE=1.5 pwsh -NoProfile \
      -File .github/scripts/test-lens-desktop.ps1 -Configuration Debug -NoBuild \
      -ResultsDirectory TestResults/lens-desktop-150-xft'
```

This requires `xrdb` in addition to the basic Xvfb prerequisites. For 125%, use
120 for both DPI values, expected scale 1.25, and a separate `125-xft` results
directory. For 200%, use 192, expected scale 2.0, and `200-xft`.
Setting Xvfb's `-dpi` alone left Avalonia at scale 1.0 in the recorded environment;
the expected-scale guard rejected that configuration. `Xft.dpi` plus `-noreset`
produced the verified native scales above. No test assigns `RenderScaling`.

| Actual OS scale | Light | Dark | System (OS light / OS dark) |
| --- | --- | --- | --- |
| 100% / 1.0 | Run | Run | Run twice |
| 125% / 1.25 | Run | Run | Run twice |
| 150% / 1.5 | Run | Run | Run twice |
| 200% / 2.0 | Run | Run | Run twice |

For each cell, retain TRX and the logged OS, client size and actual render scale.
Use `-ResultsDirectory TestResults/lens-desktop-150-light` or another unique
repository-relative directory to keep runs separate. Record OS version,
resolution, window manager and GPU/backend in the release evidence.

At minimum width, showing the explorer and diagnostics together intentionally
leaves a narrow document area. Automated checks verify the shell actions remain
reachable and that hiding the explorer recovers enough space for the Monitor's
400 DIP fixed columns. They do **not** claim every tool fits without scrolling
when every pane is expanded. Manually inspect resizing, the splitters, scrolling
and tool-specific truncation with realistic long node names and values.

## Manual input and assistive-technology checks

Run these separately and report them as manual evidence, not NUnit passes:

1. Using only a physical keyboard, move through connection controls, the catalog,
   document tabs, rename and close, then use save/open and cancel the native file
   choosers. Check Tab/Shift+Tab order, visible focus and Escape dismissal.
2. With Narrator or NVDA on Windows, or the platform screen reader on Linux,
   inspect the accessible names and roles of icon actions, catalog entries,
   document close buttons and dialog inputs. Confirm selection, validation and
   busy/result messages are understandable without relying on color.
3. At each DPI/theme matrix cell, inspect the 980 × 640 DIP shell and minimum-size
   dialogs. Verify action buttons, focus indicators, text contrast, long labels,
   monitor columns, chart controls and scrollbars remain usable.
4. Move the shell and an owned dialog between monitors with different DPI,
   switch the OS light/dark preference while System is selected, and repeat focus
   and dismissal checks.

No automated pass substitutes for these checks. Hardware identities, live-server
operations, screen-reader announcements and mixed-monitor DPI transitions remain
outside this offline desktop lane.
