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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

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

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task DirectNonDefaultValidationReportsFailureWithoutImplicitProjection(
            bool autoRefresh, bool nativeMethod)
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
                VersionId = "invalid-v2", SetAsDefault = false,
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
                Assert.That(after.DefaultVersionId, Is.EqualTo("v1"));
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
    }
}
