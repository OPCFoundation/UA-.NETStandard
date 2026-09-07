# UaLens

Cross-platform **Avalonia 11** desktop demo for exercising OPC UA
subscriptions against the OPC Foundation .NET Standard stack.  Runs on
**Windows, Linux, macOS** with .NET 10.

## What it does

- Connects a `ManagedSession` to any OPC UA server (defaults to
  `ConsoleReferenceServer` on `localhost:62541`).
- Lets you swap **the subscription engine** at runtime between the new
  channel-based V2 (`DefaultSubscriptionEngineFactory`) and the classic
  per-callback engine (`ClassicSubscriptionEngineFactory`).
- Streams **data, event, and keep-alive notifications** through a bounded
  channel and renders them in a 60 fps log-scaled bar chart.
- Lets you add / remove monitored items through a node-picker dialog and
  edit subscription parameters (publishing interval, keep-alive, lifetime,
  priority, max-notifs, publishing enabled) through a settings dialog.

## Layout

```
┌─ ◆ UaLens — OPC UA Lens ──────────────────────────  [≡ Log] [? Help] [⏏ Quit] ┐
│                                                                                │
│ ┌─ Address space ─┐  ┌─ 🔌 Connection ────────────────────────────────────────┐│
│ │ ◉ Objects       │  │ Endpoint  opc.tcp://...  [⏻ Connect]  [↻ Engine: V2]  ││
│ │ ◉ Server        │  │ ● Connected — ChannelV2                                ││
│ │   ○ Time        │  └────────────────────────────────────────────────────────┘│
│ │   ◉ Diagnostics │  ┌─ ⚡ Subscription          [✚ Add] [✗ Remove] [⚙ Settings]┐│
│ │     …           │  │ engine=ChannelV2  pub=1000ms  KA=10  life=1000          ││
│ │                 │  │ seq  data:42  evt:0  ka:180         Σ data:5  evt:0    ││
│ │                 │  │   ▆     ▇  █     ▃        ▆  ▅       █  ◀ live bars   ││
│ │                 │  │ ● pub=1000ms / KA=10 / life=1000                       ││
│ │                 │  │ ┌────────────────────────────────────────────────────┐ ││
│ │                 │  │ │ 1 value:ns=2;s=Time   smp=1000ms                   │ ││
│ │                 │  │ └────────────────────────────────────────────────────┘ ││
│ └─────────────────┘  └────────────────────────────────────────────────────────┘│
│ Tab cycles focus · F1 help · Ctrl-A hides attributes · Ctrl-L hides log · Ctrl-Q quits │
│ ┌─ ≡ Log ────────────────────────────────────────────────────────────────────┐ │
│ │ 11:42:31.012 info Connection: Connecting to opc.tcp://...                  │ │
│ │ 11:42:31.482 info ChannelV2Adapter: Subscription created.                  │ │
│ └────────────────────────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────────────────┘
```

## Subscription animation

The Subscription panel hosts a custom Avalonia control
(`Views/AnimationCanvas.cs`) that:

- Maintains a 200-column ring buffer (50 ms / column → 10 s window).
- Drains the adapter''s `ChannelReader<NotificationEvent>` on every render
  tick and accumulates per-column counts of DataChange, Event, and
  KeepAlive notifications.
- Renders each column as a stacked bar with three colours
  (**green** = data, **amber** = event, **violet** = keep-alive).
- Bar height is log-scaled, so 1 KA/s and 10 kHz data both render
  readably.
- Header shows the active engine, revised publishing / keep-alive /
  lifetime values, and the latest sequence number per notification kind.
- Right side shows running totals (Σ messages and total values).

Driven by a `DispatcherTimer` at `DispatcherPriority.Render`,
GPU-accelerated through Avalonia''s render pipeline — so the chart is
smooth at 60 fps regardless of the subscription rate.

## Keyboard

| Action                              | Shortcut       |
|-------------------------------------|----------------|
| Cycle focus                         | Tab / Shift+Tab |
| Navigate inside the focused widget  | arrow keys     |
| Expand / collapse a tree node       | → / ←          |
| Default button (OK in dialogs)      | Enter          |
| Cancel a dialog                     | Esc            |
| Help                                | F1             |
| Toggle attributes panel             | Ctrl-A         |
| Toggle log                          | Ctrl-L         |
| Cycle animation view (Dots ↔ Bars)  | Ctrl-V         |
| Quit                                | Ctrl-Q         |

Avalonia''s native focus model handles Tab cycling — the action bar,
endpoint, Connect / Toggle Engine, the Add / Remove / Settings buttons,
the address-space tree, and the monitored-items list are all in the cycle
without any custom focus walker.

