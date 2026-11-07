# Opc.Ua.PackML.Client

Typed discovery, status and administration reads, shared subscriptions and
generated-proxy commands for OPC 30050 PackML 1.01.
Construct `PackMLClient` with a borrowed session and telemetry context, or use
`AddPackMLClient` with the fluent client registration.

Read results preserve value presence, decode status, quality and timestamps.
A successful command means equipment acceptance, not completion.
The client never retries an uncertain equipment operation.

See the [PackML guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/PackML.md).
