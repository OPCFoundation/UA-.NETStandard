# Aggregates (OPC UA Part 13)

## Overview

The .NET Standard stack implements **OPC UA Part 13 (OPC 10000-13) v1.05.06 Aggregates** on the server
side, computed over historical data retrieved through [Historical Access](HistoricalAccess.md) (Part 11).
All **37 standard aggregate functions** are supported and advertised through the address space.

Aggregates are requested with the `HistoryRead` service using `ReadProcessedDetails` (a `startTime`,
`endTime`, `ProcessingInterval`, one or more `AggregateType` NodeIds, and an optional
`AggregateConfiguration`). The server divides the time range into intervals of `ProcessingInterval`
milliseconds and produces one aggregate value per interval.

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

The exact per-aggregate semantics (type, bounding behaviour, timestamp, status-code rules, special cases)
follow Part 13 §5.4.3.4–§5.4.3.40.

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
  `AggregateConfiguration`, and the `MinimumProcessingInterval`. The standard functions are registered via
  `Aggregators` and advertised into `Server.ServerCapabilities.AggregateFunctions` and
  `HistoryServerCapabilities.AggregateFunctions`.
- **`IAggregateCalculator`** implementations (`AggregateCalculator` and the specialized
  `Average`/`MinMax`/`Count`/`StartEnd`/`Status`/`StdDev` calculators) compute the aggregates from a stream
  of raw `DataValue`s.
- A historian provider may compute aggregates itself by implementing `IHistorianProcessedProvider`
  (native push-down). When it does not, the framework streams raw values through the calculator.

### AnnotationCount

`AnnotationCount` (Part 13 §5.4.3.20) counts **Annotations** in each interval, not raw data values.
It is therefore computed from the node's annotation history via `IHistorianAnnotationProvider`, not from
the raw-value calculator. If the resolved provider does not expose annotation history, an `AnnotationCount`
request returns `Bad_AggregateNotSupported`.

## Server configuration

`AggregateConfiguration` controls how non-Good data affects the result:

| Property | Default | Meaning |
|---|---|---|
| `TreatUncertainAsBad` | **`true`** | Whether Uncertain samples are treated as Bad when computing the aggregate `StatusCode` (Part 13 §4.2.1.2). |
| `PercentDataBad` | `100` | Minimum % of Bad data in an interval for the interval `StatusCode` to be Bad. |
| `PercentDataGood` | `100` | Minimum % of Good data in an interval for the interval `StatusCode` to be Good. |
| `UseSlopedExtrapolation` | `false` | Stepped (hold-last) vs sloped extrapolation past the last value. Ignored for Simple Bounds. |

The server's default configuration is returned by `AggregateManager.GetDefaultConfiguration(...)` and is
used whenever a request sets `AggregateConfiguration.UseServerCapabilitiesDefaults = true`. Per Part 13
§4.2.1.2 the default `TreatUncertainAsBad` value is `true`.

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

Discover the aggregates a server supports with `session.Historian().GetServerCapabilitiesAsync(...)` or by
browsing `Server.ServerCapabilities.AggregateFunctions`.

## Notes and limitations

- `AnnotationCount` requires a provider with annotation history; otherwise it returns
  `Bad_AggregateNotSupported`.
- The bundled in-memory historian (`InMemoryHistorianProvider`) supports all aggregates through the
  framework fallback and supports annotation history, so `AnnotationCount` works out of the box.
- Custom providers can override aggregate computation by implementing `IHistorianProcessedProvider`.

## References

- OPC 10000-13 (Aggregates) v1.05.06: https://reference.opcfoundation.org/Core/Part13/v105/docs/
- [Historical Access (Part 11)](HistoricalAccess.md)
- [Migration Guide](MigrationGuide.md)
