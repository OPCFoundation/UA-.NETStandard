/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Server.Tests.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.AliasNames
{
    /// <summary>
    /// Verifies alias refresh failure isolation, reference identity, and bounded background ownership.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    public sealed class AliasRefreshRegressionTests
    {
        [Test]
        public async Task InitialMaterializationRetriesOneTimedOutSnapshotAsync()
        {
            await using var harness = new RefreshHarness();
            int attempts = 0;
            harness.Query = (id, pattern, reference, types, ct) =>
            {
                Assert.That(harness.Category, Is.Null);
                if (++attempts == 1)
                {
                    return new ValueTask<IReadOnlyList<AliasNameVerboseDataType>>(
                        Task.FromException<IReadOnlyList<AliasNameVerboseDataType>>(
                            new ServiceResultException(StatusCodes.BadTimeout)));
                }
                return harness.Data.FindAliasVerboseAsync(id, pattern, reference, types, ct);
            };

            await harness.InitializeAsync().ConfigureAwait(false);

            Assert.That(attempts, Is.EqualTo(2));
            AliasNameState alias = harness.FindAlias("Alpha");
            Assert.That(alias, Is.Not.Null);
            Assert.That(harness.Category.ReferenceExists(ReferenceTypeIds.Organizes, false, alias.NodeId), Is.True);
            Assert.That(harness.Target.ReferenceExists(ReferenceTypeIds.AliasFor, true, alias.NodeId), Is.True);
        }

        [Test]
        public async Task InitialMaterializationPropagatesRepeatedTimeoutAsync()
        {
            await using var harness = new RefreshHarness();
            var failure = new ServiceResultException(StatusCodes.BadTimeout);
            int attempts = 0;
            harness.Query = (_, _, _, _, _) =>
            {
                attempts++;
                return new ValueTask<IReadOnlyList<AliasNameVerboseDataType>>(
                    Task.FromException<IReadOnlyList<AliasNameVerboseDataType>>(failure));
            };

            ServiceResultException actual = Assert.ThrowsAsync<ServiceResultException>(
                async () => await harness.InitializeAsync().ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(failure));
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(harness.Category, Is.Null);
            Assert.That(harness.FindAlias("Alpha"), Is.Null);
        }

        [Test]
        public async Task InitialMaterializationDoesNotRetryNonTimeoutFailureAsync()
        {
            await using var harness = new RefreshHarness();
            var failure = new IOException("Initial alias store unavailable.");
            int attempts = 0;
            harness.Query = (_, _, _, _, _) =>
            {
                attempts++;
                return new ValueTask<IReadOnlyList<AliasNameVerboseDataType>>(
                    Task.FromException<IReadOnlyList<AliasNameVerboseDataType>>(failure));
            };

            IOException actual = Assert.ThrowsAsync<IOException>(
                async () => await harness.InitializeAsync().ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(failure));
            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(harness.Category, Is.Null);
        }

        [Test]
        public async Task InitialMaterializationDoesNotRetryCanceledQueryAsync()
        {
            await using var harness = new RefreshHarness();
            using var cancellation = new CancellationTokenSource();
            var failure = new ServiceResultException(StatusCodes.BadTimeout);
            int attempts = 0;
            harness.Query = (_, _, _, _, ct) =>
            {
                Assert.That(ct, Is.EqualTo(cancellation.Token));
                attempts++;
                cancellation.Cancel();
                return new ValueTask<IReadOnlyList<AliasNameVerboseDataType>>(
                    Task.FromException<IReadOnlyList<AliasNameVerboseDataType>>(failure));
            };

            ServiceResultException actual = Assert.ThrowsAsync<ServiceResultException>(
                async () => await harness.InitializeAsync(cancellation.Token).ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(failure));
            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(harness.Category, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task StoreQueryFailurePreservesMaterializedAliasNodesAsync(bool timeout)
        {
            await using var harness = new RefreshHarness();
            await harness.InitializeAsync().ConfigureAwait(false);
            AliasNameState original = harness.FindAlias("Alpha");
            Exception failure = timeout
                ? new ServiceResultException(StatusCodes.BadTimeout)
                : new IOException("Remote alias store unavailable.");
            harness.Query = (_, _, _, _, _) =>
                new ValueTask<IReadOnlyList<AliasNameVerboseDataType>>(
                    Task.FromException<IReadOnlyList<AliasNameVerboseDataType>>(failure));

            Exception actual = Assert.ThrowsAsync(failure.GetType(), async () =>
                await harness.Materializer.RefreshCategoryAsync(
                    harness.Store.Object, harness.CategoryId, materializeAliasNodes: true)
                    .ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(failure));
            Assert.That(harness.FindAlias("Alpha"), Is.SameAs(original));
            Assert.That(harness.Category.ReferenceExists(ReferenceTypeIds.Organizes, false, original.NodeId), Is.True);
            Assert.That(original.ReferenceExists(ReferenceTypeIds.AliasFor, false, harness.Target.NodeId), Is.True);
            Assert.That(harness.Target.ReferenceExists(ReferenceTypeIds.AliasFor, true, original.NodeId), Is.True);
        }

        [Test]
        public async Task NullStoreSnapshotPreservesAliasesAndAllowsLiveRetryAsync()
        {
            await using var harness = new RefreshHarness();
            await harness.InitializeAsync().ConfigureAwait(false);
            AliasNameState original = harness.FindAlias("Alpha");
            var queried = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            harness.Query = (id, pattern, reference, types, ct) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    queried.TrySetResult(true);
                    return new ValueTask<IReadOnlyList<AliasNameVerboseDataType>>(
                        (IReadOnlyList<AliasNameVerboseDataType>)null!);
                }
                return harness.Data.FindAliasVerboseAsync(id, pattern, reference, types, ct);
            };
            await using var coordinator = new AliasNameRefreshCoordinator(
                "NullAliasSnapshot", NUnitTelemetryContext.Create(),
                (_, ct) => harness.Materializer.RefreshCategoryAsync(
                    harness.Store.Object, harness.CategoryId, materializeAliasNodes: true, cancellationToken: ct));
            harness.Category.OnStateChanged += (_, _, _) =>
            {
                if (harness.FindAlias("Gamma") != null)
                {
                    published.TrySetResult(true);
                }
            };

            await harness.Data.AddAliasesAsync(harness.CategoryId,
                [new AliasAddRequest("Beta", harness.Target.NodeId, null, ReferenceTypeIds.AliasFor)])
                .ConfigureAwait(false);
            coordinator.RequestRefresh();
            await queried.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(harness.FindAlias("Alpha"), Is.SameAs(original));
            await harness.Data.AddAliasesAsync(harness.CategoryId,
                [new AliasAddRequest("Gamma", harness.Target.NodeId, null, ReferenceTypeIds.AliasFor)])
                .ConfigureAwait(false);
            coordinator.RequestRefresh();
            await published.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(2));
            Assert.That(harness.FindAlias("Alpha"), Is.SameAs(original));
            Assert.That(harness.FindAlias("Beta"), Is.Not.Null);
            Assert.That(harness.Target.ReferenceExists(
                ReferenceTypeIds.AliasFor, true, original.NodeId), Is.True);
            Assert.That(harness.Target.ReferenceExists(
                ReferenceTypeIds.AliasFor, true, harness.FindAlias("Gamma").NodeId), Is.True);
        }

        [Test]
        public async Task RefreshDiffRetainsUnchangedAliasesAndUpdatesActualTargetInversesAsync()
        {
            await using var harness = new RefreshHarness();
            harness.Data.Seed(harness.CategoryId, "Removed", harness.Target.NodeId, null, ReferenceTypeIds.AliasFor);
            await harness.InitializeAsync().ConfigureAwait(false);
            AliasNameState original = harness.FindAlias("Alpha");
            AliasNameState removed = harness.FindAlias("Removed");
            await harness.Data.AddAliasesAsync(harness.CategoryId,
                [new AliasAddRequest("Beta", harness.Target.NodeId, null, ReferenceTypeIds.AliasFor)])
                .ConfigureAwait(false);
            await harness.Data.DeleteAliasesAsync(harness.CategoryId,
                [new AliasDeleteRequest("Removed", harness.Target.NodeId)]).ConfigureAwait(false);

            await harness.RefreshAsync().ConfigureAwait(false);

            AliasNameState added = harness.FindAlias("Beta");
            Assert.That(harness.FindAlias("Alpha"), Is.SameAs(original));
            Assert.That(harness.FindAlias("Removed"), Is.Null);
            Assert.That(harness.Target.ReferenceExists(ReferenceTypeIds.AliasFor, true, original.NodeId), Is.True);
            Assert.That(harness.Target.ReferenceExists(ReferenceTypeIds.AliasFor, true, removed.NodeId), Is.False);
            Assert.That(added.ReferenceExists(ReferenceTypeIds.AliasFor, false, harness.Target.NodeId), Is.True);
            Assert.That(harness.Target.ReferenceExists(ReferenceTypeIds.AliasFor, true, added.NodeId), Is.True);
            Assert.That(harness.Category.ReferenceExists(ReferenceTypeIds.Organizes, false, removed.NodeId), Is.False);
            await harness.RefreshAsync().ConfigureAwait(false);
            Assert.That(harness.FindAlias("Beta"), Is.SameAs(added));
            Assert.That(harness.FindAlias("Alpha"), Is.SameAs(original));
        }

        [Test]
        public async Task RefreshUpdatesTargetsWithoutReplacingAliasInstanceAsync()
        {
            await using var harness = new RefreshHarness();
            await harness.InitializeAsync().ConfigureAwait(false);
            AliasNameState original = harness.FindAlias("Alpha");
            var replacement = new BaseDataVariableState(null) { NodeId = new NodeId("Replacement", 1) };
            await ((IAliasNameMaterializerHost)harness.Manager).RegisterNodeAsync(
                replacement, CancellationToken.None).ConfigureAwait(false);
            await harness.Data.AddAliasesAsync(harness.CategoryId,
                [new AliasAddRequest("Alpha", replacement.NodeId, null, ReferenceTypeIds.AliasFor)])
                .ConfigureAwait(false);
            await harness.Data.DeleteAliasesAsync(harness.CategoryId,
                [new AliasDeleteRequest("Alpha", harness.Target.NodeId)]).ConfigureAwait(false);

            await harness.RefreshAsync().ConfigureAwait(false);

            Assert.That(harness.FindAlias("Alpha"), Is.SameAs(original));
            Assert.That(original.ReferenceExists(ReferenceTypeIds.AliasFor, false, harness.Target.NodeId), Is.False);
            Assert.That(harness.Target.ReferenceExists(ReferenceTypeIds.AliasFor, true, original.NodeId), Is.False);
            Assert.That(original.ReferenceExists(ReferenceTypeIds.AliasFor, false, replacement.NodeId), Is.True);
            Assert.That(replacement.ReferenceExists(ReferenceTypeIds.AliasFor, true, original.NodeId), Is.True);
        }

        [Test]
        public async Task RefreshOptOutDoesNotQueryOrChangeTheSnapshotAsync()
        {
            await using var harness = new RefreshHarness();
            await harness.InitializeAsync().ConfigureAwait(false);
            AliasNameState original = harness.FindAlias("Alpha");
            await harness.Data.AddAliasesAsync(harness.CategoryId,
                [new AliasAddRequest("Beta", harness.Target.NodeId, null, ReferenceTypeIds.AliasFor)])
                .ConfigureAwait(false);
            harness.Store.Invocations.Clear();

            await harness.Materializer.RefreshCategoryAsync(
                harness.Store.Object, harness.CategoryId, materializeAliasNodes: false).ConfigureAwait(false);

            harness.Store.Verify(store => store.FindAliasVerboseAsync(
                It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<NodeId>(), It.IsAny<ITypeTable>(),
                It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(harness.FindAlias("Alpha"), Is.SameAs(original));
            Assert.That(harness.FindAlias("Beta"), Is.Null);
        }

        [Test]
        public async Task StaleRefreshGenerationDoesNotMutateTheSnapshotAsync()
        {
            await using var harness = new RefreshHarness();
            await harness.InitializeAsync().ConfigureAwait(false);
            AliasNameState original = harness.FindAlias("Alpha");
            await harness.Data.DeleteAliasesAsync(harness.CategoryId,
                [new AliasDeleteRequest("Alpha", harness.Target.NodeId)]).ConfigureAwait(false);

            await harness.Materializer.RefreshCategoryAsync(harness.Store.Object, harness.CategoryId,
                materializeAliasNodes: true, isCurrentGeneration: () => false).ConfigureAwait(false);

            Assert.That(harness.FindAlias("Alpha"), Is.SameAs(original));
            Assert.That(harness.Target.ReferenceExists(ReferenceTypeIds.AliasFor, true, original.NodeId), Is.True);
        }

        [TestCase(41u, 42u)]
        [TestCase(uint.MaxValue, 0u)]
        public async Task DelayedStoreEventsCannotRegressLastChangeAsync(uint previous, uint current)
        {
            await using var harness = new RefreshHarness();
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.Store.Setup(store => store.GetLastChange(harness.CategoryId)).Returns(previous);
            harness.Store.Raise(store => store.Changed += null,
                new AliasStoreChangedEventArgs(harness.CategoryId, previous));
            harness.Store.Setup(store => store.GetLastChange(harness.CategoryId)).Returns(current);
            harness.Store.Raise(store => store.Changed += null,
                new AliasStoreChangedEventArgs(harness.CategoryId, current));

            harness.Store.Raise(store => store.Changed += null,
                new AliasStoreChangedEventArgs(harness.CategoryId, previous));

            Assert.That(harness.Category.LastChange.Value, Is.EqualTo(current));
        }

        [Test]
        public async Task LiveRefreshPublishesAliasesAndKeepsUnchangedNodesAsync()
        {
            await using var harness = new RefreshHarness(refreshOnChange: true);
            await harness.InitializeAsync().ConfigureAwait(false);
            AliasNameState original = harness.FindAlias("Alpha");
            var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Category.OnStateChanged += (_, _, _) =>
            {
                if (harness.FindAlias("Beta") != null)
                {
                    published.TrySetResult(true);
                }
            };

            await harness.Data.AddAliasesAsync(harness.CategoryId,
                [new AliasAddRequest("Beta", harness.Target.NodeId, null, ReferenceTypeIds.AliasFor)])
                .ConfigureAwait(false);
            await published.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(harness.FindAlias("Alpha"), Is.SameAs(original));
            Assert.That(harness.Target.ReferenceExists(
                ReferenceTypeIds.AliasFor, true, harness.FindAlias("Beta").NodeId), Is.True);
        }

        [Test]
        public async Task ChangeStormUsesOneWorkerAndOnePendingSignalAsync()
        {
            var entered = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            await using var coordinator = new AliasNameRefreshCoordinator(
                "AliasStormTest", NUnitTelemetryContext.Create(), async (generation, ct) =>
                {
                    if (Interlocked.Increment(ref calls) == 1)
                    {
                        entered.TrySetResult(generation);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        second.TrySetResult(true);
                        try
                        {
                            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            stopped.TrySetResult(true);
                        }
                    }
                });
            coordinator.RequestRefresh();
            long firstGeneration = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            for (int ii = 0; ii < 10000; ii++)
            {
                coordinator.RequestRefresh();
            }
            Assert.That(coordinator.PendingTaskCount, Is.EqualTo(1));
            Assert.That(coordinator.PendingRefreshCount, Is.EqualTo(1));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(coordinator.IsCurrent(firstGeneration), Is.False);
            release.TrySetResult(true);
            await second.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            await coordinator.DisposeAsync().ConfigureAwait(false);
            coordinator.RequestRefresh();

            Assert.That(stopped.Task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(coordinator.PendingTaskCount, Is.Zero);
            Assert.That(coordinator.PendingRefreshCount, Is.Zero);
            Assert.That(calls, Is.EqualTo(2));
        }

        [TestCaseSource(nameof(s_backendFailures))]
        public async Task RefreshFaultIsLoggedAndNextChangeRetriesAsync(Exception failure)
        {
            await using var harness = new RefreshHarness();
            await harness.InitializeAsync().ConfigureAwait(false);
            AliasNameState original = harness.FindAlias("Alpha");
            ITelemetryContext telemetry = CreateRefreshTelemetry(out Mock<ILogger> logger);
            Task<bool> logged = ObserveError(logger, "AliasRefreshFailed");
            Task<bool> terminated = ObserveError(logger, "BackgroundTaskFailed");
            var retried = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            harness.Query = (id, pattern, reference, types, ct) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw failure;
                }
                return harness.Data.FindAliasVerboseAsync(id, pattern, reference, types, ct);
            };
            await using var coordinator = new AliasNameRefreshCoordinator(
                "AliasFailureTest", telemetry, async (_, ct) =>
                {
                    await harness.Materializer.RefreshCategoryAsync(
                        harness.Store.Object, harness.CategoryId, materializeAliasNodes: true, cancellationToken: ct)
                        .ConfigureAwait(false);
                    retried.TrySetResult(true);
                });

            await harness.Data.AddAliasesAsync(harness.CategoryId,
                [new AliasAddRequest("Beta", harness.Target.NodeId, null, ReferenceTypeIds.AliasFor)])
                .ConfigureAwait(false);
            coordinator.RequestRefresh();
            Task<bool> outcome = await Task.WhenAny(logged, terminated).ConfigureAwait(false);

            Assert.That(outcome, Is.SameAs(logged), "A backend failure must not terminate the only refresh worker.");
            Assert.That(harness.FindAlias("Alpha"), Is.SameAs(original));
            Assert.That(harness.FindAlias("Beta"), Is.Null);
            Assert.That(harness.Target.ReferenceExists(ReferenceTypeIds.AliasFor, true, original.NodeId), Is.True);

            await harness.Data.AddAliasesAsync(harness.CategoryId,
                [new AliasAddRequest("Gamma", harness.Target.NodeId, null, ReferenceTypeIds.AliasFor)])
                .ConfigureAwait(false);
            coordinator.RequestRefresh();
            await retried.Task.ConfigureAwait(false);

            logger.Verify(log => log.Log(LogLevel.Error,
                It.Is<EventId>(id => id.Name == "AliasRefreshFailed"), It.IsAny<It.IsAnyType>(), failure,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
            Assert.That(terminated.IsCompleted, Is.False);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(coordinator.PendingTaskCount, Is.EqualTo(1));
            Assert.That(harness.FindAlias("Alpha"), Is.SameAs(original));
            Assert.That(harness.FindAlias("Beta"), Is.Not.Null);
            Assert.That(harness.FindAlias("Gamma"), Is.Not.Null);
            Assert.That(harness.Target.ReferenceExists(
                ReferenceTypeIds.AliasFor, true, harness.FindAlias("Beta").NodeId), Is.True);
            Assert.That(harness.Target.ReferenceExists(
                ReferenceTypeIds.AliasFor, true, harness.FindAlias("Gamma").NodeId), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ShutdownDistinguishesCancellationFromBackendFailureAsync(bool backendFailure)
        {
            ITelemetryContext telemetry = CreateRefreshTelemetry(out Mock<ILogger> logger);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failure = new HttpRequestException("Alias store failed during shutdown.");
            int calls = 0;
            await using var coordinator = new AliasNameRefreshCoordinator(
                "AliasShutdownTest", telemetry, async (_, ct) =>
                {
                    Interlocked.Increment(ref calls);
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    if (backendFailure)
                    {
                        throw failure;
                    }
                    ct.ThrowIfCancellationRequested();
                });
            try
            {
                coordinator.RequestRefresh();
                await entered.Task.ConfigureAwait(false);
                coordinator.RequestRefresh();
                coordinator.Dispose();
            }
            finally
            {
                release.TrySetResult(true);
            }
            await coordinator.DisposeAsync().ConfigureAwait(false);
            coordinator.RequestRefresh();

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(coordinator.PendingTaskCount, Is.Zero);
            Assert.That(coordinator.PendingRefreshCount, Is.Zero);
            logger.Verify(log => log.Log(LogLevel.Error,
                It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), backendFailure ? Times.Once : Times.Never);
            logger.Verify(log => log.Log(LogLevel.Error,
                It.Is<EventId>(id => id.Name == "AliasRefreshFailed"), It.IsAny<It.IsAnyType>(), failure,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), backendFailure ? Times.Once : Times.Never);
            logger.Verify(log => log.Log(LogLevel.Error,
                It.Is<EventId>(id => id.Name == "BackgroundTaskFailed"), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Never);
        }

        [TestCaseSource(nameof(s_fatalFailures))]
        public async Task FatalRefreshFailureIsNotRetriedAsync(Exception failure)
        {
            ITelemetryContext telemetry = CreateRefreshTelemetry(out Mock<ILogger> logger);
            Task<bool> logged = ObserveError(logger, "AliasRefreshFailed");
            Task<bool> terminated = ObserveError(logger, "BackgroundTaskFailed");
            int calls = 0;
            await using var coordinator = new AliasNameRefreshCoordinator(
                "AliasFatalTest", telemetry, (_, _) =>
                {
                    Interlocked.Increment(ref calls);
                    throw failure;
                });

            coordinator.RequestRefresh();
            Task<bool> outcome = await Task.WhenAny(logged, terminated).ConfigureAwait(false);
            Assert.That(outcome, Is.SameAs(terminated));
            await coordinator.DisposeAsync().ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(coordinator.PendingTaskCount, Is.Zero);
            logger.Verify(log => log.Log(LogLevel.Error,
                It.Is<EventId>(id => id.Name == "BackgroundTaskFailed"), It.IsAny<It.IsAnyType>(), failure,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
            logger.Verify(log => log.Log(LogLevel.Error,
                It.Is<EventId>(id => id.Name == "AliasRefreshFailed"), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Never);
        }

        [Test]
        public async Task HostDisposalCancelsAndDrainsAnInFlightStoreQueryAsync()
        {
            await using var harness = new RefreshHarness(refreshOnChange: true);
            await harness.InitializeAsync().ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Query = async (_, _, _, _, ct) =>
            {
                entered.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    return [];
                }
                finally
                {
                    stopped.TrySetResult(true);
                }
            };
            await harness.Data.AddAliasesAsync(harness.CategoryId,
                [new AliasAddRequest("Beta", harness.Target.NodeId, null, ReferenceTypeIds.AliasFor)])
                .ConfigureAwait(false);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            await harness.Manager.DisposeAsync().ConfigureAwait(false);

            Assert.That(stopped.Task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            harness.Store.Invocations.Clear();
            await harness.Data.AddAliasesAsync(harness.CategoryId,
                [new AliasAddRequest("Gamma", harness.Target.NodeId, null, ReferenceTypeIds.AliasFor)])
                .ConfigureAwait(false);
            harness.Store.Verify(store => store.FindAliasVerboseAsync(
                It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<NodeId>(), It.IsAny<ITypeTable>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task AncestorNotificationsCoalesceWithoutPublishingStaleGenerationsAsync()
        {
            await using var harness = new RefreshHarness(refreshOnChange: true, nested: true);
            await harness.InitializeAsync().ConfigureAwait(false);
            AliasNameState original = harness.FindAlias("Alpha");
            AliasNameCategoryState child = harness.Manager.FindPredefinedNode<AliasNameCategoryState>(
                harness.ChildCategoryId);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var queried = new ConcurrentQueue<NodeId>();
            var observedConcurrency = new ConcurrentQueue<int>();
            var publishedCounts = new ConcurrentQueue<int>();
            int active = 0;
            harness.Query = async (id, pattern, reference, types, ct) =>
            {
                queried.Enqueue(id);
                observedConcurrency.Enqueue(Interlocked.Increment(ref active));
                try
                {
                    IReadOnlyList<AliasNameVerboseDataType> snapshot = await harness.Data.FindAliasVerboseAsync(
                        id, pattern, reference, types, ct).ConfigureAwait(false);
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    return snapshot;
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            };
            child.OnStateChanged += (_, _, _) =>
            {
                if (harness.FindAlias("Nested", harness.ChildCategoryId) != null)
                {
                    var references = new List<IReference>();
                    child.GetReferences(harness.Manager.SystemContext, references);
                    publishedCounts.Enqueue(references.FindAll(
                        reference => !reference.IsInverse && reference.ReferenceTypeId == ReferenceTypeIds.Organizes)
                        .Count);
                    if (harness.FindAlias("Child31", harness.ChildCategoryId) != null)
                    {
                        published.TrySetResult(true);
                    }
                }
            };
            await harness.Data.AddAliasesAsync(harness.ChildCategoryId,
                [new AliasAddRequest("Nested", harness.Target.NodeId, null, ReferenceTypeIds.AliasFor)])
                .ConfigureAwait(false);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            for (int ii = 0; ii < 32; ii++)
            {
                await harness.Data.AddAliasesAsync(harness.ChildCategoryId,
                    [new AliasAddRequest("Child" + ii, harness.Target.NodeId, null, ReferenceTypeIds.AliasFor)])
                    .ConfigureAwait(false);
            }
            Assert.That(queried, Has.Count.EqualTo(1));
            release.TrySetResult(true);
            await published.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(queried, Has.Count.EqualTo(2));
            Assert.That(queried, Is.All.EqualTo(harness.CategoryId));
            Assert.That(observedConcurrency, Is.All.EqualTo(1));
            Assert.That(publishedCounts, Is.Not.Empty.And.All.EqualTo(33));
            Assert.That(harness.FindAlias("Alpha"), Is.SameAs(original));
            Assert.That(child.LastChange.Value, Is.EqualTo(33u));
            Assert.That(harness.Category.LastChange.Value, Is.EqualTo(33u));
        }

        private static ITelemetryContext CreateRefreshTelemetry(out Mock<ILogger> logger)
        {
            logger = new Mock<ILogger>();
            logger.Setup(log => log.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            var factory = new Mock<ILoggerFactory>();
            factory.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(logger.Object);
            var telemetry = new Mock<ITelemetryContext>();
            telemetry.SetupGet(value => value.LoggerFactory).Returns(factory.Object);
            return telemetry.Object;
        }

        private static Task<bool> ObserveError(Mock<ILogger> logger, string eventName)
        {
            var observed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            logger.Setup(log => log.Log(LogLevel.Error,
                It.Is<EventId>(id => id.Name == eventName), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()))
                .Callback(() => observed.TrySetResult(true));
            return observed.Task;
        }

        private sealed class RefreshHarness : IAsyncDisposable
        {
            public delegate ValueTask<IReadOnlyList<AliasNameVerboseDataType>> AliasQuery(
                NodeId categoryId,
                string pattern,
                NodeId referenceType,
                ITypeTable typeTable,
                CancellationToken cancellationToken);

            public RefreshHarness(bool refreshOnChange = false, bool nested = false)
            {
                Mock<IServerInternal> server = DeterministicServerMock.Create(out m_queues);
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                server.SetupGet(instance => instance.Telemetry).Returns(telemetry);
                var descriptor = new AliasNameCategoryDescriptor(CategoryId,
                    new QualifiedName("Category", 1), AliasNameCapabilities.All,
                    nested
                        ? [new AliasNameCategoryDescriptor(
                            ChildCategoryId, new QualifiedName("Child", 1), AliasNameCapabilities.All)]
                        : null);
                Data = new InMemoryAliasNameStore([descriptor]);
                Data.Seed(CategoryId, "Alpha", Target.NodeId, null, ReferenceTypeIds.AliasFor);
                Store.SetupGet(store => store.RootCategories).Returns(Data.RootCategories);
                Store.Setup(store => store.OwnsCategory(It.IsAny<NodeId>()))
                    .Returns((NodeId id) => Data.OwnsCategory(id));
                Store.Setup(store => store.GetLastChange(It.IsAny<NodeId>()))
                    .Returns((NodeId id) => Data.GetLastChange(id));
                Store.Setup(store => store.FindAliasVerboseAsync(
                    It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<NodeId>(), It.IsAny<ITypeTable>(),
                    It.IsAny<CancellationToken>()))
                    .Returns((NodeId id, string pattern, NodeId reference, ITypeTable types, CancellationToken ct) =>
                    {
                        AliasQuery query = Query;
                        return query != null
                            ? query(id, pattern, reference, types, ct)
                            : Data.FindAliasVerboseAsync(id, pattern, reference, types, ct);
                    });
                Data.Changed += (_, args) => Store.Raise(store => store.Changed += null, args);
                Manager = new AliasNameNodeManager(server.Object, new ApplicationConfiguration(), Store.Object,
                    new AliasNameNodeManagerOptions
                    {
                        NamespaceUri = DeterministicServerMock.TestNamespaceUri,
                        RegisterWithServerRegistry = false,
                        RefreshAliasNodesOnChange = refreshOnChange
                    });
                m_registry.Register(Store.Object);
                Materializer = new AliasNameNodeMaterializer(
                    Manager, m_registry, _ => false,
                    telemetry.CreateLogger<AliasNameNodeMaterializer>());
            }

            public NodeId CategoryId { get; } = new("Category", 1);

            public NodeId ChildCategoryId { get; } = new("Child", 1);

            public BaseDataVariableState Target { get; } = new(null)
            {
                NodeId = new NodeId("Target", 1),
                BrowseName = new QualifiedName("Target", 1),
                DataType = DataTypeIds.Int32,
                Value = 42
            };

            public InMemoryAliasNameStore Data { get; }

            public Mock<IAliasNameStore> Store { get; } = new(MockBehavior.Strict);

            public AliasQuery Query
            {
                get => Volatile.Read(ref m_query);
                set => Volatile.Write(ref m_query, value);
            }

            public AliasNameNodeManager Manager { get; }

            public AliasNameNodeMaterializer Materializer { get; }

            public AliasNameCategoryState Category =>
                Manager.FindPredefinedNode<AliasNameCategoryState>(CategoryId);

            public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
            {
                await ((IAliasNameMaterializerHost)Manager).RegisterNodeAsync(Target, cancellationToken)
                    .ConfigureAwait(false);
                var references = new Dictionary<NodeId, IList<IReference>>();
                await Manager.CreateAddressSpaceAsync(references, cancellationToken).ConfigureAwait(false);
                await Manager.AddReferencesAsync(references, cancellationToken).ConfigureAwait(false);
            }

            public AliasNameState FindAlias(string name, NodeId categoryId = default)
            {
                return Manager.FindPredefinedNode<AliasNameState>(
                    new NodeId(Utils.Format("{0}.{1}", categoryId.IsNull ? CategoryId : categoryId, name), 1));
            }

            public ValueTask RefreshAsync()
            {
                return Materializer.RefreshCategoryAsync(Store.Object, CategoryId, materializeAliasNodes: true);
            }

            public async ValueTask DisposeAsync()
            {
                await Manager.DisposeAsync().ConfigureAwait(false);
                m_registry.Dispose();
                Data.Dispose();
                m_queues.Dispose();
            }

            private readonly AliasNameStoreRegistry m_registry = new();
            private readonly MonitoredItemQueueFactory m_queues;
            private AliasQuery m_query;
        }

        private static readonly TestCaseData[] s_backendFailures =
        [
            new TestCaseData(new IOException("Alias store offline."))
                .SetArgDisplayNames(nameof(IOException)),
            new TestCaseData(new HttpRequestException("Alias HTTP store offline."))
                .SetArgDisplayNames(nameof(HttpRequestException)),
            new TestCaseData(new Mock<DbException>().Object)
                .SetArgDisplayNames(nameof(DbException)),
            new TestCaseData(new SocketException((int)SocketError.ConnectionReset))
                .SetArgDisplayNames(nameof(SocketException)),
            new TestCaseData(new Mock<NullReferenceException>().Object)
                .SetArgDisplayNames(nameof(NullReferenceException)),
            new TestCaseData(new OperationCanceledException("Alias query canceled.", new CancellationToken(true)))
                .SetArgDisplayNames(nameof(OperationCanceledException))
        ];

        private static readonly TestCaseData[] s_fatalFailures =
        [
            new TestCaseData(new Mock<OutOfMemoryException>().Object)
                .SetArgDisplayNames(nameof(OutOfMemoryException)),
            new TestCaseData(new Mock<AccessViolationException>().Object)
                .SetArgDisplayNames(nameof(AccessViolationException))
        ];
    }
}
