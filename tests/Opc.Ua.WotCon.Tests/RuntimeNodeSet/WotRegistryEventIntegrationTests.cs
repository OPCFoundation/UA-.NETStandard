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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry.Server;
using Quickstarts.ReferenceServer;
using UaBrowseNames = global::Opc.Ua.BrowseNames;
using UaObjectIds = global::Opc.Ua.ObjectIds;
using UaObjectTypeIds = global::Opc.Ua.ObjectTypeIds;
using WotConModel = Opc.Ua.WotCon;
using XRegistryModel = Opc.Ua.XRegistry;

#nullable disable warnings

namespace Opc.Ua.WotCon.Tests.RuntimeNodeSet
{
    /// <summary>
    /// End-to-end tests that a real subscription with a real
    /// <see cref="EventFilter"/> receives the generated WoT V2 event types through
    /// the running server's notifier chain, and that every typed event field
    /// populated by <see cref="WotRegistryNodeManager"/> from the coordinator's
    /// event arguments is delivered and resolvable via the filter's
    /// <see cref="SimpleAttributeOperand"/> select clauses.
    /// </summary>
    [TestFixture]
    [Category("RuntimeNodeSet")]
    [Category("WotCon")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class WotRegistryEventIntegrationTests
    {
        private string m_pkiRoot = null!;
        private ServerFixture<ReferenceServer> m_fixture = null!;
        private ReferenceServer m_server = null!;
        private RequestHeader m_requestHeader = null!;
        private SecureChannelContext m_secureChannelContext = null!;
        private WotRegistryService m_registry = null!;
        private WotMaterializationCoordinator m_coordinator = null!;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(WotRegistryEventIntegrationTests),
                Guid.NewGuid().ToString("N"));

            m_fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);

            (m_requestHeader, m_secureChannelContext) = await m_server
                .CreateAndActivateSessionAsync(TestContext.CurrentContext.Test.Name)
                .ConfigureAwait(false);
            m_requestHeader.Timestamp = DateTimeUtc.Now;

