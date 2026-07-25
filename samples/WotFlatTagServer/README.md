# WotFlatTagServer

This sample runs one deterministic flat OPC UA source for the WoT aggregation scenario. Start it twice with the Source A and Source B namespace/endpoint options from the [WoT aggregation sample guide](../../docs/WoTAggregationSample.md).

The server intentionally exposes flat variables rather than a Pumps companion-model hierarchy; the generic aggregation server creates that hierarchy from the checked-in WoT documents.
