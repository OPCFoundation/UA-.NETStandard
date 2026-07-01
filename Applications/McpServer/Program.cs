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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Pcap.DependencyInjection;
using Opc.Ua.PubSub.Pcap;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;

Console.Error.WriteLine("OPC UA MCP Server");
Console.Error.WriteLine(
    "OPC UA library: {0} @ {1} -- {2}",
    Opc.Ua.Utils.GetAssemblyBuildNumber(),
    Opc.Ua.Utils.GetAssemblyTimestamp().ToString("G", System.Globalization.CultureInfo.InvariantCulture),
    Opc.Ua.Utils.GetAssemblySoftwareVersion()
);

var transportOption = new Option<string>("--transport", "-t")
{
    Description = "Transport mode: 'stdio' (default) or 'sse' for HTTP/SSE",
    DefaultValueFactory = _ => "stdio"
};

var portOption = new Option<int>("--port", "-p")
{
    Description = "HTTP port for SSE transport (default: 5100)",
    DefaultValueFactory = _ => 5100
};

var rootCommand = new RootCommand("OPC UA MCP Server - Exposes OPC UA Part 4 services as MCP tools")
{
    transportOption,
    portOption
};

rootCommand.SetAction(async (parseResult, ct) =>
{
    string transport = parseResult.GetValue(transportOption)!;
    int port = parseResult.GetValue(portOption);

    if (transport.Equals("sse", StringComparison.OrdinalIgnoreCase))
    {
        await RunSseServerAsync(port, ct).ConfigureAwait(false);
    }
    else
    {
        await RunStdioServerAsync(ct).ConfigureAwait(false);
    }
});

return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);

static async Task RunStdioServerAsync(CancellationToken ct)
{
    await Console.Error.WriteLineAsync(
        "Starting MCP server with stdio transport...").ConfigureAwait(false);

    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    ConfigureLogging(builder.Logging);

    PcapOptions pcapOptions = CreatePcapOptions(builder.Configuration);
    bool diagnosticsToolsEnabled = AreDiagnosticsToolsEnabled(pcapOptions);
    ConfigureServices(builder.Services, pcapOptions);

    IMcpServerBuilder mcpServerBuilder = builder.Services
        .AddMcpServer()
        .WithStdioServerTransport();
    ConfigureMcpTools(mcpServerBuilder, diagnosticsToolsEnabled);

    IHost app = builder.Build();
    LogDiagnosticsToolsWarning(app.Services, diagnosticsToolsEnabled);
    await app.RunAsync(ct).ConfigureAwait(false);
}

static async Task RunSseServerAsync(int port, CancellationToken ct)
{
    await Console.Error.WriteLineAsync(
        $"Starting MCP server with HTTP/SSE transport on port {port}...").ConfigureAwait(false);

    WebApplicationBuilder builder = WebApplication.CreateBuilder();
    ConfigureLogging(builder.Logging);

    PcapOptions pcapOptions = CreatePcapOptions(builder.Configuration);
    bool diagnosticsToolsEnabled = AreDiagnosticsToolsEnabled(pcapOptions);
    ConfigureServices(builder.Services, pcapOptions);

    IMcpServerBuilder mcpServerBuilder = builder.Services
        .AddMcpServer()
        .WithHttpTransport();
    ConfigureMcpTools(mcpServerBuilder, diagnosticsToolsEnabled);

    WebApplication app = builder.Build();
    LogDiagnosticsToolsWarning(app.Services, diagnosticsToolsEnabled);
    app.MapMcp();
    app.Urls.Add($"http://localhost:{port}");

    await app.RunAsync(ct).ConfigureAwait(false);
}

