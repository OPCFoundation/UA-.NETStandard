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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [Test]
        public async Task NativeDependencyFailureEventMatchesTheActualResultPhase()
        {
            await m_server.NodeManagerLifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, m_registry, m_coordinator),
                callerContext: null).ConfigureAwait(false);
            WotResource first = await AddAsync("cycle-first").ConfigureAwait(false);
            WotResource second = await AddAsync("cycle-second").ConfigureAwait(false);
            await SetUnitReferenceAsync(first, second.ResourceId, "tm:extends").ConfigureAwait(false);
            await SetUnitReferenceAsync(second, first.ResourceId, "tm:extends").ConfigureAwait(false);
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            uint subscription = await CreateFailureContextSubscriptionAsync().ConfigureAwait(false);
            try
            {
                m_events.Clear();
                WotRefreshResult result = await m_coordinator.RefreshAsync(
                    HandoffRequest("dependency-context")).ConfigureAwait(false);
                ArrayOf<Variant> observed = await CollectFailureContextAsync(
                    subscription, first.ResourceId, WotMaterializationEventKind.LoadFailure, "dependency-context")
                    .ConfigureAwait(false);

                Assert.That(result.Summary.Failed, Is.EqualTo(2u));
                WoTResourceLoadResultDataType row = result.Results.Single(value => value.Xid == first.Xid);
                Assert.That(row.Phase, Is.EqualTo(WoTPhaseEnum.DependencyResolution));
                Assert.That(observed[2].TryGetValue(out WoTPhaseEnum phase), Is.True);
                Assert.That(phase, Is.EqualTo(row.Phase));
                Assert.That(observed[3].TryGetValue(out string version), Is.True);
                Assert.That(version, Is.EqualTo(row.VersionId));
                Assert.That(observed[4].TryGetValue(out uint generation), Is.True);
                Assert.That(generation, Is.EqualTo(result.NewGeneration));
                Assert.That(observed[1].TryGetValue(out NodeId source), Is.True);
                Assert.That(source, Is.EqualTo(new NodeId(
                    $"WoTRegistry/groups/{first.GroupId}/resources/{first.ResourceId}/versions/{row.VersionId}",
                    ResourceId(first).NamespaceIndex)));
                WotMaterializationEventArgs produced = m_events.Single(change =>
                    change.Kind == WotMaterializationEventKind.LoadFailure && change.ResourceId == first.ResourceId);
                Assert.That(produced.Phase, Is.EqualTo(row.Phase));
                Assert.That(produced.Generation, Is.EqualTo(result.NewGeneration));
            }
            finally
            {
                await m_session.DeleteSubscriptionsAsync(null, [subscription], CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeBindingFailureIdentifiesTheSelectedVersionAndCommittedGeneration(bool strict)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views, converter: m_converter,
                binderRegistry: new WotProtocolBinderRegistry([])).ConfigureAwait(false);
            WotResource serving = await AddAsync("serving-binding-control").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("before-binding-failure")).ConfigureAwait(false);
            m_coordinator.StrictBindings = strict;
            WotResource source = await AddAsync("unsupported-binding").ConfigureAwait(false);
            WotRegistryMutationResult versioned = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = source.GroupId, ResourceId = source.ResourceId, VersionId = "selected-v2",
                Kind = source.Kind, SetAsDefault = false,
                Content = ByteString.From(Encoding.UTF8.GetBytes("""
                    {"@context":"https://www.w3.org/2022/wot/td/v1.1","@type":"uav:object",
                     "id":"urn:unsupported-binding","title":"Selected unsupported binding",
                     "properties":{"value":{"type":"number","forms":[{"href":"ftp://example.test/value"}]}}}
                    """))
            }).ConfigureAwait(false);
            Assert.That(versioned.Changed, Is.True, versioned.Message);
            WotResourceVersion selected = versioned.Resource!.FindVersion("selected-v2")!;
            Assert.That(versioned.Resource.DefaultVersionId, Is.EqualTo("v1"));
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            uint subscription = await CreateFailureContextSubscriptionAsync().ConfigureAwait(false);
            try
            {
                m_events.Clear();
                WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
                {
                    ExpectedGeneration = 1,
                    RequestId = "selected-binding-failure",
                    Selection =
                    [
                        new WoTResourceSelectorDataType
                        {
                            Kind = source.Kind, Xid = WotDependencyGraph.VersionXid(source, selected)
                        }
                    ],
                    Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerResource }
                }).ConfigureAwait(false);
                ArrayOf<Variant> observed = await CollectFailureContextAsync(subscription, source.ResourceId,
                    WotMaterializationEventKind.BindingFailure, "selected-binding-failure").ConfigureAwait(false);

                Assert.That(result.NewGeneration, Is.EqualTo(strict ? 1u : 2u));
                Assert.That(result.Summary.Failed, Is.EqualTo(strict ? 1u : 0u));
                WotMaterializationEventArgs produced = m_events.Single(change =>
                    change.Kind == WotMaterializationEventKind.BindingFailure && change.ResourceId == source.ResourceId);
                Assert.That(produced.VersionId, Is.EqualTo(selected.VersionId));
                Assert.That(produced.Phase, Is.EqualTo(WoTPhaseEnum.Projection));
                Assert.That(produced.Generation, Is.EqualTo(result.NewGeneration));
                Assert.That(produced.Xid, Is.EqualTo(source.Xid));
                Assert.That(observed[1].TryGetValue(out NodeId node), Is.True);
                Assert.That(node, Is.EqualTo(new NodeId(
                    $"WoTRegistry/groups/{source.GroupId}/resources/{source.ResourceId}/versions/{selected.VersionId}",
                    ResourceId(source).NamespaceIndex)));
                Assert.That(observed[2].TryGetValue(out WoTPhaseEnum phase), Is.True);
                Assert.That(phase, Is.EqualTo(WoTPhaseEnum.Projection));
                Assert.That(observed[3].TryGetValue(out string version), Is.True);
                Assert.That(version, Is.EqualTo(selected.VersionId));
                Assert.That(observed[4].TryGetValue(out uint generation), Is.True);
                Assert.That(generation, Is.EqualTo(result.NewGeneration));
                Assert.That(m_registry.Current.FindResourceByXid(serving.Xid)!.ActiveVersionId, Is.EqualTo("v1"));
                Assert.That((await ReadNodeClassAsync(Root(serving)).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
            }
            finally
            {
                await m_session.DeleteSubscriptionsAsync(null, [subscription], CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        [TestCase(WoTPhaseEnum.Fetch)]
        [TestCase(WoTPhaseEnum.DependencyResolution)]
        [TestCase(WoTPhaseEnum.Projection)]
        [TestCase(WoTPhaseEnum.Activation)]
        [TestCase(WoTPhaseEnum.FormatValidation)]
        [TestCase(WoTPhaseEnum.CompatibilityValidation)]
        public async Task NativeConverterFailurePreservesItsDeclaredPhaseAndExactVersion(WoTPhaseEnum declaredPhase)
        {
            var converter = new Mock<IWotDocumentConverter>(MockBehavior.Strict);
            converter.Setup(value => value.ConvertAsync(
                It.IsAny<WotResource>(), It.IsAny<ByteString>(), It.IsAny<WotRegistrySnapshot>(),
                It.IsAny<IReadOnlyDictionary<string, ByteString>>(), It.IsAny<CancellationToken>()))
                .Returns((WotResource resource, ByteString content, WotRegistrySnapshot snapshot,
                    IReadOnlyDictionary<string, ByteString> contents, CancellationToken token) =>
                    resource.DefaultVersionId == "rejected-v2"
                        ? new ValueTask<WotConversionOutput>(WotConversionOutput.Failure(
                            declaredPhase, "The selected input failed at its declared phase."))
                        : m_converter.ConvertAsync(resource, content, snapshot, contents, token));
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views, converter: converter.Object).ConfigureAwait(false);
            WotResource resource = await AddAsync("converter-context").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("serving-converter-context")).ConfigureAwait(false);
            WotRegistryMutationResult rejected = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = resource.GroupId, ResourceId = resource.ResourceId,
                VersionId = "rejected-v2", Kind = resource.Kind, SetAsDefault = false,
                Content = ByteString.From(TestMaterialization.Td("urn:converter-context", "2"))
            }).ConfigureAwait(false);
            Assert.That(rejected.Changed, Is.True, rejected.Message);
            Assert.That(rejected.Resource!.DefaultVersionId, Is.EqualTo("v1"));
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            uint subscription = await CreateFailureContextSubscriptionAsync().ConfigureAwait(false);
            try
            {
                WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
                {
                    ExpectedGeneration = 1,
                    RequestId = "declared-failure-phase",
                    Selection =
                    [
                        new WoTResourceSelectorDataType
                        {
                            Kind = resource.Kind, GroupId = resource.GroupId,
                            ResourceId = resource.ResourceId, VersionId = "rejected-v2"
                        }
                    ],
                    Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }
                }).ConfigureAwait(false);
                ArrayOf<Variant> observed = await CollectFailureContextAsync(
                    subscription, resource.ResourceId,
                    declaredPhase is WoTPhaseEnum.FormatValidation or WoTPhaseEnum.CompatibilityValidation
                        ? WotMaterializationEventKind.ValidationFailure : WotMaterializationEventKind.LoadFailure,
                    "declared-failure-phase").ConfigureAwait(false);

                Assert.That(result.NewGeneration, Is.EqualTo(1u));
                Assert.That(result.Results.Single().Phase, Is.EqualTo(declaredPhase));
                Assert.That(observed[2].TryGetValue(out WoTPhaseEnum phase), Is.True);
                Assert.That(phase, Is.EqualTo(declaredPhase));
                Assert.That(observed[3].TryGetValue(out string version), Is.True);
                Assert.That(version, Is.EqualTo("rejected-v2"));
                Assert.That(observed[4].TryGetValue(out uint generation), Is.True);
                Assert.That(generation, Is.EqualTo(1u));
                if (declaredPhase is WoTPhaseEnum.FormatValidation or WoTPhaseEnum.CompatibilityValidation)
                {
                    WotMaterializationEventArgs produced = m_events.Last(change =>
                        change.Kind == WotMaterializationEventKind.ValidationFailure &&
                        change.ResourceId == resource.ResourceId);
                    Assert.That(produced.Validation, Is.Not.Null);
                    Assert.That(produced.Validation!.FormatOutcome,
                        Is.EqualTo(declaredPhase == WoTPhaseEnum.FormatValidation
                            ? WoTOutcomeEnum.Failed : WoTOutcomeEnum.Skipped));
                    Assert.That(produced.Validation.CompatibilityOutcome,
                        Is.EqualTo(declaredPhase == WoTPhaseEnum.CompatibilityValidation
                            ? WoTOutcomeEnum.Failed : WoTOutcomeEnum.Skipped));
                }
                Assert.That(observed[1].TryGetValue(out NodeId source), Is.True);
                Assert.That(source, Is.EqualTo(new NodeId(
                    $"WoTRegistry/groups/{resource.GroupId}/resources/{resource.ResourceId}/versions/rejected-v2",
                    ResourceId(resource).NamespaceIndex)));
                Assert.That(m_registry.Current.FindResourceByXid(resource.Xid)!.ActiveVersionId, Is.EqualTo("v1"));
                Assert.That((await ReadNodeClassAsync(Root(resource)).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
            }
            finally
            {
                await m_session.DeleteSubscriptionsAsync(null, [subscription], CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        private async Task<uint> CreateFailureContextSubscriptionAsync()
        {
            await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            ushort ns = (ushort)m_session.NamespaceUris.GetIndex(Namespaces.WotCon);
            var filter = new EventFilter
            {
                SelectClauses =
                [
                    FailureField(Ua.BrowseNames.EventType, 0),
                    FailureField(Ua.BrowseNames.SourceNode, 0),
                    FailureField(BrowseNames.Phase, ns),
                    FailureField(BrowseNames.VersionId, ns),
                    FailureField(BrowseNames.Generation, ns),
                    FailureField(BrowseNames.ResourceId, ns),
                    FailureField(BrowseNames.RequestId, ns)
                ]
            };
            CreateSubscriptionResponse subscription = await m_session.CreateSubscriptionAsync(
                null, 100, 1000, 1, 0, true, 0, CancellationToken.None).ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse monitored = await m_session.CreateMonitoredItemsAsync(null,
                    subscription.SubscriptionId, TimestampsToReturn.Neither,
                    [new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = ExpandedNodeId.ToNodeId(ObjectIds.WoTRegistry, m_session.NamespaceUris),
                            AttributeId = Attributes.EventNotifier
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1, SamplingInterval = 0, QueueSize = 100, DiscardOldest = true,
                            Filter = new ExtensionObject(filter)
                        }
                    }], CancellationToken.None).ConfigureAwait(false);
                Assert.That(monitored.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                return subscription.SubscriptionId;
            }
            catch
            {
                await m_session.DeleteSubscriptionsAsync(null, [subscription.SubscriptionId], CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
        }

        private async Task<ArrayOf<Variant>> CollectFailureContextAsync(
            uint subscription, string resourceId, WotMaterializationEventKind kind, string requestId)
        {
            NodeId type = ExpandedNodeId.ToNodeId(kind switch
            {
                WotMaterializationEventKind.BindingFailure => ObjectTypeIds.WoTBindingFailureEventType,
                WotMaterializationEventKind.ValidationFailure => ObjectTypeIds.WoTValidationFailureEventType,
                _ => ObjectTypeIds.WoTLoadFailureEventType
            }, m_session.NamespaceUris);
            NodeId completed = ExpandedNodeId.ToNodeId(
                ObjectTypeIds.WoTRefreshCompletedEventType, m_session.NamespaceUris);
            ArrayOf<Variant> failure = default;
            bool finished = false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            for (int attempt = 0; attempt < 30 && !finished; attempt++)
            {
                PublishResponse response = await m_session.PublishAsync(null, [], timeout.Token).ConfigureAwait(false);
                foreach (ExtensionObject extension in response.NotificationMessage.NotificationData)
                {
                    if (!extension.TryGetValue(out EventNotificationList? notification) || notification is null)
                    {
                        continue;
                    }
                    foreach (EventFieldList item in notification.Events)
                    {
                        ArrayOf<Variant> fields = item.EventFields;
                        Assert.That(fields.Count, Is.EqualTo(7));
                        Assert.That(fields[0].TryGetValue(out NodeId eventType), Is.True);
                        if (eventType == type && fields[5].TryGetValue(out string resource) && resource == resourceId)
                        {
                            failure = fields;
                        }
                        if (eventType == completed && fields[6].TryGetValue(out string request) && request == requestId)
                        {
                            finished = true;
                        }
                    }
                }
            }
            Assert.That(finished, Is.True, "A concrete refresh completion is the native event delivery barrier.");
            Assert.That(failure.IsEmpty, Is.False, "The expected producer failure must reach its exact Version notifier.");
            return failure;
        }

        private static SimpleAttributeOperand FailureField(string name, ushort ns)
        {
            return new SimpleAttributeOperand
            {
                TypeDefinitionId = Ua.ObjectTypeIds.BaseEventType,
                AttributeId = Attributes.Value,
                BrowsePath = [new QualifiedName(name, ns)]
            };
        }
    }
}
