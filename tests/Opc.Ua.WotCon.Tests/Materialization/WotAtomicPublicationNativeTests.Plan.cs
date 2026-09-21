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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [TestCase(WoTAtomicityEnum.PerResource, 2)]
        [TestCase(WoTAtomicityEnum.PerGroup, 1)]
        [TestCase(WoTAtomicityEnum.PerClosure, 2)]
        [TestCase(WoTAtomicityEnum.PerRegistry, 1)]
        [Platform("Win")]
        public async Task FinalPlanPrecedesEveryNativeSwitchAndDryRunLeavesItUnchanged(
            WoTAtomicityEnum atomicity, int units)
        {
            HandoffProbe probe = ObserveHandoff();
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            WotResource second = await AddAsync("second").ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            Assert.That(m_coordinator.LastRefreshPlan, Is.Null);
            var request = new WotRefreshRequest
            {
                RequestId = "final-plan",
                Options = new WoTRefreshOptionsDataType { Atomicity = atomicity, DryRun = true }
            };
            WotRefreshResult dry = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);
            Assert.That(dry.NewGeneration, Is.Zero);
            Assert.That(dry.Results.All(row => row.Generation == 0), Is.True);
            Assert.That(m_coordinator.LastRefreshPlan, Is.Null);
            Assert.That(m_registry.Current, Is.SameAs(before));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            probe.BeforeDecisionAsync = async _ =>
            {
                WoTRefreshPlanDataType? plan = m_coordinator.LastRefreshPlan;
                Assert.That(plan, Is.Not.Null);
                Assert.That(plan!.PreparationGeneration, Is.Zero);
                Assert.That(plan.UnitCount, Is.EqualTo((uint)units));
                if (probe.DecisionCount == 0)
                {
                    entered.TrySetResult(true);
                    await resume.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            };
            request.Options.DryRun = false;
            Task<WotRefreshResult> pending = m_coordinator.RefreshAsync(request, timeout.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                WoTRefreshPlanDataType plan = m_coordinator.LastRefreshPlan!;
                Assert.That(plan.RequestId, Is.EqualTo("final-plan"));
                Assert.That(plan.RequestedAtomicity, Is.EqualTo(atomicity));
                Assert.That(plan.AppliedAtomicity, Is.EqualTo(atomicity));
                plan.RequestId = "caller-edited-copy";
                Assert.That(m_coordinator.LastRefreshPlan!.RequestId, Is.EqualTo("final-plan"));
                Assert.That(m_registry.Current, Is.SameAs(before));
                Assert.That((await ReadNodeClassAsync(Root(first)).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That((await ReadNodeClassAsync(Root(second)).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                BrowseResponse browse = await m_session.BrowseAsync(
                    null, null, 0,
                    [
                        new BrowseDescription
                        {
                            NodeId = Root(first), BrowseDirection = BrowseDirection.Forward,
                            ResultMask = (uint)BrowseResultMask.All
                        },
                        new BrowseDescription
                        {
                            NodeId = Root(second), BrowseDirection = BrowseDirection.Forward,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ], timeout.Token).ConfigureAwait(false);
                Assert.That(browse.Results.ToList().All(row => row.StatusCode == StatusCodes.BadNodeIdUnknown), Is.True);
            }
            finally
            {
                resume.TrySetResult(true);
            }
            WotRefreshResult committed = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(committed.NewGeneration, Is.EqualTo((uint)units));
            Assert.That(committed.Summary.Atomicity, Is.EqualTo(atomicity));
            WotRegistrySnapshot published = m_registry.Current;
            request.RequestId = "later-dry-run";
            request.Options.DryRun = true;
            request.Options.Force = true;

            WotRefreshResult later = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);

            Assert.That(later.NewGeneration, Is.EqualTo((uint)units));
            Assert.That(later.Results.All(row => row.Generation == (uint)units), Is.True);
            Assert.That(m_registry.Current, Is.SameAs(published));
            Assert.That(m_coordinator.LastRefreshPlan!.RequestId, Is.EqualTo("final-plan"));
            Assert.That(m_coordinator.LastRefreshPlan.PreparationGeneration, Is.Zero);
        }

        [Test]
        [Platform("Win")]
        public async Task NonzeroExpectedGenerationDoesNotConflictWithItsOwnNativeUnits()
        {
            WotResource existing = await AddAsync("existing").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("existing")).ConfigureAwait(false);
            WotResource unchanged = m_registry.Current.FindResource(existing.GroupId, existing.ResourceId)!;
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            WotResource second = await AddAsync("second").ConfigureAwait(false);

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                ExpectedGeneration = 1,
                RequestId = "own-units",
                Selection = [UnitSelector(first), UnitSelector(second)],
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerResource }
            }).ConfigureAwait(false);

            Assert.That(result.NewGeneration, Is.EqualTo(3u));
            Assert.That(result.Results.Select(row => row.Generation).Order(), Is.EqualTo(new uint[] { 2, 3 }));
            Assert.That(m_coordinator.LastRefreshPlan!.PreparationGeneration, Is.EqualTo(1u));
            Assert.That(m_registry.Current.FindResource(existing.GroupId, existing.ResourceId), Is.SameAs(unchanged));
        }

        [Test]
        [Platform("Win")]
        public async Task RegistryRootPlanIsInitiallyWaitingAndReadsTheGeneratedTypedFinalPlan()
        {
            var factory = new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, m_registry, m_coordinator);
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddAsync(factory, callerContext: null).ConfigureAwait(false);
            Assert.That(registration.NodeManager, Is.TypeOf<WotRegistryNodeManager>());
            m_session.MessageContext.Factory.Builder
                .AddEncodeableType(WoTRefreshPlanDataTypeActivator.Instance)
                .AddEnumeratedType(WoTAtomicityEnumActivator.Instance)
                .Commit();
            for (int i = 0; i < m_server.CurrentInstance.NamespaceUris.Count; i++)
            {
                m_session.NamespaceUris.GetIndexOrAppend(m_server.CurrentInstance.NamespaceUris.GetString((uint)i)!);
            }
            NodeId registryId = ExpandedNodeId.ToNodeId(ObjectIds.WoTRegistry, m_server.CurrentInstance.NamespaceUris);
            BrowseResponse browsed = await m_session.BrowseAsync(null, null, 0,
                [new BrowseDescription
                {
                    NodeId = registryId, BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty, IncludeSubtypes = true,
                    ResultMask = (uint)BrowseResultMask.All
                }], CancellationToken.None).ConfigureAwait(false);
            ReferenceDescription property = browsed.Results[0].References.ToList().Single(
                reference => reference.BrowseName.Name == BrowseNames.LastRefreshPlan);
            NodeId planId = ExpandedNodeId.ToNodeId(property.NodeId, m_session.NamespaceUris);
            ReadResponse initial = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = planId, AttributeId = Attributes.Value }],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(initial.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            WotResource first = await AddAsync("first").ConfigureAwait(false);

            WotRefreshResult result = await m_coordinator.RefreshAsync(HandoffRequest("typed-plan")).ConfigureAwait(false);

            ReadResponse read = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [
                    new ReadValueId { NodeId = planId, AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = planId, AttributeId = Attributes.DataType },
                    new ReadValueId { NodeId = planId, AttributeId = Attributes.AccessLevel }
                ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(read.Results[0].WrappedValue.TryGetStructure<WoTRefreshPlanDataType>(
                m_session.MessageContext, out WoTRefreshPlanDataType? plan), Is.True);
            Assert.That(plan, Is.Not.Null);
            Assert.That(plan!.RequestId, Is.EqualTo("typed-plan"));
            Assert.That(plan.PreparationGeneration, Is.Zero);
            Assert.That(plan.RequestedAtomicity, Is.EqualTo(WoTAtomicityEnum.PerRegistry));
            Assert.That(plan.AppliedAtomicity, Is.EqualTo(WoTAtomicityEnum.PerRegistry));
            Assert.That(plan.UnitCount, Is.EqualTo(1u));
            Assert.That(read.Results[1].WrappedValue.TryGetValue(out NodeId type), Is.True);
            Assert.That(type, Is.EqualTo(ExpandedNodeId.ToNodeId(
                DataTypeIds.WoTRefreshPlanDataType, m_session.NamespaceUris)));
            Assert.That(read.Results[2].WrappedValue.TryGetValue(out byte access), Is.True);
            Assert.That(access, Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That((await ReadNodeClassAsync(Root(first)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        [Platform("Win")]
        public async Task CompletedAttemptsKeepTheNativeGenerationAndCommittedDependencySnapshot()
        {
            m_coordinator.RegistryOrigin = new WotRegistryOrigin("urn:registry:unit-attempts");
            WotResource resource = await AddAsync("first").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("activated")).ConfigureAwait(false);
            WotDependencySnapshot committed = m_registry.Current
                .FindResource(resource.GroupId, resource.ResourceId)!.FindVersion("v1")!.DependencySnapshot!;
            Assert.That(committed.IsCommitted, Is.True);

            WotRefreshResult unchanged = await m_coordinator.RefreshAsync(HandoffRequest("checked-noop"))
                .ConfigureAwait(false);

            Assert.That(unchanged.NewGeneration, Is.EqualTo(1u));
            Assert.That(unchanged.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Unchanged));
            WotResourceVersion version = m_registry.Current.FindResource(resource.GroupId, resource.ResourceId)!
                .FindVersion("v1")!;
            Assert.That(version.DependencySnapshot!.RequestId, Is.EqualTo("activated"));
            Assert.That(version.LastDependencyAttempt!.RequestId, Is.EqualTo("checked-noop"));
            Assert.That(version.LastDependencyAttempt.IsCommitted, Is.False);
            Assert.That(version.LastDependencyAttempt.Generation, Is.EqualTo(1u));
            await UpdateHandoffResourceAsync(resource).ConfigureAwait(false);
            m_converter.MarkInvalid(resource.ResourceId);

            WotRefreshResult failed = await m_coordinator.RefreshAsync(HandoffRequest("failed-attempt"))
                .ConfigureAwait(false);

            WotResource retained = m_registry.Current.FindResource(resource.GroupId, resource.ResourceId)!;
            Assert.That(failed.NewGeneration, Is.EqualTo(1u));
            Assert.That(failed.Results[0].Generation, Is.EqualTo(1u));
            Assert.That(retained.ActiveVersionId, Is.EqualTo("v1"));
            Assert.That(retained.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            Assert.That(retained.FindVersion("v1")!.DependencySnapshot!.RequestId, Is.EqualTo("activated"));
            Assert.That(retained.FindVersion("v2")!.Validation!.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Failed));
            Assert.That(retained.FindVersion("v2")!.LastDependencyAttempt!.RequestId, Is.EqualTo("failed-attempt"));
            Assert.That(retained.FindVersion("v2")!.DependencySnapshot, Is.Null);
            Assert.That((await ReadNodeClassAsync(Root(resource)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That((await ReadNodeClassAsync(NewHandoffNode(resource)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }
    }
}