## Add monitored item

The **`✚ Add`** button on the Subscription panel acts on whatever node is
currently highlighted in the address-space tree.  It is enabled only
when that node can actually be subscribed:

| Selected node                                         | `✚ Add` | Mode  |
|-------------------------------------------------------|---------|-------|
| Variable                                              | ✓       | Value |
| Object whose `EventNotifier` has `SubscribeToEvents`  | ✓       | Event |
| Object that does not emit events                      | —       |       |
| Method, DataType, ObjectType, …                       | —       |       |

The status line under the buttons explains the current state ("○ Variable —
Value subscription · ns=…"; "◉ Object emits events (EventNotifier=0x01) · …";
"Method nodes cannot be subscribed.").  Manual NodeId entry is no longer
supported — pick the target in the tree first.

## Architecture

### Connect wizard

When the user clicks **⏻ Connect**:

1. **Discovery** — `DiscoveryClient.GetEndpointsAsync` against the typed
   URL.
2. **EndpointPickerDialog** — TreeView of every advertised endpoint with
   its `UserTokenPolicy` children (`Anonymous`, `UserName`, `Certificate`,
   `IssuedToken`).  `Certificate` and `IssuedToken` are visible but
   greyed and not selectable in this preview.  Default selection =
   `None / None` endpoint root.  Selecting an endpoint root connects
   Anonymous regardless of advertised policies; selecting a user-policy
   child uses that policy.
3. **CredentialsDialog** — only when `UserName` was explicitly chosen;
   asks for username + password (PasswordBox).
4. **CertificateTrustDialog** — only when the validator rejects the
   server certificate AND `AutoAcceptUntrustedCertificates == false`.
   Shows Subject / Issuer / NotBefore / NotAfter / Thumbprint /
   ApplicationUri / status code with **Accept once / Trust permanently
   / Reject**.  "Trust permanently" pushes the cert into the
   configured `TrustedPeerCertificates` store.

```
Avalonia (UI)
 └─ MainWindow / AddItemDialog / RemoveItemDialog / SubscriptionSettingsDialog
     └─ ViewModels (CommunityToolkit.Mvvm) — MainViewModel, BrowserViewModel
         └─ ConnectionService                         (OPC UA layer — unchanged)
             └─ ManagedSession + Subscription engine factory
                 └─ ISubscriptionAdapter             (ChannelV2 ⇄ Classic swap)
                     └─ Channel<NotificationEvent>   (bounded, drop-oldest)
                         └─ AnimationCanvas / counters

LogRingBuffer  ←  AppTelemetryContext  ←  SDK ILogger
       ↓                                      (any thread, lock-free)
       └─→ MainViewModel.LogLines (4 Hz pump on the UI thread)
```

The OPC UA layer (`Connection/`, `Subscriptions/`, `Telemetry/`,
`SmokeTest.cs`) is intentionally framework-agnostic — only `Views/` and
`ViewModels/` know about Avalonia.

## Headless smoke test

```
dotnet run -c Release -- --smoke
```

Opens a session against `opc.tcp://localhost:62541/Quickstarts/ReferenceServer`
(override with `--endpoint <url>`) and exercises both engines for 5 seconds
each, asserting that data notifications and value counts are non-zero.
Used as the regression guard for the OPC UA layer; doesn''t touch the GUI
and runs on CI / headless boxes.

## Build & run

```
dotnet build -c Release
dotnet run   -c Release
dotnet run   -c Release -- --smoke
```

Multi-targets `net8.0 ; net9.0 ; net10.0`.

### Native-AOT publish (net10 only)

```
dotnet publish -c Release -f net10.0 -r win-x64 --self-contained -o publish/aot
.\publish\aot\UaLens.exe
.\publish\aot\UaLens.exe --smoke
```

A self-contained native binary (~32 MB on Windows) is produced — no .NET
runtime install required.  Run all the `--probe-*` flags against it the
same as the JIT build.

## Packages

- `Avalonia` 11.3.14 (Desktop, Themes.Fluent, Fonts.Inter, Diagnostics)
- `CommunityToolkit.Mvvm` 8.4.2
- `Microsoft.Extensions.Logging` 10.0.7
- `Microsoft.Extensions.Hosting` 10.0.7 + `Microsoft.Extensions.Diagnostics.ResourceMonitoring` 10.5.0 — drives the bottom CPU / memory pane
- OPC UA stack: `Opc.Ua.Core`, `Opc.Ua.Configuration`, `Opc.Ua.Client`
