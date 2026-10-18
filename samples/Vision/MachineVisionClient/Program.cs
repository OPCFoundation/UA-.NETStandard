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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.MachineVision;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
string url = builder.Configuration["endpoint"] ?? "opc.tcp://localhost:62550/MachineVision";
string? insecureOption = builder.Configuration["insecure"];
bool insecure = false;
if (insecureOption != null && !bool.TryParse(insecureOption, out insecure))
{
    throw new ArgumentException("--insecure requires true or false.", nameof(args));
}
ITelemetryContext telemetry = DefaultTelemetry.Create(logging => logging.SetMinimumLevel(LogLevel.Warning));
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
CancellationToken ct = cancellation.Token;
EndpointDescription? selected = null;
using (DiscoveryClient discovery = await DiscoveryClient.CreateAsync(new Uri(url), telemetry, ct: ct)
    .ConfigureAwait(false))
{
    GetEndpointsResponse endpoints = await discovery.GetEndpointsAsync(null, url, [], [], ct).ConfigureAwait(false);
    foreach (EndpointDescription endpoint in endpoints.Endpoints)
    {
        if (endpoint.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
            endpoint.SecurityPolicyUri == SecurityPolicies.Basic256Sha256)
        {
            selected = endpoint;
            break;
        }
    }
}
if (selected == null)
{
    throw new InvalidOperationException("The demonstrator requires a Basic256Sha256 SignAndEncrypt endpoint.");
}
builder.Services.AddOpcUa().AddClient(options =>
{
    options.ApplicationName = "MachineVisionClient";
    options.ApplicationUri = "urn:localhost:OPCFoundation:MachineVisionClient";
    options.PkiRoot = builder.Configuration["pki"] ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OPCFoundation", "MachineVisionClient", "pki");
    options.AutoAcceptUntrustedCertificates = insecure;
    options.Session = new ManagedSessionOptions
    {
        Endpoint = new ConfiguredEndpoint(null, selected, null),
        SessionName = "MachineVisionClient"
    };
}).AddVisionClient();
using IHost host = builder.Build();
VisionClient vision = await host.Services.GetRequiredService<Func<CancellationToken, Task<VisionClient>>>()(ct)
    .ConfigureAwait(false);
if (vision.Session is not ManagedSession session)
{
    throw new InvalidOperationException("The client factory did not provide a managed session.");
}
await using (session.ConfigureAwait(false))
{
    MachineVisionClient client = vision.MachineVision();
    await foreach (VisionNodeEntry entry in client.EnumerateSystemsAsync(cancellationToken: ct).ConfigureAwait(false))
    {
        Console.WriteLine($"System {entry.BrowseName}: {entry.NodeId}");
        MachineVisionSystemClient system = client.System(entry.NodeId);
        VisionStateMachineTypeClient control = await system.ControlAsync(ct).ConfigureAwait(false);
        FiniteStateSnapshot state = await control.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false);
        if (state.CurrentState.Text == "Preoperational")
        {
            MachineVisionSystemClient.ThrowIfDomainError(
                await control.SelectModeAutomaticAsync(ct).ConfigureAwait(false),
                "SelectModeAutomatic");
        }
        using var recipeContent = new MemoryStream(Encoding.UTF8.GetBytes("deterministic-inspection-recipe"));
        RecipeIdInternalDataType recipe = await system.UploadRecipeAsync(
            new RecipeIdExternalDataType { Id = "sample-recipe" },
            new ProductIdDataType { Id = "sample-product" }, recipeContent, ct).ConfigureAwait(false);
        RecipeManagementTypeClient recipes = await system.RecipesAsync(ct).ConfigureAwait(false);
        var prepared = await recipes.PrepareRecipeAsync(new RecipeIdExternalDataType(), recipe, ct)
            .ConfigureAwait(false);
        MachineVisionSystemClient.ThrowIfDomainError(prepared.error, "PrepareRecipe");
        VisionAutomaticModeStateMachineTypeClient automatic = await system.AutomaticAsync(ct).ConfigureAwait(false);
        var started = await automatic.StartSingleJobAsync(
            new MeasIdDataType { Id = "sample-measurement" }, new PartIdDataType(),
            new RecipeIdExternalDataType { Id = "sample-recipe" },
            new ProductIdDataType { Id = "sample-product" }, [], ct).ConfigureAwait(false);
        MachineVisionSystemClient.ThrowIfDomainError(started.error, "StartSingleJob");
        await automatic.WaitForStateAsync(
            session.DefaultStreaming,
            NodeId.Create(VisionAutomaticModeStateMachineTypeIds.StateIds.Ready,
                Opc.Ua.Vision.MachineVision.Namespaces.MachineVision, session.NamespaceUris),
            TimeSpan.FromSeconds(15), ct: ct).ConfigureAwait(false);
        ResultManagementTypeClient results = await system.ResultsAsync(ct).ConfigureAwait(false);
        var page = await results.GetResultListFilteredAsync(
            0, new MeasIdDataType(), new PartIdDataType(), new RecipeIdExternalDataType(),
            new RecipeIdInternalDataType(), new ConfigurationIdDataType(), new ConfigurationIdDataType(),
            new ProductIdDataType(), started.jobId, 10, 0, 0, ct).ConfigureAwait(false);
        MachineVisionSystemClient.ThrowIfDomainError(page.error, "GetResultListFiltered");
        foreach (ResultDataType result in page.resultList)
        {
            Console.WriteLine(
                $"Result {result.ResultId.Id}: state={result.ResultState}, simulated={result.IsSimulated}");
        }
        MachineVisionSystemClient.ThrowIfDomainError(
            await results.ReleaseResultHandleAsync(page.resultHandle, ct).ConfigureAwait(false), "ReleaseResultHandle");
        ArrayOf<NodeId> pipelines = await vision.DiscoverPipelinesAsync(ct).ConfigureAwait(false);
        if (!pipelines.IsEmpty)
        {
            string sharedId = await vision.Pipeline(pipelines[0]).RunInferenceAsync(cancellationToken: ct)
                .ConfigureAwait(false);
            var shared = await results.GetResultByIdAsync(new ResultIdDataType { Id = sharedId }, 0, ct)
                .ConfigureAwait(false);
            MachineVisionSystemClient.ThrowIfDomainError(shared.error, "GetResultById");
            Console.WriteLine($"Draft and released models share result {shared.result.ResultId.Id}.");
            MachineVisionSystemClient.ThrowIfDomainError(
                await results.ReleaseResultHandleAsync(shared.resultHandle, ct).ConfigureAwait(false),
                "ReleaseResultHandle");
        }
    }
    await foreach (VisionNodeEntry asset in client.EnumerateAssetsAsync(cancellationToken: ct).ConfigureAwait(false))
    {
        MachineVisionAssetSnapshot snapshot = await client.ReadAssetAsync(asset.NodeId, cancellationToken: ct)
            .ConfigureAwait(false);
        Console.WriteLine($"Asset {asset.BrowseName}: {snapshot.Values.Count} values, complete={snapshot.IsComplete}");
    }
    await session.DefaultStreaming.DisposeAsync().ConfigureAwait(false);
    await session.CloseAsync(ct).ConfigureAwait(false);
}
