# Opc.Ua.Scales.Client

Client-side helpers for the **OPC 40200 Weighing Technology (Scales)**
companion specification, version 2.00.

- `EnumerateScalesAsync` discovers scales and scale systems below the DI
  `DeviceSet` and the Machinery `Machines` folder and resolves each one's
  scale kind, including vendor subtypes; `EnumerateSystemScalesAsync` lists
  the scales of a scale system.
- `ReadCurrentWeightAsync` / `ReadRegisteredWeightAsync` read a
  `WeightItemType` with its gross, net and tare and every published property
  (overload, underload, tare mode, stability, range, weight id, unit);
  `ObserveWeightAsync` streams the weight.
- `SetZeroAsync`, `SetTareAsync`, `ClearTareAsync`, `SetPresetTareAsync` and
  `RegisterWeightAsync` drive the scale.
- Products: `ReadProductsAsync`, `ReadCurrentProductsAsync`,
  `Select/Deselect/Switch/Add/RemoveProductAsync`.
- `ReadLastItemAsync` reads typed product measurements, dimensions, zone and
  price/currency fields with independent source status and timestamps.
  `ReadTipCounterAsync` preserves the full unsigned counter range.
- `ReadRecipeStatusAsync` and `ObserveRecipeReportsAsync` expose selected
  recipes and typed report entries without converting missing values to zero.
- Catchweigher zones and recipe graphs can be managed with the typed add/remove
  methods; recipe processing methods operate on the selected recipe.
- PackML: `ReadPackMLStateAsync` and `ExecutePackMLCommandAsync`.
- Type-specific methods (piece counting, laboratory, vehicle, loss-in-weight,
  feeder, totalizers, scale systems) and
  `ObserveNotificationsAsync` for scale events and alarms with their Annex C
  notification ids.
- `ReadSnapshotAsync` reads everything in one pass.

```csharp
ScalesClient scales = session.Scales(telemetry);
await foreach (ScaleEntry scale in scales.EnumerateScalesAsync())
{
    ScaleReading weight = await scales.ReadCurrentWeightAsync(scale.NodeId);
    Console.WriteLine($"{scale.DisplayName}: {weight.Net} (tare {weight.Tare})");
}
```

## Target frameworks

net48, net8.0, net9.0, net10.0

## Additional documentation

See the [Scales developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Scales.md).
