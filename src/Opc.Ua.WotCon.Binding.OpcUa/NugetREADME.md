# OPC UA WoT Connectivity — OPC UA Executor

`OPCFoundation.NetStandard.Opc.Ua.WotCon.Binding.OpcUa` executes OPC UA WoT
Connectivity binding forms (OPC 10101) compiled by the OPC UA planner in
`Opc.Ua.WotCon.Binding`, enabling OPC UA-to-OPC UA translation.

It uses the `Opc.Ua.Client` session abstractions to implement read / write /
observe / invoke / subscribe-event against portable `uav:id` NodeIds (including
the portable `nsu=` namespace-URI form), preserving Method argument order and
`StatusCode` / `DataValue` metadata. Observe and event subscription are native
`Subscription` / `MonitoredItem` pairs, not polling. Sessions are supplied
through an injectable session factory.

Register it with `builder.AddOpcUaWotBinding(...)`.
