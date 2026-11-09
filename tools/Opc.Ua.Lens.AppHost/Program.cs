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
