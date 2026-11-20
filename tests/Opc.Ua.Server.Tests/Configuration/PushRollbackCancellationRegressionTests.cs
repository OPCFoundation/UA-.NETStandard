// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies that push-configuration compensation uses cancellation independent of the failed request.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    public sealed class PushRollbackCancellationRegressionTests
    {
        /// <summary>
        /// Verifies that request cancellation still rolls back committed operations in reverse order.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RequestCancellationDoesNotCancelCompensationForCommittedOperationsAsync(bool tokenException)
        {
            using var request = new CancellationTokenSource();
            var coordinator = new PushConfigurationTransactionCoordinator(NUnitTelemetryContext.Create());
            var active = new HashSet<int>();
            var reversed = new List<int>();
            int disposed = 0;
            for (int i = 1; i <= 2; i++)
            {
                int operation = i;
                coordinator.Stage(s_sessionId, new PushConfigurationOperation
                {
                    AffectedTrustList = new NodeId((uint)operation, 1),
                    CommitAsync = _ =>
                    {
                        active.Add(operation);
                        return Task.CompletedTask;
                    },
                    RollbackAsync = ct =>
                    {
                        ct.ThrowIfCancellationRequested();
                        active.Remove(operation);
                        reversed.Add(operation);
                        return Task.CompletedTask;
                    },
                    DisposeStaged = () => disposed++
                });
            }
            coordinator.Stage(s_sessionId, new PushConfigurationOperation
            {
                AffectedTrustList = new NodeId(3, 1),
                CommitAsync = ct =>
                {
                    request.Cancel();
                    if (tokenException)
                    {
                        ct.ThrowIfCancellationRequested();
                    }
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "original failure");
                },
                DisposeStaged = () => disposed++
            });
            ServiceResult result = await coordinator.ApplyChangesAsync(s_sessionId, request.Token).ConfigureAwait(false);
            Assert.That(active, Is.Empty);
            Assert.That(reversed, Is.EqualTo(s_expectedReverseOrder));
            Assert.That(result.StatusCode, Is.EqualTo(tokenException ? StatusCodes.Bad : StatusCodes.BadCertificateInvalid));
            PushConfigurationTransactionSnapshot snapshot = coordinator.GetSnapshot();
            Assert.That(snapshot.Errors, Has.Count.EqualTo(1));
            Assert.That(snapshot.Errors[0].TargetId, Is.EqualTo(new NodeId(3, 1)));
            Assert.That(snapshot.Errors[0].Error, Is.EqualTo(result.StatusCode));
            Assert.That(snapshot.IsActive, Is.False);
            Assert.That(disposed, Is.EqualTo(3));
        }

        /// <summary>
        /// Verifies that an expired rollback deadline records the failure but still compensates earlier operations.
        /// </summary>
        [Test]
        public async Task RollbackDeadlineIsIndependentAndDoesNotPreventEarlierCompensationAsync()
        {
            var clock = new FakeTimeProvider();
            var coordinator = new PushConfigurationTransactionCoordinator(NUnitTelemetryContext.Create(), clock);
            var reversed = new List<int>();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var safety = new CancellationTokenSource();
            int disposed = 0;
            coordinator.Stage(s_sessionId, new PushConfigurationOperation
            {
                AffectedTrustList = new NodeId(1, 1),
                CommitAsync = _ => Task.CompletedTask,
                RollbackAsync = ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    reversed.Add(1);
                    return Task.CompletedTask;
                },
                DisposeStaged = () => disposed++
            });
            coordinator.Stage(s_sessionId, new PushConfigurationOperation
            {
                AffectedTrustList = new NodeId(2, 1),
                CommitAsync = _ => Task.CompletedTask,
                RollbackAsync = async ct =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, safety.Token);
                    entered.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, linked.Token).ConfigureAwait(false);
                },
                DisposeStaged = () => disposed++
            });
            coordinator.Stage(s_sessionId, new PushConfigurationOperation
            {
                AffectedTrustList = new NodeId(3, 1),
                CommitAsync = _ => throw new ServiceResultException(StatusCodes.BadCertificateInvalid),
                DisposeStaged = () => disposed++
            });
            Task<ServiceResult> applying = coordinator.ApplyChangesAsync(s_sessionId, CancellationToken.None).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromSeconds(29));
                Assert.That(applying.IsCompleted, Is.False);
                Assert.That(disposed, Is.Zero);
                clock.Advance(TimeSpan.FromSeconds(1));
                ServiceResult result = await applying.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
                Assert.That(reversed, Is.EqualTo(s_expectedEarlierRollback));
                PushConfigurationTransactionSnapshot snapshot = coordinator.GetSnapshot();
                Assert.That(snapshot.Errors, Has.Count.EqualTo(2));
                Assert.That(snapshot.Errors[1].TargetId, Is.EqualTo(new NodeId(2, 1)));
                Assert.That(StatusCode.IsBad(snapshot.Errors[1].Error), Is.True);
                Assert.That(disposed, Is.EqualTo(3));
            }
            finally
            {
                safety.Cancel();
                await applying.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Identifies the session that owns the staged configuration transaction.
        /// </summary>
        private static readonly NodeId s_sessionId = new(100, 1);

        /// <summary>
        /// Defines the compensation order for the two successfully committed operations.
        /// </summary>
        private static readonly int[] s_expectedReverseOrder = [2, 1];

        /// <summary>
        /// Identifies the earlier operation that must still be compensated after a later rollback times out.
        /// </summary>
        private static readonly int[] s_expectedEarlierRollback = [1];
    }
}
