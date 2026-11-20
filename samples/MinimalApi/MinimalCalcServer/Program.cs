// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
const string applicationName = "MinimalCalcServer";

builder.Logging.ClearProviders();
builder.Logging.EnableOpcUaRedaction();
builder.Logging.AddConsole();

int port = int.TryParse(builder.Configuration["port"], out int p) ? p : 62542;

builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = applicationName;
        o.ApplicationUri = "urn:localhost:OPCFoundation:MinimalCalcServer";
        o.ProductUri = "uri:opcfoundation.org:MinimalCalcServer";
        o.SubjectName = "CN=MinimalCalcServer, O=OPC Foundation, DC=localhost";
        // Sample convenience only; never auto-accept untrusted certificates in production.
        o.AutoAcceptUntrustedCertificates = true;
        o.PkiRoot = Path.Combine(
            Path.GetTempPath(),
            "OPC Foundation",
            applicationName,
            "pki");
        o.RejectSHA1Certificates = true;
        o.MinCertificateKeySize = 2048;
        o.IncludeSignAndEncryptPolicies = true;
        o.IncludeUnsecurePolicyNone = false;
        o.IncludeEccPolicies = false;
        o.UserTokenPolicies.Add(new OpcUaUserTokenPolicy
        {
            TokenType = UserTokenType.Anonymous,
        });
        o.EndpointUrls.Add($"opc.tcp://localhost:{port}/MinimalCalcServer");
    })
    .AddDefaultIdentityAuthenticators(o =>
    {
        o.EnableAnonymous = true;
        o.EnableUserNamePassword = false;
        o.EnableX509 = false;
        o.EnableJwt = false;
    })
    .AddNodeManager<Calc.CalcNodeManagerFactory>();

await builder.Build().RunAsync().ConfigureAwait(false);
