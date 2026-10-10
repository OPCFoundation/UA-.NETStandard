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
 *
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
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using WotConModel = Opc.Ua.WotCon;
using XRegistryModel = Opc.Ua.XRegistry;

namespace Opc.Ua.WotCon.Tests.RuntimeNodeSet
{
    public sealed partial class WotRegistryEventIntegrationTests
    {
        [SetUp]
        public void RecordEventExecutionRuntime()
        {
            using Process process = Process.GetCurrentProcess();
            TestContext.Out.WriteLine(
                $"WOT-R34-RUNTIME;pid={process.Id};framework={RuntimeInformation.FrameworkDescription};" +
                $"clr={Environment.Version};entry={typeof(WotRegistryEventIntegrationTests).Assembly.Location};" +
                $"core={typeof(object).Assembly.Location}");
        }

        [Test]
        public async Task ActivationNeverEmitsAbstractWotResourceEvent()
        {
            NamespaceTable namespaces = m_server.CurrentInstance.NamespaceUris;
            NodeId registryId = ExpandedNodeId.ToNodeId(WotConModel.ObjectIds.WoTRegistry, namespaces);
            NodeId completedType = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectTypeIds.WoTRefreshCompletedEventType, namespaces);
            NodeId abstractType = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectTypeIds.WoTResourceEventType, namespaces);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            uint subscriptionId = await CreateEventSubscriptionAsync(services, registryId).ConfigureAwait(false);
            try
            {
                await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "taxonomy-good",
                    VersionId = "v1",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = ByteString.From(SelectiveConverter.ValidTd("taxonomy-good"))
                }).ConfigureAwait(false);
                WotRefreshResult result = await m_coordinator.RefreshAsync(
                    new WotRefreshRequest { RequestId = "concrete-events" }).ConfigureAwait(false);
                Assert.That(result.Results.Single().LoadState, Is.EqualTo(WoTLoadStateEnum.Active));

                var observedTypes = new List<NodeId>();
                EventFieldList? completed = await CollectEventAsync(services, subscriptionId, fields =>
                {
                    NodeId eventType = EventTypeOf(fields);
                    observedTypes.Add(eventType);
                    return eventType == completedType &&
                        AsString(fields.EventFields[Field.RequestId]) == "concrete-events";
                }).ConfigureAwait(false);
                Assert.That(completed, Is.Not.Null, "Refresh completion is the positive observation barrier.");
                Assert.That(observedTypes, Does.Not.Contain(abstractType),
                    "The abstract WoTResourceEventType must never be instantiated for successful activation.");
            }
            finally
            {
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ValidationFailureUsesExactVersionAsSourceNode()
        {
            NamespaceTable namespaces = m_server.CurrentInstance.NamespaceUris;
            NodeId registryId = ExpandedNodeId.ToNodeId(WotConModel.ObjectIds.WoTRegistry, namespaces);
            NodeId versionCreatedType = ExpandedNodeId.ToNodeId(
                XRegistryModel.ObjectTypeIds.VersionCreatedEventType, namespaces);
            NodeId failureType = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectTypeIds.WoTValidationFailureEventType, namespaces);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            EventFilter filter = BuildWotEventFilterWithSourceNode();
            uint subscriptionId = await CreateEventSubscriptionAsync(services, registryId, filter)
                .ConfigureAwait(false);
            try
            {
                await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "bad-taxonomy",
                    VersionId = "rejected-version",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = ByteString.From(SelectiveConverter.ValidTd("bad-taxonomy"))
                }).ConfigureAwait(false);
                EventFieldList? created = await CollectEventAsync(services, subscriptionId,
                    fields => EventTypeOf(fields) == versionCreatedType &&
                        HasVersionSubject(fields, filter, "bad-taxonomy", "rejected-version"))
                    .ConfigureAwait(false);
                Assert.That(created, Is.Not.Null);
                Assert.That(FieldByBrowseName(created!, filter, Ua.BrowseNames.SourceNode)
                    .TryGetValue(out NodeId exactVersion), Is.True);
                Assert.That(exactVersion.IsNull, Is.False);
                NodeState? version = m_eventManager.Find(exactVersion);
                Assert.That(version, Is.InstanceOf<BaseInstanceState>());
                var instance = (BaseInstanceState)version!;
                Assert.That(instance.Parent, Is.Not.Null);
                Assert.That(instance.Parent!.BrowseName.Name, Is.EqualTo("Versions"),
                    "The oracle must identify an indexed exact Version, not the logical Resource.");

                WotMaterializationEventArgs? produced = null;
                m_coordinator.Event += (_, change) =>
                {
                    if (change.Kind == WotMaterializationEventKind.ValidationFailure &&
                        change.ResourceId == "bad-taxonomy")
                    {
                        produced = change;
                    }
                };
                await m_coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
                EventFieldList? failure = await CollectEventAsync(services, subscriptionId,
                    fields => EventTypeOf(fields) == failureType).ConfigureAwait(false);
                Assert.That(failure, Is.Not.Null);
                Assert.That(produced, Is.Not.Null);
                Assert.That(FieldByBrowseName(failure!, filter, Ua.BrowseNames.SourceNode)
                    .TryGetValue(out NodeId source), Is.True);
                Assert.That(source, Is.EqualTo(exactVersion),
                    "The concrete failure must originate at the exact failing Version.");
                Assert.That(AsString(failure!.EventFields[Field.VersionId]), Is.EqualTo("rejected-version"));
                Assert.That(failure.EventFields[Field.Phase].TryGetValue(out WoTPhaseEnum phase), Is.True);
                Assert.That(phase, Is.EqualTo(produced!.Phase));
                Assert.That(AsUInt32(failure.EventFields[Field.Generation]), Is.EqualTo(produced.Generation));
            }
            finally
            {
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RejectedCandidateFailureDoesNotNameTheServingVersion()
        {
            const string ResourceId = "versioned-failure";
            NamespaceTable namespaces = m_server.CurrentInstance.NamespaceUris;
            NodeId registryId = ExpandedNodeId.ToNodeId(WotConModel.ObjectIds.WoTRegistry, namespaces);
            NodeId createdType = ExpandedNodeId.ToNodeId(
                XRegistryModel.ObjectTypeIds.VersionCreatedEventType, namespaces);
            NodeId failureType = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectTypeIds.WoTValidationFailureEventType, namespaces);
            await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = ResourceId,
                VersionId = "serving",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(SelectiveConverter.ValidTd(ResourceId, "Serving version"))
            }).ConfigureAwait(false);
            WotRefreshResult initial = await m_coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
            Assert.That(initial.Results.Single().LoadState, Is.EqualTo(WoTLoadStateEnum.Active));

            var services = new ServerTestServices(m_server, m_secureChannelContext);
            EventFilter filter = BuildWotEventFilterWithSourceNode();
            uint subscriptionId = await CreateEventSubscriptionAsync(services, registryId, filter)
                .ConfigureAwait(false);
            try
            {
                ByteString rejected = ByteString.From(
                    SelectiveConverter.ValidTd(ResourceId, "Rejected candidate"));
                m_failureConverter.RejectedContent = rejected;
                await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = ResourceId,
                    VersionId = "rejected",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = rejected,
                    SetAsDefault = true
                }).ConfigureAwait(false);
                EventFieldList? created = await CollectEventAsync(services, subscriptionId,
                    fields => EventTypeOf(fields) == createdType &&
                        HasVersionSubject(fields, filter, ResourceId, "rejected")).ConfigureAwait(false);
                Assert.That(created, Is.Not.Null);
                Assert.That(FieldByBrowseName(created!, filter, Ua.BrowseNames.SourceNode)
                    .TryGetValue(out NodeId rejectedVersion), Is.True);
                WotResource before = m_registry.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions, ResourceId)!;
                Assert.That(before.DefaultVersionId, Is.EqualTo("rejected"));
                Assert.That(before.ActiveVersionId, Is.EqualTo("serving"));

                await m_coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
                EventFieldList? failure = await CollectEventAsync(services, subscriptionId,
                    fields => EventTypeOf(fields) == failureType).ConfigureAwait(false);
                Assert.That(failure, Is.Not.Null);
                Assert.That(FieldByBrowseName(failure!, filter, Ua.BrowseNames.SourceNode)
                    .TryGetValue(out NodeId source), Is.True);
                Assert.That(source, Is.EqualTo(rejectedVersion));
                Assert.That(AsString(failure!.EventFields[Field.VersionId]), Is.EqualTo("rejected"));
            }
            finally
            {
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConcreteFailureVersionAndSuccessfulTaxonomyCrossEncryptedTransport()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var client = new ClientFixture(false, false, telemetry);
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            var endpoint = new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}");
            using ISession session = await client.ConnectAsync(endpoint, SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            try
            {
                Assert.That(session.ConfiguredEndpoint.Description.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                NamespaceTable namespaces = session.NamespaceUris;
                NodeId registryId = ExpandedNodeId.ToNodeId(WotConModel.ObjectIds.WoTRegistry, namespaces);
                NodeId createdType = ExpandedNodeId.ToNodeId(
                    XRegistryModel.ObjectTypeIds.VersionCreatedEventType, namespaces);
                NodeId failureType = ExpandedNodeId.ToNodeId(
                    WotConModel.ObjectTypeIds.WoTValidationFailureEventType, namespaces);
                NodeId completedType = ExpandedNodeId.ToNodeId(
                    WotConModel.ObjectTypeIds.WoTRefreshCompletedEventType, namespaces);
                NodeId abstractType = ExpandedNodeId.ToNodeId(
                    WotConModel.ObjectTypeIds.WoTResourceEventType, namespaces);
                var services = new ClientTestServices(session, telemetry);
                var requestHeader = new RequestHeader();
                EventFilter filter = BuildWotEventFilterWithSourceNode();
                uint subscriptionId = await CreateEventSubscriptionAsync(
                    services, registryId, filter, requestHeader).ConfigureAwait(false);
                try
                {
                    const string ResourceId = "native-taxonomy";
                    await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
                    {
                        GroupId = WotRegistryGroups.ThingDescriptions,
                        ResourceId = ResourceId,
                        VersionId = "serving",
                        Kind = WoTDocumentKindEnum.ThingDescription,
                        Content = ByteString.From(SelectiveConverter.ValidTd(ResourceId, "Serving version"))
                    }).ConfigureAwait(false);
                    WotRefreshResult result = await m_coordinator.RefreshAsync(
                        new WotRefreshRequest { RequestId = "native-concrete-success" }).ConfigureAwait(false);
                    Assert.That(result.Results.Single().LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
                    var observed = new List<NodeId>();
                    EventFieldList? completed = await CollectEventAsync(services, subscriptionId, fields =>
                    {
                        NodeId type = EventTypeOf(fields);
                        observed.Add(type);
                        return type == completedType &&
                            AsString(fields.EventFields[Field.RequestId]) == "native-concrete-success";
                    }, requestHeader).ConfigureAwait(false);
                    Assert.That(completed, Is.Not.Null);
                    Assert.That(observed, Does.Not.Contain(abstractType));

                    ByteString rejected = ByteString.From(
                        SelectiveConverter.ValidTd(ResourceId, "Rejected version"));
                    m_failureConverter.RejectedContent = rejected;
                    await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
                    {
                        GroupId = WotRegistryGroups.ThingDescriptions,
                        ResourceId = ResourceId,
                        VersionId = "rejected",
                        Kind = WoTDocumentKindEnum.ThingDescription,
                        Content = rejected,
                        SetAsDefault = true
                    }).ConfigureAwait(false);
                    EventFieldList? created = await CollectEventAsync(services, subscriptionId,
                        fields => EventTypeOf(fields) == createdType &&
                            HasVersionSubject(fields, filter, ResourceId, "rejected"), requestHeader)
                        .ConfigureAwait(false);
                    Assert.That(created, Is.Not.Null);
                    Assert.That(FieldByBrowseName(created!, filter, Ua.BrowseNames.SourceNode)
                        .TryGetValue(out NodeId rejectedVersion), Is.True);

                    await m_coordinator.RefreshAsync(
                        new WotRefreshRequest { RequestId = "native-concrete-failure" }).ConfigureAwait(false);
                    EventFieldList? failure = null;
                    completed = await CollectEventAsync(services, subscriptionId, fields =>
                    {
                        NodeId type = EventTypeOf(fields);
                        observed.Add(type);
                        if (type == failureType &&
                            AsString(fields.EventFields[Field.ResourceId]) == ResourceId)
                        {
                            failure = fields;
                        }
                        return type == completedType &&
                            AsString(fields.EventFields[Field.RequestId]) == "native-concrete-failure";
                    }, requestHeader).ConfigureAwait(false);
                    Assert.That(completed, Is.Not.Null);
                    Assert.That(failure, Is.Not.Null);
                    Assert.That(FieldByBrowseName(failure!, filter, Ua.BrowseNames.SourceNode)
                        .TryGetValue(out NodeId source), Is.True);
                    Assert.That(source, Is.EqualTo(rejectedVersion));
                    Assert.That(AsString(failure!.EventFields[Field.VersionId]), Is.EqualTo("rejected"));
                    Assert.That(observed, Does.Not.Contain(abstractType));
                }
                finally
                {
                    await DeleteSubscriptionAsync(services, subscriptionId, requestHeader).ConfigureAwait(false);
                }
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ImmediateFailureRetainsExactSourceWithoutWaitingForVersionCreation()
        {
            const string ResourceId = "bad-pending-events";
            NamespaceTable namespaces = m_server.CurrentInstance.NamespaceUris;
            NodeId registryId = ExpandedNodeId.ToNodeId(WotConModel.ObjectIds.WoTRegistry, namespaces);
            NodeId createdType = ExpandedNodeId.ToNodeId(
                XRegistryModel.ObjectTypeIds.VersionCreatedEventType, namespaces);
            NodeId failureType = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectTypeIds.WoTValidationFailureEventType, namespaces);
            NodeId completedType = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectTypeIds.WoTRefreshCompletedEventType, namespaces);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            EventFilter filter = BuildWotEventFilterWithSourceNode();
            uint subscriptionId = await CreateEventSubscriptionAsync(services, registryId, filter)
                .ConfigureAwait(false);
            try
            {
                for (int index = 0; index < 4; index++)
                {
                    string versionId = "v" + index.ToString(CultureInfo.InvariantCulture);
                    string requestId = "immediate-" + versionId;
                    await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
                    {
                        GroupId = WotRegistryGroups.ThingDescriptions,
                        ResourceId = ResourceId,
                        VersionId = versionId,
                        Kind = WoTDocumentKindEnum.ThingDescription,
                        Content = ByteString.From(SelectiveConverter.ValidTd(ResourceId, versionId)),
                        SetAsDefault = true
                    }).ConfigureAwait(false);
                    await m_coordinator.RefreshAsync(
                        new WotRefreshRequest { RequestId = requestId }).ConfigureAwait(false);

                    EventFieldList? created = null;
                    EventFieldList? failure = null;
                    bool completed = false;
                    EventFieldList? barrier = await CollectEventAsync(services, subscriptionId, fields =>
                    {
                        NodeId type = EventTypeOf(fields);
                        if (type == createdType && HasVersionSubject(fields, filter, ResourceId, versionId))
                        {
                            created = fields;
                        }
                        if (type == failureType &&
                            AsString(fields.EventFields[Field.VersionId]) == versionId)
                        {
                            failure = fields;
                        }
                        if (type == completedType &&
                            AsString(fields.EventFields[Field.RequestId]) == requestId)
                        {
                            completed = true;
                        }
                        return created is not null && failure is not null && completed;
                    }).ConfigureAwait(false);
                    Assert.That(barrier, Is.Not.Null, versionId);
                    Assert.That(created, Is.Not.Null, versionId);
                    Assert.That(failure, Is.Not.Null, versionId);
                    Assert.That(FieldByBrowseName(created!, filter, Ua.BrowseNames.SourceNode)
                        .TryGetValue(out NodeId exactVersion), Is.True, versionId);
                    Assert.That(FieldByBrowseName(failure!, filter, Ua.BrowseNames.SourceNode)
                        .TryGetValue(out NodeId source), Is.True, versionId);
                    Assert.That(source, Is.EqualTo(exactVersion),
                        "Failure delivery cannot depend on the consumer waiting for VersionCreated: " + versionId);
                }
            }
            finally
            {
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task QueuedFailureIsDeliveredBeforeItsVersionDeletion()
        {
            const string ResourceId = "bad-delete-after-failure";
            NamespaceTable namespaces = m_server.CurrentInstance.NamespaceUris;
            NodeId registryId = ExpandedNodeId.ToNodeId(WotConModel.ObjectIds.WoTRegistry, namespaces);
            NodeId createdType = ExpandedNodeId.ToNodeId(
                XRegistryModel.ObjectTypeIds.VersionCreatedEventType, namespaces);
            NodeId deletedType = ExpandedNodeId.ToNodeId(
                XRegistryModel.ObjectTypeIds.VersionDeletedEventType, namespaces);
            NodeId failureType = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectTypeIds.WoTValidationFailureEventType, namespaces);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            EventFilter filter = BuildWotEventFilterWithSourceNode();
            uint subscriptionId = await CreateEventSubscriptionAsync(services, registryId, filter)
                .ConfigureAwait(false);
            try
            {
                await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = ResourceId,
                    VersionId = "v1",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = ByteString.From(SelectiveConverter.ValidTd(ResourceId))
                }).ConfigureAwait(false);
                await m_coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
                WotRegistryMutationResult deleted = await m_registry.DeleteResourceAsync(
                    WotRegistryGroups.ThingDescriptions, ResourceId).ConfigureAwait(false);
                Assert.That(deleted.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));

                EventFieldList? created = null;
                EventFieldList? failure = null;
                var order = new List<string>();
                EventFieldList? barrier = await CollectEventAsync(services, subscriptionId, fields =>
                {
                    NodeId type = EventTypeOf(fields);
                    if (type == createdType && HasVersionSubject(fields, filter, ResourceId, "v1"))
                    {
                        created = fields;
                        order.Add("create");
                    }
                    if (type == failureType)
                    {
                        failure = fields;
                        order.Add("failure");
                    }
                    if (type == deletedType && HasVersionSubject(fields, filter, ResourceId, "v1"))
                    {
                        order.Add("delete");
                        return true;
                    }
                    return false;
                }).ConfigureAwait(false);
                Assert.That(barrier, Is.Not.Null);
                Assert.That(created, Is.Not.Null);
                Assert.That(failure, Is.Not.Null,
                    "The matching Version and notifier chain must remain until its queued failure is delivered.");
                Assert.That(order, Is.EqualTo(s_expectedQueuedFailureOrder));
                Assert.That(FieldByBrowseName(created!, filter, Ua.BrowseNames.SourceNode)
                    .TryGetValue(out NodeId exactVersion), Is.True);
                Assert.That(FieldByBrowseName(failure!, filter, Ua.BrowseNames.SourceNode)
                    .TryGetValue(out NodeId source), Is.True);
                Assert.That(source, Is.EqualTo(exactVersion));
            }
            finally
            {
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        [TestCase(null, "v1", false)]
        [TestCase("", "v1", false)]
        [TestCase("/groups/g/resources/r", null, false)]
        [TestCase("/groups/g/resources/r", "", false)]
        [TestCase("/groups/g/resources/r", "unknown", true)]
        public void FailureSourceLookupRejectsMissingOrUnknownIdentity(
            string? xid, string? versionId, bool unknown)
        {
            using var projection = new WotRegistryProjection(
                m_eventManager, m_registry, new WotRegistryServerOptions());
            StatusCode expectedStatus = unknown ? StatusCodes.BadNodeIdUnknown : StatusCodes.BadInvalidArgument;
            Assert.That(() => projection.EventSourceForFailure(xid, versionId),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(expectedStatus));
        }

        [Test]
        public void LegacyFailureSourceLookupReportsUnsupportedCapability()
        {
            var legacy = new Mock<IWotRegistryService>(MockBehavior.Strict);
            using var projection = new WotRegistryProjection(
                m_eventManager, legacy.Object, new WotRegistryServerOptions());
            Assert.That(() => projection.EventSourceForFailure("/groups/g/resources/r", "v1"),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task ReadinessCancellationCannotPreventAddressSpaceCleanup()
        {
            await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "readiness-cancel",
                VersionId = "v1",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(SelectiveConverter.ValidTd("readiness-cancel"))
            }).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            void CancelAfterCompletion(object? sender, WotMaterializationEventArgs change)
            {
                if (change.Kind == WotMaterializationEventKind.RefreshCompleted)
                {
                    cancellation.Cancel();
                }
            }
            m_coordinator.Event += CancelAfterCompletion;
            try
            {
                await Assert.ThatAsync(
                    () => m_eventManager.OnServerReadyAsync(cancellation.Token).AsTask(),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            }
            finally
            {
                m_coordinator.Event -= CancelAfterCompletion;
            }

            await Assert.ThatAsync(
                () => m_eventManager.DeleteAddressSpaceAsync(CancellationToken.None).AsTask(),
                Throws.Nothing).ConfigureAwait(false);
            NodeId registryId = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectIds.WoTRegistry, m_server.CurrentInstance.NamespaceUris);
            Assert.That(m_eventManager.Find(registryId), Is.Null);
        }

        [Test]
        public async Task NativeDeletionCannotOvertakeQueuedVersionFailure()
        {
            const string ResourceId = "bad-native-delete";
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var client = new ClientFixture(false, false, telemetry);
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            var endpoint = new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}");
            using ISession session = await client.ConnectAsync(endpoint, SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            try
            {
                Assert.That(session.ConfiguredEndpoint.Description.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                NamespaceTable namespaces = session.NamespaceUris;
                NodeId registryId = ExpandedNodeId.ToNodeId(WotConModel.ObjectIds.WoTRegistry, namespaces);
                NodeId createdType = ExpandedNodeId.ToNodeId(
                    XRegistryModel.ObjectTypeIds.VersionCreatedEventType, namespaces);
                NodeId deletedType = ExpandedNodeId.ToNodeId(
                    XRegistryModel.ObjectTypeIds.VersionDeletedEventType, namespaces);
                NodeId failureType = ExpandedNodeId.ToNodeId(
                    WotConModel.ObjectTypeIds.WoTValidationFailureEventType, namespaces);
                var services = new ClientTestServices(session, telemetry);
                var header = new RequestHeader();
                EventFilter filter = BuildWotEventFilterWithSourceNode();
                uint subscriptionId = await CreateEventSubscriptionAsync(services, registryId, filter, header)
                    .ConfigureAwait(false);
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
                    {
                        GroupId = WotRegistryGroups.ThingDescriptions,
                        ResourceId = ResourceId,
                        VersionId = "v1",
                        Kind = WoTDocumentKindEnum.ThingDescription,
                        Content = ByteString.From(SelectiveConverter.ValidTd(ResourceId))
                    }).ConfigureAwait(false);
                    EventFieldList? created = await CollectEventAsync(services, subscriptionId,
                        fields => EventTypeOf(fields) == createdType &&
                            HasVersionSubject(fields, filter, ResourceId, "v1"), header).ConfigureAwait(false);
                    Assert.That(created, Is.Not.Null);
                    Assert.That(FieldByBrowseName(created!, filter, Ua.BrowseNames.SourceNode)
                        .TryGetValue(out NodeId exactVersion), Is.True);
                    var version = (BaseInstanceState)m_eventManager.Find(exactVersion)!;
                    var versions = (BaseInstanceState)version.Parent!;
                    NodeState logical = versions.Parent!;
                    ushort registryNamespace = (ushort)namespaces.GetIndex(
                        XRegistryModel.XRegistryWellKnown.XRegistryNamespaceUri);
                    var delete = (MethodState)logical.FindChild(m_eventManager.SystemContext,
                        new QualifiedName(XRegistryModel.BrowseNames.Delete, registryNamespace))!;
                    Assert.That(delete, Is.Not.Null);

                    FieldInfo field = typeof(WotRegistryNodeManager).GetField(
                        "m_reconcileQueue", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException("The existing reconciliation queue was not found.");
                    var queue = field.GetValue(m_eventManager) as WotRegistryReconcileQueue
                        ?? throw new InvalidOperationException("The existing reconciliation queue was unavailable.");
                    queue.Enqueue(async () =>
                    {
                        entered.TrySetResult(true);
                        await release.Task.ConfigureAwait(false);
                    });
                    await entered.Task.ConfigureAwait(false);
                    await m_coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

                    Task<CallResponse> deleting = DeleteOverTransportAsync();
                    // Allow native mutation to progress while failure delivery is held.
                    await Task.WhenAny(deleting, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
                    release.TrySetResult(true);
                    CallResponse result = await deleting.ConfigureAwait(false);
                    Assert.That(result.Results.Count, Is.EqualTo(1));
                    Assert.That(result.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));

                    EventFieldList? failure = null;
                    EventFieldList? barrier = await CollectEventAsync(services, subscriptionId, fields =>
                    {
                        NodeId type = EventTypeOf(fields);
                        if (type == failureType &&
                            AsString(fields.EventFields[Field.ResourceId]) == ResourceId)
                        {
                            failure = fields;
                        }
                        return type == deletedType && HasVersionSubject(fields, filter, ResourceId, "v1");
                    }, header).ConfigureAwait(false);
                    Assert.That(barrier, Is.Not.Null);
                    Assert.That(failure, Is.Not.Null,
                        "Native Delete must not remove a Version/notifier ahead of its queued mandatory failure.");
                    Assert.That(FieldByBrowseName(failure!, filter, Ua.BrowseNames.SourceNode)
                        .TryGetValue(out NodeId source), Is.True);
                    Assert.That(source, Is.EqualTo(exactVersion));

                    async Task<CallResponse> DeleteOverTransportAsync()
                    {
                        return await session.CallAsync(null,
                            [new CallMethodRequest
                            {
                                ObjectId = logical.NodeId,
                                MethodId = delete.NodeId,
                                InputArguments = [new Variant(0u)]
                            }], CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    release.TrySetResult(true);
                    await DeleteSubscriptionAsync(services, subscriptionId, header).ConfigureAwait(false);
                }
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
            }
        }

        private EventFilter BuildWotEventFilterWithSourceNode()
        {
            EventFilter filter = BuildWotEventFilter();
            List<SimpleAttributeOperand> clauses = filter.SelectClauses.ToList();
            clauses.Add(new SimpleAttributeOperand
            {
                AttributeId = Attributes.Value,
                TypeDefinitionId = Ua.ObjectTypeIds.BaseEventType,
                BrowsePath = [QualifiedName.From(Ua.BrowseNames.SourceNode)]
            });
            ushort registryNamespace = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(
                XRegistryModel.XRegistryWellKnown.XRegistryNamespaceUri);
            clauses.Add(new SimpleAttributeOperand
            {
                AttributeId = Attributes.Value,
                TypeDefinitionId = Ua.ObjectTypeIds.BaseEventType,
                BrowsePath = [new QualifiedName(XRegistryModel.BrowseNames.Subject, registryNamespace)]
            });
            filter.SelectClauses = clauses.ToArrayOf();
            return filter;
        }

        private static bool HasVersionSubject(
            EventFieldList fields, EventFilter filter, string resourceId, string versionId)
        {
            string expected =
                $"/groups/{WotRegistryGroups.ThingDescriptions}/resources/{resourceId}/versions/{versionId}";
            return string.Equals(
                AsString(FieldByBrowseName(fields, filter, XRegistryModel.BrowseNames.Subject)),
                expected, StringComparison.Ordinal);
        }

        private WotRegistryNodeManager m_eventManager = null!;
        private SelectiveConverter m_failureConverter = null!;
        private static readonly string[] s_expectedQueuedFailureOrder = ["create", "failure", "delete"];
    }
}
