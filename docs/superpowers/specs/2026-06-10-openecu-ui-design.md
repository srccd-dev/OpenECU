# OpenECU Avalonia UI — Design Spec

> Live-gauge desktop UI for OpenECU's read-only diagnostics, built on the existing
> `OpenEcu.Core` + `OpenEcu.Transport.Serial`. Cross-platform Avalonia; light/dark themes
> (light is the default).

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
- **Constant** live polling of supported PIDs + periodic DTC refresh (~5 s) — essential
  real-time feedback for tuning, treated as a core reliability requirement (see §8).
- An **optional Racing Dashboard** mode — a sporty, analog-tachometer dashboard *skin* the
  user can toggle to (a fun perk). The standard dashboard ships first (plan 9); the Racing
  mode is a focused fast-follow (plan 10), enabled by the data-driven/pluggable architecture
  (see §7).
- **Light and dark themes** (light is the default; dark for power users), with a
  user-selectable accent color (white, teal, blue, green, yellow, red, black).

**Non-goals (v1) — but explicitly *designed for* (see §7):**
- Writing/flashing or any ECU modification (stays read-only; v2+).
- **Heat-map / fuel-ignition map visualization** (needs reading map tables — v2/write
  territory). v1 only ensures the shell + data flow accommodate a future Maps view.
- **Map difference reporting** (store the original map as a baseline; show the modified map
  beside it with differences highlighted, side-by-side and easy to read) — v2; v1 keeps the
  data model snapshot-friendly so baseline-vs-modified diffing slots in later (see §7).
- **User-customizable dashboard layout** (choosing which readings go on which gauge/tile).
  v1 ships a fixed default layout but stores it as *data* so v2 adds editing + persistence.
- Mode 04 clear-DTCs (UI button is present but disabled/stubbed in v1).
- ELM327/Bluetooth transport (separate plan).

## 3. Locked decisions (from brainstorming)

| Decision | Choice |
|---|---|
| Framework | Avalonia 11, .NET 8, Fluent theme |
| Theme | **Light default** + dark option (user toggle) |
| Accent color (v1) | User-selectable, **default teal**: white, teal, blue, green, yellow, red, black (theme-aware) |
| Settings | Theme + accent **persist** to a small settings file across sessions |
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

**Settings** (a small control set in the connection bar or a settings flyout): a
**light/dark theme toggle** (default light) and an **accent-color picker** (white, teal,
blue, green, yellow, red, black — rendered theme-aware for contrast). These bind to app-level
theme resources so all gauges/tiles/controls pick them up live. Theme + accent **persist** to
a small settings file and are restored on launch. Default theme **light**, default accent
**teal**.

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

**Optional Racing Dashboard (plan 11).** A second, optional dashboard *mode* — an alternate
skin, not a separate data path — reached via a **Standard / Racing** toggle. Inspired by
race-cockpit displays (e.g. UMA Racing), modernized: a large **analog tachometer** (arc +
tick marks + sweeping needle + redline zone) with a digital RPM readout, a **gear box** (kept
as a greyed `—/n/a` since OBD-II doesn't expose gear on the 955i — retained for the look and
for bikes that *do* report it), a prominent **speed**, and a **left-aligned** stack of angular
"race readout" bars for the metrics we actually read (throttle, coolant, timing advance, O2).
Dark cockpit by default, but **honors the app light/dark theme** and accent. **Tach range is
configurable** (`TachConfig` = max + redline RPM; default redline 9,500 / max 11,000 for the
955i) so it can adjust per model later. It binds to the same `LiveDataService` metrics, so it
needs one new custom control — an `AnalogTachometer` (sibling of `RadialGauge`) — plus the
racing layout and the mode toggle. **Not built in v1**; built in plan 11 once the core app
runs. The pluggable view system + data-driven layout accommodate it without rework.

**Map difference reporting (v2).** A headline v2 capability: when a user edits a map, they
should never guess what changed — OpenECU stores the **original map as a baseline** and shows
the **modified map beside it with differences highlighted**, side-by-side and easy to read.
v1 designs for this by keeping tabular/map data as **immutable snapshots** (a reading or a
loaded map is a value snapshot, never a mutated buffer), so capturing a baseline and diffing
two snapshots — and rendering the diff with the same color-ramp the `HeatMapControl` uses — is
straightforward to add. Not built in v1.

## 8. Polling model

Constant, low-latency feedback is **essential during tuning**, so the polling loop is a core
reliability requirement, not best-effort. On connect, `LiveDataService` reads supported PIDs
once, then runs a continuous background loop:

- **Weighted round-robin** — hero PIDs (RPM, coolant) are polled **every cycle**; the
  remaining PIDs are interleaved across cycles, so the headline gauges stay snappy while
  everything still refreshes (a full sweep is ~1–2 s on K-line).
- **DTCs refresh every ~5 s** (firm requirement) and on demand.
- **Per-PID failures are isolated** — logged, that tile shows "—", and the PID is retried on
  the next cycle; one bad read never stalls the stream.
- The loop is cancellable; Disconnect stops it and closes the port. A dropped connection is
  detected (next-I/O error) and surfaced so the user can reconnect without restarting.
- The service exposes a simple "last update" heartbeat so the UI can show that data is live
  (and flag a stall immediately, which matters mid-tune).

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
4. App shell: project scaffold, light/dark theme + accent picker, `MainWindow` + connection
   bar + view switcher.
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

## 13. Resolved decisions

- **Hero PIDs are weighted highest** — RPM + coolant polled every cycle; non-hero PIDs
  interleaved one-per-cycle (exact ratio tunable on hardware).
- **Theme + accent persist** to a small settings file, restored on launch.
- **Default theme = light, default accent = teal**; full accent picker
  (white/teal/blue/green/yellow/red/black) ships in v1.
