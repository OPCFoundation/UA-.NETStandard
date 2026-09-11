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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Samples;
using Opc.Ua.Server;
using Robotics.IntentEnabledRobot;
using Robotics.IntentEnabledRobot.Simulation;

Option<bool> autoAcceptOption = SampleCommandLine.CreateAutoAcceptOption("--autoaccept", "-a", "--insecure");
Option<string> portOption = SampleCommandLine.CreateInt32ConfigurationOption(
    "--port", "Endpoint port (default 62840).", 1, 65535);
var hostOption = new Option<string>("--host") { Description = "Endpoint host (default localhost)." };
Argument<string[]> configurationArgument = SampleCommandLine.CreateConfigurationArgument();
(string[] sampleArguments, string[] forwardedArguments) = SampleCommandLine.SplitHostArguments(args);
var command = new RootCommand(
    "OPC UA Intent Enabled Robot: --insecure is a trust-only alias; endpoints retain message security.")
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

int port = 62840;
if (builder.Configuration["port"] is string configuredPort &&
    (!int.TryParse(configuredPort, out port) || port is < 1 or > 65535))
{
    Console.Error.WriteLine("The configured port must be an integer between 1 and 65535. Use --help.");
    return 1;
}
string host = builder.Configuration["host"] is { Length: > 0 } configuredHost
    ? configuredHost
    : "localhost";

builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IRobotIntentModelProvider, OpenUsdIntentModelProvider>());
builder.Services.AddSingleton<SimulatedArmExecutor>();
builder.Services.AddSingleton<SampleSafetySource>();
builder.Services.AddSingleton<IntentRobotCell>();
builder.Services.AddHostedService<SafetyConsoleService>();

builder.Services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "IntentEnabledRobot";
        options.ApplicationUri = "urn:localhost:OPCFoundation:IntentEnabledRobot";
        options.ProductUri = "uri:opcfoundation.org:IntentEnabledRobot";
        options.AutoAcceptUntrustedCertificates = autoAccept;
        options.IncludeUnsecurePolicyNone = false;
        options.EndpointUrls.Add($"opc.tcp://{host}:{port}/IntentEnabledRobot");
    })
    .ConfigureRoles(options => options.Roles.Add(new RoleDefinitionOptions
    {
        Name = BrowseNames.WellKnownRole_Operator,
        Identities =
        {
            new RoleIdentityMappingOptions
            {
                CriteriaType = IdentityCriteriaType.Anonymous
            }
        }
    }))
    .AddRobotIntent()
    .AddRobotIntentExecutor<SafetyAwareArmExecutor>()
    .ConfigureRobotIntent(async (context, cancellationToken) =>
        await context.GetRequiredService<IntentRobotCell>()
            .ConfigureAsync(context, cancellationToken).ConfigureAwait(false));

using IHost app = builder.Build();
await app.RunAsync().ConfigureAwait(false);
return 0;
