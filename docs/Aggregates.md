# Aggregates (OPC UA Part 13)

## Overview

The .NET Standard stack implements **OPC UA Part 13 (OPC 10000-13) v1.05.07 Aggregates** on the
server. The server computes aggregates from historical data retrieved through [Historical Access](HistoricalAccess.md)
(Part 11) and advertises support for all **37 standard aggregate functions** through the address space.

Clients request aggregates with the `HistoryRead` service and `ReadProcessedDetails`. A request includes:

- `startTime` and `endTime`
- `ProcessingInterval` in milliseconds
- One or more `AggregateType` NodeIds
- An optional `AggregateConfiguration`

The server divides the time range into intervals of `ProcessingInterval` milliseconds and produces one
aggregate value per interval.

## Contents

- [Overview](#overview)
- [Supported aggregate functions](#supported-aggregate-functions)
- [Architecture](#architecture)
  - [`AnnotationCount`](#annotationcount)
- [Server configuration](#server-configuration)
- [Client usage](#client-usage)
- [Notes and limitations](#notes-and-limitations)
- [References](#references)

## Supported aggregate functions

| Category | Functions |
|---|---|
| Interpolative | `Interpolative` |
| Averages / integrals | `Average`, `TimeAverage`, `TimeAverage2`, `Total`, `Total2` |
| Extrema | `Minimum`, `Maximum`, `MinimumActualTime`, `MaximumActualTime`, `Range`, `Minimum2`, `Maximum2`, `MinimumActualTime2`, `MaximumActualTime2`, `Range2` |
| Counts | `Count`, `AnnotationCount`, `DurationInStateZero`, `DurationInStateNonZero`, `NumberOfTransitions` |
| Start / end | `Start`, `End`, `Delta`, `StartBound`, `EndBound`, `DeltaBounds` |
| Quality / time-in-state | `DurationGood`, `DurationBad`, `PercentGood`, `PercentBad`, `WorstQuality`, `WorstQuality2` |
| Statistics | `StandardDeviationSample`, `VarianceSample`, `StandardDeviationPopulation`, `VariancePopulation` |

Part 13 §§5.4.3.4–5.4.3.40 defines each aggregate's result type, bounding behaviour, timestamp,
status-code rules, and special cases.

## Architecture

```
HistoryRead (ReadProcessedDetails)
      ↓
AsyncCustomNodeManager / CustomNodeManager2  → HistorianDispatcher.DispatchProcessedReadAsync
      ↓                                              ├─ provider implements IHistorianProcessedProvider → native push-down
      ↓                                              └─ otherwise: framework fallback
      ↓                                                    ├─ AnnotationCount → IHistorianAnnotationProvider (counts Annotations)
      ↓                                                    └─ other aggregates → AggregateManager.CreateCalculator
      ↓                                                          → stream raw values through IAggregateCalculator
```

- **`AggregateManager`** (`Opc.Ua.Server`) owns the registered aggregate factories, the server default
  `AggregateConfiguration`, and the `MinimumProcessingInterval`. `Aggregators` registers the standard
  functions. The server advertises them in `Server.ServerCapabilities.AggregateFunctions` and
  `HistoryServerCapabilities.AggregateFunctions`.
- **`IAggregateCalculator`** implementations (`AggregateCalculator` and the specialized
  `Average`/`MinMax`/`Count`/`StartEnd`/`Status`/`StdDev` calculators) compute the aggregates from a stream
  of raw `DataValue`s.
- A historian provider may compute aggregates itself by implementing `IHistorianProcessedProvider`
  (native push-down). When it does not, the framework streams raw values through the calculator,
  using the node's `Stepped` capability. After each raw sample, the framework collects results for completed intervals.
  The fallback returns `Bad_TooManyOperations` if it exceeds the 100,000-output buffer limit.

### AnnotationCount

`AnnotationCount` (Part 13 §5.4.3.20) counts **Annotations** in each interval, not raw data values.
It uses the node's annotation history through `IHistorianAnnotationProvider`, not the raw-value calculator.
If the resolved provider does not expose annotation history, an `AnnotationCount` request returns
`Bad_AggregateNotSupported`.

## Server configuration

`AggregateConfiguration` controls how non-Good data affects the result:

| Property | Default | Meaning |
|---|---|---|
| `TreatUncertainAsBad` | **`true`** | Treat Uncertain samples as Bad when computing the aggregate `StatusCode` (Part 13 §4.2.1.2). |
| `PercentDataBad` | `100` | Minimum % of Bad data in an interval for the interval `StatusCode` to be Bad. |
| `PercentDataGood` | `100` | Minimum % of Good data in an interval for the interval `StatusCode` to be Good. |
| `UseSlopedExtrapolation` | `false` | Stepped (hold-last) vs sloped extrapolation past the last value. Ignored for Simple Bounds. |

For processed history reads, `AggregateConfiguration.UseServerCapabilitiesDefaults = true` selects
the node's `HistorianNodeCapabilities.DefaultAggregateConfiguration`, matching its advertised
`HistoricalDataConfiguration` object. The dispatcher snapshots those defaults for the request;
an explicit client configuration overrides them without modifying the advertised defaults.
`AggregateManager.GetDefaultConfiguration(...)` exposes the server-wide defaults used by other
aggregate consumers. Per Part 13 §4.2.1.2 the default `TreatUncertainAsBad` value is `true`.

Aggregate-specific rules still apply. Basic Minimum, Maximum, Range and their ActualTime variants
select Good raw extrema. An Uncertain value beyond the selected extremum makes the result
`Uncertain_DataSubNormal, Calculated` without replacing the Good value. An interval with no Good
extremum returns `Bad_NoData`; the separate Minimum2/Maximum2 families also consider their defined bounds.

> **Migration note:** in earlier builds the server default used `TreatUncertainAsBad = false`. Clients that
> require the old behaviour should send an explicit `AggregateConfiguration` with
> `TreatUncertainAsBad = false` rather than relying on `UseServerCapabilitiesDefaults`. See the
> [Migration Guide](MigrationGuide.md).

`AggregateManager.MinimumProcessingInterval` bounds the smallest interval the server will accept.

## Client usage

Use the `HistoryClient` (`session.Historian()`) `ReadProcessedAsync` helper, which streams one
`DataValue` per processing interval as an `IAsyncEnumerable<DataValue>`:

```csharp
// Average of a historizing variable over the last hour, in 1-minute buckets.
DateTime end = DateTime.UtcNow;
DateTime start = end.AddHours(-1);

await foreach (DataValue value in session.Historian().ReadProcessedAsync(
    nodeId,
    aggregateFunctionId: ObjectIds.AggregateFunction_Average,
    startTime: start,
    endTime: end,
    processingInterval: 60_000 /* ms */))
{
    Console.WriteLine($"{value.SourceTimestamp:O}: {value.Value} ({value.StatusCode})");
}
```

Pass an explicit `AggregateConfiguration` to override the server defaults:

```csharp
await foreach (DataValue value in session.Historian().ReadProcessedAsync(
    nodeId,
    ObjectIds.AggregateFunction_TimeAverage,
    start,
    end,
    processingInterval: 60_000,
    configuration: new AggregateConfiguration
    {
        TreatUncertainAsBad = false,
        PercentDataBad = 100,
        PercentDataGood = 100,
        UseSlopedExtrapolation = false
    }))
{
    // ...
}
```

Call `session.Historian().GetServerCapabilitiesAsync(...)` to discover supported aggregates, or browse
`Server.ServerCapabilities.AggregateFunctions`.

## Notes and limitations

- `AnnotationCount` requires a provider with annotation history; otherwise it returns
  `Bad_AggregateNotSupported`.
- The bundled in-memory historian (`InMemoryHistorianProvider`) supports all aggregates through the
  framework fallback and supports annotation history, so `AnnotationCount` works out of the box.
- Custom providers can override aggregate computation by implementing `IHistorianProcessedProvider`.

## References

- OPC 10000-13 (Aggregates) v1.05.07: https://reference.opcfoundation.org/Core/Part13/v105/docs/
- [Historical Access (Part 11)](HistoricalAccess.md)
- [Migration Guide](MigrationGuide.md)
