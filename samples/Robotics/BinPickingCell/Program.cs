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
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Server;
using Opc.Ua.Vision.OpenUsd;
using Robotics.IntentEnabledRobot.Simulation;
using Vision.BinPickingCell;

BinPickingCellStage stage = new();
string stagePath = stage.Extract();

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = int.TryParse(builder.Configuration["port"], out int configuredPort)
    ? configuredPort
    : 62855;
string host = builder.Configuration["host"] is { Length: > 0 } configuredHost
    ? configuredHost
    : "localhost";
bool captureOnStartup = !string.Equals(builder.Configuration["captureOnStartup"], "false",
    StringComparison.OrdinalIgnoreCase);
string? artifactDirectory = builder.Configuration["artifactDirectory"];
BinPickingCellOptions cellOptions = BuildCellOptions(builder.Configuration);
bool offServer = cellOptions.InferenceLocation == BinPickingInferenceLocation.EdgeOffServer;

var sensorSpec = new BinPickingSensorSpec(
    StageIdentifier: stagePath,
    CameraPrimPath: BinPickingVisionCell.CameraPrimPath,
    PixelFormat: BinPickingVisionCell.PixelFormat,
    CaptureWidth: 1280,
    CaptureHeight: 1024);

builder.Services.AddSingleton(stage);
builder.Services.AddSingleton(sensorSpec);
builder.Services.AddSingleton(cellOptions);
builder.Services.AddSingleton<SimulatedArmExecutor>();
builder.Services.AddSingleton<BinPickingRobotCell>();
builder.Services.AddSingleton<BinPickingWorldState>();
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
        options.AutoAcceptUntrustedCertificates = true;

        // A 1280x1024 RGB frame is just under 4 MB, which is exactly the default
        // ByteString ceiling, so GetClip refused its own camera output with
        // BadEncodingLimitsExceeded. Advertise room for the frames this cell serves.
        options.MaxByteStringLength = 32 * 1024 * 1024;
        options.MaxArrayLength = 32 * 1024 * 1024;
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
    .AddVision(options =>
    {
        options.InstanceNamespaceUri = "urn:opcfoundation:BinPickingCell:vision:instances";
    })
    .AddVisionMediaProvider<BinPickingMediaProvider>(
        BinPickingVisionCell.SensorTwinBrowseName)
    .ConfigureVision(async (context, cancellationToken) =>
    {
        BinPickingVisionCell cell = context.GetRequiredService<BinPickingVisionCell>();
        await cell.ConfigureAsync(context, cancellationToken).ConfigureAwait(false);
    });

using IHost app = builder.Build();
await app.RunAsync().ConfigureAwait(false);

static BinPickingCellOptions BuildCellOptions(Microsoft.Extensions.Configuration.IConfiguration configuration)
{
    string? raw = configuration["inferenceLocation"];
    // A parse failure just means "use the OnServer default"; the out value is set for us
    // and the caller does not need a separate error path for an unknown key.
    _ = BinPickingCellOptions.TryParseLocation(raw, out BinPickingInferenceLocation location);
    return new BinPickingCellOptions
    {
        InferenceLocation = location
    };
}
