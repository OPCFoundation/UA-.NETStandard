# Opc.Ua.OpenUsd

Draft **OPC UA – OpenUSD Bindings** companion information model for the OPC UA .NET Standard stack.

It carries the shared `Opc.Ua.OpenUsdBinding` NodeSet and the source-generated model
(type NodeIds, `NodeState` classes and enumerations) in the `Opc.Ua.OpenUsd` namespace:

- `OpenUsdRootType` / `OpenUsdStageType` / `OpenUsdRepresentationType`
- the abstract `OpenUsdLiveBindingType` and its intent subtypes
  (`OpenUsdValueChangeBindingType`, `OpenUsdAlarmBindingType`, `OpenUsdHistoryBindingType`,
  `OpenUsdCommandBindingType`)
- `OpenUsdComponentBindingType` (composition/aggregation) and `OpenUsdAssetType : FileType`
  (Part 5 asset content delivery)

Server-side reusable functionality lives in **Opc.Ua.OpenUsd.Server**; the generic,
dependency-injectable connector lives in **Opc.Ua.OpenUsd.Client**.

This is a draft companion model published for review; NodeIds and shapes may change.
