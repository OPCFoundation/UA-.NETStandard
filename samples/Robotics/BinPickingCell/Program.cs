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
using Opc.Ua.Samples;
using Opc.Ua.Server;
using Opc.Ua.Vision.OpenUsd;
using Robotics.IntentEnabledRobot.Simulation;
using Vision.BinPickingCell;

Option<bool> autoAcceptOption = SampleCommandLine.CreateAutoAcceptOption("--autoaccept", "-a", "--insecure");
Option<string> portOption = SampleCommandLine.CreateInt32ConfigurationOption(
    "--port", "Endpoint port (default 62855).", 1, 65535);
var hostOption = new Option<string>("--host") { Description = "Endpoint host (default localhost)." };
var inferenceOption = new Option<string>("--inferenceLocation")
{
    Description = "Inference location: OnServer (default), EdgeOffServer or OffServer."
};
var captureOption = new Option<string>("--captureOnStartup")
{
    Description = "Capture a frame at startup: true or false (default true)."
};
var artifactOption = new Option<string>("--artifactDirectory")
{
    Description = "Capture-proof output directory."
};
captureOption.Validators.Add(result =>
{
    if (!bool.TryParse(result.GetValueOrDefault<string>(), out _))
    {
        result.AddError("--captureOnStartup expects true or false.");
    }
});
inferenceOption.Validators.Add(result =>
{
    if (!BinPickingCellOptions.TryParseLocation(result.GetValueOrDefault<string>(), out _))
    {
        result.AddError("--inferenceLocation expects OnServer, EdgeOffServer or OffServer.");
    }
});
Argument<string[]> configurationArgument = SampleCommandLine.CreateConfigurationArgument();
(string[] sampleArguments, string[] forwardedArguments) = SampleCommandLine.SplitHostArguments(args);
var command = new RootCommand(
    "OPC UA Bin Picking Cell: --insecure is a trust-only alias; endpoints retain message security.")
{
    autoAcceptOption,
    portOption,
    hostOption,
    inferenceOption,
    captureOption,
    artifactOption,
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
    parsed, forwardedArguments, configurationArgument,
    portOption, hostOption, inferenceOption, captureOption, artifactOption));

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = 62855;
if (builder.Configuration["port"] is string configuredPort &&
    (!int.TryParse(configuredPort, out port) || port is < 1 or > 65535))
{
    Console.Error.WriteLine("The configured port must be an integer between 1 and 65535. Use --help.");
    return 1;
}
string host = builder.Configuration["host"] is { Length: > 0 } configuredHost
    ? configuredHost
    : "localhost";
bool captureOnStartup = true;
if (builder.Configuration["captureOnStartup"] is string configuredCapture &&
    !bool.TryParse(configuredCapture, out captureOnStartup))
{
    Console.Error.WriteLine("The configured captureOnStartup setting must be true or false. Use --help.");
    return 1;
}
if (builder.Configuration["inferenceLocation"] is string configuredInference &&
    !BinPickingCellOptions.TryParseLocation(configuredInference, out _))
{
    Console.Error.WriteLine(
        "The configured inferenceLocation must be OnServer, EdgeOffServer or OffServer. Use --help.");
    return 1;
}
string? artifactDirectory = builder.Configuration["artifactDirectory"];
BinPickingCellOptions cellOptions = BuildCellOptions(builder.Configuration);
bool offServer = cellOptions.InferenceLocation == BinPickingInferenceLocation.EdgeOffServer;

BinPickingCellStage stage = new();
string stagePath = stage.Extract();

var sensorSpec = new BinPickingSensorSpec(
    StageIdentifier: stagePath,
    CameraPrimPath: BinPickingVisionCell.CameraPrimPath,
    PixelFormat: BinPickingVisionCell.PixelFormat,
    CaptureWidth: (int)BinPickingVisionCell.SensorWidth,
    CaptureHeight: (int)BinPickingVisionCell.SensorHeight);

builder.Services.AddSingleton(stage);
builder.Services.AddSingleton(sensorSpec);
builder.Services.AddSingleton(cellOptions);