static void ConfigureServices(IServiceCollection services, PcapOptions pcapOptions)
{
    services.AddOpcUa().AddClient(options => { });
    services.AddSingleton<OpcUaSessionManager>();
    services.AddSingleton<PubSubRuntimeManager>();
    services.AddSingleton(_ => CreateMcpServerOptions());
    services.AddPcap(options =>
    {
        options.BaseFolder = pcapOptions.BaseFolder;
        options.MaxActiveSessions = pcapOptions.MaxActiveSessions;
        options.EnableDiagnosticsTools = pcapOptions.EnableDiagnosticsTools;
    });
    services.AddPcapFormatters();
    services.AddPcapReplay();
    services.AddPubSubPcap();
}

static McpServerOptions CreateMcpServerOptions()
{
    return new McpServerOptions
    {
        NodeSetExportRoot = Environment.GetEnvironmentVariable("OPCUA_MCP_NODESET_EXPORT_ROOT"),
        PcapBaseFolder = Environment.GetEnvironmentVariable("OPCUA_MCP_PCAP_BASE_FOLDER")
    };
}

static PcapOptions CreatePcapOptions(IConfiguration configuration)
{
    var options = new PcapOptions();

    string? enableDiagnosticsTools = configuration["Pcap:EnableDiagnosticsTools"];
    if (bool.TryParse(enableDiagnosticsTools, out bool parsedEnableDiagnosticsTools))
    {
        options.EnableDiagnosticsTools = parsedEnableDiagnosticsTools;
    }

    return options;
}

static bool AreDiagnosticsToolsEnabled(PcapOptions pcapOptions)
{
    return pcapOptions.EnableDiagnosticsTools ||
        string.Equals(
            Environment.GetEnvironmentVariable("OPCUA_PCAP_ENABLE_DIAGNOSTICS"),
            "1",
            StringComparison.Ordinal) ||
        string.Equals(
            Environment.GetEnvironmentVariable("OPCUA_PCAP_ENABLE_DIAGNOSTICS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
}

static void ConfigureMcpTools(IMcpServerBuilder mcpServerBuilder, bool diagnosticsToolsEnabled)
{
    mcpServerBuilder
        .WithTools<AttributeServiceTools>()
        .WithTools<ConfigurationTools>()
        .WithTools<ConnectionTools>()
        .WithTools<ConvenienceTools>()
        .WithTools<DiscoveryServiceTools>()
        .WithTools<MethodServiceTools>()
        .WithTools<MonitoredItemServiceTools>()
        .WithTools<NodeManagementServiceTools>()
        .WithTools<NodeSetExportTools>()
        .WithTools<PacketCaptureTools>()
        .WithTools<PkiTools>()
        .WithTools<PubSubCaptureTools>()
        .WithTools<PubSubActionTools>()
        .WithTools<PubSubDiscoveryTools>()
        .WithTools<PubSubRuntimeTools>()
        .WithTools<SubscriptionServiceTools>()
        .WithTools<ViewServiceTools>();

    if (diagnosticsToolsEnabled)
    {
        mcpServerBuilder
            .WithTools<PacketDecodeTools>()
            .WithTools<PacketReplayTools>()
            .WithTools<PubSubDecodeTools>();
    }

    mcpServerBuilder.WithResources<SessionResources>();
}

static void LogDiagnosticsToolsWarning(IServiceProvider services, bool diagnosticsToolsEnabled)
{
    if (!diagnosticsToolsEnabled)
    {
        return;
    }

    ILoggerFactory loggerFactory = services.GetRequiredService<ILoggerFactory>();
    ILogger logger = loggerFactory.CreateLogger("Opc.Ua.Mcp.Program");
    logger.LogWarning(
        "OPC UA Pcap diagnostics MCP tools (dump_keys, decode_pcap_with_keys, replay_pcap) are ENABLED. " +
        "These tools disclose symmetric channel keys and can be used to replay captured traffic. " +
        "Ensure the MCP transport is authenticated and audited.");
}

static void ConfigureLogging(ILoggingBuilder logging)
{
    logging.ClearProviders();
    logging.SetMinimumLevel(LogLevel.Information);
    logging.AddSimpleConsole(options =>
    {
        options.UseUtcTimestamp = true;
        options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    });
}
