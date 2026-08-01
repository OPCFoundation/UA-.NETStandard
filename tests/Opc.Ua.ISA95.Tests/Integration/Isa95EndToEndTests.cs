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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.ISA95.Client;
using Opc.Ua.ISA95.Server;
using Opc.Ua.ISA95.Server.Builders;
using Opc.Ua.ISA95.Server.Hosting;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.ISA95.Tests.Providers;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using ClientSession = Opc.Ua.Client.ISession;
using ClientSubscriptionManager =
    Opc.Ua.Client.Subscriptions.ISubscriptionManager;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.Tests.Integration
{
    [TestFixture]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class Isa95EndToEndTests
    {
        [Test]
        public async Task ServerFixtureAndTypedClientExerciseIsa95Async()
        {
            string testRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(Isa95EndToEndTests),
                Guid.NewGuid().ToString("N"));
            using var locationProvider = new InMemoryGeoLocationProvider();
            locationProvider.Update(
                "plant",
                new GeoPosition(47.3769, 8.5417, EpsgCode: 4326));
            using var jobProvider = new InMemoryIsa95JobControlProvider();
            var statusSource = new TrackingStatusSource(jobProvider);
            Isa95GeoSpatialLocationBinding? locationBinding = null;
            NodeId locationNodeId = NodeId.Null;
            NodeId subtypeEndpointFolderId = NodeId.Null;
            var options = new Isa95ServerOptions();
            var providers = new Isa95ServerProviders
            {
                JobOrderReceiverV1 = jobProvider,
                JobResponseProviderV1 = jobProvider,
                JobResponseReceiverV1 = jobProvider,
                JobOrderReceiverV2 = jobProvider,
                JobResponseProviderV2 = jobProvider,
                JobResponseReceiverV2 = jobProvider,
                JobStatusSourceV2 = statusSource,
                JobOrderCatalog = jobProvider,
                JobOrderCatalogChangeSource = jobProvider
            };
            var configurator = new DelegateModelConfigurator(async (model, ct) =>
            {
                PersonnelClassState personnelClass =
                    await model.CreatePersonnelClassAsync(
                        model.Root,
                        "Operators",
                        ct).ConfigureAwait(false);
                PersonState person = await model.CreatePersonAsync(
                    model.Root,
                    "Operator-1",
                    ct).ConfigureAwait(false);
                model.DefinedByPersonnelClass(person, personnelClass);

                EquipmentClassState equipmentClass =
                    await model.CreateEquipmentClassAsync(
                        model.Root,
                        "Reactors",
                        ct).ConfigureAwait(false);
                EquipmentState equipment = await model.CreateEquipmentAsync(
                    model.Root,
                    "Reactor-1",
                    ct).ConfigureAwait(false);
                model.DefinedByEquipmentClass(equipment, equipmentClass);

                PhysicalAssetState asset =
                    await model.CreatePhysicalAssetAsync(
                        model.Root,
                        "Vessel-1",
                        ct).ConfigureAwait(false);
                MaterialClassState materialClass =
                    await model.CreateMaterialClassAsync(
                        model.Root,
                        "Feedstock",
                        ct).ConfigureAwait(false);
                MaterialDefinitionState definition =
                    await model.CreateMaterialDefinitionAsync(
                        model.Root,
                        "Feedstock-A",
                        ct).ConfigureAwait(false);
                model.DefinedByMaterialClass(definition, materialClass);
                MaterialLotState lot = await model.CreateMaterialLotAsync(
                    model.Root,
                    "Lot-1",
                    ct).ConfigureAwait(false);
                model.DefinedByMaterialDefinition(lot, definition);

                locationBinding = await model.CreateGeoSpatialLocationAsync(
                    model.Root,
                    "PlantLocation",
                    locationProvider,
                    "plant",
                    cancellationToken: ct).ConfigureAwait(false);
                locationNodeId = locationBinding.State.NodeId;
                PhysicalAssetPropertyState locationProperty =
                    await model.AddPropertyAsync(
                        asset,
                        "LocationReference",
                        cancellationToken: ct).ConfigureAwait(false);
                model.LocatedIn(locationProperty, locationBinding.State);

                ushort namespaceIndex = model.Root.NodeId.NamespaceIndex;
                var subtypeEndpointFolder = new FolderState(model.Root)
                {
                    NodeId = new NodeId("SubtypeEndpoints", namespaceIndex),
                    BrowseName = new QualifiedName(
                        "SubtypeEndpoints",
                        namespaceIndex),
                    DisplayName = new LocalizedText("Subtype Endpoints"),
                    TypeDefinitionId = Ua.ObjectTypeIds.FolderType,
                    ReferenceTypeId = Ua.ReferenceTypeIds.Organizes
                };
                model.Root.AddChild(subtypeEndpointFolder);
                await model.RegisterAsync(subtypeEndpointFolder, ct)
                    .ConfigureAwait(false);
                subtypeEndpointFolderId = subtypeEndpointFolder.NodeId;

                var responseReceiverSubtype = new BaseObjectTypeState
                {
                    NodeId = new NodeId(
                        "DerivedJobResponseReceiverType",
                        namespaceIndex),
                    BrowseName = new QualifiedName(
                        "DerivedJobResponseReceiverType",
                        namespaceIndex),
                    DisplayName = new LocalizedText(
                        "Derived Job Response Receiver Type"),
                    SuperTypeId = ExpandedNodeId.ToNodeId(
                        V2.ObjectTypeIds.ISA95JobResponseReceiverObjectType,
                        model.Context.NamespaceUris),
                    IsPartOfTypeHierarchy = true
                };
                await model.RegisterAsync(responseReceiverSubtype, ct)
                    .ConfigureAwait(false);

                var subtypeEndpoint = new BaseObjectState(subtypeEndpointFolder)
                {
                    NodeId = new NodeId(
                        "DerivedJobResponseReceiver",
                        namespaceIndex),
                    BrowseName = new QualifiedName(
                        "DerivedJobResponseReceiver",
                        namespaceIndex),
                    DisplayName = new LocalizedText(
                        "Derived Job Response Receiver"),
                    TypeDefinitionId = responseReceiverSubtype.NodeId,
                    ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent
                };
                subtypeEndpointFolder.AddChild(subtypeEndpoint);
                await model.RegisterAsync(subtypeEndpoint, ct)
                    .ConfigureAwait(false);
            });
            var serverFixture = new ServerFixture<Isa95IntegrationServer>(
                telemetry => new Isa95IntegrationServer(
                    telemetry,
                    options,
                    providers,
                    [configurator]))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var clientFixture = new ClientFixture(telemetry)
            {
                OperationTimeout = 30_000,
                SessionTimeout = 60_000
            };
            clientFixture.UseSubscriptionEngineFactory(
                DefaultSubscriptionEngineFactory.Instance);
            ClientSession? session = null;

            try
            {
                await serverFixture.LoadConfigurationAsync(testRoot)
                    .ConfigureAwait(false);
                await serverFixture.StartAsync().ConfigureAwait(false);
                Assert.That(
                    await WaitForAsync(
                        () => statusSource.IsSubscribed,
                        TimeSpan.FromSeconds(10),
                        CancellationToken.None).ConfigureAwait(false),
                    Is.True);
                Uri endpointUrl = new(
                    $"{Utils.UriSchemeOpcTcp}://localhost:{serverFixture.Port}");

                await clientFixture.LoadClientConfigurationAsync(
                    testRoot,
                    "Isa95IntegrationClient").ConfigureAwait(false);
                session = await clientFixture.ConnectAsync(
                    endpointUrl,
                    SecurityPolicies.None).ConfigureAwait(false);

                using var testCts =
                    new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var client = new Isa95Client(session, telemetry);
                await AssertCommonModelAsync(
                    client,
                    session,
                    locationNodeId,
                    testCts.Token).ConfigureAwait(false);
                await AssertSubtypeDiscoveryAsync(
                    client,
                    subtypeEndpointFolderId,
                    testCts.Token).ConfigureAwait(false);
                await ExerciseJobControlAsync(
                    client,
                    jobProvider,
                    statusSource,
                    serverFixture.Server.Manager!,
                    testCts.Token).ConfigureAwait(false);
            }
            finally
            {
                if (session != null)
                {
                    using var closeCts =
                        new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await session.CloseAsync(
                        5_000,
                        closeChannel: true,
                        closeCts.Token).ConfigureAwait(false);
                    session.Dispose();
                }
                clientFixture.Dispose();
                locationBinding?.Dispose();
                await serverFixture.StopAsync().ConfigureAwait(false);
                if (Directory.Exists(testRoot))
                {
                    Directory.Delete(testRoot, recursive: true);
                }
            }
        }

        private static async Task AssertSubtypeDiscoveryAsync(
            Isa95Client client,
            NodeId folderId,
            CancellationToken ct)
        {
            Isa95JobControlDiscovery discovery =
                await client.DiscoverJobControlAsync(folderId, ct)
                    .ConfigureAwait(false);

            Assert.That(discovery.V1Endpoints, Is.Empty);
            Assert.That(discovery.V2Endpoints, Has.Count.EqualTo(1));
            Assert.That(
                discovery.V2Endpoints[0].Facet,
                Is.EqualTo(Isa95JobControlFacet.JobResponseReceiver));
        }

        private static async Task AssertCommonModelAsync(
            Isa95Client client,
            ClientSession session,
            NodeId locationNodeId,
            CancellationToken ct)
        {
            int namespaceIndex = session.NamespaceUris.GetIndex(kInstanceNamespaceUri);
            Assert.That(namespaceIndex, Is.GreaterThanOrEqualTo(0));
            var rootNodeId = new NodeId(kRootBrowseName, (ushort)namespaceIndex);

            ArrayOf<Isa95CommonObjectEntry> common =
                await client.DiscoverCommonObjectsAsync(rootNodeId, ct: ct)
                    .ConfigureAwait(false);
            (ArrayOf<ArrayOf<ReferenceDescription>> descriptions, _) =
                await session.ManagedBrowseAsync(
                    requestHeader: null,
                    view: null,
                    nodesToBrowse: [rootNodeId],
                    maxResultsToReturn: 0,
                    browseDirection: BrowseDirection.Forward,
                    referenceTypeId: default,
                    includeSubtypes: true,
                    nodeClassMask: 0,
                    ct: ct).ConfigureAwait(false);
            var rootChildren = new List<string>();
            if (descriptions.Count > 0)
            {
                foreach (ReferenceDescription reference in descriptions[0])
                {
                    rootChildren.Add(
                        $"{reference.BrowseName}:{reference.NodeClass}:" +
                        $"{reference.TypeDefinition}");
                }
            }
            Assert.That(
                common,
                Is.Not.Empty,
                $"Root children: {string.Join(", ", rootChildren)}");
            var kinds = new HashSet<Isa95CommonObjectKind>();
            foreach (Isa95CommonObjectEntry entry in common)
            {
                kinds.Add(entry.Kind);
            }
            Assert.That(kinds, Does.Contain(Isa95CommonObjectKind.Person));
            Assert.That(kinds, Does.Contain(Isa95CommonObjectKind.Equipment));
            Assert.That(kinds, Does.Contain(Isa95CommonObjectKind.PhysicalAsset));
            Assert.That(kinds, Does.Contain(Isa95CommonObjectKind.MaterialLot));

            Assert.That(locationNodeId.IsNull, Is.False);
            DataValue location = await session.ReadValueAsync(locationNodeId, ct)
                .ConfigureAwait(false);
            Assert.That(location.StatusCode, Is.EqualTo(StatusCodes.Good));
            // OPC 10030 declares GeoSpatialLocationType with ValueRank
            // OneOrMoreDimensions, so the value is an array of literals.
            Assert.That(
                location.WrappedValue.TryGetValue(out ArrayOf<string> literals),
                Is.True);
            Assert.That(literals.Count, Is.EqualTo(1));
            Assert.That(
                literals[0],
                Is.EqualTo("SRID=4326;POINT (8.5417 47.3769)"));
        }

        private static async Task ExerciseJobControlAsync(
            Isa95Client client,
            InMemoryIsa95JobControlProvider execution,
            TrackingStatusSource statusSource,
            Isa95NodeManager manager,
            CancellationToken ct)
        {
            Isa95JobControlDiscovery discovery =
                await client.DiscoverJobControlAsync(ct).ConfigureAwait(false);
            Assert.That(discovery.V1Endpoints, Has.Count.EqualTo(3));
            Assert.That(discovery.V2Endpoints, Has.Count.EqualTo(3));

            Isa95JobControlEndpoint v1Order =
                GetEndpoint(discovery.V1Endpoints, Isa95JobControlFacet.JobOrderReceiver);
            Isa95JobControlEndpoint v1Provider =
                GetEndpoint(discovery.V1Endpoints, Isa95JobControlFacet.JobResponseProvider);
            Isa95JobControlEndpoint v1Receiver =
                GetEndpoint(discovery.V1Endpoints, Isa95JobControlFacet.JobResponseReceiver);
            Isa95JobControlEndpoint v2Order =
                GetEndpoint(discovery.V2Endpoints, Isa95JobControlFacet.JobOrderReceiver);
            Isa95JobControlEndpoint v2Provider =
                GetEndpoint(discovery.V2Endpoints, Isa95JobControlFacet.JobResponseProvider);
            Isa95JobControlEndpoint v2Receiver =
                GetEndpoint(discovery.V2Endpoints, Isa95JobControlFacet.JobResponseReceiver);
            await AssertMetadataCurrentStateAsync(
                client.Session,
                v2Order.NodeId,
                ct).ConfigureAwait(false);
            Isa95JobControlV1Client v1 = client.CreateJobControlV1Client(
                v1Order.NodeId,
                v1Provider.NodeId,
                v1Receiver.NodeId);
            Isa95JobControlV2Client v2 = client.CreateJobControlV2Client(
                v2Order.NodeId,
                v2Provider.NodeId,
                v2Receiver.NodeId);

            Assert.That(
                client.Session.TryGetSubscriptionManager(
                    out ClientSubscriptionManager? subscriptionManager),
                Is.True);
            ArrayOf<LocalizedText> auditComment =
            [
                new LocalizedText("en-US", "Created by integration test")
            ];
            V2.ISA95JobOrderStatusEventTypeRecord statusEvent =
                await CaptureStatusEventAsync(
                    v2,
                    v2Provider.NodeId,
                    subscriptionManager!,
                    statusSource,
                    manager,
                    () => v2.StoreAsync(
                        Isa95TestData.V2Order("v2-main"),
                        auditComment,
                        ct),
                    ct).ConfigureAwait(false);
            Assert.That(statusEvent.JobOrder.JobOrderID, Is.EqualTo("v2-main"));
            Assert.That(statusEvent.JobState, Is.Not.Empty);
            Assert.That(statusSource.LastComment, Has.Count.EqualTo(1));
            Assert.That(
                statusSource.LastComment[0].Text,
                Is.EqualTo("Created by integration test"));
            var abstractStatusEventType = ExpandedNodeId.ToNodeId(
                V2.ObjectTypeIds.ISA95JobOrderStatusEventType,
                client.Session.NamespaceUris);
            Assert.That(statusEvent.EventType, Is.Not.EqualTo(abstractStatusEventType));
            Assert.That(
                await client.Session.NodeCache.IsTypeOfAsync(
                    statusEvent.EventType,
                    abstractStatusEventType,
                    ct).ConfigureAwait(false),
                Is.True);

            V2.ISA95JobOrderDataType externalUpdate =
                Isa95TestData.V2Order("v2-main");
            externalUpdate.Priority = 77;
            Isa95JobOrderReceiptV2 externalUpdateResult =
                await execution.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.Update,
                    externalUpdate,
                    [new LocalizedText("en-US", "Updated externally")],
                    ct).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(externalUpdateResult.Result), Is.True);
            NodeId jobOrderListId = await FindChildAsync(
                client.Session,
                v2Order.NodeId,
                V2.BrowseNames.JobOrderList,
                V2.Namespaces.ISA95JobControlV2,
                ct).ConfigureAwait(false);
            Assert.That(
                await WaitForJobOrderPriorityAsync(
                    client.Session,
                    jobOrderListId,
                    "v2-main",
                    77,
                    ct).ConfigureAwait(false),
                Is.True);

            AssertSuccess(await v2.UpdateAsync(Isa95TestData.V2Order("v2-main"), ct: ct)
                .ConfigureAwait(false));
            AssertSuccess(await v2.StartAsync("v2-main", ct: ct).ConfigureAwait(false));
            AssertSuccess(await v2.RevokeStartAsync("v2-main", ct: ct).ConfigureAwait(false));
            AssertSuccess(await v2.StartAsync("v2-main", ct: ct).ConfigureAwait(false));

            Isa95JobOrderReceiptV2 begin = await execution.TransitionAsync(
                "v2-main",
                Isa95JobExecutionTransition.BeginExecution,
                ct).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(begin.Result), Is.True);
            AssertSuccess(await v2.PauseAsync("v2-main", ct: ct).ConfigureAwait(false));
            Assert.That(
                await WaitForJobOrderSubstateNamespaceAsync(
                    client.Session,
                    jobOrderListId,
                    "v2-main",
                    (ushort)client.Session.NamespaceUris.GetIndex(
                        V2.Namespaces.ISA95JobControlV2),
                    ct).ConfigureAwait(false),
                Is.True);
            AssertSuccess(await v2.ResumeAsync("v2-main", ct: ct).ConfigureAwait(false));
            AssertSuccess(await v2.StopAsync("v2-main", ct: ct).ConfigureAwait(false));
            AssertSuccess(await v2.ClearAsync("v2-main", ct: ct).ConfigureAwait(false));

            AssertSuccess(await v2.StoreAsync(Isa95TestData.V2Order("v2-cancel"), ct: ct)
                .ConfigureAwait(false));
            AssertSuccess(await v2.CancelAsync("v2-cancel", ct: ct).ConfigureAwait(false));

            AssertSuccess(await v2.StoreAndStartAsync(
                Isa95TestData.V2Order("v2-abort"),
                ct: ct).ConfigureAwait(false));
            Isa95JobOrderReceiptV2 abortBegin = await execution.TransitionAsync(
                "v2-abort",
                Isa95JobExecutionTransition.BeginExecution,
                ct).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(abortBegin.Result), Is.True);
            AssertSuccess(await v2.AbortAsync("v2-abort", ct: ct).ConfigureAwait(false));

            ulong unknown = await v2.StartAsync("missing", ct: ct).ConfigureAwait(false);
            Assert.That(unknown, Is.EqualTo(Isa95JobReturnStatus.UnknownJobOrderId));

            AssertSuccess(await v1.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Store,
                Isa95TestData.V1Order("cross-version"),
                ct).ConfigureAwait(false));
            AssertSuccess(await v2.StartAsync("cross-version", ct: ct).ConfigureAwait(false));
            ulong invalidCommand = await v1.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Undefined,
                Isa95TestData.V1Order("invalid"),
                ct).ConfigureAwait(false);
            Assert.That(invalidCommand, Is.EqualTo(Isa95JobReturnStatus.InvalidCommand));

            AssertSuccess(await v1.ReceiveJobResponseAsync(
                Isa95TestData.V1Response("v1-response", "response-from-v1"),
                ct).ConfigureAwait(false));
            (V2.ISA95JobResponseDataType fromV1, ulong fromV1Status) =
                await v2.RequestJobResponseByJobOrderIdAsync(
                    "response-from-v1",
                    ct).ConfigureAwait(false);
            AssertSuccess(fromV1Status);
            Assert.That(fromV1.JobResponseID, Is.EqualTo("v1-response"));

            V2.ISA95JobResponseDataType response =
                Isa95TestData.V2Response("v2-response", "response-from-v2");
            AssertSuccess(await v2.ReceiveJobResponseAsync(response, ct)
                .ConfigureAwait(false));
            (ArrayOf<V1.ISA95JobResponseDataType> fromV2, ulong fromV2Status) =
                await v1.RequestJobResponseAsync(
                    "response-from-v2",
                    V1.ISA95JobOrderStateEnum.Undefined,
                    ct).ConfigureAwait(false);
            AssertSuccess(fromV2Status);
            bool foundV2Response = false;
            foreach (V1.ISA95JobResponseDataType item in fromV2)
            {
                foundV2Response |= item.ID == "v2-response";
            }
            Assert.That(foundV2Response, Is.True);

            (ArrayOf<V2.ISA95JobResponseDataType> byState, ulong byStateStatus) =
                await v2.RequestJobResponseByJobOrderStateAsync(
                    response.JobState,
                    ct).ConfigureAwait(false);
            AssertSuccess(byStateStatus);
            bool foundByState = false;
            foreach (V2.ISA95JobResponseDataType item in byState)
            {
                foundByState |= item.JobResponseID == "v2-response";
            }
            Assert.That(foundByState, Is.True);
        }

        private static Isa95JobControlEndpoint GetEndpoint(
            ArrayOf<Isa95JobControlEndpoint> endpoints,
            Isa95JobControlFacet facet)
        {
            foreach (Isa95JobControlEndpoint endpoint in endpoints)
            {
                if (endpoint.Facet == facet)
                {
                    return endpoint;
                }
            }
            throw new InvalidOperationException(
                $"The {facet} endpoint was not discovered.");
        }

        private static async Task<V2.ISA95JobOrderStatusEventTypeRecord>
            CaptureStatusEventAsync(
                Isa95JobControlV2Client client,
                NodeId notifierId,
                ClientSubscriptionManager subscriptionManager,
                TrackingStatusSource statusSource,
                Isa95NodeManager manager,
                Func<ValueTask<ulong>> operation,
                CancellationToken ct)
        {
            await using var streaming = new StreamingSubscription(
                subscriptionManager);
            using var eventCts =
                CancellationTokenSource.CreateLinkedTokenSource(ct);
            eventCts.CancelAfter(TimeSpan.FromSeconds(15));
            IAsyncEnumerator<V2.ISA95JobOrderStatusEventTypeRecord> enumerator =
                client.SubscribeJobOrderStatusEventsAsync(
                    streaming,
                    notifierId,
                    ct: eventCts.Token).GetAsyncEnumerator(eventCts.Token);
            try
            {
                Task<bool> eventPending = enumerator.MoveNextAsync().AsTask();
                bool subscriptionReady = await WaitForAsync(
                    () => subscriptionManager.Items.Any(subscription =>
                        subscription.Created &&
                        subscription.MonitoredItems.Items.Any(item =>
                            item.Created &&
                            ServiceResult.IsGood(item.Error))),
                    TimeSpan.FromSeconds(15),
                    ct).ConfigureAwait(false);
                Assert.That(subscriptionReady, Is.True);

                BaseObjectState notifier =
                    manager.FindPredefinedNode<BaseObjectState>(notifierId) ??
                    throw new InvalidOperationException(
                        "The V2 response-provider notifier is missing.");
                Assert.That(notifier.AreEventsMonitored, Is.True);
                int reportCount = 0;
                notifier.OnReportEvent += OnReportEvent;
                try
                {
                    AssertSuccess(await operation().ConfigureAwait(false));
                    Assert.That(
                        await WaitForAsync(
                            () => statusSource.NotificationCount > 0,
                            TimeSpan.FromSeconds(5),
                            ct).ConfigureAwait(false),
                        Is.True);
                    Assert.That(
                        await WaitForAsync(
                            () => Volatile.Read(ref reportCount) > 0,
                            TimeSpan.FromSeconds(5),
                            ct).ConfigureAwait(false),
                        Is.True);
                    Assert.That(await eventPending.ConfigureAwait(false), Is.True);
                    return enumerator.Current;
                }
                finally
                {
                    notifier.OnReportEvent -= OnReportEvent;
                }

                void OnReportEvent(
                    ISystemContext context,
                    NodeState node,
                    IFilterTarget e) => Interlocked.Increment(ref reportCount);
            }
            finally
            {
                await streaming.DisposeAsync().ConfigureAwait(false);
                while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                }
                await enumerator.DisposeAsync().ConfigureAwait(false);
                eventCts.Cancel();
            }
        }

        private static void AssertSuccess(ulong returnStatus)
        {
            Assert.That(returnStatus, Is.EqualTo(Isa95JobReturnStatus.Success));
        }

        private static async Task<NodeId> FindChildAsync(
            ClientSession session,
            NodeId parentId,
            string browseName,
            string namespaceUri,
            CancellationToken ct)
        {
            (ArrayOf<ArrayOf<ReferenceDescription>> descriptions, ArrayOf<ServiceResult> errors) =
                await session.ManagedBrowseAsync(
                    requestHeader: null,
                    view: null,
                    nodesToBrowse: [parentId],
                    maxResultsToReturn: 0,
                    browseDirection: BrowseDirection.Forward,
                    referenceTypeId: Ua.ReferenceTypeIds.HierarchicalReferences,
                    includeSubtypes: true,
                    nodeClassMask: (uint)NodeClass.Variable,
                    ct: ct).ConfigureAwait(false);
            for (int ii = 0; ii < errors.Count; ii++)
            {
                Assert.That(ServiceResult.IsGood(errors[ii]), Is.True);
            }
            Assert.That(descriptions, Has.Count.EqualTo(1));

            ushort namespaceIndex = (ushort)session.NamespaceUris.GetIndex(
                namespaceUri);
            ArrayOf<ReferenceDescription> references = descriptions[0];
            for (int ii = 0; ii < references.Count; ii++)
            {
                ReferenceDescription reference = references[ii];
                if (reference.BrowseName ==
                    new QualifiedName(browseName, namespaceIndex))
                {
                    return ExpandedNodeId.ToNodeId(
                        reference.NodeId,
                        session.NamespaceUris);
                }
            }
            Assert.Fail($"The child '{browseName}' was not found.");
            return NodeId.Null;
        }

        private static async Task AssertMetadataCurrentStateAsync(
            ClientSession session,
            NodeId receiverId,
            CancellationToken ct)
        {
            NodeId currentStateId = await FindChildAsync(
                session,
                receiverId,
                Ua.BrowseNames.CurrentState,
                Ua.Namespaces.OpcUa,
                ct).ConfigureAwait(false);
            ReadResponse response = await session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead:
                [
                    new ReadValueId
                    {
                        NodeId = currentStateId,
                        AttributeId = Attributes.Value
                    }
                ],
                ct: ct).ConfigureAwait(false);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            DataValue currentState = response.Results[0];
            Assert.That(StatusCode.IsBad(currentState.StatusCode), Is.True);
        }

        private static async Task<bool> WaitForJobOrderPriorityAsync(
            ClientSession session,
            NodeId jobOrderListId,
            string jobOrderId,
            ushort priority,
            CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
            {
                DataValue value = await session.ReadValueAsync(
                    jobOrderListId,
                    ct).ConfigureAwait(false);
                if (StatusCode.IsGood(value.StatusCode) &&
                    value.WrappedValue.TryGetStructure(
                        out ArrayOf<V2.ISA95JobOrderAndStateDataType> orders))
                {
                    for (int ii = 0; ii < orders.Count; ii++)
                    {
                        V2.ISA95JobOrderAndStateDataType order = orders[ii];
                        if (string.Equals(
                            order.JobOrder.JobOrderID,
                            jobOrderId,
                            StringComparison.Ordinal) &&
                            order.JobOrder.Priority == priority)
                        {
                            return true;
                        }
                    }
                }
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
            return false;
        }

        private static async Task<bool> WaitForJobOrderSubstateNamespaceAsync(
            ClientSession session,
            NodeId jobOrderListId,
            string jobOrderId,
            ushort namespaceIndex,
            CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
            {
                DataValue value = await session.ReadValueAsync(
                    jobOrderListId,
                    ct).ConfigureAwait(false);
                if (StatusCode.IsGood(value.StatusCode) &&
                    value.WrappedValue.TryGetStructure(
                        out ArrayOf<V2.ISA95JobOrderAndStateDataType> orders))
                {
                    for (int ii = 0; ii < orders.Count; ii++)
                    {
                        V2.ISA95JobOrderAndStateDataType order = orders[ii];
                        if (string.Equals(
                                order.JobOrder.JobOrderID,
                                jobOrderId,
                                StringComparison.Ordinal) &&
                            order.State.Count > 1 &&
                            order.State[1].BrowsePath.Elements.Count > 0)
                        {
                            QualifiedName targetName =
                                order.State[1].BrowsePath.Elements[0].TargetName;
                            return targetName.NamespaceIndex == namespaceIndex &&
                                string.Equals(
                                    targetName.Name,
                                    V2.BrowseNames.InterruptedSubstates,
                                    StringComparison.Ordinal);
                        }
                    }
                }
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
            return false;
        }

        private static async Task<bool> WaitForAsync(
            Func<bool> condition,
            TimeSpan timeout,
            CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                ct.ThrowIfCancellationRequested();
                if (condition())
                {
                    return true;
                }
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
            return condition();
        }

        private sealed class Isa95IntegrationServer : StandardServer
        {
            public Isa95IntegrationServer(
                ITelemetryContext telemetry,
                Isa95ServerOptions options,
                Isa95ServerProviders providers,
                IReadOnlyList<IIsa95ModelConfigurator> configurators)
                : base(telemetry)
            {
                m_options = options;
                m_providers = providers;
                m_configurators = configurators;
            }

            protected override ValueTask<IMasterNodeManager>
                CreateMasterNodeManagerAsync(
                    IServerInternal server,
                    ApplicationConfiguration configuration,
                    CancellationToken cancellationToken = default)
            {
                Manager = new Isa95NodeManager(
                    server,
                    configuration,
                    m_options,
                    m_providers,
                    m_configurators);
                IMasterNodeManager master = new MasterNodeManager(
                    server,
                    configuration,
                    null,
                    [Manager]);
                return new ValueTask<IMasterNodeManager>(master);
            }

            public Isa95NodeManager? Manager { get; private set; }

            private readonly Isa95ServerOptions m_options;
            private readonly Isa95ServerProviders m_providers;
            private readonly IReadOnlyList<IIsa95ModelConfigurator> m_configurators;
        }

        private sealed class DelegateModelConfigurator : IIsa95ModelConfigurator
        {
            public DelegateModelConfigurator(
                Func<IIsa95ModelBuilder, CancellationToken, ValueTask> configure)
            {
                m_configure = configure;
            }

            public ValueTask ConfigureAsync(
                IIsa95ModelBuilder builder,
                CancellationToken cancellationToken)
            {
                return m_configure(builder, cancellationToken);
            }

            private readonly Func<
                IIsa95ModelBuilder,
                CancellationToken,
                ValueTask> m_configure;
        }

        private sealed class TrackingStatusSource : IIsa95JobStatusSourceV2
        {
            public TrackingStatusSource(IIsa95JobStatusSourceV2 inner)
            {
                m_inner = inner;
            }

            public bool IsSubscribed => Volatile.Read(ref m_isSubscribed) != 0;

            public int NotificationCount =>
                Volatile.Read(ref m_notificationCount);

            public ArrayOf<LocalizedText> LastComment
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_lastComment;
                    }
                }
            }

            public async IAsyncEnumerable<Isa95JobStatusNotificationV2>
                SubscribeAsync(
                    [EnumeratorCancellation]
                    CancellationToken cancellationToken = default)
            {
                Volatile.Write(ref m_isSubscribed, 1);
                await foreach (Isa95JobStatusNotificationV2 status in
                    m_inner.SubscribeAsync(cancellationToken)
                        .ConfigureAwait(false))
                {
                    lock (m_lock)
                    {
                        m_lastComment = status.Comment;
                    }
                    Interlocked.Increment(ref m_notificationCount);
                    yield return status;
                }
            }

            private readonly IIsa95JobStatusSourceV2 m_inner;
            private readonly Lock m_lock = new();
            private ArrayOf<LocalizedText> m_lastComment;
            private int m_isSubscribed;
            private int m_notificationCount;
        }

        private const string kInstanceNamespaceUri =
            "urn:opcfoundation:ua:isa95:server";

        private const string kRootBrowseName = "ISA95";
    }
}
