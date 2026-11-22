// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Tests;
using static Opc.Ua.Server.Tests.NodeManager.LifecycleInspection;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchMigrationInvalidatesNativeContinuationAndRetiresOwnerAsync(bool replace)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            IServerInternal server = m_server.CurrentInstance;
            server.NamespaceUris.GetIndexOrAppend("urn:opcfoundation.org:Tests:ContinuationPadding");
            TrackingLifecycleNodeManager? originalManager = null;
            LifecycleResult publicationResult1 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kGeneration1Value, manager => originalManager = manager)), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration original = RequireRegistration(publicationResult1.Registrations[0]);
            AssertLifecycleValue(originalManager);
            await originalManager.AddContinuationChildrenAsync("Old", timeout.Token).ConfigureAwait(false);
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                BrowseResult page = await BrowseContinuationRootAsync(session, ns, 1, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(page.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(page.References.Count, Is.EqualTo(1));
                Assert.That(page.ContinuationPoint.Length, Is.GreaterThan(0));
                Assert.That(server.SubscriptionManager.GetSubscriptions(), Is.Empty);
                TrackingLifecycleNodeManager? replacement = null;
                INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
                LifecycleResult result;
                await using ILifecycleWriter exactWriter1 = await Capture(
                    m_server.NodeManagerLifecycle, original).BeginAsync(timeout.Token).ConfigureAwait(false);
                await using (IPreparedChanges prepared = await exactWriter1.PrepareAsync([
                        replace
                            ? LifecycleChange.Replace(original.Slot, CreateTrackingNodeManagementFactory(
                                kGeneration2Value, manager => replacement = manager))
                            : LifecycleChange.Remove(original.Slot)
                    ], timeout.Token).ConfigureAwait(false))
                {
                    if (replace)
                    {
                        AssertLifecycleValue(replacement);
                        await replacement.AddContinuationChildrenAsync("New", timeout.Token).ConfigureAwait(false);
                    }
                    result = await prepared.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                    await exactWriter1.DisposeAsync().ConfigureAwait(false);
                }
                await result.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
                int deletedAfterRetirement = originalManager.DeleteAddressSpaceCount;
                int disposedAfterRetirement = originalManager.DisposeCount;
                BrowseResult current = await BrowseContinuationRootAsync(session, ns, 0, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(current.StatusCode, Is.EqualTo(replace ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown));
                if (replace)
                {
                    ReferenceDescription[] currentReferences = [.. current.References];
                    Assert.That(currentReferences.Select(reference => reference.BrowseName.Name),
                        Is.EquivalentTo(new[] { kValueBrowseName, "NewFirst", "NewSecond" }));
                }

                BrowseNextResponse stale = await session.BrowseNextAsync(
                    null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
                Assert.That(stale.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                await ReleaseContinuationAsync(session, page.ContinuationPoint, timeout.Token).ConfigureAwait(false);
                await originalManager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(deletedAfterRetirement, Is.EqualTo(1));
                    Assert.That(disposedAfterRetirement, Is.EqualTo(1));
                    Assert.That(result.Retired.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(originalManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                    Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
                }
                if (replace)
                {
                    TrackingLifecycleNodeManager replacementGeneration = RequireLifecycleValue(replacement);
                    Assert.That(replacementGeneration.DisposeCount, Is.Zero);
                }
            }
            finally
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task RetiredBatchInvalidatesContinuationsAndReleasesIndependentOwnersAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var first = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var second = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var unowned = await AddContinuationOwnerAsync(
                "urn:opcfoundation.org:Tests:ContinuationUnowned", timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                BrowseResult firstPoint = await BrowseContinuationRootAsync(
                    session, first.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
                BrowseResult anotherFirstPoint = await BrowseContinuationRootAsync(
                    session, first.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
                BrowseResult secondPoint = await BrowseContinuationRootAsync(
                    session, second.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
                Assert.That(firstPoint.ContinuationPoint.Length, Is.GreaterThan(0));
                Assert.That(anotherFirstPoint.ContinuationPoint.Length, Is.GreaterThan(0));
                Assert.That(secondPoint.ContinuationPoint.Length, Is.GreaterThan(0));
                INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
                await using ILifecycleWriter exactWriter2 = await Capture(
                    m_server.NodeManagerLifecycle, first.Registration, second.Registration,
                    unowned.Registration).BeginAsync(timeout.Token).ConfigureAwait(false);
                await using (IPreparedChanges prepared = await exactWriter2.PrepareAsync([
                        LifecycleChange.Remove(first.Registration.Slot),
                        LifecycleChange.Remove(second.Registration.Slot),
                        LifecycleChange.Remove(unowned.Registration.Slot)
                    ], timeout.Token).ConfigureAwait(false))
                {
                    LifecycleResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                        .ConfigureAwait(false);
                    await exactWriter2.DisposeAsync().ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                    await result.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
                }

                BrowseNextResponse stale = await session.BrowseNextAsync(
                    null,
                    releaseContinuationPoints: false,
                    [
                        firstPoint.ContinuationPoint,
                        anotherFirstPoint.ContinuationPoint,
                        secondPoint.ContinuationPoint
                    ],
                    timeout.Token).ConfigureAwait(false);
                Assert.That(stale.Results.Count, Is.EqualTo(3));
                Assert.Multiple(() =>
                {
                    Assert.That(stale.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                    Assert.That(stale.Results[1].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                    Assert.That(stale.Results[2].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                });

                await Task.WhenAll(
                    first.Manager.DisposalCompleted,
                    second.Manager.DisposalCompleted).WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(unowned.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(second.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(first.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(first.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(second.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
            }
            finally
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task GracefulRetirementDoesNotWaitForIdleContinuationAsync(bool dispose)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var owner = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            BrowseResult page = await BrowseContinuationRootAsync(
                session, owner.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
            Assert.That(page.ContinuationPoint.Length, Is.GreaterThan(0));
            LifecycleResult result = await RetireContinuationOwnerAsync(
                owner.Registration, timeout.Token).ConfigureAwait(false);
            // An idle continuation point does not retain its issuing generation; it is validated against that
            // generation on use and rejected once the generation is retired.
            await result.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
            await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            BrowseNextResponse stale = await session.BrowseNextAsync(
                null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
            Assert.That(stale.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
            if (dispose)
            {
                ISession serverSession = m_server.CurrentInstance.SessionManager.GetSessions()
                    .Single(candidate => candidate.Id == session.SessionId);
                serverSession.Dispose();
                serverSession.Dispose();
            }
            else
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(owner.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            if (dispose)
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            }
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task ContinuationUsesCurrentRequestViewAfterUnrelatedPublicationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var owner = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var other = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            BrowseResult page = await BrowseContinuationRootAsync(
                session, owner.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
            Assert.That(page.ContinuationPoint.Length, Is.GreaterThan(0));
            var added = await AddContinuationOwnerAsync(
                "urn:opcfoundation.org:Tests:ContinuationAddedAfterBrowse",
                timeout.Token).ConfigureAwait(false);
            bool capturedCurrentView = false;
            owner.Manager.ValidateNodeCallback = (context, _, _) =>
            {
                capturedCurrentView = context.OperationContext!.RoutingSnapshot!
                    .VisibleNamespaceManagers[added.NamespaceIndex].Contains(
                        added.Manager);
                return default;
            };
            BrowseNextResponse next = await session.BrowseNextAsync(
                null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
            Assert.That(next.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(next.Results[0].ContinuationPoint.Length, Is.GreaterThan(0));
            Assert.That(capturedCurrentView, Is.True);
            await ReleaseContinuationAsync(session, next.Results[0].ContinuationPoint, timeout.Token)
                .ConfigureAwait(false);
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task InFlightBrowseNextFailureOrCancellationReleasesRetiredOwnerAsync(
            bool releasePoint,
            bool cancel)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var owner = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            BrowseResult page = await BrowseContinuationRootAsync(
                session, owner.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
            Assert.That(page.ContinuationPoint.Length, Is.GreaterThan(0));
            owner.Manager.ValidateNodeCallback = async (_, _, token) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                throw new IOException("The old source failed while validating BrowseNext.");
            };
            var header = new RequestHeader { RequestHandle = 9917 };
            Task<BrowseNextResponse> pending = session.BrowseNextAsync(
                header, releasePoint, [page.ContinuationPoint], timeout.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                LifecycleResult retirement = await RetireContinuationOwnerAsync(
                    owner.Registration, timeout.Token).ConfigureAwait(false);
                Assert.That(retirement.RetiredCount, Is.Zero);
                Assert.That(owner.Manager.DisposeCount, Is.Zero);
                if (cancel)
                {
                    CancelResponse cancelled = await session.CancelAsync(null, header.RequestHandle, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(cancelled.CancelCount, Is.EqualTo(1u));
                }
                else
                {
                    release.TrySetResult(true);
                }
                await Assert.ThatAsync(() => pending,
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(cancel
                            ? StatusCodes.BadRequestCancelledByClient
                            : StatusCodes.BadUnexpectedError)).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
                owner.Manager.ValidateNodeCallback = (_, _, _) => default;
            }
            await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(owner.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            BrowseNextResponse stale = await session.BrowseNextAsync(
                null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
            Assert.That(stale.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task RetiredContinuationCannotReenterDisposedOwnerAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var owner = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            BrowseResult page = await BrowseContinuationRootAsync(
                session, owner.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
            Assert.That(page.ContinuationPoint.Length, Is.GreaterThan(0));
            int callbackCount = 0;
            owner.Manager.ValidateNodeCallback = (_, _, _) =>
            {
                Interlocked.Increment(ref callbackCount);
                return default;
            };
            LifecycleResult retirement = await RetireContinuationOwnerAsync(
                owner.Registration, timeout.Token).ConfigureAwait(false);
            await retirement.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
            await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);

            BrowseNextResponse stale = await session.BrowseNextAsync(
                null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
            Assert.That(stale.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
            Assert.That(callbackCount, Is.Zero);
            Assert.That(owner.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchImmediateRetirementInvalidatesNativeContinuationAsync(bool replace)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var owner = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            BrowseResult page = await BrowseContinuationRootAsync(
                session, owner.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
            Assert.That(page.ContinuationPoint.Length, Is.GreaterThan(0));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using ILifecycleWriter exactWriter3 = await Capture(
                m_server.NodeManagerLifecycle, owner.Registration).BeginAsync(timeout.Token).ConfigureAwait(false);
            await using (IPreparedChanges prepared = await exactWriter3.PrepareAsync([
                    replace
                        ? LifecycleChange.Replace(
                            owner.Registration.Slot, CreateTrackingNodeManagementFactory(
                            kGeneration2Value, _ => { }), NodeManagerMonitoredItemTransitionMode.Invalidate)
                        : LifecycleChange.Remove(owner.Registration.Slot)
                ], timeout.Token).ConfigureAwait(false))
            {
                LifecycleResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                await exactWriter3.DisposeAsync().ConfigureAwait(false);
                await result.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
            }
            Assert.That(owner.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            BrowseNextResponse stale = await session.BrowseNextAsync(
                null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
            Assert.That(stale.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
            await ReleaseContinuationAsync(session, page.ContinuationPoint, timeout.Token).ConfigureAwait(false);
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task ShadowReloadInvalidatesNativeContinuationWithoutMonitoredItemsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var owner = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            BrowseResult page = await BrowseContinuationRootAsync(
                session, owner.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
            Assert.That(page.ContinuationPoint.Length, Is.GreaterThan(0));
            RequireRegistration(
                (await CommitChangeAsync(
                Capture(
                m_server.NodeManagerLifecycle, owner.Registration), LifecycleChange.Replace(
                owner.Registration.Slot, CreateTrackingNodeManagementFactory(
                kGeneration2Value, _ => { })), timeout.Token).ConfigureAwait(false)).Registrations[0]);
            await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            BrowseNextResponse next = await session.BrowseNextAsync(
                null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
            Assert.That(next.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
            await ReleaseContinuationAsync(session, page.ContinuationPoint, timeout.Token).ConfigureAwait(false);
            Assert.That(owner.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task ShutdownDoesNotRedisposeRetiredOwnerWithOutstandingNativeContinuationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var owner = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            BrowseResult page = await BrowseContinuationRootAsync(
                session, owner.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
            Assert.That(page.ContinuationPoint.Length, Is.GreaterThan(0));
            LifecycleResult result = await RetireContinuationOwnerAsync(
                owner.Registration, timeout.Token).ConfigureAwait(false);
            await result.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            await m_server.ShutdownInternalsAsync(timeout.Token).ConfigureAwait(false);
            await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(owner.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            AssertServerInternalsDisposed();
            await FinishShutdownTestAsync().ConfigureAwait(false);
        }

        private async Task<(NodeManagerRegistration Registration, TrackingLifecycleNodeManager Manager,
            ushort NamespaceIndex)> AddContinuationOwnerAsync(string namespaceUri, CancellationToken cancellationToken)
        {
            TrackingLifecycleNodeManager? manager = null;
            LifecycleResult publicationResult2 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kGeneration1Value, created => manager = created, namespaceUri)), cancellationToken)
                .ConfigureAwait(false);
            NodeManagerRegistration registration = RequireRegistration(publicationResult2.Registrations[0]);
            AssertLifecycleValue(manager);
            await manager.AddContinuationChildrenAsync("Old", cancellationToken).ConfigureAwait(false);
            return (registration, manager, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(namespaceUri));
        }

        private async Task<LifecycleResult> RetireContinuationOwnerAsync(
            NodeManagerRegistration registration,
            CancellationToken cancellationToken)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using ILifecycleWriter exactWriter4 = await Capture(
                m_server.NodeManagerLifecycle, registration).BeginAsync(cancellationToken).ConfigureAwait(false);
            await using IPreparedChanges prepared = await exactWriter4.PrepareAsync(
                [LifecycleChange.Remove(registration.Slot)], cancellationToken)
                .ConfigureAwait(false);
            LifecycleResult result = await prepared.CommitAsync(_ => default, cancellationToken)
                .ConfigureAwait(false);
            await exactWriter4.DisposeAsync().ConfigureAwait(false);
            Assert.That(result.CleanupFailure, Is.Null);
            Assert.That(m_server.CurrentInstance.SubscriptionManager.GetSubscriptions(), Is.Empty);
            return result;
        }

        private static async Task ReleaseContinuationAsync(
            Opc.Ua.Client.ISession session,
            ByteString continuationPoint,
            CancellationToken cancellationToken)
        {
            BrowseNextResponse response = await session.BrowseNextAsync(
                null, true, [continuationPoint], cancellationToken).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].ContinuationPoint.IsEmpty, Is.True);
            Assert.That(response.Results[0].References.IsEmpty, Is.True);
        }

        private static async Task<BrowseResult> BrowseContinuationRootAsync(
            Opc.Ua.Client.ISession session,
            ushort namespaceIndex,
            uint maximumReferences,
            CancellationToken cancellationToken)
        {
            BrowseResponse response = await session.BrowseAsync(null, new ViewDescription(), maximumReferences,
                [
                    new BrowseDescription
                    {
                        NodeId = new NodeId(kRootNodeId, namespaceIndex),
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        IncludeSubtypes = false,
                        NodeClassMask = (uint)NodeClass.Variable,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], cancellationToken).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }
    }
}
