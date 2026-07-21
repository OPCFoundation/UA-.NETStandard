# OPC UA WoT Connectivity — MQTT Executor

`OPCFoundation.NetStandard.Opc.Ua.WotCon.Binding.Mqtt` executes MQTT WoT
Connectivity binding forms compiled by the MQTT planner in
`Opc.Ua.WotCon.Binding`.

It uses the repository's MQTTnet infrastructure (kept out of the core model
assembly) to implement publish / subscribe / RPC patterns per the pinned MQTT
binding, with bounded QoS, topic, payload sizes and timeouts. The MQTT client
factory is injectable.

Register it with `builder.AddMqttWotBinding(...)`.
