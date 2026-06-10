# OpenECU Avalonia UI — Design Spec

> Live-gauge desktop UI for OpenECU's read-only diagnostics, built on the existing
> `OpenEcu.Core` + `OpenEcu.Transport.Serial`. Cross-platform Avalonia, dark theme.

- **Date:** 2026-06-10
- **Status:** Approved (design); pending implementation plan
- **Author:** Michael Neumann (with Claude)
- **Repo:** <https://github.com/srccd-dev/OpenECU> · License: MIT

---

## 1. Background & goal

The OpenECU core is complete and hardware-validated: it connects to the bike (ISO9141-2,
`KLineObdSession`) and exposes `ConnectAsync`, `ReadSupportedPidsAsync`, `ReadPidAsync`,
`ReadDtcsAsync`. This spec covers the **UI** — a real app users run, not a console probe —
that turns those reads into a live dashboard, a diagnostics view, and a raw console.

Visual inspiration: the original TuneECU dashboard (gauge-forward) and the modern,
data-dense style of teslax.app / ecubooster.com.

## 2. Scope / non-goals

**In scope (v1):**
- A cross-platform Avalonia desktop app with a shared connection bar and three views:
  **Dashboard** (live gauges + tiles + fault strip), **Diagnostics** (full PID table + DTC
  panel), **Console** (raw protocol log).
- Continuous live polling of supported PIDs; periodic DTC refresh.
- Dark theme matching the ECU-monitor aesthetic.

**Non-goals (v1) — but explicitly *designed for* (see §7):**
- Writing/flashing or any ECU modification (stays read-only; v2+).
- **Heat-map / fuel-ignition map visualization** (needs reading map tables — v2/write
  territory). v1 only ensures the shell + data flow accommodate a future Maps view.
- **User-customizable dashboard layout** (choosing which readings go on which gauge/tile).
  v1 ships a fixed default layout but stores it as *data* so v2 adds editing + persistence.
- Mode 04 clear-DTCs (UI button is present but disabled/stubbed in v1).
- ELM327/Bluetooth transport (separate plan).

## 3. Locked decisions (from brainstorming)

| Decision | Choice |
|---|---|
| Framework | Avalonia 11, .NET 8, Fluent **dark** theme |
| MVVM | `CommunityToolkit.Mvvm` (MIT) |
| Dashboard layout | **Hybrid** — hero radial gauges + tile grid (option C) |
| Hero gauges (v1 default) | **RPM + Coolant** |
| Gauge rendering | **Custom `RadialGauge` control** (Avalonia geometry; no charting dep) |
| Views (v1) | Dashboard, Diagnostics, Console |
| New project | `src/OpenEcu.App` |

## 4. Architecture

MVVM. The app never touches the transport or session directly from views — everything
flows through a `LiveDataService`.

```
SerialPortTransport ──► LoggingTransport (NEW decorator) ──► KLineObdSession (Core)
   (IEcuTransport+IBreakLine)   │ raises Tx/Rx byte events            ▲
                                ▼                                     │
                          ConsoleViewModel                     LiveDataService (NEW)
                                                               • owns session + connect/disconnect
                                                               • background poll loop (round-robin
                                                                 supported PIDs; periodic DTC refresh)
                                                               • raises: per-PID PidReading updates,
                                                                 DTC list, ConnectionState
                                                                     │  (marshalled to UI thread)
                                                                     ▼
                                          DashboardViewModel · DiagnosticsViewModel · ConsoleViewModel
                                                                     │
                                                                  Views (XAML, thin)
```

- **`LiveDataService`** is the testable heart: connection lifecycle + the polling/aggregation
  loop. It holds a dictionary of current `MetricViewModel`s keyed by PID and raises change
  notifications. Unit-tested by driving a `KLineObdSession` over the existing `FakeEcu`.
- **`LoggingTransport`** — a thin `IEcuTransport` decorator wrapping the real transport,
  raising `BytesWritten`/`BytesRead` events. Feeds the Console with zero coupling to the
  session. Unit-testable.
