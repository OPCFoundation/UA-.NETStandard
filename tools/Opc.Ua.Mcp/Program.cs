/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua.Mcp;
using Opc.Ua.Pcap.DependencyInjection;

Console.Error.WriteLine("OPC UA MCP Server");
Console.Error.WriteLine(
    "OPC UA library: {0} @ {1} -- {2}",
    Opc.Ua.Utils.GetAssemblyBuildNumber(),
    Opc.Ua.Utils.GetAssemblyTimestamp().ToString("G", System.Globalization.CultureInfo.InvariantCulture),
    Opc.Ua.Utils.GetAssemblySoftwareVersion()
);

var transportOption = new Option<string>("--transport", "-t")
{
    Description = "Transport mode: 'stdio' (default) or 'http'. 'sse' is a deprecated alias for 'http'.",
    DefaultValueFactory = _ => "stdio"
};

var portOption = new Option<int>("--port", "-p")
{
    Description = "Port for Streamable HTTP transport (default: 5100)",
    DefaultValueFactory = _ => 5100
};

var profileOption = new Option<McpToolProfile?>("--profile")
{
    Description = "Tool profile: core, services, administration, pubsub, diagnostics, or full (default)"
};

var rootCommand = new RootCommand("OPC UA MCP Server - Exposes OPC UA Part 4 services as MCP tools")
{
    transportOption,
    portOption,
    profileOption
};

rootCommand.SetAction(async (parseResult, ct) =>
{
    string transport = parseResult.GetValue(transportOption)!;
    int port = parseResult.GetValue(portOption);
    McpToolProfile? toolProfile = parseResult.GetValue(profileOption);

    if (transport.Equals("stdio", StringComparison.OrdinalIgnoreCase))
    {
        await RunStdioServerAsync(toolProfile, ct).ConfigureAwait(false);
        return 0;
    }

    if (transport.Equals("http", StringComparison.OrdinalIgnoreCase) ||
        transport.Equals("sse", StringComparison.OrdinalIgnoreCase))
    {
        await RunHttpServerAsync(port, toolProfile, ct).ConfigureAwait(false);
        return 0;
    }

    await Console.Error.WriteLineAsync(
        $"Unknown transport '{transport}'. Valid transports: stdio, http, sse.").ConfigureAwait(false);
    return 2;
});

return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);

static async Task RunStdioServerAsync(McpToolProfile? toolProfileOverride, CancellationToken ct)
{
    await Console.Error.WriteLineAsync(
        "Starting MCP server with stdio transport...").ConfigureAwait(false);

    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    McpHostBuilder.ConfigureLogging(builder.Logging, useStdioTransport: true);

    PcapOptions pcapOptions = McpHostBuilder.CreatePcapOptions(builder.Configuration);
    bool diagnosticsToolsEnabled = McpHostBuilder.AreDiagnosticsToolsEnabled(pcapOptions);
    McpServerOptions mcpServerOptions = McpHostBuilder.CreateMcpServerOptions(
        builder.Configuration,
        toolProfileOverride);
    McpHostBuilder.ConfigureServices(builder.Services, pcapOptions, mcpServerOptions);

    IMcpServerBuilder mcpServerBuilder = builder.Services
        .AddMcpServer()
        .WithStdioServerTransport();
    McpHostBuilder.ConfigureMcpTools(
        mcpServerBuilder,
        mcpServerOptions.ToolProfile,
        diagnosticsToolsEnabled);

    IHost app = builder.Build();
    McpHostBuilder.LogDiagnosticsToolsWarning(app.Services, diagnosticsToolsEnabled);
    await app.RunAsync(ct).ConfigureAwait(false);
}

static async Task RunHttpServerAsync(
    int port,
    McpToolProfile? toolProfileOverride,
    CancellationToken ct)
{
    await Console.Error.WriteLineAsync(
        $"Starting MCP server with Streamable HTTP transport on port {port} at /mcp...").ConfigureAwait(false);

    WebApplicationBuilder builder = WebApplication.CreateBuilder();
    McpHostBuilder.ConfigureLogging(builder.Logging, useStdioTransport: false);

    PcapOptions pcapOptions = McpHostBuilder.CreatePcapOptions(builder.Configuration);
    bool diagnosticsToolsEnabled = McpHostBuilder.AreDiagnosticsToolsEnabled(pcapOptions);
    McpServerOptions mcpServerOptions = McpHostBuilder.CreateMcpServerOptions(
        builder.Configuration,
        toolProfileOverride);
    McpHostBuilder.ConfigureServices(builder.Services, pcapOptions, mcpServerOptions);

    IMcpServerBuilder mcpServerBuilder = builder.Services
        .AddMcpServer()
        .WithHttpTransport();
    McpHostBuilder.ConfigureMcpTools(
        mcpServerBuilder,
        mcpServerOptions.ToolProfile,
        diagnosticsToolsEnabled);

    WebApplication app = builder.Build();
    McpHostBuilder.LogDiagnosticsToolsWarning(app.Services, diagnosticsToolsEnabled);
    app.MapMcp("/mcp");
    app.Urls.Add($"http://localhost:{port}");

    await app.RunAsync(ct).ConfigureAwait(false);
}
