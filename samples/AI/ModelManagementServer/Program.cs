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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.AI.Inference;
using Opc.Ua.AI.Server;
using Opc.Ua.AI.Server.Hosting;
using Opc.Ua.Samples;
using Opc.Ua.Server.Fluent;

Option<bool> autoAcceptOption = SampleCommandLine.CreateAutoAcceptOption("--autoaccept", "-a");
Option<string> portOption = SampleCommandLine.CreateInt32ConfigurationOption(
    "--port", "Endpoint port (default 62640).", 1, 65535);
var hostOption = new Option<string>("--host") { Description = "Endpoint host (default 0.0.0.0)." };
Argument<string[]> configurationArgument = SampleCommandLine.CreateConfigurationArgument();
(string[] sampleArguments, string[] forwardedArguments) = SampleCommandLine.SplitHostArguments(args);
var command = new RootCommand("OPC UA AI Model Management Server: trusted certificates and secure endpoints.")
{
    autoAcceptOption,
    portOption,
    hostOption,
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
    parsed, forwardedArguments, configurationArgument, portOption, hostOption));

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = 62640;
if (builder.Configuration["port"] is string configuredPort &&
    (!int.TryParse(configuredPort, out port) || port is < 1 or > 65535))
{
    Console.Error.WriteLine("The configured port must be an integer between 1 and 65535. Use --help.");
    return 1;
}

// 0.0.0.0 so the Server is reachable from outside a container. Override with
// --host for local-only development.
string host = builder.Configuration["host"] is { Length: > 0 } h ? h : "0.0.0.0";

builder.Services.AddRestChatCompletionsAIChatClientFactory();

// InferenceBackend:Kind defaults to ChatClient, the Microsoft.Extensions.AI
// path. Set it to RestChatCompletions only for endpoints where the host cannot
// supply an IChatClient and the OpenAI-compatible REST contract is the wire
// contract itself.
builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "ModelManagementServer";
        o.ApplicationUri = "urn:localhost:OPCFoundation:ModelManagementServer";
        o.ProductUri = "uri:opcfoundation.org:ModelManagementServer";
        // Sample convenience only; never auto-accept untrusted certificates in
        // production.
        o.AutoAcceptUntrustedCertificates = autoAccept;
        o.IncludeUnsecurePolicyNone = false;
        o.PkiRoot = Path.Combine(AppContext.BaseDirectory, "pki");
        o.RejectSHA1Certificates = true;
        o.MinCertificateKeySize = 2048;
        o.EndpointUrls.Add($"opc.tcp://{host}:{port}/ModelManagementServer");
    })
    .AddAI(
        ai => builder.Configuration.GetSection(AIOptions.SectionName).Bind(ai),
        backend => builder.Configuration.GetSection(InferenceBackendOptions.SectionName).Bind(backend),
        fallback => builder.Configuration
            .GetSection(InferenceBackendOptions.FallbackSectionName)
            .Bind(fallback));

await builder.Build().RunAsync().ConfigureAwait(false);
return 0;
