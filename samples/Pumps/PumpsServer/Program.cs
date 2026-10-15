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
using System.Globalization;
using System.IO;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PumpsSample;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = 62547;
if (builder.Configuration["port"] is { } portOption &&
    (!int.TryParse(portOption, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535))
{
    await Console.Error.WriteLineAsync("--port must be an integer from 1 through 65535.").ConfigureAwait(false);
    return 2;
}
string bindHost = builder.Configuration["host"] ?? "localhost";
if (Uri.CheckHostName(bindHost) == UriHostNameType.Unknown)
{
    await Console.Error.WriteLineAsync("--host must be a host name or IP address.").ConfigureAwait(false);
    return 2;
}
string? pkiRoot = builder.Configuration["pki-root"];
if (pkiRoot is not null && !Path.IsPathFullyQualified(pkiRoot))
{
    await Console.Error.WriteLineAsync("--pki-root must be an absolute path.").ConfigureAwait(false);
    return 2;
}
bool autoAccept = true;
if (builder.Configuration["autoaccept"] is { } acceptOption && !bool.TryParse(acceptOption, out autoAccept))
{
    await Console.Error.WriteLineAsync("--autoaccept must be true or false.").ConfigureAwait(false);
    return 2;
}
int runSeconds = 0;
if (builder.Configuration["run-seconds"] is { } runOption &&
    (!int.TryParse(runOption, NumberStyles.None, CultureInfo.InvariantCulture, out runSeconds) ||
        runSeconds is < 1 or > 3600))
{
    await Console.Error.WriteLineAsync("--run-seconds must be an integer from 1 through 3600.").ConfigureAwait(false);
    return 2;
}
string endpointUrl = new UriBuilder("opc.tcp", bindHost, port, "/PumpsServer").Uri.AbsoluteUri;

// The fleet creates the pumps once the address space is ready; the
// simulation writes the live values through the handles it keeps.
builder.Services.AddSingleton<PumpFleet>();
builder.Services.AddHostedService<PumpSimulation>();

builder.Services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "PumpsServer";
        options.ApplicationUri = "urn:localhost:OPCFoundation:PumpsServer";
        options.ProductUri = "uri:opcfoundation.org:PumpsServer";
        // Sample convenience only; never auto-accept untrusted certificates in production.
        options.AutoAcceptUntrustedCertificates = autoAccept;
        if (pkiRoot is not null)
        {
            options.PkiRoot = pkiRoot;
        }
        options.EndpointUrls.Add(endpointUrl);
    })
    // AddPumps loads DI, IA, Machinery and Pumps into one node manager and
    // claims the Device Integration address space. A server that already owns
    // DI loads the model into its own manager with nodes.AddOpcUaPumps(context)
    // instead; see samples/DI/PumpDeviceIntegrationServer.
    .AddPumps()
    .ConfigurePumps(context => context.GetRequiredService<PumpFleet>().ConfigureAsync(context));

using IHost host = builder.Build();
using var runLifetime = new CancellationTokenSource();
if (runSeconds > 0)
{
    runLifetime.CancelAfter(TimeSpan.FromSeconds(runSeconds));
}
await host.RunAsync(runLifetime.Token).ConfigureAwait(false);
return 0;
