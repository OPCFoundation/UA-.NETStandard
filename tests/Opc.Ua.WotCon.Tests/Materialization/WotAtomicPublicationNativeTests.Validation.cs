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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task SyntaxAndProjectionDoNotClaimUnperformedValidationPolicies(bool project)
        {
            await m_server.NodeManagerLifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, m_registry, m_coordinator),
                callerContext: null).ConfigureAwait(false);
            WotResource resource = await AddAsync("validation-policy-control").ConfigureAwait(false);
            WoTValidationOutcomeDataType outcome;
            if (project)
            {
                await m_coordinator.RefreshAsync(HandoffRequest("projection-without-validation-policies"))
                    .ConfigureAwait(false);
                outcome = m_registry.Current.FindResourceByXid(resource.Xid)!.ActiveVersion!.Validation!;
            }
            else
            {
                outcome = await m_registry.ValidateVersionAsync(resource.GroupId, resource.ResourceId, "v1")
                    .ConfigureAwait(false);
            }

            Assert.That(outcome, Is.Not.Null);
            Assert.That(outcome.FormatValidated, Is.False,
                "Syntax/admission checks do not establish a complete TD/TM schema-validation success.");
            Assert.That(outcome.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Skipped));
            Assert.That(outcome.FormatReason, Is.Not.Null.And.Not.Empty);
            Assert.That(outcome.CompatibilityValidated, Is.False,
                "Projection success does not establish that a compatibility policy was executed.");
            Assert.That(outcome.CompatibilityOutcome, Is.EqualTo(WoTOutcomeEnum.Skipped));
            Assert.That(outcome.CompatibilityReason, Is.Not.Null.And.Not.Empty);
            Assert.That(m_coordinator.Generation, Is.EqualTo(project ? 1u : 0u));
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, true)]
        public async Task DirectNonDefaultValidationReportsFailureWithoutImplicitProjection(
            bool autoRefresh, bool nativeMethod, bool defaultVersion)
        {
            var options = new WotRegistryServerOptions
            {
                AutoRefresh = false,
                ManagementAccess = new WotManagementAccessPolicy
                {
                    MinimumSecurityMode = MessageSecurityMode.None,
                    AllowAnonymous = true,
                    RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
                }
            };
            await m_server.NodeManagerLifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                options, m_registry, m_coordinator), callerContext: null).ConfigureAwait(false);
            WotResource source = await AddAsync("validate-selected-version").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("before-direct-validation")).ConfigureAwait(false);
            WotRegistryMutationResult bad = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = source.GroupId, ResourceId = source.ResourceId, Kind = source.Kind,
                VersionId = "invalid-v2", SetAsDefault = defaultVersion,
                Content = ByteString.From(Encoding.UTF8.GetBytes("""
                    {
                      "id":"urn:validate-selected-version","title":"Selected Version",
                      "uav:metadata":{"nested":{"depth":3}}
                    }
                    """))
            }).ConfigureAwait(false);
            Assert.That(bad.Resource, Is.Not.Null, bad.Message);
            Assert.That(bad.Resource!.FindVersion("invalid-v2")!.HasContent, Is.True);
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            uint subscription = await CreateFailureContextSubscriptionAsync(includeValidation: true).ConfigureAwait(false);
            try
            {
                m_session.MessageContext.Factory.Builder.AddOpcUaWotCon().Commit();
                WoTDocumentTypeClient? selected = nativeMethod
                    ? new WoTDocumentTypeClient(m_session, new NodeId(
                        $"WoTRegistry/groups/{source.GroupId}/resources/{source.ResourceId}/versions/invalid-v2",
                        ResourceId(source).NamespaceIndex), NUnitTelemetryContext.Create())
                    : null;
                WotRegistrySnapshot before = m_registry.Current;
                options.AutoRefresh = autoRefresh;
                m_events.Clear();

                int depth = m_registry.Bounds.MaxJsonDepth;
                WoTValidationOutcomeDataType outcome;
                try
                {
                    m_registry.Bounds.MaxJsonDepth = 2;
                    outcome = selected is null
                        ? await m_registry.ValidateVersionAsync(source.GroupId, source.ResourceId, "invalid-v2")
                            .ConfigureAwait(false)
                        : await selected.ValidateAsync().ConfigureAwait(false);
                }
                finally
                {
                    m_registry.Bounds.MaxJsonDepth = depth;
                }

                Assert.That(outcome.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Failed));
                string reason = outcome.FormatReason!;
                outcome.FormatOutcome = WoTOutcomeEnum.Success;
                outcome.FormatReason = "Caller changed the returned object.";
                Assert.That(m_registry.Current.Generation, Is.EqualTo(before.Generation + 1));
                Assert.That(m_registry.Current.RefreshGeneration, Is.EqualTo(1u));
                WotResource after = m_registry.Current.FindResourceByXid(source.Xid)!;
                Assert.That(after.ActiveVersionId, Is.EqualTo("v1"));
                Assert.That(after.DefaultVersionId, Is.EqualTo(defaultVersion ? "invalid-v2" : "v1"));
                Assert.That(after.RootNodeId, Is.EqualTo(before.FindResourceByXid(source.Xid)!.RootNodeId));
                Assert.That(after.FindVersion("v1")!.Validation!.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Skipped));
                Assert.That(after.FindVersion("invalid-v2")!.Validation!.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Failed));
                Assert.That(after.FindVersion("invalid-v2")!.Validation!.FormatReason, Is.EqualTo(reason));
                Assert.That((await ReadNodeClassAsync(Root(source)).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                const string requestId = "validate-delivery-barrier";
                await m_coordinator.RefreshAsync(new WotRefreshRequest
                {
                    RequestId = requestId,
                    Selection =
                    [
                        new WoTResourceSelectorDataType
                        {
                            Kind = WoTDocumentKindEnum.All, Xid = "/groups/unselected/resources/unselected"
                        }
                    ]
                }).ConfigureAwait(false);
                ArrayOf<Variant> observed = await CollectFailureContextAsync(
                    subscription, source.ResourceId, WotMaterializationEventKind.ValidationFailure, requestId,
                    fieldCount: 8, expectedFailureCount: 1)
                    .ConfigureAwait(false);

                string[] expectedRequests = [requestId];
                Assert.That(m_events.Where(change => change.Kind == WotMaterializationEventKind.RefreshCompleted)
                    .Select(change => change.RequestId), Is.EqualTo(expectedRequests));
                Assert.That(observed[1].TryGetValue(out NodeId eventSource), Is.True);
                Assert.That(eventSource, Is.EqualTo(new NodeId(
                    $"WoTRegistry/groups/{source.GroupId}/resources/{source.ResourceId}/versions/invalid-v2",
                    ResourceId(source).NamespaceIndex)));
                Assert.That(observed[2].TryGetValue(out WoTPhaseEnum phase), Is.True);
                Assert.That(phase, Is.EqualTo(WoTPhaseEnum.FormatValidation));
                Assert.That(observed[3].TryGetValue(out string version), Is.True);
                Assert.That(version, Is.EqualTo("invalid-v2"));
                Assert.That(observed[4].TryGetValue(out uint generation), Is.True);
                Assert.That(generation, Is.EqualTo(1u));
                Assert.That(observed[7].TryGetStructure<WoTValidationOutcomeDataType>(
                    out WoTValidationOutcomeDataType? reported), Is.True);
                Assert.That(reported, Is.Not.Null);
                Assert.That(reported!.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Failed));
                Assert.That(reported.FormatReason, Is.EqualTo(reason));
                Assert.That(await ReadValidationVersionStateAsync(source, "invalid-v2").ConfigureAwait(false),
                    Is.EqualTo(WoTLoadStateEnum.Failed));
                Assert.That(await ReadValidationVersionStateAsync(source, "v1").ConfigureAwait(false),
                    Is.EqualTo(WoTLoadStateEnum.Active));
                Assert.That(await ReadValidationVersionStateAsync(source, string.Empty).ConfigureAwait(false),
                    Is.EqualTo(WoTLoadStateEnum.Active),
                    "The serving logical Resource remains active even when its desired Version fails validation.");
            }
            finally
            {
                await m_session.DeleteSubscriptionsAsync(null, [subscription], CancellationToken.None).ConfigureAwait(false);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task ValidationFailureNotificationFollowsThePersistenceDecision(int decision)
        {
            await m_server.NodeManagerLifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, m_registry, m_coordinator),
                callerContext: null).ConfigureAwait(false);
            WotRegistryMutationResult added = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions, ResourceId = "validation-decision", VersionId = "v1",
                Content = ByteString.From(Encoding.UTF8.GetBytes("""
                    {"id":"urn:validation-decision","title":"Decision","uav:metadata":{"nested":{"depth":3}}}
                    """))
            }).ConfigureAwait(false);
            Assert.That(added.Changed, Is.True, added.Message);
            WotResource resource = added.Resource!;
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            uint subscription = await CreateFailureContextSubscriptionAsync().ConfigureAwait(false);
            int depth = m_registry.Bounds.MaxJsonDepth;
            using var cancellation = new CancellationTokenSource();
            try
            {
                WotRegistrySnapshot before = m_registry.Current;
                m_failDecision = decision == 0;
                m_committedWarning = decision == 1;
                if (decision == 2)
                {
                    cancellation.Cancel();
                }
                m_registry.Bounds.MaxJsonDepth = 2;
                try
                {
                    await Assert.ThatAsync(async () => await m_registry.ValidateVersionAsync(
                        resource.GroupId, resource.ResourceId, "v1", cancellation.Token).ConfigureAwait(false),
                        decision switch
                        {
                            0 => Throws.TypeOf<WotRegistryCommitNotCommittedException>(),
                            1 => Throws.TypeOf<WotRegistryCommitDurabilityUncertainException>(),
                            _ => Throws.InstanceOf<OperationCanceledException>()
                        }).ConfigureAwait(false);
                }
                finally
                {
                    m_failDecision = false;
                    m_committedWarning = false;
                    m_registry.Bounds.MaxJsonDepth = depth;
                }

                Assert.That(m_coordinator.Generation, Is.Zero);
                if (decision == 1)
                {
                    Assert.That(m_registry.Current.Generation, Is.EqualTo(before.Generation + 1));
                    Assert.That(m_registry.Current.FindResourceByXid(resource.Xid)!.DefaultVersion!.Validation!.FormatOutcome,
                        Is.EqualTo(WoTOutcomeEnum.Failed));
                }
                else
                {
                    Assert.That(m_registry.Current, Is.SameAs(before));
                }
                const string requestId = "validation-decision-barrier";
                await m_coordinator.RefreshAsync(new WotRefreshRequest
                {
                    RequestId = requestId,
                    Selection =
                    [
                        new WoTResourceSelectorDataType
                        {
                            Kind = WoTDocumentKindEnum.All, Xid = "/groups/unselected/resources/unselected"
                        }
                    ]
                }).ConfigureAwait(false);
                await CollectFailureContextAsync(
                    subscription, resource.ResourceId, WotMaterializationEventKind.ValidationFailure, requestId,
                    expectedFailureCount: decision == 1 ? 1 : 0).ConfigureAwait(false);
            }
            finally
            {
                await m_session.DeleteSubscriptionsAsync(null, [subscription], CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task NativeCommittedValidationWarningReturnsTheCommittedOutcome()
        {
            await m_server.NodeManagerLifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions
                {
                    AutoRefresh = false,
                    ManagementAccess = new WotManagementAccessPolicy
                    {
                        MinimumSecurityMode = MessageSecurityMode.None,
                        AllowAnonymous = true,
                        RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
                    }
                }, m_registry, m_coordinator), callerContext: null).ConfigureAwait(false);
            WotRegistryMutationResult added = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions, ResourceId = "validation-warning", VersionId = "v1",
                Content = ByteString.From(Encoding.UTF8.GetBytes("""
                    {"id":"urn:validation-warning","title":"Warning","uav:metadata":{"nested":{"depth":3}}}
                    """))
            }).ConfigureAwait(false);
            Assert.That(added.Changed, Is.True, added.Message);
            WotResource resource = added.Resource!;
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            m_session.MessageContext.Factory.Builder.AddOpcUaWotCon().Commit();
            long generation = m_registry.Current.Generation;
            int depth = m_registry.Bounds.MaxJsonDepth;
            CallResponse response;
            try
            {
                m_committedWarning = true;
                m_registry.Bounds.MaxJsonDepth = 2;
                response = await m_session.CallAsync(null,
                    [
                        new CallMethodRequest
                        {
                            ObjectId = new NodeId(
                                $"WoTRegistry/groups/{resource.GroupId}/resources/{resource.ResourceId}/versions/v1",
                                ResourceId(resource).NamespaceIndex),
                            MethodId = ExpandedNodeId.ToNodeId(MethodIds.WoTDocumentType_Validate, m_session.NamespaceUris),
                            InputArguments = []
                        }
                    ], CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                m_registry.Bounds.MaxJsonDepth = depth;
                m_committedWarning = false;
            }

            Assert.That(m_registry.Current.Generation, Is.EqualTo(generation + 1));
            Assert.That(m_coordinator.Generation, Is.Zero);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.GoodResultsMayBeIncomplete));
            Assert.That(response.Results[0].OutputArguments.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].OutputArguments[0].TryGetStructure<WoTValidationOutcomeDataType>(
                out WoTValidationOutcomeDataType? outcome), Is.True);
            Assert.That(outcome, Is.Not.Null);
            Assert.That(outcome!.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Failed));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LogicalValidationWarningReportsTheExecutedVersion(bool versioned)
        {
            m_coordinator.Dispose();
            var registry = new Mock<IWotRegistryService>(MockBehavior.Strict);
            registry.SetupGet(value => value.Current).Returns(() => m_registry.Current);
            registry.SetupGet(value => value.Bounds).Returns(m_registry.Bounds);
            registry.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) => m_registry.InitializeAsync(token));
            registry.SetupAdd(value => value.Changed += It.IsAny<EventHandler<WotRegistryChangedEventArgs>>())
                .Callback<EventHandler<WotRegistryChangedEventArgs>>(handler => m_registry.Changed += handler);
            registry.SetupRemove(value => value.Changed -= It.IsAny<EventHandler<WotRegistryChangedEventArgs>>())
                .Callback<EventHandler<WotRegistryChangedEventArgs>>(handler => m_registry.Changed -= handler);
            async ValueTask SelectSecondVersionAsync(string groupId, string resourceId, CancellationToken token)
            {
                WotRegistryMutationResult changed = await m_registry.SetDefaultVersionAsync(
                    groupId, resourceId, "v2", cancellationToken: token).ConfigureAwait(false);
                Assert.That(changed.Changed, Is.True, changed.Message);
                m_committedWarning = true;
            }
            registry.Setup(value => value.ValidateResourceAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async (string groupId, string resourceId, CancellationToken token) =>
                {
                    await SelectSecondVersionAsync(groupId, resourceId, token).ConfigureAwait(false);
                    return await m_registry.ValidateResourceAsync(groupId, resourceId, token).ConfigureAwait(false);
                });
            if (versioned)
            {
                registry.As<IWotVersionedRegistryService>().Setup(value => value.ValidateVersionAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns(async (string groupId, string resourceId, string versionId, CancellationToken token) =>
                    {
                        await SelectSecondVersionAsync(groupId, resourceId, token).ConfigureAwait(false);
                        return await m_registry.ValidateVersionAsync(groupId, resourceId, versionId, token)
                            .ConfigureAwait(false);
                    });
            }
            m_coordinator = new WotMaterializationCoordinator(
                registry.Object, new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle),
                documentConverter: m_converter);
            var registration = await m_server.NodeManagerLifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions
                {
                    AutoRefresh = false,
                    ManagementAccess = new WotManagementAccessPolicy
                    {
                        MinimumSecurityMode = MessageSecurityMode.None,
                        AllowAnonymous = true,
                        RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
                    }
                }, registry.Object, m_coordinator), callerContext: null).ConfigureAwait(false);
            WotRegistryMutationResult first = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions, ResourceId = "validation-default-race", VersionId = "v1",
                Content = ByteString.From(Encoding.UTF8.GetBytes("""
                    {"id":"urn:validation-default-race","title":"First"}
                    """))
            }).ConfigureAwait(false);
            WotRegistryMutationResult second = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = first.Resource!.GroupId, ResourceId = first.Resource.ResourceId,
                VersionId = "v2", SetAsDefault = false,
                Content = ByteString.From(Encoding.UTF8.GetBytes("""
                    {"id":"urn:validation-default-race","title":"Second","uav:metadata":{"nested":{"depth":3}}}
                    """))
            }).ConfigureAwait(false);
            Assert.That(second.Changed, Is.True, second.Message);
            await ((WotRegistryNodeManager)registration.NodeManager).DispatchProjectionAsync(
                _ => default, CancellationToken.None).ConfigureAwait(false);
            await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            m_session.MessageContext.Factory.Builder.AddOpcUaWotCon().Commit();
            WotResource resource = second.Resource!;
            long generation = m_registry.Current.Generation;
            int depth = m_registry.Bounds.MaxJsonDepth;
            CallResponse response;
            try
            {
                m_registry.Bounds.MaxJsonDepth = 2;
                response = await m_session.CallAsync(null,
                    [
                        new CallMethodRequest
                        {
                            ObjectId = ResourceId(resource),
                            MethodId = ExpandedNodeId.ToNodeId(MethodIds.WoTDocumentType_Validate, m_session.NamespaceUris),
                            InputArguments = []
                        }
                    ], CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                m_registry.Bounds.MaxJsonDepth = depth;
                m_committedWarning = false;
            }

            Assert.That(m_registry.Current.Generation, Is.EqualTo(generation + 2),
                "Both the intervening default change and the validation observation must have committed.");
            Assert.That(m_registry.Current.FindResourceByXid(resource.Xid)!.DefaultVersionId, Is.EqualTo("v2"));
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.GoodResultsMayBeIncomplete));
            Assert.That(response.Results[0].OutputArguments[0].TryGetStructure<WoTValidationOutcomeDataType>(
                out WoTValidationOutcomeDataType? outcome), Is.True);
            Assert.That(outcome!.FormatOutcome, Is.EqualTo(versioned ? WoTOutcomeEnum.Skipped : WoTOutcomeEnum.Failed));
            WotResource current = m_registry.Current.FindResourceByXid(resource.Xid)!;
            Assert.That(current.DefaultVersionId, Is.EqualTo("v2"));
            Assert.That(current.FindVersion(versioned ? "v1" : "v2")!.Validation!.FormatOutcome,
                Is.EqualTo(outcome.FormatOutcome));
            Assert.That(current.FindVersion(versioned ? "v2" : "v1")!.Validation, Is.Null);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task ValidationRecoveryReleasesOnlyTheConfirmedFailureIntent(int recovery)
        {
            m_coordinator.Dispose();
            m_registry.Dispose();
            var faulting = new ValidationCaptureStore(m_store);
            m_registry = new WotRegistryService(faulting);
            await m_registry.InitializeAsync().ConfigureAwait(false);
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle),
                documentConverter: m_converter)
            {
                ServerNamespaceUris = m_server.CurrentInstance.NamespaceUris
            };
            m_coordinator.Event += (_, change) => m_events.Add(change);
            await m_server.NodeManagerLifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, m_registry, m_coordinator),
                callerContext: null).ConfigureAwait(false);
            WotRegistryMutationResult added = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions, ResourceId = "validation-recovery", VersionId = "v1",
                Content = ByteString.From(Encoding.UTF8.GetBytes("""
                    {"id":"urn:validation-recovery","title":"Recovery","uav:metadata":{"nested":{"depth":3}}}
                    """))
            }).ConfigureAwait(false);
            Assert.That(added.Changed, Is.True, added.Message);
            WotResource source = added.Resource!;
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            uint subscription = await CreateFailureContextSubscriptionAsync().ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            int depth = m_registry.Bounds.MaxJsonDepth;
            try
            {
                faulting.FailNextCapture = recovery == 0;
                m_indeterminateDecision = recovery != 0;
                m_registry.Bounds.MaxJsonDepth = 2;
                try
                {
                    await Assert.ThatAsync(async () => await m_registry.ValidateVersionAsync(
                        source.GroupId, source.ResourceId, "v1").ConfigureAwait(false),
                        recovery == 0 ? Throws.TypeOf<WotRegistryCommitDurabilityUncertainException>() :
                            Throws.TypeOf<WotRegistryCommitIndeterminateException>()).ConfigureAwait(false);
                }
                finally
                {
                    m_registry.Bounds.MaxJsonDepth = depth;
                    m_indeterminateDecision = false;
                }
                if (recovery != 0)
                {
                    string directory = Path.Combine(m_root, "registry");
                    File.Move(Directory.GetFiles(directory,
                        recovery == 1 ? "manifest.json.tmp-*" : "manifest.json.replace-backup-*").Single(),
                        Path.Combine(directory, "manifest.json"));
                }

                await m_registry.InitializeAsync().ConfigureAwait(false);
                await m_registry.InitializeAsync().ConfigureAwait(false);

                Assert.That(m_registry.Current.Generation, Is.EqualTo(before.Generation + (recovery == 2 ? 0 : 1)));
                Assert.That(m_registry.Current.RefreshGeneration, Is.Zero);
                const string requestId = "validation-recovery-barrier";
                await m_coordinator.RefreshAsync(new WotRefreshRequest
                {
                    RequestId = requestId,
                    Selection =
                    [
                        new WoTResourceSelectorDataType
                        {
                            Kind = WoTDocumentKindEnum.All, Xid = "/groups/unselected/resources/unselected"
                        }
                    ]
                }).ConfigureAwait(false);
                await CollectFailureContextAsync(
                    subscription, source.ResourceId, WotMaterializationEventKind.ValidationFailure, requestId,
                    expectedFailureCount: recovery == 2 ? 0 : 1).ConfigureAwait(false);
                Assert.That(m_coordinator.Generation, Is.Zero);
            }
            finally
            {
                await m_session.DeleteSubscriptionsAsync(null, [subscription], CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async Task<WoTLoadStateEnum> ReadValidationVersionStateAsync(WotResource resource, string versionId)
        {
            NodeId version = string.IsNullOrEmpty(versionId) ? ResourceId(resource) : new NodeId(
                $"WoTRegistry/groups/{resource.GroupId}/resources/{resource.ResourceId}/versions/{versionId}",
                ResourceId(resource).NamespaceIndex);
            ReferenceDescription property = (await BrowseStockAsync(version, Ua.ReferenceTypeIds.HasProperty)
                .ConfigureAwait(false)).Single(reference => reference.BrowseName.Name == BrowseNames.LoadState);
            DataValue value = await m_session.ReadValueAsync(
                ExpandedNodeId.ToNodeId(property.NodeId, m_session.NamespaceUris)).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out WoTLoadStateEnum state), Is.True);
            return state;
        }

        private sealed class ValidationCaptureStore(FileWotRegistryStore inner)
            : IWotRegistryRecoveryStore, IWotRegistryResourceStoreProvider
        {
            public bool FailNextCapture { get; set; }
            public bool SupportsPreparedCommits => inner.SupportsPreparedCommits;
            public IXRegistryResourceStore ResourceStore => ((IWotRegistryResourceStoreProvider)inner).ResourceStore;

            public ValueTask<WotRegistrySnapshot> LoadAsync(CancellationToken cancellationToken = default)
            {
                return inner.LoadAsync(cancellationToken);
            }

            public ValueTask CommitAsync(WotRegistrySnapshot snapshot, CancellationToken cancellationToken = default)
            {
                return inner.CommitAsync(snapshot, cancellationToken);
            }

            public ValueTask<IWotRegistryValidatedGeneration> CaptureValidatedGenerationAsync(
                CancellationToken cancellationToken = default)
            {
                if (FailNextCapture)
                {
                    FailNextCapture = false;
                    throw new IOException("The post-commit validated generation cannot be captured yet.");
                }
                return inner.CaptureValidatedGenerationAsync(cancellationToken);
            }

            public ValueTask<IWotRegistryPreparedCommit> PrepareCommitAsync(
                WotRegistrySnapshot intendedSnapshot, IWotRegistryValidatedGeneration expectedGeneration,
                WotRegistryCommitScope scope, CancellationToken cancellationToken = default)
            {
                return inner.PrepareCommitAsync(intendedSnapshot, expectedGeneration, scope, cancellationToken);
            }

            public ValueTask<IWotRegistryPublicationValidation> ValidatePublicationAsync(
                IWotRegistryValidatedGeneration expectedGeneration, CancellationToken cancellationToken = default)
            {
                return inner.ValidatePublicationAsync(expectedGeneration, cancellationToken);
            }
        }

    }
}
