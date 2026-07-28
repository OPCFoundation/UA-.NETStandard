# WotAggregationServer

This sample hosts the generic WoT registry, runtime document materialization, and protocol-binding projection runtime. It contains no Pump-specific generated code; DI, Machinery, Pumps, and the Pump instance are loaded at runtime from WoT files.

The executable server targets `net8.0`, `net9.0`, and `net10.0`, where its OPC UA binding executor is available. Legacy `CustomTestTarget` solution builds use a no-op shell and are not runnable sample configurations.

See the [WoT aggregation sample guide](../WotAggregation/README.md) for topology, commands, monitoring, shadow replacement, troubleshooting, and NativeAOT publishing.
