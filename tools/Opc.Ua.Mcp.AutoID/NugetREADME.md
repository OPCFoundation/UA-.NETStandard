# OPC UA AutoID MCP tools

Embed the OPC 30010 AutoID tools with `AddOpcUaMcpAutoId()` and
`WithOpcUaAutoIdTools(McpToolProfile.AutoId)`, after registering MCP Core. The
`autoid` profile discovers RFID, OCR, optical, optical verifier and RTLS readers
below the Device Integration `DeviceSet`, reads device snapshots, runs the
synchronous `Scan`, starts and stops the asynchronous scan, collects scan events
for a bounded window, reads tag memory and queries RTLS locations.

Tag commands that change a tag (`WriteTag`, `WriteTagID`, `KillTag`, `LockTag`,
`SetTagPassword`) and passwords are not exposed: the profile cannot damage a tag
or carry a secret. Use the typed `Opc.Ua.AutoID.Client` package for those.

Every call resolves its `sessionName` anew. Discovery uses live offset paging.

## Arguments and results

`autoid_scan` takes a discovered reader NodeId and the scan termination
conditions; at least one of `durationMs`, `cycles` or `dataAvailable` must end the
scan:

```json
{
  "readerNodeId": "ns=2;s=Gate1",
  "durationMs": 500,
  "dataAvailable": true,
  "sessionName": "dock"
}
```

`autoid_read_tag` addresses the tag by the hexadecimal `identifier` a scan
returned (the EPC); `autoid_get_location` takes the hexadecimal identifier of the
located object. An optical verifier has no usable `Scan` method (the NodeSet
declares its results with the wrong type): start a scan and collect events with
`autoid_observe_scan_events`.

See the [tool catalogue](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md#autoid-tools)
and the [AutoID guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/AutoID.md).
