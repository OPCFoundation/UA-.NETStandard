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
using System.CommandLine;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Samples;
using SiteComposition;

Option<bool> autoAcceptOption = SampleCommandLine.CreateAutoAcceptOption("--autoaccept", "-a");
Option<string> portOption = SampleCommandLine.CreateInt32ConfigurationOption(
    "--port", "Endpoint port (default 62544).", 1, 65535);
var hostOption = new Option<string>("--host") { Description = "Endpoint host (default 0.0.0.0)." };
var pumpOption = new Option<string>("--pump-server") { Description = "Subordinate pump server endpoint URL." };
var generatorOption = new Option<string>("--generator-server")
{
    Description = "Subordinate generator server endpoint URL."
};
foreach (Option<string> endpointOption in new[] { pumpOption, generatorOption })
{
    endpointOption.Validators.Add(result =>
    {
        if (!Uri.TryCreate(result.GetValueOrDefault<string>(), UriKind.Absolute, out Uri? uri) ||
            string.IsNullOrEmpty(uri.Host))
        {
            result.AddError($"{endpointOption.Name} must be an absolute endpoint URL.");
        }
    });
}
Argument<string[]> configurationArgument = SampleCommandLine.CreateConfigurationArgument();
(string[] sampleArguments, string[] forwardedArguments) = SampleCommandLine.SplitHostArguments(args);
var command = new RootCommand("OPC UA Site Composition Server: trusted certificates and secure endpoints.")
{
    autoAcceptOption,
    portOption,
    hostOption,
    pumpOption,
    generatorOption,
    configurationArgument
};
command.Validators.Add(result =>
{
    if (SampleCommandLine.GetHostArgumentError(forwardedArguments) is string error)
    {
        result.AddError(error);
    }
});
ParseResult? parsed = null;
command.SetAction(result => parsed = result);
int exitCode = await SampleCommandLine.InvokeAsync(
    command, sampleArguments, Console.Out, Console.Error).ConfigureAwait(false);
if (parsed is null)
{
    return exitCode;
}
bool autoAccept = parsed.GetValue(autoAcceptOption);
SampleCommandLine.WriteSecurityWarnings(Console.Error, autoAccept, false, "client", string.Empty);
HostApplicationBuilder builder = Host.CreateApplicationBuilder(SampleCommandLine.GetHostArguments(
    parsed, forwardedArguments, configurationArgument, portOption, hostOption, pumpOption, generatorOption));

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = 62544;
if (builder.Configuration["port"] is string configuredPort &&
    (!int.TryParse(configuredPort, out port) || port is < 1 or > 65535))
{
    Console.Error.WriteLine("The configured port must be an integer between 1 and 65535. Use --help.");
    return 1;
}
string host = builder.Configuration["host"] is { Length: > 0 } h ? h : "0.0.0.0";

string pumpServer = builder.Configuration["pump-server"]
    ?? "opc.tcp://localhost:62542/PumpDeviceIntegrationServer";
string generatorServer = builder.Configuration["generator-server"]
    ?? "opc.tcp://localhost:62543/GeneratorServer";
if (!Uri.TryCreate(pumpServer, UriKind.Absolute, out Uri? pumpUri) || string.IsNullOrEmpty(pumpUri.Host) ||
    !Uri.TryCreate(generatorServer, UriKind.Absolute, out Uri? generatorUri) || string.IsNullOrEmpty(generatorUri.Host))
{
    Console.Error.WriteLine(
        "The configured pump-server and generator-server must be absolute endpoint URLs. Use --help.");
    return 1;
}

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
        o.AutoAcceptUntrustedCertificates = autoAccept;
        o.IncludeUnsecurePolicyNone = false;
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
