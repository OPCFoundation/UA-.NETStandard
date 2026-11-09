# OPC UA ISA-95 MCP tools

Embed ISA-95 common-model and Job Control V1/V2 tools with `AddOpcUaMcpIsa95()`
and `WithOpcUaIsa95Tools(McpToolProfile.Isa95)`, after registering MCP Core.
Each explicit command accepts the exact endpoint role ID: an order receiver,
response provider, or response receiver. No missing role is fabricated.

All eleven V2 order verbs are separately exposed, with typed order, response,
resource and recursive parameter inputs. Optional masks preserve explicit zero
and empty values. ISA-95 return statuses are UInt64 bitmaps: only `1` is success.
Every other bitmap is a model refusal, separate from the UA service status, and
produces an MCP tool error. The exact numeric and decimal-string forms are returned.

## Registration and session selection

The `isa95` profile supplies 20 domain tools plus deduplicated connection tools.
Pass the same `McpToolProfileSet` to Core, Machinery and ISA-95 to compose
`machinery,isa95`. Each tool resolves optional `sessionName` for that invocation;
multiple active sessions require an explicit name.

## JSON contract

Order, response, resource and recursive parameter inputs are explicit DTOs,
not serialized structure strings. Arrays are ordinary JSON arrays with at most
500 entries. For example, `isa95_v2_store` accepts:

```json
{
  "receiverNodeId": "ns=2;s=OrderReceiver",
  "sessionName": "plant",
  "jobOrder": {
    "jobOrderId": "job-1",
    "priority": 0,
    "parameters": [
      { "id": "RunsPlanned", "value": { "dataType": "UInt32", "value": 0 } },
      { "id": "Overproduction", "value": { "dataType": "Boolean", "value": false } },
      {
        "id": "Orders",
        "value": { "dataType": "String", "isArray": true, "value": ["ERP-1", "ERP-2"] }
      }
    ]
  },
  "comment": { "texts": [{ "text": "Approved batch", "locale": "en" }] }
}
```

V2 optional masks distinguish omitted fields from explicit zero, false and empty
arrays. Parameters preserve type, recursive `children`, localized descriptions
and engineering units. Resource `properties` use the same typed shape, with
`children` mapped to subproperties. Trees are bounded to eight child levels.
Timestamps require UTC or an offset; Int64/UInt64 values may use decimal strings.
V1 accepts one unlocalized description and `unitOfMeasure`; V2 uses localized
descriptions and `engineeringUnits`. Incompatible metadata is rejected.

V2 response queries require exactly one of `query.jobOrderId` or `query.states`.
Each state has a `stateNumber` and optional qualified `browsePath` segments
through HasSubStateMachine. An empty path selects the root state machine; an
explicit empty selector array requests the unfiltered set. Receive-response
inputs must explicitly include `states`.

`returnStatus` retains the UInt64 bitmap and `returnStatusText` its exact decimal
spelling. Only `1` is successful: zero, refusal bits, mixed failure bits and
vendor high bits are errors even when the UA Call result is Good. UA service
failures retain their separate status code.

Lists are live pages of at most 500 results. Status observations are bounded to
30 seconds/500 events and release their enumerator, not the borrowed managed
subscription.

See the [tool catalogue](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md#isa-95-tools)
for tool behavior and the [ISA-95 guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/ISA95.md)
for the underlying client API.
