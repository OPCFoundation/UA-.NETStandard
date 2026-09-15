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
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Samples;
using Opc.Ua.Server.Hosting;

const string applicationName = "MinimalCalcServer";
Option<bool> autoAcceptOption = SampleCommandLine.CreateAutoAcceptOption("--autoaccept", "-a");
var portOption = new Option<int?>("--port")
{
    Description = "OPC UA TCP port (1-65535). Overrides host configuration; default 62542."
};
portOption.Validators.Add(result =>
{
    if (result.GetValueOrDefault<int?>() is < 1 or > 65535)
    {
        result.AddError("--port must be an integer between 1 and 65535.");
    }
});
Argument<string[]> configurationArgument = SampleCommandLine.CreateConfigurationArgument();
(string[] sampleArguments, string[] hostArguments) = SampleCommandLine.SplitHostArguments(args);
var command = new RootCommand(
    "OPC UA Minimal Calc Server: trusted certificates and signed/encrypted endpoints only.")
{
    autoAcceptOption,
    portOption,
    configurationArgument
};
command.Validators.Add(result =>
{
    string? hostArgumentError = SampleCommandLine.GetHostArgumentError(hostArguments);
    if (hostArgumentError != null)
    {
        result.AddError(hostArgumentError);
        return;
    }
    // Validate forwarded configuration without constructing a host or touching PKI.
    using var configuration = new ConfigurationManager();
    try
    {
        configuration.AddCommandLine(hostArguments);
        configuration.AddCommandLine(result.GetValue(configurationArgument) ?? []);
        string? configuredPort = configuration["port"];
        if (configuredPort != null &&
            (!int.TryParse(configuredPort, out int port) || port is < 1 or > 65535))
        {
            result.AddError("The configured port must be an integer between 1 and 65535.");
        }
    }
    catch (FormatException ex)
    {
        result.AddError($"Invalid host configuration: {ex.Message}");
    }
});
command.SetAction(async (result, cancellationToken) =>
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder(hostArguments);
    builder.Configuration.AddCommandLine(result.GetValue(configurationArgument) ?? []);
    if (result.GetValue(portOption) is int requestedPort)
    {
        builder.Configuration["port"] = requestedPort.ToString(CultureInfo.InvariantCulture);
    }
    bool autoAccept = result.GetValue(autoAcceptOption);

    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();

    int port = int.TryParse(builder.Configuration["port"], out int p) ? p : 62542;
    SampleCommandLine.WriteSecurityWarnings(Console.Error, autoAccept, false, "client", string.Empty);

    builder.Services
        .AddOpcUa()
        .AddServer(o =>
        {
            o.ApplicationName = applicationName;
            o.ApplicationUri = "urn:localhost:OPCFoundation:MinimalCalcServer";
            o.ProductUri = "uri:opcfoundation.org:MinimalCalcServer";
            o.SubjectName = "CN=MinimalCalcServer, O=OPC Foundation, DC=localhost";
            // Sample convenience only; never auto-accept untrusted certificates in production.
            o.AutoAcceptUntrustedCertificates = autoAccept;
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

    await builder.Build().RunAsync(cancellationToken).ConfigureAwait(false);
    return 0;
});

return await SampleCommandLine.InvokeAsync(command, sampleArguments, Console.Out, Console.Error).ConfigureAwait(false);
