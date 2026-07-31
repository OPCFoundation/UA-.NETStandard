# PumpX-2000 — Product Datasheet

<table>
<tr><td><b>SimPump Corp</b><br/>Single-stage end-suction centrifugal process pump</td>
<td align="right">
Document no. <b>SPC-DS-PX2000</b><br/>
Revision <b>3</b> &nbsp;·&nbsp; Issue date <b>2025-04-17</b><br/>
Supersedes revision 2 (2024-11-05)
</td></tr>
</table>

> **Simulated device.** The PumpX-2000 is a *fictitious* product used by the
> [`PumpDeviceIntegrationServer`](./README.md) sample of the OPC UA .NET Standard
> stack. It does not exist as hardware. Every figure in this datasheet is
> reproduced by the sample server's simulation and is asserted by
> `tests/Opc.Ua.Di.Tests/PumpDatasheetConformanceTests.cs`, so this document and
> the running address space cannot drift apart.

---

## 1 Product description

The PumpX-2000 is a single-stage, end-suction, radially split centrifugal pump
for clean and lightly contaminated liquids in utility and process-water duty.
The hydraulic end is close-coupled to a foot-mounted asynchronous motor; the
bearing bracket carries a temperature probe and the discharge nozzle carries a
combined pressure/flow transmitter assembly.

