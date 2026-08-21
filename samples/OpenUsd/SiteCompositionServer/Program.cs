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
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SiteComposition;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
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