- Threading: the poll loop runs on a background task; updates marshal to the UI thread via
  Avalonia's `Dispatcher.UIThread.Post`. ViewModels expose `[ObservableProperty]` fields.

## 5. Project structure

```
src/OpenEcu.App/
├─ OpenEcu.App.csproj            # Avalonia 11, net8.0, CommunityToolkit.Mvvm
├─ Program.cs / App.axaml(.cs)   # Avalonia bootstrap (desktop), dark Fluent theme
├─ Services/
│   ├─ LiveDataService.cs        # connect + poll loop + update aggregation
│   └─ LoggingTransport.cs       # IEcuTransport decorator (Tx/Rx events)
├─ Model/
│   ├─ MetricDescriptor.cs       # PID -> name, unit, min, max, accent (the catalog)
│   └─ DashboardLayout.cs        # hero-slots + tile-slots (data-driven; default instance)
├─ ViewModels/
│   ├─ MainViewModel.cs          # connection state, port list, active view, view list
│   ├─ MetricViewModel.cs        # one live reading (descriptor + current value)
│   ├─ DashboardViewModel.cs     # builds gauge/tile VMs from DashboardLayout
│   ├─ DiagnosticsViewModel.cs   # full PID table + DTC panel
│   └─ ConsoleViewModel.cs       # raw Tx/Rx log lines
├─ Controls/
│   └─ RadialGauge.axaml(.cs)    # custom bindable arc gauge
└─ Views/
    ├─ MainWindow.axaml          # connection bar + view switcher
    ├─ DashboardView.axaml       # ItemsControl over hero-slots + tile-slots
    ├─ DiagnosticsView.axaml
    └─ ConsoleView.axaml
tests/OpenEcu.App.Tests/         # LiveDataService + ViewModel logic (via FakeEcu)
```

## 6. Components

**Connection bar** (in `MainWindow`, bound to `MainViewModel`): COM-port dropdown
(auto-populated via `SerialPortEnumerator`, refreshable), Connect/Disconnect, status text
(ECU id + state), and a one-line hint to set the FTDI latency timer to 1 ms (we cannot set
it programmatically). On Connect, `LiveDataService` opens the port, `ConnectAsync`, reads
supported PIDs, and starts polling.

**`MetricDescriptor` catalog** — static table mapping each known PID to display name, unit,
sensible gauge min/max, and accent color. Single source of truth for both gauges and tiles.

**`MetricViewModel`** — one displayed reading: its descriptor + current `Value` (observable).
`LiveDataService` updates these as PID reads return; gauges/tiles bind to them.

**`RadialGauge` control** — custom Avalonia control with bindable `Value`, `Minimum`,
`Maximum`, `Unit`, `Label`, `Accent`; draws a 180° arc track + value arc + center text.
Reusable for any metric.

**Dashboard** — `DashboardView` renders an `ItemsControl` of hero `RadialGauge`s and an
`ItemsControl` of `MetricTile`s, both sourced from `DashboardViewModel`, which is built from
the `DashboardLayout` model (not hardcoded). Plus a fault-code strip bound to the DTC list.

**Diagnostics** — a `DataGrid`/table of every supported PID (name · decoded value · unit ·
raw hex), live-updating, and a DTC panel (list of codes; a "Clear codes" button present but
disabled in v1).

**Console** — a scrolling, timestamped log of raw Tx/Rx hex frames and connection events,
fed by `LoggingTransport` events. A "pause" toggle and "clear" button.

## 7. Extensibility (v2-readiness) — first-class in v1

These shape v1's structure so v2 features slot in without rework:

