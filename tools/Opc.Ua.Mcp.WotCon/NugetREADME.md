# OPC UA WoT Connectivity MCP tools

Embeddable tools for legacy WoT assets, WoT registry materialization, and
bounded document inspection, validation, binding planning and NodeSet conversion.
The `wot` and `wot-documents` profiles are independent; the latter needs no
OPC UA connection and never activates a WoT protocol executor.

```csharp
McpToolProfileSet profiles = McpToolProfileSet.Parse("wot,wot-documents");
services.AddOpcUaMcpCore(options).AddOpcUaMcpWotCon(new WotMcpOptions());
services.AddMcpServer().WithOpcUaMcpFilters()
    .WithOpcUaCoreTools(profiles).WithOpcUaWotConTools(profiles);
```

Compose with `Opc.Ua.Mcp.XRegistry` for generic groups, resources, versions,
labels and content transfers. Mutations are explicit, require encryption, and
preserve concurrency checks and partial outcomes. File transfers use the
existing host-owned transfer root and never overwrite local output.

Document processing uses supplied text/files first. Remote dependencies need
both invocation opt-in and the host's exact HTTPS origin allowlist; the default
is no egress. Redirects, ambient credentials, cookies and proxy bypasses are not
permitted. Requests and decompressed content share bounded count/depth/byte and
deadline policies.

Linked document sets are retained in one ZIP artifact with an href/digest
manifest, not flattened into a single incomplete document. Archive members are
never extracted to arbitrary host paths.

See the [MCP server guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md#wot-and-registry-tools)
for profiles, configuration, embedding and workflow details. Draft model
maturity is independent of this package's release channel.
