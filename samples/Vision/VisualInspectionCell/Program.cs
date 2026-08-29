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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.AI.Inference;
using Opc.Ua.AI.Server;
using Opc.Ua.ISA95.Server;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Vision.VisualInspectionCell;

string[] normalizedArgs = NormalizeArgs(args);
HostApplicationBuilder builder = Host.CreateApplicationBuilder(normalizedArgs);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = int.TryParse(builder.Configuration["port"], NumberStyles.Integer, CultureInfo.InvariantCulture,
    out int configuredPort)
    ? configuredPort
    : 62865;
string host = builder.Configuration["host"] is { Length: > 0 } configuredHost
    ? configuredHost
    : "localhost";
_ = VisualInspectionCellOptions.TryParseLocation(
    builder.Configuration["inferenceLocation"],
    out VisualInspectionInferenceLocation inferenceLocation);
bool insecure = bool.TryParse(builder.Configuration["insecure"], out bool parsedInsecure) && parsedInsecure;
var cellOptions = new VisualInspectionCellOptions
{
    InferenceLocation = inferenceLocation
};

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(cellOptions);
builder.Services.AddSingleton<InspectionRecipe>();
builder.Services.AddSingleton<FixtureImageAnalyzer>();
builder.Services.AddSingleton<InspectionVerdictPolicy>();
builder.Services.AddSingleton<VisualInspectionAnalysisService>();
builder.Services.AddSingleton<VisualInspectionResultPublisher>();
builder.Services.AddSingleton<VisualInspectionMediaProvider>();
builder.Services.AddSingleton<VisualInspectionInferenceProvider>();
builder.Services.AddSingleton<VisualInspectionFeedbackSink>();
builder.Services.AddSingleton<OperatorDialogController>();
builder.Services.AddSingleton<VisualInspectionCell>();
builder.Services.AddSingleton<AINodeManagerRegistry>();
builder.Services.AddSingleton(services => new InferenceBackends(
    new VisualInspectionInferenceBackend(
        services.GetRequiredService<VisualInspectionAnalysisService>(),
        services.GetRequiredService<VisualInspectionCellOptions>())));
builder.Services.AddSingleton<IServerStartupTask>(services =>
    services.GetRequiredService<AINodeManagerRegistry>());
builder.Services.AddSingleton<InspectionJobControlProvider>();
builder.Services.AddSingleton<IIsa95JobOrderReceiverV2>(services =>
    services.GetRequiredService<InspectionJobControlProvider>());
builder.Services.AddSingleton<IIsa95JobResponseProviderV2>(services =>
    services.GetRequiredService<InspectionJobControlProvider>());
builder.Services.AddSingleton<IIsa95JobResponseReceiverV2>(services =>
    services.GetRequiredService<InspectionJobControlProvider>());
builder.Services.AddSingleton<IIsa95JobStatusSourceV2>(services =>
    services.GetRequiredService<InspectionJobControlProvider>());
builder.Services.AddSingleton<IIsa95JobExecutionController>(services =>
    services.GetRequiredService<InspectionJobControlProvider>());
builder.Services.AddSingleton<IIsa95JobOrderCatalog>(services =>
    services.GetRequiredService<InspectionJobControlProvider>());
builder.Services.AddSingleton<IIsa95JobOrderCatalogChangeSource>(services =>
    services.GetRequiredService<InspectionJobControlProvider>());
builder.Services.AddHostedService<InspectionJobSeeder>();

IOpcUaServerBuilder opcUa = builder.Services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "VisualInspectionCell";
        options.ApplicationUri = "urn:localhost:OPCFoundation:VisualInspectionCell";
        options.ProductUri = "uri:opcfoundation.org:VisualInspectionCell";
        options.AutoAcceptUntrustedCertificates = insecure;
        options.EndpointUrls.Add($"opc.tcp://{host}:{port}/VisualInspectionCell");
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
    }));

opcUa.AddAI(
    ai =>
    {
        ai.PrimaryDeploymentId = "visual-inspection-primary";
        ai.FallbackDeploymentId = "visual-inspection-fallback";
        ai.EnableFallback = false;
        ai.EnableCatalogue = false;
        ai.EnableLearningLoop = true;
    },
    backend =>
    {
        backend.Site = inferenceLocation == VisualInspectionInferenceLocation.EdgeOffServer
            ? InferenceSite.EdgeOffServer
            : InferenceSite.OnServer;
        backend.EgressPermitted = false;
        backend.RetainsInput = false;
        backend.Models.Add(VisualInspectionInferenceBackend.Model);
    },
    fallback => fallback.Enabled = false);
opcUa.AddIsa95Server(options =>
{
    options.InstanceNamespaceUri = "urn:opcfoundation:VisualInspectionCell:isa95:instances";
    options.RootBrowseName = "VisualInspectionISA95";
    options.EnableJobControlV1 = false;
    options.EnableJobControlV2 = true;
});
opcUa
    .AddVision(options =>
        options.InstanceNamespaceUri = "urn:opcfoundation:VisualInspectionCell:vision:instances")
    .ConfigureVision(async (context, cancellationToken) =>
    {
        VisualInspectionCell cell = context.GetRequiredService<VisualInspectionCell>();
        await cell.ConfigureAsync(context, cancellationToken).ConfigureAwait(false);
    });

using IHost app = builder.Build();
Console.WriteLine(FormattableString.Invariant(
    $"VisualInspectionCell listening at opc.tcp://{host}:{port}/VisualInspectionCell"));
Console.WriteLine(FormattableString.Invariant(
    $"InferenceLocation={inferenceLocation}; fixtures=bracket-ok.png, bracket-not-ok.png, bracket-ambiguous.png"));
VisualInspectionAnalysisService analysis = app.Services.GetRequiredService<VisualInspectionAnalysisService>();
foreach (string fixture in analysis.FixtureNames)
{
    InspectionAnalysis result = analysis.AnalyzeByName(fixture);
    Console.WriteLine(FormattableString.Invariant($"{fixture}: {result.Verdict}"));
    foreach (Opc.Ua.Vision.VisionCharacteristicDataType characteristic in result.Characteristics)
    {
        double low = characteristic.Actual - characteristic.Uncertainty;
        double high = characteristic.Actual + characteristic.Uncertainty;
        Console.WriteLine(FormattableString.Invariant(
            $"  {characteristic.CharacteristicId}: {characteristic.Actual:0.00} mm [{low:0.00}, {high:0.00}], {characteristic.Status}"));
    }
}
await app.RunAsync().ConfigureAwait(false);

static string[] NormalizeArgs(string[] args)
{
    string[] normalized = new string[args.Length];
    for (int ii = 0; ii < args.Length; ii++)
    {
        normalized[ii] = string.Equals(args[ii], "--insecure", StringComparison.OrdinalIgnoreCase)
            ? "--insecure=true"
            : args[ii];
    }
    return normalized;
}
