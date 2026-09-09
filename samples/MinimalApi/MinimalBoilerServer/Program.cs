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

Option<bool> autoAcceptOption = SampleCommandLine.CreateAutoAcceptOption("--autoaccept", "-a");
Option<string> portOption = SampleCommandLine.CreateInt32ConfigurationOption(
    "--port", "Endpoint port (default 62541).", 1, 65535);
Argument<string[]> configurationArgument = SampleCommandLine.CreateConfigurationArgument();
(string[] sampleArguments, string[] forwardedArguments) = SampleCommandLine.SplitHostArguments(args);
var command = new RootCommand("OPC UA Minimal Boiler Server: trusted certificates and secure endpoints by default.")
{
    autoAcceptOption,
    portOption,
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
HostApplicationBuilder builder = Host.CreateApplicationBuilder(
    SampleCommandLine.GetHostArguments(parsed, forwardedArguments, configurationArgument, portOption));
const string applicationName = "MinimalBoilerServer";

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = 62541;
if (builder.Configuration["port"] is string configuredPort &&
    (!int.TryParse(configuredPort, out port) || port is < 1 or > 65535))
{
    Console.Error.WriteLine("The configured port must be an integer between 1 and 65535. Use --help.");
    return 1;
}

builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = applicationName;
        o.ApplicationUri = "urn:localhost:OPCFoundation:MinimalBoilerServer";
        o.ProductUri = "uri:opcfoundation.org:MinimalBoilerServer";
        // Sample convenience only; never auto-accept untrusted certificates in production.
        o.AutoAcceptUntrustedCertificates = autoAccept;
        o.IncludeUnsecurePolicyNone = false;
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
return 0;
