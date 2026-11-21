// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

string port = builder.Configuration["PumpServer:Port"] ?? "62542";
string endpointUrl = builder.Configuration["UaLens:EndpointUrl"] ??
    $"opc.tcp://localhost:{port}/PumpDeviceIntegrationServer";
string applicationUri = builder.Configuration["UaLens:ApplicationUri"] ??
    "urn:localhost:OPCFoundation:PumpDeviceIntegrationServer";
string securityMode = builder.Configuration["UaLens:SecurityMode"] ?? "None";
string securityPolicy = builder.Configuration["UaLens:SecurityPolicy"] ?? "None";

IResourceBuilder<ProjectResource> pumpServer = builder
    .AddProject<Projects.PumpDeviceIntegrationServer>("pump-server")
    .WithEnvironment("CustomTestTarget", "net10.0")
    .WithEnvironment("host", "localhost")
    .WithEnvironment("port", port)
    .WithEnvironment("include-unsecure", "true");

IResourceBuilder<ProjectResource> lens = builder
    .AddProject<Projects.Opc_Ua_Lens>("ua-lens")
    .WithEnvironment("CustomTestTarget", "net10.0")
    .WithEnvironment("PublishAotEnabled", "false")
    .WithEnvironment("UALENS_ENDPOINT_URL", endpointUrl)
    .WithEnvironment("UALENS_APPLICATION_URI", applicationUri)
    .WithEnvironment("UALENS_SECURITY_MODE", securityMode)
    .WithEnvironment("UALENS_SECURITY_POLICY", securityPolicy)
    .WaitFor(pumpServer);

string? username = builder.Configuration["UaLens:Username"];
string? password = builder.Configuration["UaLens:Password"];
if (username is not null)
{
    lens.WithEnvironment("UALENS_USERNAME", username);
}
if (password is not null)
{
    lens.WithEnvironment("UALENS_PASSWORD", password);
}

builder.Build().Run();