**Data-driven dashboard (user customization later).** The dashboard is rendered from a
`DashboardLayout` (ordered hero-slots + tile-slots, each referencing a PID via
`MetricDescriptor`). v1 instantiates a fixed default (`heroes=[RPM, Coolant]`,
`tiles=[Throttle, Intake, Load, Timing, O2, Speed]`). Because the layout is data and the
view is an `ItemsControl` over it, v2 adds a layout editor + JSON persistence against the
same model — no change to gauges, tiles, or the dashboard view. **No hardcoded per-PID XAML.**

**Pluggable views + future heat maps.** `MainViewModel` holds an ordered collection of
views (name + content VM), and the view switcher renders that collection — adding a **Maps**
view is appending one entry, not editing a fixed tab set. A future `HeatMapControl` (a
generic 2-D grid bound to `double[,]` + axis labels, rendered with a modern color ramp —
the ecubooster/TuneECU-style fuel/ignition surface) is a planned sibling of `RadialGauge`
and will share the accent/color-ramp concept. **Not built in v1**; v1 only guarantees the
shell, navigation, and data flow accommodate it. (Heat-map *data* depends on reading ECU map
tables — v2/write scope.)

## 8. Polling model

On connect, `LiveDataService` reads supported PIDs once, then runs a background loop that
round-robins `ReadPidAsync` over the supported set (a full cycle is ~1–2 s on K-line),
updating each `MetricViewModel` as values return. DTCs refresh on a slower cadence (e.g.
every ~5 s) and on demand. The loop is cancellable; Disconnect stops it and closes the port.
Per-PID read failures are isolated (logged, that tile shows "—") so one bad read never stalls
the loop.

## 9. Error handling

- **Connect failure** (port busy / no sync / handshake timeout): caught, surfaced in the
  status bar with a plain-English message + the latency-timer hint; app stays usable.
- **Read failure mid-session**: isolated per PID; repeated failures flip connection state to
  "lost" and stop the loop, prompting reconnect.
- **Port disappears / unplugged**: detected on the next I/O error → graceful disconnect.
- All raw traffic + errors are visible in the Console for debugging.

## 10. Testing

- **`LiveDataService`** — unit-tested by composing a real `KLineObdSession` over the existing
  `FakeEcu`: connect, supported-PID read, a poll cycle updates the metric VMs, DTC refresh,
  per-PID failure isolation, disconnect stops the loop.
- **`LoggingTransport`** — unit-tested: wraps a `SimulatedTransport`, verifies Tx/Rx events
  fire with the right bytes and it passes data through unchanged.
- **ViewModel logic** — `DashboardViewModel` builds the correct gauge/tile VMs from a given
  `DashboardLayout`; `MainViewModel` connection-state transitions.
- **Views** — not unit-tested; verified by running the app (UI shell with no hardware, then
  live on the bike).

## 11. Phasing (for the implementation plan)

Bottom-up, each step runnable/testable:
1. `LoggingTransport` (tested).
2. `MetricDescriptor` catalog + `DashboardLayout` model + `MetricViewModel`.
3. `LiveDataService` (tested via `FakeEcu`).
4. App shell: project scaffold, dark theme, `MainWindow` + connection bar + view switcher.
5. `RadialGauge` control + Dashboard view (data-driven).
6. Diagnostics view.
7. Console view.
8. Manual hardware verification on the bike.

## 12. Risks

- **Avalonia learning/scaffold friction** — mitigate by standard templates + a thin shell first.
- **UI-thread marshalling** of background poll updates — use `Dispatcher.UIThread.Post`;
  keep the service UI-agnostic.
- **Gauge math** (arc geometry) — encapsulated in `RadialGauge`, visually verified.
- **K-line throughput** — a full PID cycle is ~1–2 s; gauges update per-PID as values arrive,
  so the UI feels live even though the bus is slow. Hero PIDs can be polled more often if needed.

## 13. Open questions (non-blocking)

- Default DTC refresh cadence (start ~5 s; tune on hardware).
- Whether to poll hero PIDs (RPM/coolant) more frequently than the round-robin (likely yes —
  a weighted schedule; decide during implementation).
- Theme accent color (default teal/blue; finalize during the RadialGauge step).
