# OPC UA WoT Connectivity — HTTP Executor

`OPCFoundation.NetStandard.Opc.Ua.WotCon.Binding.Http` executes HTTP / HTTPS
WoT Connectivity binding forms compiled by the HTTP planner in
`Opc.Ua.WotCon.Binding`.

It provides an `HttpClient`-based executor for read / write / action / observe /
event operations with bounded timeouts and payload sizes, cooperative
cancellation, HTTP-to-`StatusCode` mapping, an injectable client factory and
credential-provider-driven authentication headers.

Register it with `builder.AddHttpWotBinding()`.
