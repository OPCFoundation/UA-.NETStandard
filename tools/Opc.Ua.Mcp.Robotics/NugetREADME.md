# OPCFoundation.NetStandard.Opc.Ua.Mcp.Robotics

OPC UA Model Context Protocol (MCP) tools for Robot Intent controllers,
packaged so they can be embedded in any MCP server host rather than only run as
the shipped `opcua-mcp` tool.

Use this package when an application wants an LLM agent to discover Robot
Intent capabilities, read live controller state, submit explicit intents, and
manage missions through the OPC UA Robotics Client API.

## Tools

Discovery, live state and outstanding-work monitoring, direct-control tools for
authority/cancel/pause/resume/retry and one submit tool per intent kind, plus
mission submit/update/cancel.

Refusals are returned with the server's exact `IntentFailureEnum` and message;
the MCP layer does not retry, request authority implicitly, or reinterpret
server decisions.

## Usage

```csharp
builder.Services.AddOpcUaMcpCore();
builder.Services.AddOpcUaMcpRobotics();

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(McpToolProfile.Robotics)
    .WithOpcUaRoboticsTools(McpToolProfile.Robotics)
    .WithTools<MyApplicationTools>();
```

A profile that does not select Robotics contributes no tools rather than
failing, so the same profile value can be passed to every OPC UA tool package a
host references.

## Related packages

| Package | Adds |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Core` | Part 4 service tools, session management, filters (required) |
| `OPCFoundation.NetStandard.Opc.Ua.Robotics.Client` | Robot Intent discovery, state, authority, operation and mission client API |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp` | the ready-to-run `opcua-mcp` server composing all OPC UA MCP tool packages |

## License

OPC Foundation MIT License 1.00 — <http://opcfoundation.org/License/MIT/1.00/>
