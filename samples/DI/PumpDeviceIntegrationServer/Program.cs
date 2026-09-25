/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Di;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.SoftwareUpdate;
using Opc.Ua.Pumps;
using Opc.Ua.Server.Fluent;
using Pumps;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = int.TryParse(builder.Configuration["port"], out int p) ? p : 62542;
string? softwareUpdateOption = builder.Configuration["software-update-demo"];
if (softwareUpdateOption is not null && !bool.TryParse(softwareUpdateOption, out _))
{
    Console.Error.WriteLine("--software-update-demo must be true or false.");
    return 2;
}
bool softwareUpdateDemo = bool.TryParse(softwareUpdateOption, out bool enabled) && enabled;
string? runOption = builder.Configuration["run-seconds"];
int runSeconds = 0;
if (runOption is not null &&
    (!int.TryParse(runOption, NumberStyles.None, CultureInfo.InvariantCulture, out runSeconds) ||
        runSeconds is < 1 or > 3600))
{
    Console.Error.WriteLine("--run-seconds must be an integer from 1 through 3600.");
    return 2;
}
string? autoAcceptOption = builder.Configuration["autoaccept"];
bool autoAccept = true;
if (autoAcceptOption is not null && !bool.TryParse(autoAcceptOption, out autoAccept))
{
    Console.Error.WriteLine("--autoaccept must be true or false.");
    return 2;
}
string pkiRoot = builder.Configuration["pki-root"] ?? Path.Combine(AppContext.BaseDirectory, "pki");
if (!Path.IsPathRooted(pkiRoot))
{
    Console.Error.WriteLine("--pki-root must be an absolute path.");
    return 2;
}

if (!TryReadPumpCount(builder.Configuration["pumps"], out int pumpCount, out string? pumpError))
{
    Console.Error.WriteLine(pumpError);
    return 2;
}

// Bind host for the OPC UA endpoint. Defaults to 0.0.0.0 so the server is
// reachable from outside a container; override with --host / host env var
// (e.g. "localhost" for local-only development).
string host = builder.Configuration["host"] is { Length: > 0 } h ? h : "0.0.0.0";

builder.Services.Configure<PumpDeviceIntegrationOptions>(options =>
{
    options.PumpCount = pumpCount;
});
if (softwareUpdateDemo)
{
    builder.Services.AddSingleton<ISoftwarePackageStore, MemoryPackageStore>();
}

builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "PumpDeviceIntegrationServer";
        o.ApplicationUri = "urn:localhost:OPCFoundation:PumpDeviceIntegrationServer";
        o.ProductUri = "uri:opcfoundation.org:PumpDeviceIntegrationServer";
        // Sample convenience only; never auto-accept untrusted certificates in production.
        o.AutoAcceptUntrustedCertificates = autoAccept;
        o.PkiRoot = pkiRoot;
        o.RejectSHA1Certificates = true;
        o.MinCertificateKeySize = 2048;
        o.EndpointUrls.Add($"opc.tcp://{host}:{port}/PumpDeviceIntegrationServer");
    })
    .AddNodeManager<PumpNodeManagerFactory>()
    // Demonstrate the declarative DI topology-element builder after the
    // node manager has materialised and fluently wired every pump. The
    // ad-hoc Diagnostics group is added identically to each pump.
    .ConfigureDevicesFor<PumpNodeManager>(async ctx =>
    {
        var manager = (PumpNodeManager)ctx.Manager;
        foreach (NodeId pumpNodeId in manager.PumpNodeIds)
        {
            ITopologyElementBuilder<PumpState> pump =
                ctx.TopologyElement<PumpState>(pumpNodeId);

            pump.WithFunctionalGroup(
                new QualifiedName("Diagnostics", ctx.Manager.InstanceNamespaceIndex),
                fg => fg.Configure(node =>
                    node.WithProperty("LastError", Variant.From(string.Empty), p => p.Writable())
                        .WithProperty("ErrorCount", 0)
                        .WithProperty("LastSelfTest", (DateTimeUtc)DateTime.UtcNow)));
        }

        if (softwareUpdateDemo)
        {
            await SoftwareUpdateDemo.CreateAsync(
                ctx.Manager, ctx.GetRequiredService<ISoftwarePackageStore>(), ctx.CancellationToken)
                .ConfigureAwait(false);
        }
    });

using var runLifetime = new CancellationTokenSource();
if (runSeconds != 0)
{
    runLifetime.CancelAfter(TimeSpan.FromSeconds(runSeconds));
}
await builder.Build().RunAsync(runLifetime.Token).ConfigureAwait(false);
return 0;

static bool TryReadPumpCount(string? value, out int pumpCount, out string? error)
{
    const int minPumpCount = 1;
    const int maxPumpCount = 100;

    pumpCount = 2;
    error = null;

    if (string.IsNullOrWhiteSpace(value))
    {
        return true;
    }

    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
    {
        error = "Invalid --pumps value '" + value + "'. Specify an integer between " +
            minPumpCount.ToString(CultureInfo.InvariantCulture) + " and " +
            maxPumpCount.ToString(CultureInfo.InvariantCulture) + ".";
        return false;
    }

    if (parsed < minPumpCount || parsed > maxPumpCount)
    {
        error = "Invalid --pumps value '" + value + "'. Specify an integer between " +
            minPumpCount.ToString(CultureInfo.InvariantCulture) + " and " +
            maxPumpCount.ToString(CultureInfo.InvariantCulture) + ".";
        return false;
    }

    pumpCount = parsed;
    return true;
}
