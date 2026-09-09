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
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Generators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Generators;
using Opc.Ua.Samples;
using Opc.Ua.Server.Fluent;

Option<bool> autoAcceptOption = SampleCommandLine.CreateAutoAcceptOption("--autoaccept", "-a");
Option<string> portOption = SampleCommandLine.CreateInt32ConfigurationOption(
    "--port", "Endpoint port (default 62543).", 1, 65535);
Option<string> generatorsOption = SampleCommandLine.CreateInt32ConfigurationOption(
    "--generators", "Number of generator sets (default 2).", 1, 100);
var hostOption = new Option<string>("--host") { Description = "Endpoint host (default 0.0.0.0)." };
var faultsOption = new Option<string>("--faults") { Description = "Inject faults: true or false (default true)." };
faultsOption.Validators.Add(result =>
{
    if (!bool.TryParse(result.GetValueOrDefault<string>(), out _))
    {
        result.AddError("--faults expects true or false.");
    }
});
Argument<string[]> configurationArgument = SampleCommandLine.CreateConfigurationArgument();
(string[] sampleArguments, string[] forwardedArguments) = SampleCommandLine.SplitHostArguments(args);
var command = new RootCommand("OPC UA Generator Server: trusted certificates and secure endpoints.")
{
    autoAcceptOption,
    portOption,
    hostOption,
    generatorsOption,
    faultsOption,
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
    parsed, forwardedArguments, configurationArgument, portOption, hostOption, generatorsOption, faultsOption));

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = 62543;
if (builder.Configuration["port"] is string configuredPort &&
    (!int.TryParse(configuredPort, out port) || port is < 1 or > 65535))
{
    Console.Error.WriteLine("The configured port must be an integer between 1 and 65535. Use --help.");
    return 1;
}

if (!TryReadGeneratorCount(builder.Configuration["generators"], out int generatorCount, out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

// Bind host for the OPC UA endpoint. Defaults to 0.0.0.0 so the server is
// reachable from outside a container; override with --host (for example
// "localhost" for local-only development).
string host = builder.Configuration["host"] is { Length: > 0 } h ? h : "0.0.0.0";

// A set running to its datasheet cannot protect-trip, so by default the last set
// develops faults on a slow rotation to exercise the alarm path. Pass
// --faults false for a purely healthy plant.
bool injectFaults = true;
if (builder.Configuration["faults"] is string configuredFaults &&
    !bool.TryParse(configuredFaults, out injectFaults))
{
    Console.Error.WriteLine("The configured faults setting must be true or false. Use --help.");
    return 1;
}

builder.Services.Configure<GeneratorDeviceIntegrationOptions>(options =>
{
    options.GeneratorCount = generatorCount;
    options.InjectFaults = injectFaults;
});

builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "GeneratorServer";
        o.ApplicationUri = "urn:localhost:OPCFoundation:GeneratorServer";
        o.ProductUri = "uri:opcfoundation.org:GeneratorServer";
        // Sample convenience only; never auto-accept untrusted certificates in production.
        o.AutoAcceptUntrustedCertificates = autoAccept;
        o.IncludeUnsecurePolicyNone = false;
        o.PkiRoot = Path.Combine(AppContext.BaseDirectory, "pki");
        o.RejectSHA1Certificates = true;
        o.MinCertificateKeySize = 2048;
        o.EndpointUrls.Add($"opc.tcp://{host}:{port}/GeneratorServer");
    })
    .AddNodeManager<GeneratorNodeManagerFactory>()
    // Demonstrate the declarative DI topology-element builder once the node
    // manager has materialised and wired every set. Registering this also puts
    // the post-setup runner in the container, which is what lets the node
    // manager factory receive the configured options.
    .ConfigureDevicesFor<GeneratorNodeManager>(ctx =>
    {
        var manager = (GeneratorNodeManager)ctx.Manager;
        foreach (NodeId setNodeId in manager.GeneratorNodeIds)
        {
            ITopologyElementBuilder<GeneratorSetState> set =
                ctx.TopologyElement<GeneratorSetState>(setNodeId);

            set.WithFunctionalGroup(
                new QualifiedName("Diagnostics", ctx.Manager.InstanceNamespaceIndex),
                fg => fg.Configure(node =>
                    node.WithProperty("LastProtectionTrip", Variant.From(string.Empty), p => p.Writable())
                        .WithProperty("TripCount", 0)
                        .WithProperty("LastServiceDate", (DateTimeUtc)DateTime.UtcNow)));
        }

        return new ValueTask();
    });

await builder.Build().RunAsync().ConfigureAwait(false);
return 0;

static bool TryReadGeneratorCount(string? value, out int generatorCount, out string? error)
{
    const int minCount = 1;
    const int maxCount = 100;

    generatorCount = 2;
    error = null;

    if (string.IsNullOrWhiteSpace(value))
    {
        return true;
    }

    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ||
        parsed < minCount || parsed > maxCount)
    {
        error = FormattableString.Invariant(
            $"Invalid --generators value '{value}'. Specify an integer between {minCount} and {maxCount}.");
        return false;
    }

    generatorCount = parsed;
    return true;
}
