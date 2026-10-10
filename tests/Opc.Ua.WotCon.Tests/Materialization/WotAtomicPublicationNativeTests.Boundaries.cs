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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        [Platform("Win")]
        public async Task NativeReferenceSccCoactivatesButOrderingCycleCannotPublish(bool ordering)
        {
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            WotResource second = await AddAsync("second").ConfigureAwait(false);
            string relation = ordering ? "tm:extends" : "ua:ConnectedTo";
            await SetUnitReferenceAsync(first, "second", relation).ConfigureAwait(false);
            await SetUnitReferenceAsync(second, "first", relation).ConfigureAwait(false);

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerResource }
            }).ConfigureAwait(false);

            Assert.That(result.Summary.Total, Is.EqualTo(2u));
            Assert.That(result.NewGeneration, Is.EqualTo(ordering ? 0u : 1u));
            Assert.That(result.Summary.Succeeded, Is.EqualTo(ordering ? 0u : 2u));
            Assert.That(result.Summary.Failed, Is.EqualTo(ordering ? 2u : 0u));
            Assert.That(m_coordinator.LastRefreshPlan!.AppliedAtomicity, Is.EqualTo(WoTAtomicityEnum.PerClosure));
            Assert.That(m_coordinator.LastRefreshPlan.UnitCount, Is.EqualTo(1u));
            foreach (WotResource resource in new[] { first, second })
            {
                Assert.That((await ReadNodeClassAsync(Root(resource)).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(ordering ? StatusCodes.BadNodeIdUnknown : StatusCodes.Good));
            }
            if (ordering)
            {
                Assert.That(result.Results.All(row => row.Phase == WoTPhaseEnum.DependencyResolution), Is.True);
            }
        }

        [Test]
        [Platform("Win")]
        public async Task StaleLifecycleCaptureCannotPrepareAndFreshCaptureCanPublish()
        {
            var host = new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle);
            IWotProjectionPublicationCapture captured = host.CapturePublication();
            const string firstUri = "urn:unit:revision:first";
            const string secondUri = "urn:unit:revision:second";
            var first = new WotProjectionDocument("first",
                [new WotProjectionSource("first", [firstUri], TestNodeSets.XmlBytes(firstUri))]);
            var second = new WotProjectionDocument("second",
                [new WotProjectionSource("second", [secondUri], TestNodeSets.XmlBytes(secondUri))]);
            await host.AddAsync(first).ConfigureAwait(false);
            IWotProjectionPublication stale = await captured.BeginAsync().ConfigureAwait(false);
            await using (stale.ConfigureAwait(false))
            {
                Assert.That(stale.IsCurrent, Is.False);
                await Assert.ThatAsync(async () => await stale.PrepareAsync([WotProjectionChange.Add(second)])
                    .ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(secondUri), Is.EqualTo(-1));
            }
            IWotProjectionPublication current = await host.CapturePublication().BeginAsync().ConfigureAwait(false);
            await using (current.ConfigureAwait(false))
            {
                Assert.That(current.IsCurrent, Is.True);
                IWotPreparedProjectionPublication prepared = await current.PrepareAsync([WotProjectionChange.Add(second)])
                    .ConfigureAwait(false);
                await using (prepared.ConfigureAwait(false))
                {
                    await prepared.CommitAsync(_ => default, () => { }).ConfigureAwait(false);
                    Assert.That(prepared.IsCommitted, Is.True);
                }
            }
            var root = new NodeId(5000u, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(secondUri));
            Assert.That((await ReadNodeClassAsync(root).ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        [Platform("Win")]
        public async Task ChangedLifecycleDuringAcquisitionRebuildsBeforeTheFinalPlan()
        {
            WotResource resource = await AddAsync("first").ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ObserveAcquisition(entered, resume);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            Task<WotRefreshResult> pending = m_coordinator.RefreshAsync(
                HandoffRequest("lifecycle-recapture"), timeout.Token).AsTask();
            const string model = "urn:unit:external-revision";
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                var external = new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle);
                await external.AddAsync(new WotProjectionDocument("external",
                    [new WotProjectionSource("external", [model], TestNodeSets.XmlBytes(model))]), timeout.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                resume.TrySetResult(true);
            }

            WotRefreshResult result = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);

            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That(m_coordinator.LastRefreshPlan!.RequestId, Is.EqualTo("lifecycle-recapture"));
            Assert.That(m_coordinator.LastRefreshPlan.PreparationGeneration, Is.Zero);
            Assert.That((await ReadNodeClassAsync(Root(resource)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            var externalRoot = new NodeId(5000u, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(model));
            Assert.That((await ReadNodeClassAsync(externalRoot).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        [Platform("Win")]
        public async Task UnpreparedStoreIsRejectedBeforeNativeLifecycleEffects()
        {
            using var registry = new WotRegistryService();
            WotRegistryMutationResult created = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = "things",
                ResourceId = "unprepared",
                Content = ByteString.From(TestMaterialization.Td("urn:unprepared"))
            }).ConfigureAwait(false);
            Assert.That(created.Changed, Is.True, created.Message);
            WotRegistrySnapshot before = registry.Current;
            var registrations = m_server.NodeManagerLifecycle.Registrations;
            using var coordinator = new WotMaterializationCoordinator(
                registry, new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle), documentConverter: m_converter);

            await Assert.ThatAsync(async () => await coordinator.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerClosure }
            }).ConfigureAwait(false), Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNotSupported))
                .ConfigureAwait(false);

            Assert.That(registry.Current, Is.SameAs(before));
            Assert.That(coordinator.SupportedAtomicities.IsEmpty, Is.True);
            Assert.That(coordinator.LastRefreshPlan, Is.Null);
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(registrations));
        }

        [Test]
        [Platform("Win")]
        public async Task SameContentMissingPublicationMetadataIsRepairedInsteadOfUnchanged()
        {
            WotResource resource = await AddAsync("first").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("original")).ConfigureAwait(false);
            WotResource active = m_registry.Current.FindResource(resource.GroupId, resource.ResourceId)!;
            await m_registry.ApplyProjectionResultsAsync(
                [new WotResourceProjection(active.GroupId, active.ResourceId,
                    WoTLoadStateEnum.Active, active.ActiveVersionId, active.RefreshGeneration,
                    0, NodeId.Null, active.Validation, active.Diagnostics, active.LastRefreshTime)])
                .ConfigureAwait(false);

            WotRefreshResult result = await m_coordinator.RefreshAsync(HandoffRequest("metadata-repair"))
                .ConfigureAwait(false);

            Assert.That(result.NewGeneration, Is.EqualTo(2u));
            Assert.That(result.Summary.Succeeded, Is.EqualTo(1u));
            Assert.That(result.Summary.Unchanged, Is.Zero);
            WotResource repaired = m_registry.Current.FindResource(resource.GroupId, resource.ResourceId)!;
            Assert.That(repaired.RootNodeId, Is.EqualTo(Root(resource)));
            Assert.That(repaired.MaterializedNodeCount, Is.EqualTo(active.MaterializedNodeCount));
            Assert.That((await ReadNodeClassAsync(repaired.RootNodeId).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        private async Task SetUnitReferenceAsync(WotResource resource, string target, string relation)
        {
            WotRegistryMutationResult updated = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = resource.GroupId,
                ResourceId = resource.ResourceId,
                VersionId = "v1",
                Kind = resource.Kind,
                Content = ByteString.From(Encoding.UTF8.GetBytes($$"""
                    {"@context":["https://www.w3.org/2022/wot/td/v1.1",
                      {"ua":"http://opcfoundation.org/UA/","tm":"https://www.w3.org/2019/wot/tm#"}],
                     "id":"urn:{{resource.ResourceId}}","title":"Reference unit",
                     "links":[{"rel":"{{relation}}","href":"urn:{{target}}"}]}
                    """))
            }).ConfigureAwait(false);
            Assert.That(updated.Changed, Is.True, updated.Message);
        }
    }
}
