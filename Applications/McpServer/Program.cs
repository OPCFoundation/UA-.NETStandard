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
using System.Threading;
using System.Threading.Tasks;
using System.CommandLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

    ConfigureServices(builder.Services);

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly()
        .WithResources<SessionResources>();

    IHost app = builder.Build();
    await app.RunAsync(ct).ConfigureAwait(false);
}

static async Task RunSseServerAsync(int port, CancellationToken ct)
{
    await Console.Error.WriteLineAsync(
        $"Starting MCP server with HTTP/SSE transport on port {port}...").ConfigureAwait(false);

    WebApplicationBuilder builder = WebApplication.CreateBuilder();
    ConfigureLogging(builder.Logging);

    ConfigureServices(builder.Services);

    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly()
        .WithResources<SessionResources>();

    WebApplication app = builder.Build();
    app.MapMcp();
    app.Urls.Add($"http://localhost:{port}");

    await app.RunAsync(ct).ConfigureAwait(false);
}

static void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton<OpcUaSessionManager>();
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