            var options = new WotRegistryServerOptions
            {
                // Refreshes are triggered explicitly by the tests so the raised
                // events are deterministic.
                AutoRefresh = false,
                ManagementAccess = new WotManagementAccessPolicy
                {
                    MinimumSecurityMode = MessageSecurityMode.None,
                    AllowAnonymous = true,
                    RequiredRoleId = UaObjectIds.WellKnownRole_Anonymous
                },
                XRegistryEvents = new XRegistryServerOptions
                {
                    EventsEnabled = true,
                    EventSourceUrl = "https://wot-registry.example.test",
                    ResourceDocumentAttributeName = "thing"
                }
            };
            m_registry = new WotRegistryService();
            var host = new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle);
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, host, documentConverter: new SelectiveConverter());
            var factory = new WotRegistryNodeManagerFactory(options, m_registry, m_coordinator);
            await m_server.NodeManagerLifecycle.AddAsync(factory, callerContext: null).ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_requestHeader is not null)
            {
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                await m_server
                    .CloseSessionAsync(m_secureChannelContext, m_requestHeader, true, RequestLifetime.None)
                    .ConfigureAwait(false);
            }

            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }

            m_coordinator?.Dispose();
            m_registry?.Dispose();
            m_server?.Dispose();

            if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        [Test]
        public async Task RefreshCompletedEventDeliversPopulatedSummaryFieldsThroughNotifierChain()
        {
            var registryNodeId = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectIds.WoTRegistry, m_server.CurrentInstance.NamespaceUris);

            var services = new ServerTestServices(m_server, m_secureChannelContext);
            uint subscriptionId = await CreateEventSubscriptionAsync(services, registryNodeId)
                .ConfigureAwait(false);

            // Trigger a refresh: the registry raises a RefreshCompleted event with
            // the request id, the committed generation and the refresh summary.
            const string RequestId = "req-42";
            WotRefreshResult result = await m_coordinator
                .RefreshAsync(new WotRefreshRequest { RequestId = RequestId })
                .ConfigureAwait(false);

            var refreshCompletedType = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectTypeIds.WoTRefreshCompletedEventType, m_server.CurrentInstance.NamespaceUris);

            EventFieldList? evt = await CollectEventAsync(
                services, subscriptionId,
                efl => EventTypeOf(efl) == refreshCompletedType).ConfigureAwait(false);

            Assert.That(evt, Is.Not.Null,
                "The RefreshCompleted event must be delivered through the notifier chain.");
            ArrayOf<Variant> fields = evt!.EventFields;
            Assert.That(AsString(fields[Field.RequestId]), Is.EqualTo(RequestId),
                "RequestId must be populated from the materialization event arguments.");
            Assert.That(AsUInt32(fields[Field.Generation]), Is.EqualTo(result.NewGeneration),
                "The event's Generation must match the committed refresh generation.");
            Assert.That(
                fields[Field.Summary].TryGetValue(out ExtensionObject summaryEo), Is.True,
                "The refresh Summary structure field must be populated.");
            Assert.That(summaryEo.TryGetValue(out WoTRefreshSummaryDataType? summary), Is.True);
            Assert.That(summary!.RequestId, Is.EqualTo(RequestId),
                "The Summary must carry the originating refresh request id.");

            await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task ResourceEventDeliversPopulatedIdentityFieldsThroughNotifierChain()
        {
            var registryNodeId = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectIds.WoTRegistry, m_server.CurrentInstance.NamespaceUris);

            await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "sensor",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(SelectiveConverter.ValidTd("sensor"))
            }).ConfigureAwait(false);

            var services = new ServerTestServices(m_server, m_secureChannelContext);
            uint subscriptionId = await CreateEventSubscriptionAsync(services, registryNodeId)
                .ConfigureAwait(false);

            await m_coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            var resourceType = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectTypeIds.WoTResourceEventType, m_server.CurrentInstance.NamespaceUris);

            EventFieldList? evt = await CollectEventAsync(
                services, subscriptionId,
                efl => EventTypeOf(efl) == resourceType).ConfigureAwait(false);

            Assert.That(evt, Is.Not.Null,
                "The resource activation event must be delivered through the notifier chain.");
            ArrayOf<Variant> fields = evt!.EventFields;
            Assert.That(AsString(fields[Field.ResourceId]), Is.EqualTo("sensor"));
            Assert.That(AsString(fields[Field.Xid]), Does.Contain("sensor"));
            Assert.That(
                fields[Field.DocumentKind].TryGetValue(out WoTDocumentKindEnum kind), Is.True,
                "DocumentKind must be populated from the resource kind.");
            Assert.That(kind, Is.EqualTo(WoTDocumentKindEnum.ThingDescription));
            Assert.That(
                fields[Field.Outcome].TryGetValue(out WoTOutcomeEnum _), Is.True,
                "Outcome must be populated for a resource lifecycle event.");

            await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task ValidationFailureEventDeliversValidationOutcomeThroughNotifierChain()
        {
            var registryNodeId = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectIds.WoTRegistry, m_server.CurrentInstance.NamespaceUris);

            // The selective converter fails conversion for ids containing 'bad',
            // which the coordinator surfaces as a validation failure event.
            await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "bad-thing",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(SelectiveConverter.ValidTd("bad-thing"))
            }).ConfigureAwait(false);

            var services = new ServerTestServices(m_server, m_secureChannelContext);
            uint subscriptionId = await CreateEventSubscriptionAsync(services, registryNodeId)
                .ConfigureAwait(false);

            await m_coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            var validationFailureType = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectTypeIds.WoTValidationFailureEventType, m_server.CurrentInstance.NamespaceUris);

            EventFieldList? evt = await CollectEventAsync(
                services, subscriptionId,
                efl => EventTypeOf(efl) == validationFailureType).ConfigureAwait(false);

            Assert.That(evt, Is.Not.Null,
                "The validation failure event must be delivered through the notifier chain.");
            ArrayOf<Variant> fields = evt!.EventFields;
            Assert.That(AsString(fields[Field.ResourceId]), Is.EqualTo("bad-thing"));
            Assert.That(
                fields[Field.ValidationOutcome].TryGetValue(out ExtensionObject outcomeEo), Is.True,
                "The ValidationOutcome structure field must be populated.");
            Assert.That(outcomeEo.TryGetValue(out WoTValidationOutcomeDataType? outcome), Is.True);
            Assert.That(outcome!.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Failed));

            await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task GenericXRegistryAndWotEventsCoexistOnTheRegistryNotifier()
        {
            var registryNodeId = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectIds.WoTRegistry, m_server.CurrentInstance.NamespaceUris);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            EventFilter filter = XRegistryModel.ResourceCreatedEventTypeRecord.EventFilters.Build(
                m_server.CurrentInstance.NamespaceUris,
                XRegistryModel.xRegistryEventRecordDecoders.RegisterxRegistryDecoders(
                    new EventRecordDecoderRegistry(),
                    m_server.CurrentInstance.NamespaceUris));
            uint subscriptionId = await CreateEventSubscriptionAsync(
                services,
                registryNodeId,
                filter).ConfigureAwait(false);

            await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "generic-event",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(SelectiveConverter.ValidTd("generic-event"))
            }).ConfigureAwait(false);

            NodeId resourceCreatedType = ExpandedNodeId.ToNodeId(
                Opc.Ua.XRegistry.ObjectTypeIds.ResourceCreatedEventType,
                m_server.CurrentInstance.NamespaceUris);
            EventFieldList? evt = await CollectEventAsync(
                services,
                subscriptionId,
                efl => EventTypeOf(efl, filter) == resourceCreatedType).ConfigureAwait(false);

            Assert.That(evt, Is.Not.Null);
            Assert.That(
                FieldByBrowseName(evt!, filter, Opc.Ua.XRegistry.BrowseNames.SourceUrl)
                    .TryGetValue(out string sourceUrl) ? sourceUrl : string.Empty,
                Is.EqualTo("https://wot-registry.example.test"));
            await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task StructuralPlaceholderStillEmitsVersionCreatedEvent()
        {
            var registryNodeId = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectIds.WoTRegistry, m_server.CurrentInstance.NamespaceUris);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            EventFilter filter = XRegistryModel.VersionCreatedEventTypeRecord.EventFilters.Build(
                m_server.CurrentInstance.NamespaceUris,
                XRegistryModel.xRegistryEventRecordDecoders.RegisterxRegistryDecoders(
                    new EventRecordDecoderRegistry(),
                    m_server.CurrentInstance.NamespaceUris));
            uint subscriptionId = await CreateEventSubscriptionAsync(
                services,
                registryNodeId,
                filter).ConfigureAwait(false);

            await m_registry.GetOrCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "placeholder-event",
                "v1",
                WoTDocumentKindEnum.ThingDescription).ConfigureAwait(false);

            NodeId versionCreatedType = ExpandedNodeId.ToNodeId(
                XRegistryModel.ObjectTypeIds.VersionCreatedEventType,
                m_server.CurrentInstance.NamespaceUris);
            EventFieldList? evt = await CollectEventAsync(
                services,
                subscriptionId,
                efl => EventTypeOf(efl, filter) == versionCreatedType).ConfigureAwait(false);

            Assert.That(evt, Is.Not.Null);
            await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task PlaceholderDefaultSwitchDoesNotInventOptionalChangedAttributes()
        {
            await m_registry.GetOrCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "placeholder-default",
                "v1",
                WoTDocumentKindEnum.ThingDescription).ConfigureAwait(false);

            var registryNodeId = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectIds.WoTRegistry, m_server.CurrentInstance.NamespaceUris);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            EventFilter versionFilter = XRegistryModel.VersionCreatedEventTypeRecord.EventFilters.Build(
                m_server.CurrentInstance.NamespaceUris,
                XRegistryModel.xRegistryEventRecordDecoders.RegisterxRegistryDecoders(
                    new EventRecordDecoderRegistry(),
                    m_server.CurrentInstance.NamespaceUris));
            uint versionSubscriptionId = await CreateEventSubscriptionAsync(
                services,
                registryNodeId,
                versionFilter).ConfigureAwait(false);

            await m_registry.GetOrCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "placeholder-default",
                "v2",
                WoTDocumentKindEnum.ThingDescription).ConfigureAwait(false);
            NodeId versionCreatedType = ExpandedNodeId.ToNodeId(
                XRegistryModel.ObjectTypeIds.VersionCreatedEventType,
                m_server.CurrentInstance.NamespaceUris);
            EventFieldList? v2Created = await CollectEventAsync(
                services,
                versionSubscriptionId,
                efl =>
                    EventTypeOf(efl, versionFilter) == versionCreatedType &&
                    AsString(FieldByBrowseName(
                        efl,
                        versionFilter,
                        XRegistryModel.BrowseNames.Subject)).EndsWith(
                            "/versions/v2",
                            StringComparison.Ordinal)).ConfigureAwait(false);
            Assert.That(v2Created, Is.Not.Null);
            await DeleteSubscriptionAsync(services, versionSubscriptionId).ConfigureAwait(false);

            WotResource before = m_registry.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "placeholder-default")!;
            EventFilter filter = XRegistryModel.ResourceUpdatedEventTypeRecord.EventFilters.Build(
                m_server.CurrentInstance.NamespaceUris,
                XRegistryModel.xRegistryEventRecordDecoders.RegisterxRegistryDecoders(
                    new EventRecordDecoderRegistry(),
                    m_server.CurrentInstance.NamespaceUris));
            uint subscriptionId = await CreateEventSubscriptionAsync(
                services,
                registryNodeId,
                filter).ConfigureAwait(false);
            WotRegistryMutationResult result = await m_registry.SetDefaultVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "placeholder-default",
                "v2",
                before.MetaEpoch).ConfigureAwait(false);

            NodeId resourceUpdatedType = ExpandedNodeId.ToNodeId(
                XRegistryModel.ObjectTypeIds.ResourceUpdatedEventType,
                m_server.CurrentInstance.NamespaceUris);
            EventFieldList? evt = await CollectEventAsync(
                services,
                subscriptionId,
                efl => EventTypeOf(efl, filter) == resourceUpdatedType).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(evt, Is.Not.Null);
            Variant changedField = FieldByBrowseName(
                evt!,
                filter,
                XRegistryModel.BrowseNames.Changed);
            Assert.That(changedField.TryGetValue(out ArrayOf<string> changed), Is.True);
            Assert.That(changed.ToArray(), Is.EqualTo(s_placeholderDefaultSwitchChanged));

            await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task ResourceMetadataChangesIncludeNameAndDescription()
        {
            var registryNodeId = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectIds.WoTRegistry,
                m_server.CurrentInstance.NamespaceUris);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            EventFilter versionFilter = XRegistryModel.VersionCreatedEventTypeRecord.EventFilters.Build(
                m_server.CurrentInstance.NamespaceUris,
                XRegistryModel.xRegistryEventRecordDecoders.RegisterxRegistryDecoders(
                    new EventRecordDecoderRegistry(),
                    m_server.CurrentInstance.NamespaceUris));
            uint versionSubscriptionId = await CreateEventSubscriptionAsync(
                services,
                registryNodeId,
                versionFilter).ConfigureAwait(false);

            await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "metadata-event",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(SelectiveConverter.ValidTd("metadata-event")),
                Name = "Old name",
                Description = "Old description"
            }).ConfigureAwait(false);
            NodeId versionCreatedType = ExpandedNodeId.ToNodeId(
                XRegistryModel.ObjectTypeIds.VersionCreatedEventType,
                m_server.CurrentInstance.NamespaceUris);
            EventFieldList? created = await CollectEventAsync(
                services,
                versionSubscriptionId,
                efl => EventTypeOf(efl, versionFilter) == versionCreatedType)
                .ConfigureAwait(false);
            Assert.That(created, Is.Not.Null);
            await DeleteSubscriptionAsync(services, versionSubscriptionId).ConfigureAwait(false);

            EventFilter resourceFilter = XRegistryModel.ResourceUpdatedEventTypeRecord.EventFilters.Build(
                m_server.CurrentInstance.NamespaceUris,
                XRegistryModel.xRegistryEventRecordDecoders.RegisterxRegistryDecoders(
                    new EventRecordDecoderRegistry(),
                    m_server.CurrentInstance.NamespaceUris));
            uint resourceSubscriptionId = await CreateEventSubscriptionAsync(
                services,
                registryNodeId,
                resourceFilter).ConfigureAwait(false);

            WotRegistryMutationResult result = await m_registry.UpsertResourceAsync(
                new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "metadata-event",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = ByteString.From(
                        SelectiveConverter.ValidTd(
                            "metadata-event",
                            "metadata-event-v2")),
                    Name = "New name",
                    Description = "New description"
                }).ConfigureAwait(false);

            NodeId resourceUpdatedType = ExpandedNodeId.ToNodeId(
                XRegistryModel.ObjectTypeIds.ResourceUpdatedEventType,
                m_server.CurrentInstance.NamespaceUris);
            EventFieldList? evt = await CollectEventAsync(
                services,
                resourceSubscriptionId,
                efl => EventTypeOf(efl, resourceFilter) == resourceUpdatedType)
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(evt, Is.Not.Null);
            Variant changedField = FieldByBrowseName(
                evt!,
                resourceFilter,
                XRegistryModel.BrowseNames.Changed);
            Assert.That(changedField.TryGetValue(out ArrayOf<string> changed), Is.True);
            string[] changedNames = changed.ToArray();
            WotResource current = m_registry.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "metadata-event")!;
            Assert.Multiple(() =>
            {
                Assert.That(changedNames, Does.Contain("name"));
                Assert.That(changedNames, Does.Contain("description"));
                Assert.That(
                    AsUInt32(FieldByBrowseName(
                        evt!,
                        resourceFilter,
                        XRegistryModel.BrowseNames.MetaEpoch)),
                    Is.EqualTo(checked((uint)current.MetaEpoch)));
                Assert.That(current.Name, Is.EqualTo("New name"));
                Assert.That(current.Description, Is.EqualTo("New description"));
            });

            await DeleteSubscriptionAsync(services, resourceSubscriptionId).ConfigureAwait(false);
        }

        private static class Field
        {
            public const int EventType = 0;
            public const int Xid = 1;
            public const int ResourceId = 2;
            public const int VersionId = 3;
            public const int DocumentKind = 4;
            public const int Generation = 5;
            public const int Phase = 6;
            public const int Outcome = 7;
            public const int ValidationOutcome = 8;
            public const int LoadState = 9;
            public const int FailedNodeId = 10;
            public const int Reason = 11;
            public const int BindingUri = 12;
            public const int Summary = 13;
            public const int RequestId = 14;
        }

        private EventFilter BuildWotEventFilter()
        {
            ushort v2 = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(
                WotConModel.Namespaces.WotCon);

            SimpleAttributeOperand Wot(string name)
                => new()
                {
                    AttributeId = Attributes.Value,
                    TypeDefinitionId = UaObjectTypeIds.BaseEventType,
                    BrowsePath = [new QualifiedName(name, v2)]
                };

            SimpleAttributeOperand Base(string name)
                => new()
                {
                    AttributeId = Attributes.Value,
                    TypeDefinitionId = UaObjectTypeIds.BaseEventType,
                    BrowsePath = [QualifiedName.From(name)]
                };

            return new EventFilter
            {
                SelectClauses =
                [
                    Base(UaBrowseNames.EventType),           // 0
                    Wot(WotConModel.BrowseNames.Xid),               // 1
                    Wot(WotConModel.BrowseNames.ResourceId),        // 2
                    Wot(WotConModel.BrowseNames.VersionId),         // 3
                    Wot(WotConModel.BrowseNames.DocumentKind),      // 4
                    Wot(WotConModel.BrowseNames.Generation),        // 5
                    Wot(WotConModel.BrowseNames.Phase),             // 6
                    Wot(WotConModel.BrowseNames.Outcome),           // 7
                    Wot(WotConModel.BrowseNames.ValidationOutcome), // 8
                    Wot(WotConModel.BrowseNames.LoadState),         // 9
                    Wot(WotConModel.BrowseNames.FailedNodeId),      // 10
                    Wot(WotConModel.BrowseNames.Reason),            // 11
                    Wot(WotConModel.BrowseNames.BindingUri),        // 12
                    Wot(WotConModel.BrowseNames.Summary),           // 13
                    Wot(WotConModel.BrowseNames.RequestId)          // 14
                ],
                WhereClause = new ContentFilter()
            };
        }

        private async Task<uint> CreateEventSubscriptionAsync(
            ServerTestServices services,
            NodeId sourceNodeId,
            EventFilter? filter = null)
        {
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            CreateSubscriptionResponse subscription = await services
                .CreateSubscriptionAsync(requestHeader, 100, 1200, 20, 0, true, 0)
                .ConfigureAwait(false);
            uint subscriptionId = subscription.SubscriptionId;

            ArrayOf<MonitoredItemCreateRequest> items =
            [
                new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        NodeId = sourceNodeId,
                        AttributeId = Attributes.EventNotifier
                    },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = ClientHandle,
                        SamplingInterval = 0,
                        QueueSize = 100,
                        DiscardOldest = true,
                        Filter = new ExtensionObject(filter ?? BuildWotEventFilter())
                    }
                }
            ];
            requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            CreateMonitoredItemsResponse created = await services
                .CreateMonitoredItemsAsync(
                    requestHeader, subscriptionId, TimestampsToReturn.Neither, items)
                .ConfigureAwait(false);
            Assert.That(created.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good),
                "The event monitored item must be created on the WoTRegistry notifier.");
            return subscriptionId;
        }

        private async Task<EventFieldList?> CollectEventAsync(
            ServerTestServices services, uint subscriptionId, Func<EventFieldList, bool> predicate)
        {
            ArrayOf<SubscriptionAcknowledgement> acks = default;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                RequestHeader requestHeader = m_requestHeader;
                requestHeader.Timestamp = DateTimeUtc.Now;
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                PublishResponse response = await services
                    .PublishAsync(requestHeader, acks, timeoutCts.Token).ConfigureAwait(false);
                acks = response.AvailableSequenceNumbers.ToArrayOf(
                    sequenceNumber => new SubscriptionAcknowledgement
                    {
                        SubscriptionId = subscriptionId,
                        SequenceNumber = sequenceNumber
                    });

                if (response.NotificationMessage is { } message)
                {
                    ArrayOf<ExtensionObject> notifications = message.NotificationData;
                    for (int n = 0; n < notifications.Count; n++)
                    {
                        if (!notifications[n].TryGetValue(out EventNotificationList? events))
                        {
                            continue;
                        }
                        for (int i = 0; i < events.Events.Count; i++)
                        {
                            EventFieldList efl = events.Events[i];
                            if (efl.ClientHandle == ClientHandle && predicate(efl))
                            {
                                return efl;
                            }
                        }
                    }
                }
            }
            return null;
        }

        private async Task DeleteSubscriptionAsync(ServerTestServices services, uint subscriptionId)
        {
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            ArrayOf<uint> ids = [subscriptionId];
            await services.DeleteSubscriptionsAsync(requestHeader, ids).ConfigureAwait(false);
        }

        private static NodeId EventTypeOf(EventFieldList efl)
        {
            return efl.EventFields[Field.EventType].TryGetValue(out NodeId n) ? n : NodeId.Null;
        }

        private static NodeId EventTypeOf(EventFieldList efl, EventFilter filter)
        {
            Variant field = FieldByBrowseName(efl, filter, UaBrowseNames.EventType);
            return field.TryGetValue(out NodeId nodeId) ? nodeId : NodeId.Null;
        }

        private static Variant FieldByBrowseName(
            EventFieldList efl,
            EventFilter filter,
            string browseName)
        {
            for (int index = 0; index < filter.SelectClauses.Count; index++)
            {
                SimpleAttributeOperand clause = filter.SelectClauses[index];
                if (clause.BrowsePath.Count > 0 &&
                    string.Equals(
                        clause.BrowsePath[^1].Name,
                        browseName,
                        StringComparison.Ordinal))
                {
                    return efl.EventFields[index];
                }
            }
            return Variant.Null;
        }

        private static string AsString(Variant variant)
        {
            return variant.TryGetValue(out string s) ? s : string.Empty;
        }

        private static uint AsUInt32(Variant variant)
        {
            return variant.TryGetValue(out uint u) ? u : 0u;
        }

        private const uint ClientHandle = 77;
        private static readonly string[] s_placeholderDefaultSwitchChanged =
        [
            "createdat",
            "epoch",
            "isdefault",
            "meta.defaultversionid",
            "meta.epoch",
            "meta.modifiedat",
            "modifiedat",
            "versionid",
            "xid"
        ];

        /// <summary>
        /// A converter that emits a minimal valid projection for a resource, but
        /// fails conversion for any resource id containing "bad" so a validation
        /// failure event can be exercised deterministically.
        /// </summary>
        private sealed class SelectiveConverter : IWotDocumentConverter
        {
            private const string ModelUri = "urn:wot:events:model";

            public static byte[] ValidTd(string id)
            {
                return ValidTd(id, id);
            }

            public static byte[] ValidTd(string id, string title)
            {
                return Encoding.UTF8.GetBytes(
                    "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                    "\"@type\":\"uav:object\",\"id\":\"urn:" +
                    id +
                    "\",\"title\":\"" +
                    title +
                    "\"}");
            }

            public ValueTask<WotConversionOutput> ConvertAsync(
                WotResource resource,
                ByteString content,
                WotRegistrySnapshot snapshot,
                IReadOnlyDictionary<string, ByteString> contents,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<WotConversionOutput>(Convert(resource));
            }

            private static WotConversionOutput Convert(WotResource resource)
            {
                if (resource.ResourceId.Contains("bad", StringComparison.Ordinal))
                {
                    return WotConversionOutput.Failure(
                        $"Injected conversion failure for '{resource.ResourceId}'.");
                }

                string ns = ModelUri + "/" + resource.ResourceId;
                string xml = $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"
                               xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
                      <NamespaceUris><Uri>{ns}</Uri></NamespaceUris>
                      <Models><Model ModelUri="{ns}" /></Models>
                      <UAObject NodeId="ns=1;i=5000" BrowseName="1:Root">
                        <DisplayName>Root</DisplayName>
                        <References>
                          <Reference ReferenceType="i=40">i=58</Reference>
                          <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
                        </References>
                      </UAObject>
                    </UANodeSet>
                    """;
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
                UANodeSet nodeSet = UANodeSet.Read(stream)!;
                return WotConversionOutput.Success(nodeSet);
            }
        }
    }
}
