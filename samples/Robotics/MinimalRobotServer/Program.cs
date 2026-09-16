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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Positioning.Server.Hosting;
using Opc.Ua.Samples;
using Robotics;

// Self-contained OPC UA server exposing an OPC 40010 Robotics MotionDeviceSystem
// (a robot cell of two 6-axis articulated robots) bound to OpenUSD via the draft
// OPC UA - OpenUSD Bindings companion model. A generic connector renders the cell
// live: each Axis' ActualPosition articulates one joint, the cell emergency-stop
// drives a safety visual, a gripper tool is composed dynamically, and the robots
// compose recursively (system -> devices -> axes).
Option<bool> autoAcceptOption = SampleCommandLine.CreateAutoAcceptOption("--autoaccept", "-a", "--insecure");
Option<string> portOption = SampleCommandLine.CreateInt32ConfigurationOption(
    "--port", "Endpoint port (default 62830).", 1, 65535);
var hostOption = new Option<string>("--host") { Description = "Endpoint host (default 0.0.0.0)." };
Argument<string[]> configurationArgument = SampleCommandLine.CreateConfigurationArgument();
(string[] sampleArguments, string[] forwardedArguments) = SampleCommandLine.SplitHostArguments(args);
var command = new RootCommand(
    "OPC UA Minimal Robot Server: --insecure is a trust-only alias; endpoints retain message security.")
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

int port = 62830;
if (builder.Configuration["port"] is string configuredPort &&
    (!int.TryParse(configuredPort, out port) || port is < 1 or > 65535))
{
    Console.Error.WriteLine("The configured port must be an integer between 1 and 65535. Use --help.");
    return 1;
}

// Bind host for the OPC UA endpoint. Defaults to 0.0.0.0 so the server is reachable
// from outside a container; override with --host / host env var (e.g. "localhost").
string host = builder.Configuration["host"] is { Length: > 0 } h ? h : "0.0.0.0";

builder.Services.AddOptions<MobileRobotPositionOptions>()
    .Bind(builder.Configuration.GetSection("Robots"));
builder.Services.AddSingleton<RobotPositioningScenario>();
builder.Services.AddSingleton<CellChoreographer>();

IPositioningServerBuilder positioning = builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "MinimalRobotServer";
        o.ApplicationUri = "urn:localhost:OPCFoundation:MinimalRobotServer";
        o.ProductUri = "uri:opcfoundation.org:MinimalRobotServer";
        o.AutoAcceptUntrustedCertificates = autoAccept;
        o.IncludeUnsecurePolicyNone = false;
        o.EndpointUrls.Add($"opc.tcp://{host}:{port}/MinimalRobotServer");
    })
    .AddRobotics()
    .AddRoboticsModel<OpenUsdModelProvider>()
    .AddRoboticsModel<RslModelProvider>()
    .AddRoboticsModel<GposModelProvider>()
    .ConfigureRobotics<RobotCell>()
    .ConfigureRobotics(async context =>
        await context.GetRequiredService<IPositioningPostSetupRunner>()
            .RunAsync(context.Manager, context.CancellationToken).ConfigureAwait(false))
    .AddPositioningFor<Opc.Ua.Robotics.Server.RoboticsNodeManager>();

positioning
    .AddGeoLocationProvider<MobileRobotPositionProvider>()
    .ConfigurePositioningFor<Opc.Ua.Robotics.Server.RoboticsNodeManager>(
        context => RobotCell.GetForManager(context.Manager)
            .ConfigurePositioningAsync(context));

using IHost app = builder.Build();
await app.RunAsync().ConfigureAwait(false);
return 0;
