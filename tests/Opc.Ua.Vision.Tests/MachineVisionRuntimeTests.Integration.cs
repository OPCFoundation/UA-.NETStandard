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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.MachineVision;
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Vision.Tests
{
    public sealed partial class MachineVisionRuntimeTests
    {
        [Test]
        public void MachineryCompositionRequiresExistingProviderRegistration()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "MachineVisionRegistration");
            Assert.Throws<InvalidOperationException>(() => builder.AddVisionToMachinery());
            builder.AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks);
            Assert.DoesNotThrow(() => builder.AddVisionToMachinery());
        }

        [Test]
        public void StandaloneAssetsRejectASecondMachineryOwner()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "MachineVisionRegistration")
                .AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks);
            Assert.Throws<InvalidOperationException>(() => builder.AddVision(
                options => options.MachineVisionParts = MachineVisionParts.Assets).AddVisionMachinery());
        }

        [Test]
        [Category("Integration")]
        [NonParallelizable]
        public async Task MachineryCompositionSupportsBothPartsOverTcpAsync()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = deadline.Token;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            string url = $"opc.tcp://localhost:{port}/MachineVision";
            string root = Path.Combine(Path.GetTempPath(), "MachineVisionWire-" + Guid.NewGuid().ToString("N"));
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            MachineVisionSystemHandle? runtime = null;
            MachineVisionAssetHandle? asset = null;
            builder.Services.AddOpcUa()
                .AddServer(options =>
                {
                    options.ApplicationName = "MachineVisionWireServer";
                    options.ApplicationUri = "urn:opcfoundation:tests:machinevision:server";
                    options.PkiRoot = Path.Combine(root, "server");
                    options.AutoAcceptUntrustedCertificates = true;
                    options.IncludeUnsecurePolicyNone = true;
                    options.EndpointUrls.Add(url);
                })
                .AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks)
                .AddVisionToMachinery()
                .ConfigureVisionFor<MachineryNodeManager>(async (vision, token) =>
                {
                    runtime = await vision.AddMachineVisionSystemAsync(
                        "Inspection", Provider().Object, cancellationToken: token).ConfigureAwait(false);
                    asset = await vision.AddMachineVisionAsset("InspectionAsset")
                        .WithIdentification(Identification("wire", "urn:opcfoundation:tests:machinevision:wire"))
                        .RepresentsSameSystem(runtime.State)
                        .BuildAsync(token).ConfigureAwait(false);
                });
            using IHost server = builder.Build();
            try
            {
                await server.StartAsync(ct).ConfigureAwait(false);
                var services = new ServiceCollection();
                services.AddOpcUa().AddClient(options =>
                {
                    options.ApplicationName = "MachineVisionWireClient";
                    options.ApplicationUri = "urn:opcfoundation:tests:machinevision:client";
                    options.PkiRoot = Path.Combine(root, "client");
                    options.AutoAcceptUntrustedCertificates = true;
                    options.Session = new ManagedSessionOptions
                    {
                        SessionName = "MachineVisionWireClient",
                        Endpoint = new ConfiguredEndpoint(null, new EndpointDescription
                        {
                            EndpointUrl = url,
                            SecurityMode = MessageSecurityMode.None,
                            SecurityPolicyUri = SecurityPolicies.None
                        }, null)
                    };
                }).AddVisionClient();
                await using ServiceProvider serviceProvider = services.BuildServiceProvider();
                VisionClient visionClient = await serviceProvider
                    .GetRequiredService<Func<CancellationToken, Task<VisionClient>>>()(ct).ConfigureAwait(false);
                Assert.That(visionClient.Session, Is.InstanceOf<ManagedSession>());
                var session = (ManagedSession)visionClient.Session;
                await using (session.ConfigureAwait(false))
                {
                    DataValue conformance = await session.ReadValueAsync(
                        VariableIds.Server_ServerCapabilities_ConformanceUnits, ct)
                        .ConfigureAwait(false);
                    Assert.That(conformance.WrappedValue.TryGetValue(out ArrayOf<QualifiedName> units), Is.True);
                    Assert.That(units.ToArray(), Does.Contain(new QualifiedName("VSA Basic",
                        (ushort)session.NamespaceUris.GetIndex(MachineVisionModels.AssetNamespaceUri))));
                    Assert.That(units.ToArray(), Does.Contain(new QualifiedName("Automatic Mode",
                        (ushort)session.NamespaceUris.GetIndex(MachineVisionModels.ControlNamespaceUri))));
                    MachineVisionClient client = visionClient.MachineVision();
                    var systems = new List<VisionNodeEntry>();
                    await foreach (VisionNodeEntry system in client.EnumerateSystemsAsync(cancellationToken: ct)
                        .ConfigureAwait(false))
                    {
                        systems.Add(system);
                    }
                    Assert.That(systems, Has.Count.EqualTo(1));
                    Assert.That(systems[0].NodeId, Is.EqualTo(runtime!.State.NodeId));
                    var assets = new List<VisionNodeEntry>();
                    await foreach (VisionNodeEntry entry in client.EnumerateAssetsAsync(cancellationToken: ct)
                        .ConfigureAwait(false))
                    {
                        assets.Add(entry);
                    }
                    Assert.That(assets, Has.Count.EqualTo(1));
                    Assert.That(assets[0].NodeId, Is.EqualTo(asset!.State.NodeId));
                    ArrayOf<NodeId> linked = await client.GetSameEntityNodesAsync(asset.State.NodeId, ct)
                        .ConfigureAwait(false);
                    Assert.That(linked.ToArray(), Does.Contain(runtime.State.NodeId));
                    MachineVisionSystemClient systemClient = client.System(runtime.State.NodeId);
                    VisionStateMachineTypeClient control = await systemClient.ControlAsync(ct).ConfigureAwait(false);
                    Assert.That(await control.SelectModeAutomaticAsync(ct).ConfigureAwait(false), Is.Zero);
                    using var content = new MemoryStream([1, 2, 3]);
                    RecipeIdInternalDataType recipe = await systemClient.UploadRecipeAsync(
                        new RecipeIdExternalDataType { Id = "wire-recipe" },
                        new ProductIdDataType { Id = "wire-product" }, content, ct).ConfigureAwait(false);
                    RecipeManagementTypeClient recipes = await systemClient.RecipesAsync(ct).ConfigureAwait(false);
                    (RecipeIdInternalDataType? internalIdOut, bool isCompleted, int error) =
                        await recipes.PrepareRecipeAsync(new RecipeIdExternalDataType(), recipe, ct)
                        .ConfigureAwait(false);
                    Assert.That(error, Is.Zero);
                    Assert.That(isCompleted, Is.True);
                    VisionAutomaticModeStateMachineTypeClient automatic = await systemClient.AutomaticAsync(ct)
                        .ConfigureAwait(false);
                    FiniteStateSnapshot ready = await automatic.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false);
                    Assert.That(ready.CurrentState.Text, Is.EqualTo("Ready"));
                    using var configurationContent = new MemoryStream([9, 8, 7]);
                    ConfigurationIdDataType configuration = await systemClient.UploadConfigurationAsync(
                        new ConfigurationIdDataType { Id = "wire-configuration" }, configurationContent, ct)
                        .ConfigureAwait(false);
                    ConfigurationManagementTypeClient configurations = await systemClient.ConfigurationsAsync(ct)
                        .ConfigureAwait(false);
                    Assert.That(await configurations.ActivateConfigurationAsync(configuration, ct)
                        .ConfigureAwait(false), Is.Zero);
                    (uint configurationHandle, ConfigurationDataType configuration, int error) configured =
                        await configurations.GetConfigurationByIdAsync(configuration, 0, ct)
                        .ConfigureAwait(false);
                    Assert.That(configured.error, Is.Zero);
                    Assert.That(configured.configuration.ExternalId.Id, Is.EqualTo("wire-configuration"));
                    Assert.That(configured.configuration.HasTransferableDataOnFile, Is.True);
                    Assert.That(await configurations.ReleaseConfigurationHandleAsync(configured.configurationHandle, ct)
                        .ConfigureAwait(false), Is.Zero);
                    ServiceResultException? activeRemoval = Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await configurations.RemoveConfigurationAsync(configuration, ct).ConfigureAwait(false));
                    Assert.That(activeRemoval!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                    await runtime.PublishResultAsync(new ResultDataType
                    {
                        ResultId = new ResultIdDataType { Id = "wire-download" },
                        ResultState = 77,
                        InternalConfigurationId = configuration,
                        ResultContent = [Variant.From(123)],
                        EncodingMask = (uint)ResultDataTypeFields.ResultContent
                    }, ByteString.From(new byte[] { 5, 4, 3, 2, 1 }), ct).ConfigureAwait(false);
                    using var download = new MemoryStream();
                    await systemClient.DownloadResultAsync(
                        new ResultIdDataType { Id = "wire-download" }, download, ct).ConfigureAwait(false);
                    Assert.That(download.ToArray(), Is.EqualTo(new byte[] { 5, 4, 3, 2, 1 }));
                    ResultManagementTypeClient results = await systemClient.ResultsAsync(ct).ConfigureAwait(false);
                    (bool hasTransferableDataOnFile, uint resultHandle, bool isPartial, bool isSimulated,
                        int resultState, MeasIdDataType measId, PartIdDataType partId,
                        RecipeIdExternalDataType externalRecipeId, RecipeIdInternalDataType internalRecipeId,
                        ProductIdDataType productId, ConfigurationIdDataType externalConfigurationId,
                        ConfigurationIdDataType internalConfigurationId, JobIdDataType jobId, DateTimeUtc creationTime,
                        ProcessingTimesDataType processingTimes, ArrayOf<Variant> resultContent,
                        int error) components = await results.GetResultComponentsByIdAsync(
                        new ResultIdDataType { Id = "wire-download" }, 0, ct).ConfigureAwait(false);
                    Assert.That(components.error, Is.Zero);
                    Assert.That(components.resultState, Is.EqualTo(77));
                    Assert.That(components.hasTransferableDataOnFile, Is.True);
                    Assert.That(components.resultContent[0].TryGetValue(out int value), Is.True);
                    Assert.That(value, Is.EqualTo(123));
                    Assert.That(await results.ReleaseResultHandleAsync(components.resultHandle, ct)
                        .ConfigureAwait(false), Is.Zero);
                    SafetyStateManagementTypeClient safety = await systemClient.SafetyAsync(ct).ConfigureAwait(false);
                    Assert.That(await safety.ReportSafetyStateAsync(true, "test interlock", ct)
                        .ConfigureAwait(false), Is.Zero);
                    Assert.That(runtime.State.SafetyStateManagement!.VisionSafetyTriggered!.Value, Is.True);
                    MachineVisionAssetSnapshot snapshot = await client.ReadAssetAsync(asset.State.NodeId,
                        cancellationToken: ct).ConfigureAwait(false);
                    Assert.That(snapshot.Values.Count, Is.GreaterThan(0));
                    MachineVisionCapabilities capabilities = await client.ReadCapabilitiesAsync(
                        runtime.State.NodeId, ct).ConfigureAwait(false);
                    Assert.That(capabilities.SupportsControl, Is.True);
                    Assert.That(capabilities.SupportsAssets, Is.True);
                    Assert.That(capabilities.Interfaces.ToArray(), Has.Some.Matches<MachineVisionInterfaceCapability>(
                        member => member.Path == "RecipeManagement/RecipeTransfer" && !member.NodeId.IsNull));
                    MachineVisionStatusSnapshot status = await client.ReadStatusAsync(runtime.State.NodeId, ct)
                        .ConfigureAwait(false);
                    Assert.That(status.Values.Count, Is.EqualTo(5));
                    Assert.That(status.Values[3].Value.WrappedValue.TryGetValue(out bool safetyTriggered), Is.True);
                    Assert.That(safetyTriggered, Is.True);
                    MachineVisionRelationshipPage relations = await client.ReadRelationshipsAsync(
                        asset.State.NodeId, MachineVisionRelationship.SameEntity, cancellationToken: ct)
                        .ConfigureAwait(false);
                    Assert.That(relations.IsComplete, Is.True);
                    Assert.That(relations.References.Count, Is.EqualTo(1));
                    Assert.That(ExpandedNodeId.ToNodeId(relations.References[0].NodeId, session.NamespaceUris),
                        Is.EqualTo(runtime.State.NodeId));

                    await runtime.ReportErrorAsync("wire-observation", new LocalizedText("Camera unavailable"),
                        true, persistent: true, cancellationToken: ct).ConfigureAwait(false);
                    Assert.That(session.TryGetSubscriptionManager(out ISubscriptionManager? subscriptions), Is.True);
                    int existingSubscriptions = subscriptions!.Count;
                    MachineVisionObservation observation = await client.ObserveAsync(
                        ObjectIds.Server,
                        new MachineVisionObservationOptions
                        {
                            Mode = MachineVisionObservationMode.Conditions,
                            RefreshConditions = true,
                            MaximumNotifications = 1
                        }, ct).ConfigureAwait(false);
                    Assert.That(observation.Events.Count, Is.EqualTo(1));
                    MachineVisionObservedEvent conditionEvent = observation.Events[0];
                    Assert.That(conditionEvent.ConditionId.IsNull, Is.False);
                    Assert.That(conditionEvent.EventId.Length, Is.GreaterThan(0));
                    Assert.That(conditionEvent.Decoded, Is.InstanceOf<VisionPersistentErrorConditionTypeRecord>());
                    Assert.That(subscriptions.Count, Is.EqualTo(existingSubscriptions));
                    var condition = new AcknowledgeableConditionTypeClient(
                        session, conditionEvent.ConditionId, visionClient.Telemetry);
                    await condition.AcknowledgeAsync(conditionEvent.EventId, new LocalizedText("Observed"), ct)
                        .ConfigureAwait(false);
                    await session.DefaultStreaming.DisposeAsync().ConfigureAwait(false);
                    await session.CloseAsync(ct).ConfigureAwait(false);
                }
            }
            finally
            {
                await server.StopAsync(CancellationToken.None).ConfigureAwait(false);
                server.Dispose();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }
    }
}
