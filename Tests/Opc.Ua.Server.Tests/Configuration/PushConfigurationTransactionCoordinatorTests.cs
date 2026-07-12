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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Direct unit tests of <see cref="PushConfigurationTransactionCoordinator"/>,
    /// independent of the address-space / method-call plumbing exercised by
    /// <see cref="ConfigurationNodeManagerPushTests"/>. Uses distinct,
    /// deterministic Session NodeIds (unlike the shared-fixture push tests,
    /// which all resolve to <see cref="NodeId.Null"/>) so cross-Session
    /// ownership behavior can be verified precisely.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [Parallelizable(ParallelScope.None)]
    public class PushConfigurationTransactionCoordinatorTests
    {
        private static readonly int[] s_expectedCommitOrder = [1, 2];
        private static readonly int[] s_expectedRollbackOrder = [2, 1];

        private static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.Create();
        private static readonly NodeId s_sessionA = new(Guid.NewGuid(), 1);
        private static readonly NodeId s_sessionB = new(Guid.NewGuid(), 1);

        [Test]
        public void NewCoordinatorHasNoActiveTransaction()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);

            Assert.That(coordinator.IsTransactionActive, Is.False);
            Assert.That(coordinator.OwnerSessionId.IsNull, Is.True);
            Assert.That(coordinator.HasOpenTrustListWriter, Is.False);
        }

        [Test]
        public void StageStartsTransactionOwnedByStagingSession()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            bool disposed = false;

            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ => Task.CompletedTask,
                DisposeStaged = () => disposed = true
            });

            Assert.That(coordinator.IsTransactionActive, Is.True);
            Assert.That(coordinator.OwnerSessionId, Is.EqualTo(s_sessionA));
            Assert.That(disposed, Is.False);
        }

        [Test]
        public void StageFromAnotherSessionThrowsBadTransactionPending()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ => Task.CompletedTask
            });

            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                coordinator.Stage(s_sessionB, new PushConfigurationOperation
                {
                    CommitAsync = _ => Task.CompletedTask
                }));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadTransactionPending));
        }

        [Test]
        public void ValidateSessionCanParticipateThrowsForConflictingSession()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ => Task.CompletedTask
            });

            Assert.DoesNotThrow(() => coordinator.ValidateSessionCanParticipate(s_sessionA));
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                coordinator.ValidateSessionCanParticipate(s_sessionB));
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadTransactionPending));
        }

        [Test]
        public void StagingSameSlotTwiceSupersedesAndDisposesThePreviousOperation()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            var group = new NodeId(Guid.NewGuid(), 1);
            var certType = new NodeId(Guid.NewGuid(), 1);
            bool firstDisposed = false;
            bool secondDisposed = false;

            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                AffectedCertificateGroup = group,
                AffectedCertificateType = certType,
                CommitAsync = _ => Task.CompletedTask,
                DisposeStaged = () => firstDisposed = true
            });
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                AffectedCertificateGroup = group,
                AffectedCertificateType = certType,
                CommitAsync = _ => Task.CompletedTask,
                DisposeStaged = () => secondDisposed = true
            });

            Assert.That(firstDisposed, Is.True, "the superseded operation must be disposed immediately");
            Assert.That(secondDisposed, Is.False);
        }

        [Test]
        public async Task ApplyChangesWithNoActiveTransactionReturnsBadNothingToDoAsync()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);

            ServiceResult result = await coordinator.ApplyChangesAsync(s_sessionA, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task ApplyChangesFromWrongSessionReturnsBadSessionIdInvalidAsync()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ => Task.CompletedTask
            });

            ServiceResult result = await coordinator.ApplyChangesAsync(s_sessionB, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(coordinator.IsTransactionActive, Is.True, "a wrong-Session apply must not clear the transaction");
        }

        [Test]
        public async Task ApplyChangesCommitsOperationsInRequestOrderAsync()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            var order = new System.Collections.Generic.List<int>();

            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ =>
                {
                    order.Add(1);
                    return Task.CompletedTask;
                }
            });
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                AffectedTrustList = new NodeId(Guid.NewGuid(), 1),
                CommitAsync = _ =>
                {
                    order.Add(2);
                    return Task.CompletedTask;
                }
            });

            ServiceResult result = await coordinator.ApplyChangesAsync(s_sessionA, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(order, Is.EqualTo(s_expectedCommitOrder));
            Assert.That(coordinator.IsTransactionActive, Is.False);
        }

        [Test]
        public async Task ApplyChangesClearsOwnershipAtomicallySoANewSessionCanImmediatelyStageAsync()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ => Task.CompletedTask
            });

            ServiceResult result = await coordinator.ApplyChangesAsync(s_sessionA, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True);

            Assert.DoesNotThrow(() =>
                coordinator.Stage(s_sessionB, new PushConfigurationOperation
                {
                    CommitAsync = _ => Task.CompletedTask
                }));
            Assert.That(coordinator.OwnerSessionId, Is.EqualTo(s_sessionB));
        }

        [Test]
        public async Task StagingFromAnotherSessionDuringAnInFlightCommitReturnsBadTransactionPendingThenSucceedsImmediatelyAfterCompletionAsync()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            var commitEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var commitGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = async _ =>
                {
                    // Simulate in-flight, asynchronous store/registry I/O
                    // that the test can hold open for as long as needed.
                    commitEntered.TrySetResult(true);
                    await commitGate.Task.ConfigureAwait(false);
                }
            });

            // Session A starts committing. Its single staged operation
            // blocks on commitGate until this test releases it below.
            Task<ServiceResult> applyTask = coordinator.ApplyChangesAsync(s_sessionA, CancellationToken.None)
                .AsTask();

            await commitEntered.Task.ConfigureAwait(false);

            // While Session A's commit is still in flight, Session B must
            // not be able to stage against the same certificate/TrustList
            // resources: ownership is only released once the commit and
            // its diagnostics finalization both complete.
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                coordinator.Stage(s_sessionB, new PushConfigurationOperation
                {
                    CommitAsync = _ => Task.CompletedTask
                }));
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadTransactionPending));
            Assert.That(coordinator.IsTransactionActive, Is.True);
            Assert.That(coordinator.OwnerSessionId, Is.EqualTo(s_sessionA));

            // Release the blocked commit and let ApplyChanges finish.
            commitGate.TrySetResult(true);
            ServiceResult result = await applyTask.ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True);

            // Now that commit and diagnostics finalization have both
            // completed, Session B must be able to stage and succeed
            // immediately, without needing to retry.
            Assert.DoesNotThrow(() =>
                coordinator.Stage(s_sessionB, new PushConfigurationOperation
                {
                    CommitAsync = _ => Task.CompletedTask
                }));
            Assert.That(coordinator.OwnerSessionId, Is.EqualTo(s_sessionB));
            Assert.That(coordinator.IsTransactionActive, Is.True);
        }

        [Test]
        public async Task ApplyChangesWithFailingCommitRollsBackEarlierOperationsInReverseOrderAsync()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            var rollbackOrder = new System.Collections.Generic.List<int>();

            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ => Task.CompletedTask,
                RollbackAsync = _ =>
                {
                    rollbackOrder.Add(1);
                    return Task.CompletedTask;
                }
            });
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                AffectedTrustList = new NodeId(Guid.NewGuid(), 1),
                CommitAsync = _ => Task.CompletedTask,
                RollbackAsync = _ =>
                {
                    rollbackOrder.Add(2);
                    return Task.CompletedTask;
                }
            });
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                AffectedTrustList = new NodeId(Guid.NewGuid(), 1),
                CommitAsync = _ => throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "boom")
            });

            ServiceResult result = await coordinator.ApplyChangesAsync(s_sessionA, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
            // Only the two operations that actually committed are rolled
            // back, in reverse order; the failing (third) operation never
            // committed so it has nothing to compensate.
            Assert.That(rollbackOrder, Is.EqualTo(s_expectedRollbackOrder));
        }

        [Test]
        public async Task ApplyChangesDisposesEveryStagedOperationRegardlessOfOutcomeAsync()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            bool firstDisposed = false;
            bool secondDisposed = false;

            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ => Task.CompletedTask,
                DisposeStaged = () => firstDisposed = true
            });
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                AffectedTrustList = new NodeId(Guid.NewGuid(), 1),
                CommitAsync = _ => throw new InvalidOperationException("boom"),
                DisposeStaged = () => secondDisposed = true
            });

            await coordinator.ApplyChangesAsync(s_sessionA, CancellationToken.None).ConfigureAwait(false);

            Assert.That(firstDisposed, Is.True);
            Assert.That(secondDisposed, Is.True);
        }

        [Test]
        public async Task ApplyChangesWithOpenTrustListWriterReturnsBadInvalidStateWithoutClearingTransactionAsync()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            var trustListId = new NodeId(Guid.NewGuid(), 1);
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                AffectedTrustList = trustListId,
                CommitAsync = _ => Task.CompletedTask
            });
            coordinator.SetTrustListWriteOpen(trustListId, true);

            ServiceResult result = await coordinator.ApplyChangesAsync(s_sessionA, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(coordinator.IsTransactionActive, Is.True);

            coordinator.SetTrustListWriteOpen(trustListId, false);
            ServiceResult retryResult = await coordinator.ApplyChangesAsync(s_sessionA, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(retryResult), Is.True);
        }

        [Test]
        public void CancelChangesWithNoActiveTransactionReturnsBadNothingToDo()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);

            ServiceResult result = coordinator.CancelChanges(s_sessionA);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public void CancelChangesFromWrongSessionReturnsBadSessionIdInvalid()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ => Task.CompletedTask
            });

            ServiceResult result = coordinator.CancelChanges(s_sessionB);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(coordinator.IsTransactionActive, Is.True);
        }

        [Test]
        public void CancelChangesDiscardsStagedOperationsWithoutCommitting()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            bool committed = false;
            bool disposed = false;

            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ =>
                {
                    committed = true;
                    return Task.CompletedTask;
                },
                DisposeStaged = () => disposed = true
            });

            ServiceResult result = coordinator.CancelChanges(s_sessionA);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(committed, Is.False);
            Assert.That(disposed, Is.True);
            Assert.That(coordinator.IsTransactionActive, Is.False);
        }

        [Test]
        public async Task CancelChangesDuringAnInFlightCommitReturnsBadInvalidStateWithoutDisruptingTheCommitAsync()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            var commitEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var commitGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool committed = false;

            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = async _ =>
                {
                    commitEntered.TrySetResult(true);
                    await commitGate.Task.ConfigureAwait(false);
                    committed = true;
                }
            });

            Task<ServiceResult> applyTask = coordinator.ApplyChangesAsync(s_sessionA, CancellationToken.None)
                .AsTask();
            await commitEntered.Task.ConfigureAwait(false);

            // A concurrent CancelChanges from the owning Session cannot
            // discard operations that ApplyChanges already took ownership
            // of, and must not clear transaction state out from under the
            // in-flight commit.
            ServiceResult cancelResult = coordinator.CancelChanges(s_sessionA);
            Assert.That(cancelResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(coordinator.IsTransactionActive, Is.True);
            Assert.That(coordinator.OwnerSessionId, Is.EqualTo(s_sessionA));

            commitGate.TrySetResult(true);
            ServiceResult result = await applyTask.ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(committed, Is.True, "the in-flight commit must run to completion, unaffected by the concurrent cancel");
            Assert.That(coordinator.IsTransactionActive, Is.False);
        }

        [Test]
        public async Task CancelForSessionCloseDuringAnInFlightCommitIsANoOpAsync()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            var commitEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var commitGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool committed = false;

            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = async _ =>
                {
                    commitEntered.TrySetResult(true);
                    await commitGate.Task.ConfigureAwait(false);
                    committed = true;
                }
            });

            Task<ServiceResult> applyTask = coordinator.ApplyChangesAsync(s_sessionA, CancellationToken.None)
                .AsTask();
            await commitEntered.Task.ConfigureAwait(false);

            // Session A closing (e.g. a timeout) while its own commit is
            // still in flight must not disrupt that commit or let a third
            // Session observe a cleared transaction before it finishes.
            coordinator.CancelForSessionClose(s_sessionA);
            Assert.That(coordinator.IsTransactionActive, Is.True);
            Assert.That(coordinator.OwnerSessionId, Is.EqualTo(s_sessionA));

            commitGate.TrySetResult(true);
            ServiceResult result = await applyTask.ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(committed, Is.True);
            Assert.That(coordinator.IsTransactionActive, Is.False);
        }

        [Test]
        public void CancelForSessionCloseIsNoOpForNonOwningSession()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ => Task.CompletedTask
            });

            coordinator.CancelForSessionClose(s_sessionB);

            Assert.That(coordinator.IsTransactionActive, Is.True);
            Assert.That(coordinator.OwnerSessionId, Is.EqualTo(s_sessionA));
        }

        [Test]
        public void CancelForSessionCloseDiscardsTransactionOwnedByThatSession()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            bool disposed = false;
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ => Task.CompletedTask,
                DisposeStaged = () => disposed = true
            });

            coordinator.CancelForSessionClose(s_sessionA);

            Assert.That(coordinator.IsTransactionActive, Is.False);
            Assert.That(disposed, Is.True);
        }

        [Test]
        public void ResetDiscardsAnyActiveTransactionAndClearsOpenTrustListWriters()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            var trustListId = new NodeId(Guid.NewGuid(), 1);
            bool disposed = false;
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                AffectedTrustList = trustListId,
                CommitAsync = _ => Task.CompletedTask,
                DisposeStaged = () => disposed = true
            });
            coordinator.SetTrustListWriteOpen(trustListId, true);

            coordinator.Reset();

            Assert.That(coordinator.IsTransactionActive, Is.False);
            Assert.That(disposed, Is.True);
            Assert.That(coordinator.HasOpenTrustListWriter, Is.False);
        }

        [Test]
        public async Task GetSnapshotReflectsActiveThenMostRecentlyCompletedTransactionAsync()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry, TimeProvider.System);
            var group = new NodeId(Guid.NewGuid(), 1);

            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                AffectedCertificateGroup = group,
                AffectedCertificateType = new NodeId(Guid.NewGuid(), 1),
                CommitAsync = _ => Task.CompletedTask
            });

            PushConfigurationTransactionSnapshot activeSnapshot = coordinator.GetSnapshot();
            Assert.That(activeSnapshot.IsActive, Is.True);
            Assert.That(activeSnapshot.OwnerSessionId, Is.EqualTo(s_sessionA));

            await coordinator.ApplyChangesAsync(s_sessionA, CancellationToken.None).ConfigureAwait(false);

            PushConfigurationTransactionSnapshot completedSnapshot = coordinator.GetSnapshot();
            Assert.That(completedSnapshot.IsActive, Is.False);
            Assert.That(completedSnapshot.OwnerSessionId.IsNull, Is.True);
            Assert.That(completedSnapshot.Result, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(completedSnapshot.AffectedCertificateGroups.Contains(group), Is.True);
            Assert.That(completedSnapshot.EndTime, Is.GreaterThanOrEqualTo(completedSnapshot.StartTime));
        }

        [Test]
        public async Task GetSnapshotRecordsErrorsFromAFailedCommitAsync()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            var trustListId = new NodeId(Guid.NewGuid(), 1);

            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                AffectedTrustList = trustListId,
                CommitAsync = _ => throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "invalid payload")
            });

            await coordinator.ApplyChangesAsync(s_sessionA, CancellationToken.None).ConfigureAwait(false);

            PushConfigurationTransactionSnapshot snapshot = coordinator.GetSnapshot();
            Assert.That(snapshot.Result, Is.EqualTo((StatusCode)StatusCodes.BadCertificateInvalid));
            Assert.That(snapshot.Errors.Count, Is.EqualTo(1));
            Assert.That(snapshot.Errors[0].TargetId, Is.EqualTo(trustListId));
            Assert.That(snapshot.Errors[0].Error, Is.EqualTo((StatusCode)StatusCodes.BadCertificateInvalid));
        }

        [Test]
        public void ConstructorWithNullTelemetryThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new PushConfigurationTransactionCoordinator(null!));
        }

        [Test]
        public void StageWithNullOperationThrowsArgumentNullException()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            Assert.Throws<ArgumentNullException>(() => coordinator.Stage(s_sessionA, null!));
        }
    }
}
