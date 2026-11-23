// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Generators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Generators;
using Opc.Ua.Server.Fluent;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.EnableOpcUaRedaction();
builder.Logging.AddConsole();

int port = int.TryParse(builder.Configuration["port"], out int p) ? p : 62543;

if (!TryReadGeneratorCount(builder.Configuration["generators"], out int generatorCount, out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

// Bind host for the OPC UA endpoint. Defaults to 0.0.0.0 so the server is
// reachable from outside a container; override with --host (for example
// "localhost" for local-only development).
string host = builder.Configuration["host"] is { Length: > 0 } h ? h : "0.0.0.0";

// A set running to its datasheet cannot protect-trip, so by default the last set
// develops faults on a slow rotation to exercise the alarm path. Pass
// --faults false for a purely healthy plant.
bool injectFaults = !bool.TryParse(builder.Configuration["faults"], out bool f) || f;

builder.Services.Configure<GeneratorDeviceIntegrationOptions>(options =>
{
    options.GeneratorCount = generatorCount;
    options.InjectFaults = injectFaults;
});

builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "GeneratorServer";
        o.ApplicationUri = "urn:localhost:OPCFoundation:GeneratorServer";
        o.ProductUri = "uri:opcfoundation.org:GeneratorServer";
        // Sample convenience only; never auto-accept untrusted certificates in production.
        o.AutoAcceptUntrustedCertificates = true;
        // Sample convenience only: anonymous sessions may write and call. Remove in production.
        o.AnonymousAccess = Opc.Ua.Server.Hosting.AnonymousAccess.Unrestricted;
        o.PkiRoot = Path.Combine(AppContext.BaseDirectory, "pki");
        o.RejectSHA1Certificates = true;
        o.MinCertificateKeySize = 2048;
        o.EndpointUrls.Add($"opc.tcp://{host}:{port}/GeneratorServer");
    })
    .AddNodeManager<GeneratorNodeManagerFactory>()
    // Demonstrate the declarative DI topology-element builder once the node
    // manager has materialised and wired every set. Registering this also puts
    // the post-setup runner in the container, which is what lets the node
    // manager factory receive the configured options.
    .ConfigureDevicesFor<GeneratorNodeManager>(ctx =>
    {
        var manager = (GeneratorNodeManager)ctx.Manager;
        foreach (NodeId setNodeId in manager.GeneratorNodeIds)
        {
            ITopologyElementBuilder<GeneratorSetState> set =
                ctx.TopologyElement<GeneratorSetState>(setNodeId);

            set.WithFunctionalGroup(
                new QualifiedName("Diagnostics", ctx.Manager.InstanceNamespaceIndex),
                fg => fg.Configure(node =>
                    node.WithProperty("LastProtectionTrip", Variant.From(string.Empty), p => p.Writable())
                        .WithProperty("TripCount", 0)
                        .WithProperty("LastServiceDate", (DateTimeUtc)DateTime.UtcNow)));
        }

        return new ValueTask();
    });

await builder.Build().RunAsync().ConfigureAwait(false);
return 0;

static bool TryReadGeneratorCount(string? value, out int generatorCount, out string? error)
{
    const int minCount = 1;
    const int maxCount = 100;

    generatorCount = 2;
    error = null;

    if (string.IsNullOrWhiteSpace(value))
    {
        return true;
    }

    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ||
        parsed < minCount || parsed > maxCount)
    {
        error = FormattableString.Invariant($"Invalid --generators value '{value}'. Specify an integer between {minCount} and {maxCount}.");
        return false;
    }

    generatorCount = parsed;
    return true;
}
