// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.GranulateDrying.Server;

namespace Opc.Ua.GranulateDrying.Tests
{
    /// <summary>
    /// Verifies cancellation admission and complete drainage independently of physical-provider timing.
    /// </summary>
    [TestFixture]
    public sealed class GranulateDryingOperationLifetimeTests
    {
        /// <summary>
        /// All concurrent disposers wait for every lease, including a caller queued at an execution gate.
        /// </summary>
        [Test]
        public async Task ConcurrentDisposersWaitForAllAdmittedCallsAsync()
        {
            await using var lifetime = new GranulateDryingOperationLifetime();
            using GranulateDryingOperationLifetime.Lease active = lifetime.Enter(CancellationToken.None);
            using GranulateDryingOperationLifetime.Lease queued = lifetime.Enter(CancellationToken.None);
            Task first = lifetime.DisposeAsync().AsTask();
            Task second = lifetime.DisposeAsync().AsTask();
            Assert.Multiple(() =>
            {
                Assert.That(active.Token.IsCancellationRequested, Is.True);
                Assert.That(queued.Token.IsCancellationRequested, Is.True);
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(second.IsCompleted, Is.False);
            });
            active.Dispose();
            Assert.That(first.IsCompleted, Is.False);
            queued.Dispose();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.Throws<ObjectDisposedException>(() => lifetime.Enter(CancellationToken.None));
        }

        /// <summary>
        /// Caller cancellation does not cancel the shutdown-only token needed to publish an already committed result.
        /// </summary>
        [Test]
        public async Task CommittedPublicationRetainsShutdownOnlyCancellationAsync()
        {
            await using var lifetime = new GranulateDryingOperationLifetime();
            using var caller = new CancellationTokenSource();
            using GranulateDryingOperationLifetime.Lease operation = lifetime.Enter(caller.Token);
            caller.Cancel();
            Assert.That(operation.Token.IsCancellationRequested, Is.True);
            Assert.That(operation.StoppingToken.IsCancellationRequested, Is.False);
            Task disposing = lifetime.DisposeAsync().AsTask();
            Assert.That(operation.StoppingToken.IsCancellationRequested, Is.True);
            operation.Dispose();
            await disposing.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }

        /// <summary>
        /// A failed provider cancellation callback is surfaced only after admitted work has drained.
        /// </summary>
        [Test]
        public async Task CancellationCallbackFailureStillDrainsOperationsAsync()
        {
            var lifetime = new GranulateDryingOperationLifetime();
            using GranulateDryingOperationLifetime.Lease operation = lifetime.Enter(CancellationToken.None);
            using CancellationTokenRegistration registration = operation.Token.Register(
                () => throw new InvalidOperationException("Deliberate cancellation failure."));
            Task disposing = lifetime.DisposeAsync().AsTask();
            Assert.That(disposing.IsCompleted, Is.False);
            operation.Dispose();
            await Assert.ThrowsAsync<AggregateException>(async () =>
                await disposing.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            await Assert.ThrowsAsync<AggregateException>(async () =>
                await lifetime.DisposeAsync().ConfigureAwait(false));
        }
    }
}
