// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SiteComposition;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.EnableOpcUaRedaction();
builder.Logging.AddConsole();

int port = int.TryParse(builder.Configuration["port"], out int p) ? p : 62544;
string host = builder.Configuration["host"] is { Length: > 0 } h ? h : "0.0.0.0";

string pumpServer = builder.Configuration["pump-server"]
    ?? "opc.tcp://localhost:62542/PumpDeviceIntegrationServer";
string generatorServer = builder.Configuration["generator-server"]
    ?? "opc.tcp://localhost:62543/GeneratorServer";

builder.Services.Configure<SiteCompositionOptions>(options =>
{
    options.PumpServerEndpointUrl = pumpServer;
    options.GeneratorServerEndpointUrl = generatorServer;
});

builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "SiteCompositionServer";
        o.ApplicationUri = "urn:localhost:OPCFoundation:SiteCompositionServer";
        o.ProductUri = "uri:opcfoundation.org:SiteCompositionServer";
        // Sample convenience only; never auto-accept untrusted certificates in production.
        o.AutoAcceptUntrustedCertificates = true;
        o.PkiRoot = Path.Combine(AppContext.BaseDirectory, "pki");
        o.RejectSHA1Certificates = true;
        o.MinCertificateKeySize = 2048;
        o.EndpointUrls.Add($"opc.tcp://{host}:{port}/SiteCompositionServer");
    })
    .AddNodeManager<SiteNodeManagerFactory>();

Console.WriteLine($"Site composition server on opc.tcp://{host}:{port}/SiteCompositionServer");
Console.WriteLine($"  pump hall   <- {pumpServer}");
Console.WriteLine($"  powerhouse  <- {generatorServer}");
Console.WriteLine("Render it with: Opc.Ua.OpenUsd.Connector --server <this> --federate --view");

await builder.Build().RunAsync().ConfigureAwait(false);
return 0;