// The arm is bolted to the bench, so the bench is z = 0 in its own base frame. Telling
// the solver that stops it handing back configurations that reach through the work
// surface - several inverse-kinematic solutions for a target near the bench do exactly
// that, and taking the nearest one regardless renders an arm passing through its table.
//
// The height alone is not enough, though: it is a plane sampled at the joint origins, so a
// link can span it, and it says nothing about the bin or the fixture.
// BinPickingCellGeometry.CreateCollisionModel() describes the cell's furniture as solids
// and SimulatedArmKinematics.Collisions enforces it along every link, so a configuration
// that puts part of the arm inside the bench or through a bin wall is refused rather than
// rendered. The palletizer kinematics tests pin the remaining work envelope at every cell
// work position.
builder.Services.AddSingleton(_ => new BinPickingPalletizerKinematics
{
    MinimumLinkHeight =
        BinPickingCellGeometry.BenchTopMetres - BinPickingCellGeometry.RobotBaseHeightMetres,
    Collisions = BinPickingCellGeometry.CreateCollisionModel()
});
builder.Services.AddSingleton<BinPickingRobotCell>();
builder.Services.AddSingleton<BinPickingWorldState>();
builder.Services.AddSingleton<IBinPickingTargetProvider, BinPickingTargetProvider>();
builder.Services.AddSingleton<BinPickingGroundTruthInferenceProvider>();
builder.Services.AddSingleton<BinPickingAgentInferenceProvider>();
builder.Services.AddSingleton<BinPickingVisionCell>();
builder.Services.AddSingleton<BinPickingMediaProvider>();
builder.Services.AddOpenUsdSceneCameraCaptureProvider();
builder.Services.AddHostedService(services =>
    new BinPickingCaptureProof(
        services.GetRequiredService<ISceneCameraCaptureProvider>(),
        services.GetRequiredService<BinPickingCellStage>(),
        services.GetRequiredService<ILogger<BinPickingCaptureProof>>(),
        enabled: captureOnStartup,
        artifactDirectory: artifactDirectory));
if (offServer)
{
    builder.Services.AddHostedService(services =>
        new BinPickingOffServerProof(
            services.GetRequiredService<BinPickingAgentInferenceProvider>(),
            services.GetRequiredService<ILogger<BinPickingOffServerProof>>()));
}
else
{
    builder.Services.AddHostedService(services =>
        new BinPickingInferenceProof(
            services.GetRequiredService<BinPickingGroundTruthInferenceProvider>(),
            services.GetRequiredService<BinPickingWorldState>(),
            services.GetRequiredService<ILogger<BinPickingInferenceProof>>()));
}

builder.Services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "BinPickingCell";
        options.ApplicationUri = "urn:localhost:OPCFoundation:BinPickingCell";
        options.ProductUri = "uri:opcfoundation.org:BinPickingCell";
        options.AutoAcceptUntrustedCertificates = autoAccept;
        options.IncludeUnsecurePolicyNone = false;

        // A rendered frame is a few hundred kilobytes and the response that carries it
        // larger still, so leave the transport room for a busier scene rather than have
        // GetClip refuse the cell's own camera output. Note the failure this originally
        // chased was not a quota at all - the provider was embedding the encoded image in
        // a String field - so none of these were ever the fix; see BinPickingMediaProvider.
        options.MaxByteStringLength = 32 * 1024 * 1024;
        options.MaxArrayLength = 32 * 1024 * 1024;
        options.MaxMessageSize = 64 * 1024 * 1024;
        options.EndpointUrls.Add($"opc.tcp://{host}:{port}/BinPickingCell");
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
    .AddRobotIntentExecutor<SimulatedArmExecutor>()
    .ConfigureRobotIntent(async (context, cancellationToken) =>
        await context.GetRequiredService<BinPickingRobotCell>()
            .ConfigureAsync(context, cancellationToken).ConfigureAwait(false))
    .AddVision(options => options.InstanceNamespaceUri = "urn:opcfoundation:BinPickingCell:vision:instances")
    .AddVisionMediaProvider<BinPickingMediaProvider>(
        BinPickingVisionCell.SensorTwinBrowseName)
    .ConfigureVision(async (context, cancellationToken) =>
    {
        BinPickingVisionCell cell = context.GetRequiredService<BinPickingVisionCell>();
        await cell.ConfigureAsync(context, cancellationToken).ConfigureAwait(false);
    });

// AddRobotIntentExecutor registers the concrete executor so its IIntentExecutor and any
// observers share one instance. Replace that default-constructed UR executor after the
// fluent registration with the palletizer-backed instance for this cell only.
builder.Services.Replace(ServiceDescriptor.Singleton(
    services => new SimulatedArmExecutor(
        services.GetRequiredService<BinPickingPalletizerKinematics>())));

using IHost app = builder.Build();
await app.RunAsync().ConfigureAwait(false);
return 0;

static BinPickingCellOptions BuildCellOptions(Microsoft.Extensions.Configuration.IConfiguration configuration)
{
    string? raw = configuration["inferenceLocation"];
    // Supplied values have already been validated; an omitted setting uses OnServer.
    _ = BinPickingCellOptions.TryParseLocation(raw, out BinPickingInferenceLocation location);
    return new BinPickingCellOptions
    {
        InferenceLocation = location
    };
}
