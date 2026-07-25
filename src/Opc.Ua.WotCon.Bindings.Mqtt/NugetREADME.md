# OPC UA WoT Connectivity — MQTT Executor

`OPCFoundation.NetStandard.Opc.Ua.WotCon.Bindings.Mqtt` executes MQTT WoT Connectivity binding forms compiled by the MQTT planner in `Opc.Ua.WotCon.Bindings`.

It uses the repository's MQTTnet infrastructure (kept out of the core model assembly) to implement publish / subscribe / RPC patterns per the pinned MQTT binding, with bounded QoS, topic, payload sizes and timeouts. The MQTT client factory is injectable.

## Transport security

- An `mqtts://` href always enables TLS and defaults to port 8883; an `mqtt://` href stays explicit plaintext (port 1883). There is no silent plaintext downgrade.
- Username / password credentials, the TLS client certificate and the TLS trust anchors are resolved through the registered `IWotCredentialProvider`. A form that declares a security scheme fails closed (the connection is refused) when the provider resolves no credential.
- Username / password credentials are refused over a plaintext `mqtt://` connection unless `MqttWotBindingOptions.AllowCredentialsOverPlaintext` is set for an explicitly accepted plaintext deployment. `ValidateServerCertificate` controls broker-certificate validation for `mqtts://`.

Register it with `builder.AddMqttWotBinding(...)`.

The package targets `net8.0`, `net9.0`, and `net10.0`. The MQTT planner and common binding contracts are supplied by `OPCFoundation.NetStandard.Opc.Ua.WotCon.Bindings`.

See the [protocol binding overview](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/WoTProtocolBindings.md) and [binding-authoring guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/WoTBindingDevelopment.md).