The pump is instrumented as an OPC UA asset. It exposes its nameplate, its
process values and its supervision states through the
[OPC 40223 Pumps](https://reference.opcfoundation.org/specs/OPC-40223) companion
specification, layered on
[OPC 40001-1 Machinery](https://reference.opcfoundation.org/Machinery/v102/docs/)
and [OPC 10000-100 Device Integration](https://reference.opcfoundation.org/DI/v104/docs/).

| Item | Value |
|---|---|
| Type designation | PumpX-2000 |
| Product code | PX2000-32-160 |
| Construction | Single-stage, end-suction, radially split |
| Impeller | Closed, 160 mm nominal diameter |
| Service | Clean and lightly contaminated liquids, pH 5–10 |
| Mounting | Horizontal, foot-mounted baseplate |
| Companion specification | OPC 40223 `PumpType` |

---

## 2 Nameplate and identification data

Every nameplate field is published as a property of the pump's `Identification`
functional group. Browse names are listed with their namespace; the namespace
prefixes below map to `http://opcfoundation.org/UA/DI/` (DI),
`http://opcfoundation.org/UA/Machinery/` (Machinery) and
`http://opcfoundation.org/UA/Pumps/` (Pumps).

| Property | Namespace | DataType | Unit SN-001 | Unit SN-002 |
|---|---|---|---|---|
| `Manufacturer` | DI | `LocalizedText` | SimPump Corp | SimPump Corp |
| `ManufacturerUri` | DI | `String` | `https://simpump.example` | `https://simpump.example` |
| `Model` | DI | `LocalizedText` | PumpX-2000 | PumpX-2000 |
| `ProductCode` | DI | `String` | PX2000-32-160 | PX2000-32-160 |
| `DeviceClass` | DI | `String` | Pump | Pump |
| `HardwareRevision` | DI | `String` | 1.4 | 1.4 |
| `SoftwareRevision` | DI | `String` | 2.5.3 | 2.5.3 |
| `SerialNumber` | DI | `String` | SN-001 | SN-002 |
| `ProductInstanceUri` | DI | `String` | `urn:simdevice:SimPump:PumpX-2000:SN-001` | `urn:simdevice:SimPump:PumpX-2000:SN-002` |
| `AssetId` | DI | `String` | PMP-1001 | PMP-1002 |
| `ComponentName` | DI | `LocalizedText` | Feed Pump A | Feed Pump B |
| `Location` | Machinery | `String` | Plant 1 / Utility Skid / Bay 3 | Plant 1 / Utility Skid / Bay 4 |
| `YearOfConstruction` | Machinery | `UInt16` | 2025 | 2025 |
| `MonthOfConstruction` | Machinery | `Byte` | 4 | 4 |
| `DayOfConstruction` | Pumps | `Int32` | 17 | 17 |
| `ArticleNumber` | Pumps | `String` | PX2000-32-160-CI | PX2000-32-160-CI |
| `OrderProductCode` | Pumps | `String` | PX2000-32-160-CI-M30 | PX2000-32-160-CI-M30 |
| `TypeOfProduct` | Pumps | `String` | Centrifugal pump, end-suction | Centrifugal pump, end-suction |
| `Supplier` | Pumps | `String` | SimPump Corp | SimPump Corp |
| `CountryOfOrigin` | Pumps | `String` | DE | DE |
| `FabricationNumber` | Pumps | `String` | F-2025-0001 | F-2025-0002 |

Both units are the same product; they differ only in serial number, fabrication
number, asset identifier, component name and installation bay. The sample
server materialises two units by default and derives the same per-unit fields
for any further unit when started with `--pumps N` (`SN-00n`, `PMP-100n`,
`Feed Pump A/B/…`, `F-2025-000n`, `Bay n+2`).

---

## 3 Hydraulic performance

### 3.1 Design point

Reference liquid: water at 20 °C, density ρ = 998 kg/m³, ν = 1.0 mm²/s.

| Quantity | Symbol | Value |
|---|---|---|
| Rated flow (best efficiency point) | Q<sub>BEP</sub> | 25.0 m³/h (6.93 kg/s) |
| Rated head | H<sub>BEP</sub> | 25.5 m |
| Rated differential pressure | Δp<sub>BEP</sub> | 249.7 kPa (2.50 bar) |
| Rated efficiency | η<sub>BEP</sub> | 72.0 % |
| Hydraulic power | P<sub>hyd</sub> | 1.73 kW |
| Rated shaft power | P<sub>2</sub> | 2.41 kW |
| Shut-off head | H<sub>0</sub> | 32.0 m |
| NPSH required at BEP | NPSH<sub>R</sub> | 2.4 m |
| Rated speed | n | 2900 min<sup>-1</sup> |

### 3.2 Characteristic curves

The pump is characterised by a quadratic head curve and a parabolic efficiency
curve about the best efficiency point (Q in m³/h):

```text
H(Q)  = 32.0 − 0.0104 · Q²                     [m]
η(Q)  = 72.0 · (1 − 0.6 · ((Q − 25) / 25)²)    [%]
Δp(Q) = ρ · g · H(Q)                           [Pa]
ṁ(Q)  = ρ · Q / 3600                           [kg/s]
P₂(Q) = ρ · g · (Q / 3600) · H(Q) / (η(Q)/100) [W]
```

### 3.3 Performance table

| Flow Q [m³/h] | Mass flow ṁ [kg/s] | Head H [m] | Δp [kPa] | Efficiency η [%] | Shaft power P₂ [kW] |
|---:|---:|---:|---:|---:|---:|
| 10 | 2.77 | 30.96 | 303.1 | 56.4 | 1.49 |
| 15 | 4.16 | 29.66 | 290.4 | 65.1 | 1.86 |
| 20 | 5.54 | 27.84 | 272.6 | 70.3 | 2.15 |
| **25** | **6.93** | **25.50** | **249.7** | **72.0** | **2.41** |
| 30 | 8.32 | 22.64 | 221.7 | 70.3 | 2.63 |
| 35 | 9.70 | 19.26 | 188.6 | 65.1 | 2.82 |

Permissible continuous operating window: 0.5 · Q<sub>BEP</sub> … 1.3 · Q<sub>BEP</sub>
(12.5 … 32.5 m³/h).

### 3.4 Process schematic and instrument tags

```mermaid
flowchart LR
    V[("Suction vessel<br/>V-101")]
    LT(["LT-101<br/>Level"])
    S1[/"Suction nozzle<br/>DN 50"/]
    P(["P-101<br/>PumpX-2000"])
    M["M-101<br/>3.0 kW motor"]
    TT(["TT-102<br/>Bearing temp."])
    TT1(["TT-101<br/>Fluid temp."])
    PT(["PDT-101<br/>Differential pressure"])
    FT(["FT-101<br/>Mass flow"])
    JE(["JE-101<br/>Power / efficiency"])
    D[/"Discharge nozzle<br/>DN 32"/]
    H[("Process header")]

    V --> LT --> S1 --> P --> D --> FT --> H
    M -.drive.-> P
    TT -.bearing bracket.-> P
    TT1 -.suction line.-> S1
    PT -.suction vs. discharge.-> D
    JE -.motor terminals.-> M

    classDef sensor fill:#eef6ff,stroke:#4a76a8,stroke-width:1px;
    class LT,TT,TT1,PT,FT,JE sensor;
```

| Tag | Measurand | OPC UA browse path (relative to the pump) | Unit |
|---|---|---|---|
| PDT-101 | Differential pressure | `Operational/Measurements/DifferentialPressure` | Pa |
| TT-101 | Fluid temperature | `Operational/Measurements/FluidTemperature` | K |
| TT-102 | Bearing temperature | `Operational/Measurements/BearingTemperature` | K |
| JE-101 | Shaft power input | `Operational/Measurements/PumpPowerInput` | W |
| FT-101 | Mass flow | `Operational/Measurements/MassFlow` | kg/s |
| JE-101 | Pump efficiency | `Operational/Measurements/PumpEfficiency` | % |
| LT-101 | Suction vessel level | `Operational/Measurements/Level` | m |
| — | Start counter | `Operational/Measurements/NumberOfStarts` | – |

---

## 4 Operating limits

Each limit is published as the `EURange` property of the corresponding
measurement variable, and each measurement carries the `EngineeringUnits`
property with the UNECE unit code shown below.

| Measurement | Engineering unit | `EURange` low | `EURange` high | Nominal |
|---|---|---:|---:|---:|
| Differential pressure | Pa (Pascal) | 0 | 400 000 | 249 655 |
| Fluid temperature | K (Kelvin) | 263.15 (−10 °C) | 393.15 (120 °C) | 313.15 (40 °C) |
| Bearing temperature | K (Kelvin) | 273.15 (0 °C) | 423.15 (150 °C) | 333.15 (60 °C) |
| Shaft power input | W (Watt) | 0 | 4 000 | 2 408 |
| Mass flow | kg/s | 0 | 10 | 6.93 |
| Pump efficiency | % | 0 | 100 | 72 |
| Suction vessel level | m (Metre) | 0 | 5 | 2.5 |

| Additional limit | Value |
|---|---|
| Maximum casing working pressure | 10 bar |
| Maximum permissible starts per hour | 15 |
| Minimum continuous flow | 12.5 m³/h |
| Permissible ambient temperature | −10 … 40 °C |
| Sound pressure level at 1 m | ≤ 72 dB(A) |

---

## 5 Motor and electrical data

| Item | Value |
|---|---|
| Rated power | 3.0 kW |
| Supply | 400 V, 3~, 50 Hz |
| Rated speed | 2900 min<sup>-1</sup> |
| Rated current | 6.1 A |
| Efficiency class | IE3 |
| Insulation / protection | Class F / IP55 |
| Duty type | S1 (continuous) |

---

## 6 Materials and connections

| Component | Material |
|---|---|
| Casing | Cast iron EN-GJL-250 |
| Impeller | Bronze CuSn10 |
| Shaft | Stainless steel 1.4021 |
| Shaft seal | Mechanical seal, SiC/carbon/EPDM |
| Bearings | Grease-lubricated deep-groove ball bearings |

| Connection | Size | Standard |
|---|---|---|
| Suction nozzle | DN 50, PN 16 | EN 1092-2 |
| Discharge nozzle | DN 32, PN 16 | EN 1092-2 |

| Dimension | Value |
|---|---|
| Length × width × height | 620 × 240 × 380 mm |
| Mass (pump with motor) | 68 kg |

---

## 7 Monitoring, supervision and alarms

### 7.1 Trip points

The bearing-temperature chain is monitored by a `NonExclusiveLimitAlarmType`
instance published at `Events/OverTempAlarm`. Its `SourceNode` is the
`BearingTemperature` variable and its limits are the datasheet trip points:

| Limit | Value | Equivalent | Action |
|---|---:|---|---|
| `HighHighLimit` | 373.15 K | 100 °C | Trip — stop the pump |
| `HighLimit` | 363.15 K | 90 °C | Alarm — reduce load, check cooling |
| `LowLimit` | 283.15 K | 10 °C | Warning — lubricant below operating viscosity |
| `LowLowLimit` | 278.15 K | 5 °C | Warning — risk of freezing |

The alarm is acknowledgeable; the sample accepts every acknowledge request.

### 7.2 Supervision states (NAMUR-style)

| Supervision variable | Browse path | Set condition | Reset condition |
|---|---|---|---|
| Motor overheat | `Events/SupervisionPumpOperation/MotorOverheat` | Bearing temperature ≥ 363.15 K (90 °C) | Bearing temperature < 361.15 K (88 °C) |
| Cavitation | `Events/SupervisionProcessFluid/Cavitation` | Suction level < 2.10 m (NPSH<sub>A</sub> < NPSH<sub>R</sub>) | Suction level > 2.20 m |

`MotorOverheat` drives the activation of `OverTempAlarm`; the resulting
condition events are delivered through the `HasNotifier` chain from the pump to
the `Server` object.

---

## 8 Simulation profile

The sample server reproduces the datasheet with a deterministic model driven by
a single independent variable — volumetric flow. Every other published value is
derived from the characteristic curves in section 3.2, so the published values
are mutually consistent at all times. The simulation advances on one shared
250 ms tick; each pump uses a fixed phase offset of 17 ticks per instance so the
units never move in lockstep.

| Signal | Model | Simulated range |
|---|---|---|
| Flow | `Q(t) = 25 · (1 + 0.30 · sin(0.03 · t))` | 17.50 … 32.50 m³/h |
| Differential pressure | `ρ · g · H(Q)` | 205.7 … 282.1 kPa |
| Mass flow | `ρ · Q / 3600` | 4.85 … 9.01 kg/s |
| Efficiency | `η(Q)` | 68.1 … 72.0 % |
| Shaft power | `ρ · g · (Q/3600) · H(Q) / η` | 2.01 … 2.73 kW |
| Bearing temperature | `323.15 + 10 · (P₂/P₂,BEP) + cooling-fault excursion` | 331.5 … 378.2 K (58.4 … 105.1 °C) |
| Fluid temperature | `313.15 + 5 · sin(0.01 · t)` | 308.15 … 318.15 K (35 … 45 °C) |
| Suction level | `2.5 + 0.5 · sin(0.02 · t)` | 2.00 … 3.00 m |
| Start counter | one start per 15 simulated minutes | monotonic |

*Cooling-fault excursion*: every 64 ticks (16 s) the simulated bearing-cooling
water is interrupted for the last 8 ticks (2 s), ramping the bearing temperature
linearly by up to +43.75 K. The excursion crosses both the `HighLimit` and the
`HighHighLimit` trip points, so a full alarm activate/clear cycle — including the
`MotorOverheat` supervision transition — is observable roughly every 16 seconds.

*Cavitation*: the suction level sine dips below the 2.10 m NPSH threshold once
per level cycle (≈ 78 s). Because the state only clears again above 2.20 m, the
`Cavitation` supervision state is held for approximately 19.6 s (78 ticks) per
cycle.

---

## 9 Standards and references

| Reference | Title |
|---|---|
| OPC 40223 | OPC UA for Pumps and Vacuum Pumps — Part 1: Pumps |
| OPC 40001-1 | OPC UA for Machinery — Part 1: Basic Building Blocks |
| OPC 10000-100 | OPC UA Part 100: Devices |
| EN ISO 9906 | Rotodynamic pumps — Hydraulic performance acceptance tests, grade 2B |
| EN 1092-2 | Flanges and their joints — Cast iron flanges |
| IEC 60034-30-1 | Rotating electrical machines — Efficiency classes (IE3) |

---

## 10 Document history

| Revision | Date | Change |
|---|---|---|
| 3 | 2025-04-17 | Bearing-temperature trip points aligned with IE3 motor package; simulation profile section added. |
| 2 | 2024-11-05 | NPSH<sub>R</sub> corrected to 2.4 m at BEP. |
| 1 | 2024-03-12 | First issue. |

**Disclaimer** — SimPump Corp, the PumpX-2000 and all identifiers in this
document are fictitious and exist only to give the
[`PumpDeviceIntegrationServer`](./README.md) sample a realistic asset to
publish. Do not use these figures for engineering purposes.
