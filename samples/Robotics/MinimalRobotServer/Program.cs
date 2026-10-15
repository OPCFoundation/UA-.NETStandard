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
using System.Globalization;
using System.IO;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Positioning.Server.Hosting;
using Robotics;

// Self-contained OPC UA server exposing an OPC 40010 Robotics MotionDeviceSystem
// (a robot cell of two 6-axis articulated robots) bound to OpenUSD via the draft
// OPC UA - OpenUSD Bindings companion model. A generic connector renders the cell
// live: each Axis' ActualPosition articulates one joint, the cell emergency-stop
// drives a safety visual, a gripper tool is composed dynamically, and the robots
// compose recursively (system -> devices -> axes).
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = 62830;
if (builder.Configuration["port"] is { } portOption &&
    (!int.TryParse(portOption, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535))
{
    await Console.Error.WriteLineAsync("--port must be an integer from 1 through 65535.").ConfigureAwait(false);
    return 2;
}

// Bind host for the OPC UA endpoint. Defaults to 0.0.0.0 so the server is reachable
// from outside a container; override with --host / host env var (e.g. "localhost").
string host = builder.Configuration["host"] is { Length: > 0 } h ? h : "0.0.0.0";
if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
{
    await Console.Error.WriteLineAsync("--host must be a host name or IP address.").ConfigureAwait(false);
    return 2;
}
string? pkiRoot = builder.Configuration["pki-root"];
if (pkiRoot is not null && !Path.IsPathFullyQualified(pkiRoot))
{
    await Console.Error.WriteLineAsync("--pki-root must be an absolute path.").ConfigureAwait(false);
    return 2;
}
bool autoAccept = true;
if (builder.Configuration["autoaccept"] is { } trustOption && !bool.TryParse(trustOption, out autoAccept))
{
    await Console.Error.WriteLineAsync("--autoaccept must be true or false.").ConfigureAwait(false);
    return 2;
}
int runSeconds = 0;
if (builder.Configuration["run-seconds"] is { } runOption &&
    (!int.TryParse(runOption, NumberStyles.None, CultureInfo.InvariantCulture, out runSeconds) ||
        runSeconds is < 1 or > 3600))
{
    await Console.Error.WriteLineAsync("--run-seconds must be an integer from 1 through 3600.").ConfigureAwait(false);
    return 2;
}

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
        if (pkiRoot is not null)
        {
            o.PkiRoot = pkiRoot;
        }
        o.EndpointUrls.Add(new UriBuilder("opc.tcp", host, port, "/MinimalRobotServer").Uri.AbsoluteUri);
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
using var runLifetime = new CancellationTokenSource();
if (runSeconds > 0)
{
    runLifetime.CancelAfter(TimeSpan.FromSeconds(runSeconds));
}
await app.RunAsync(runLifetime.Token).ConfigureAwait(false);
return 0;
