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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [Test]
        [Platform("Win")]
        public async Task LaterConfirmedNoncommitReportsAndRetainsEarlierNativeSuccess()
        {
            HandoffProbe probe = ObserveHandoff();
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            WotResource second = await AddAsync("second").ConfigureAwait(false);
            probe.BeforeDecisionAsync = _ =>
            {
                m_failDecision = probe.DecisionCount == 1;
                return default;
            };

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                RequestId = "partial-store-failure",
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerResource }
            }).ConfigureAwait(false);

            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That(result.Summary.Generation, Is.EqualTo(1u));
            Assert.That(result.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
            Assert.That(result.Summary.Succeeded, Is.EqualTo(1u));
            Assert.That(result.Summary.Failed, Is.EqualTo(1u));
            WotResource active = m_registry.Current.AllResources().Single(resource => resource.ActiveVersionId == "v1");
            WotResource rejected = active.ResourceId == first.ResourceId ? second : first;
            Assert.That((await ReadNodeClassAsync(Root(active)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That((await ReadNodeClassAsync(Root(rejected)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(result.Results.Single(row => row.ResourceId == rejected.ResourceId).Generation, Is.EqualTo(1u));
            using (var observer = new FileWotRegistryStore(Path.Combine(m_root, "registry")))
            {
                WotRegistrySnapshot durable = await observer.LoadAsync().ConfigureAwait(false);
                Assert.That(durable.RefreshGeneration, Is.EqualTo(1u));
                Assert.That(durable.AllResources().Count(resource => resource.ActiveVersionId == "v1"), Is.EqualTo(1));
            }
            m_failDecision = false;
            probe.BeforeDecisionAsync = null;

            WotRefreshResult retry = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                ExpectedGeneration = 1,
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerResource }
            }).ConfigureAwait(false);

            Assert.That(retry.NewGeneration, Is.EqualTo(2u));
            Assert.That(retry.Summary.Succeeded, Is.EqualTo(1u));
            Assert.That(retry.Summary.Unchanged, Is.EqualTo(1u));
            Assert.That((await ReadNodeClassAsync(Root(rejected)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        [Platform("Win")]
        public async Task CommittedWarningDoesNotBlockTheNextNativeUnit()
        {
            HandoffProbe probe = ObserveHandoff();
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            WotResource second = await AddAsync("second").ConfigureAwait(false);
            probe.BeforeDecisionAsync = _ =>
            {
                m_committedWarning = probe.DecisionCount == 0;
                return default;
            };

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerResource }
            }).ConfigureAwait(false);

            Assert.That(result.NewGeneration, Is.EqualTo(2u));
            Assert.That(result.Summary.Succeeded, Is.EqualTo(2u));
            Assert.That(result.Summary.Failed, Is.Zero);
            Assert.That(result.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
            Assert.That(result.Results.Count(row => row.Message!.Contains("durability", StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(result.Results.Single(row => row.Generation == 1).Message, Does.Contain("durability"));
            Assert.That((await ReadNodeClassAsync(Root(first)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That((await ReadNodeClassAsync(Root(second)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [TestCase(false)]
        [TestCase(true)]
        [Platform("Win")]
        public async Task InterruptedInvocationRetainsItsAcceptedUnitAndAdmitsRetry(bool afterDecision)
        {
            HandoffProbe probe = ObserveHandoff();
            await AddAsync("first").ConfigureAwait(false);
            await AddAsync("second").ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            if (afterDecision)
            {
                probe.AfterDecisionAsync = _ =>
                {
                    cancellation.Cancel();
                    return default;
                };
            }
            else
            {
                probe.BeforeDecisionAsync = _ =>
                {
                    if (probe.DecisionCount == 1)
                    {
                        cancellation.Cancel();
                    }
                    return default;
                };
            }
            await Assert.ThatAsync(async () => await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                RequestId = "interrupted",
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerResource }
            }, cancellation.Token).ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);

            Assert.That(m_registry.Current.RefreshGeneration, Is.EqualTo(1u));
            Assert.That(m_events.Any(change => change.RequestId == "interrupted" &&
                change.Kind == WotMaterializationEventKind.RefreshCompleted), Is.False);
            WotResource active = m_registry.Current.AllResources().Single(resource => resource.ActiveVersionId == "v1");
            Assert.That((await ReadNodeClassAsync(Root(active)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            probe.BeforeDecisionAsync = null;
            probe.AfterDecisionAsync = null;

            WotRefreshResult retry = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                ExpectedGeneration = 1,
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerResource }
            }).ConfigureAwait(false);

            Assert.That(retry.NewGeneration, Is.EqualTo(2u));
            Assert.That(retry.Summary.Unchanged, Is.EqualTo(1u));
            Assert.That(retry.Summary.Succeeded, Is.EqualTo(1u));
        }

        [TestCase(WoTAtomicityEnum.PerResource)]
        [TestCase(WoTAtomicityEnum.PerGroup)]
        [TestCase(WoTAtomicityEnum.PerClosure)]
        [TestCase(WoTAtomicityEnum.PerRegistry)]
        [Platform("Win")]
        public async Task UnsupportedOwnersRejectRequestedUnitsBeforeEffects(WoTAtomicityEnum atomicity)
        {
            WotResource resource = await AddAsync("first").ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            var host = new FakeWotProjectionHost();
            using var unsupported = new WotMaterializationCoordinator(
                m_registry, host, documentConverter: m_converter);
            Assert.That(unsupported.SupportedAtomicities.IsEmpty, Is.True);

            await Assert.ThatAsync(async () => await unsupported.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Atomicity = atomicity }
            }).ConfigureAwait(false), Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNotSupported))
                .ConfigureAwait(false);

            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(host.Operations, Is.Empty);
            Assert.That(unsupported.LastRefreshPlan, Is.Null);
            Assert.That((await ReadNodeClassAsync(Root(resource)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [TestCase(WoTAtomicityEnum.PerResource)]
        [TestCase(WoTAtomicityEnum.PerGroup)]
        [TestCase(WoTAtomicityEnum.PerClosure)]
        [Platform("Win")]
        public async Task CustomOwnerRejectsModesOutsideItsDeclaredSubset(WoTAtomicityEnum atomicity)
        {
            WotResource resource = await AddAsync("first").ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            var host = new Mock<IWotInvocationProjectionHost>(MockBehavior.Strict);
            host.SetupGet(value => value.SupportedAtomicities).Returns([WoTAtomicityEnum.PerRegistry]);
            using var limited = new WotMaterializationCoordinator(
                m_registry, host.Object, documentConverter: m_converter);
            Assert.That(limited.SupportedAtomicities.ToList(), Is.EqualTo(new[] { WoTAtomicityEnum.PerRegistry }));

            await Assert.ThatAsync(async () => await limited.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Atomicity = atomicity }
            }).ConfigureAwait(false), Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNotSupported))
                .ConfigureAwait(false);

            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(limited.LastRefreshPlan, Is.Null);
            Assert.That((await ReadNodeClassAsync(Root(resource)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        [Platform("Win")]
        public async Task RefreshTimeoutCoversBlockedCaptureWithoutPublishingAPlan()
        {
            WotResource resource = await AddAsync("first").ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            IWotRegistryPublication publication = await m_registry.BeginPublicationAsync().ConfigureAwait(false);
            await using (publication.ConfigureAwait(false))
            {
                await Assert.ThatAsync(async () => await m_coordinator.RefreshAsync(new WotRefreshRequest
                {
                    Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry, Timeout = 100 }
                }, watchdog.Token).ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>())
                    .ConfigureAwait(false);
                Assert.That(watchdog.IsCancellationRequested, Is.False, "The request budget, not the watchdog, must cancel.");
                Assert.That(m_coordinator.LastRefreshPlan, Is.Null);
                Assert.That(m_registry.Current, Is.SameAs(before));
            }
            WotRefreshResult retry = await m_coordinator.RefreshAsync(HandoffRequest("timeout-retry"))
                .ConfigureAwait(false);
            Assert.That(retry.NewGeneration, Is.EqualTo(1u));
            Assert.That((await ReadNodeClassAsync(Root(resource)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        [Platform("Win")]
        public async Task InvalidDependentViewCannotPublishItsSourcePeer()
        {
            WotResource source = await AddAsync("first").ConfigureAwait(false);
            WotResource independent = await AddAsync("independent").ConfigureAwait(false);
            WotRegistryMutationResult view = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = source.GroupId,
                ResourceId = "view",
                VersionId = "v1",
                Format = "WoT-Projection/1.2",
                ContentType = "application/ld+json; profile=\"http://opcfoundation.org/UA/WoT-Binding/v1.2/projection\"",
                Content = ByteString.From(Encoding.UTF8.GetBytes("""
                    {
                      "@context":["https://www.w3.org/2022/wot/td/v1.1",
                        {"uav":"http://opcfoundation.org/UA/WoT-Binding/"}],
                      "@type":["uav:projection"],"uav:projectionKind":"ThingDescription",
                      "id":"urn:unit:view","title":"Invalid dependent View","uav:scenario":"urn:unit:scenario",
                      "uav:id":"nsu=urn:unit:unregistered-view;s=View",
                      "securityDefinitions":{"none":{"scheme":"nosec"}},"security":"none",
                      "uav:projects":[{"uav:sourceName":"source","href":"urn:first","type":"application/td+json",
                        "uav:routing":"source","uav:selectAll":true}]
                    }
                    """))
            }).ConfigureAwait(false);
            Assert.That(view.Changed, Is.True, view.Message);

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerResource }
            }).ConfigureAwait(false);

            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That(result.Summary.Succeeded, Is.EqualTo(1u));
            Assert.That(result.Summary.Failed, Is.EqualTo(2u));
            Assert.That(result.Summary.Atomicity, Is.EqualTo(WoTAtomicityEnum.PerClosure));
            Assert.That(m_coordinator.LastRefreshPlan!.AppliedAtomicity, Is.EqualTo(WoTAtomicityEnum.PerClosure));
            Assert.That(m_coordinator.LastRefreshPlan.UnitCount, Is.EqualTo(2u));
            Assert.That(result.Results.Single(row => row.ResourceId == source.ResourceId).Phase,
                Is.EqualTo(WoTPhaseEnum.Activation));
            Assert.That((await ReadNodeClassAsync(Root(source)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That((await ReadNodeClassAsync(Root(independent)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex("urn:unit:unregistered-view"), Is.EqualTo(-1));
        }

        [Test]
        [Platform("Win")]
        public async Task PreparedViewCannotWidenItsPublicationUnitToAnUnselectedResource()
        {
            WotResource unrelated = await AddAsync("unrelated").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("unrelated")).ConfigureAwait(false);
            WotResource source = await AddAsync("first").ConfigureAwait(false);
            m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend("urn:unit:prepared-view");
            WotRegistryMutationResult created = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = source.GroupId, ResourceId = "view", VersionId = "v1",
                Format = "WoT-Projection/1.2",
                ContentType = "application/ld+json; profile=\"http://opcfoundation.org/UA/WoT-Binding/v1.2/projection\"",
                Content = ByteString.From(Encoding.UTF8.GetBytes("""
                    {
                      "@context":["https://www.w3.org/2022/wot/td/v1.1",
                        {"uav":"http://opcfoundation.org/UA/WoT-Binding/"}],
                      "@type":["uav:projection"],"uav:projectionKind":"ThingDescription",
                      "id":"urn:unit:prepared-view","title":"Prepared View","uav:scenario":"urn:unit:scenario",
                      "uav:id":"nsu=urn:unit:prepared-view;s=View",
                      "securityDefinitions":{"none":{"scheme":"nosec"}},"security":"none",
                      "uav:projects":[{"uav:sourceName":"source","href":"urn:first","type":"application/td+json",
                        "uav:routing":"source","uav:selectAll":true}]
                    }
                    """))
            }).ConfigureAwait(false);
            Assert.That(created.Changed, Is.True, created.Message);
            WotRegistrySnapshot before = m_registry.Current;
            var participant = new Mock<IWotPreparedViewPublication>(MockBehavior.Strict);
            participant.SetupGet(value => value.Changes).Returns(ArrayOf<NodeManagerBatchChange>.Empty);
            participant.Setup(value => value.BindPreparedRegistrations(It.IsAny<ArrayOf<NodeManagerRegistration>>()))
                .Returns(new WotPreparedViewGraphState(ByteString.Empty, [], [unrelated.Xid]));
            participant.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
            var viewHost = new Mock<IWotPreparedViewProjectionHost>(MockBehavior.Strict);
            viewHost.SetupGet(value => value.SupportsPreparedPublication).Returns(true);
            viewHost.Setup(value => value.PrepareAsync(
                It.IsAny<ArrayOf<WotViewProjectionRequest>>(), It.IsAny<ArrayOf<WotViewProjectionHandle>>(),
                It.IsAny<WotCommittedPublicationState>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IWotPreparedViewPublication>(participant.Object));
            using var coordinator = new WotMaterializationCoordinator(
                m_registry, new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle),
                documentConverter: m_converter, viewProjectionHost: viewHost.Object)
            {
                ServerNamespaceUris = m_server.CurrentInstance.NamespaceUris
            };

            await Assert.ThatAsync(async () => await coordinator.RefreshAsync(new WotRefreshRequest
            {
                Selection = [UnitSelector(created.Resource!)],
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }
            }).ConfigureAwait(false), Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadInvalidState))
                .ConfigureAwait(false);

            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(coordinator.Generation, Is.EqualTo(1u));
            Assert.That((await ReadNodeClassAsync(Root(unrelated)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That((await ReadNodeClassAsync(Root(source)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }
    }
}
