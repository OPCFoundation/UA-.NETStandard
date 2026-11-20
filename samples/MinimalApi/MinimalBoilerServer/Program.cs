// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
const string applicationName = "MinimalBoilerServer";

builder.Logging.ClearProviders();
builder.Logging.EnableOpcUaRedaction();
builder.Logging.AddConsole();

int port = int.TryParse(builder.Configuration["port"], out int p) ? p : 62541;

builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = applicationName;
        o.ApplicationUri = "urn:localhost:OPCFoundation:MinimalBoilerServer";
        o.ProductUri = "uri:opcfoundation.org:MinimalBoilerServer";
        // Sample convenience only; never auto-accept untrusted certificates in production.
        o.AutoAcceptUntrustedCertificates = true;
        o.PkiRoot = Path.Combine(
            Path.GetTempPath(),
            "OPC Foundation",
            applicationName,
            "pki");
        o.RejectSHA1Certificates = true;
        o.MinCertificateKeySize = 2048;
        o.EndpointUrls.Add($"opc.tcp://localhost:{port}/MinimalBoilerServer");
    })
    .AddNodeManager<Boiler.BoilerNodeManagerFactory>();

await builder.Build().RunAsync().ConfigureAwait(false);
