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
        private static readonly string[] s_expectedPrepareCommitOrder =
            ["prepare-1", "prepare-2", "commit-1", "commit-2"];

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
        public async Task StageFromOwningSessionDuringInFlightCommitReturnsBadInvalidStateThenSucceedsAsync()
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

            // A same-owner Stage racing an in-flight commit must be
            // rejected with BadInvalidState: silently accepting it would
            // add to m_operations after ApplyChangesAsync already took
            // ownership of the previous list, leaving the new operation
            // stranded once the commit's finally block releases ownership.
            bool raced = false;
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                coordinator.Stage(s_sessionA, new PushConfigurationOperation
                {
                    CommitAsync = _ =>
                    {
                        raced = true;
                        return Task.CompletedTask;
                    }
                }));
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(raced, Is.False);
            Assert.That(coordinator.IsTransactionActive, Is.True);

            commitGate.TrySetResult(true);
            ServiceResult result = await applyTask.ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(committed, Is.True);
            Assert.That(coordinator.IsTransactionActive, Is.False);

            // Staging must succeed immediately after the commit completes,
            // with no residual operation left over from the rejected Stage
            // above (GetStagedOperations should show exactly the one just
            // staged here).
            bool secondCommitted = false;
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ =>
                {
                    secondCommitted = true;
                    return Task.CompletedTask;
                }
            });
            Assert.That(coordinator.IsTransactionActive, Is.True);
            Assert.That(coordinator.OwnerSessionId, Is.EqualTo(s_sessionA));
            Assert.That(coordinator.GetStagedOperations().Count, Is.EqualTo(1));

            ServiceResult secondApplyResult = await coordinator
                .ApplyChangesAsync(s_sessionA, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(secondApplyResult), Is.True);
            Assert.That(secondCommitted, Is.True);
        }

        [Test]
        public void CancelChangesReportsBadRequestCancelledByClientInDiagnostics()
        {
            // OPC 10000-12 §7.10.17: "If the CancelChanges Method was
            // called the value is Bad_RequestCancelledByClient."
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry, TimeProvider.System);
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ => Task.CompletedTask
            });

            ServiceResult result = coordinator.CancelChanges(s_sessionA);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(
                coordinator.GetSnapshot().Result,
                Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
        }

        [Test]
        public void CancelForSessionCloseReportsBadRequestCancelledByRequestInDiagnostics()
        {
            // Only an explicit CancelChanges call reports
            // Bad_RequestCancelledByClient per §7.10.17; a Session closing
            // (e.g. a timeout, not necessarily the client's request to
            // cancel) reports the more generic BadRequestCancelledByRequest.
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry, TimeProvider.System);
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ => Task.CompletedTask
            });

            coordinator.CancelForSessionClose(s_sessionA);

            Assert.That(
                coordinator.GetSnapshot().Result,
                Is.EqualTo(StatusCodes.BadRequestCancelledByRequest));
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

        [Test]
        public async Task ApplyChangesRunsEveryPrepareStepBeforeAnyCommitAsync()
        {
            // OPC 10000-12 §7.10.2: the Server verifies that all changes are
            // consistent before applying any of them. Every PrepareAsync must
            // therefore run before the first CommitAsync.
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            var order = new System.Collections.Generic.List<string>();

            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                PrepareAsync = _ =>
                {
                    order.Add("prepare-1");
                    return Task.CompletedTask;
                },
                CommitAsync = _ =>
                {
                    order.Add("commit-1");
                    return Task.CompletedTask;
                }
            });
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                AffectedTrustList = new NodeId(Guid.NewGuid(), 1),
                PrepareAsync = _ =>
                {
                    order.Add("prepare-2");
                    return Task.CompletedTask;
                },
                CommitAsync = _ =>
                {
                    order.Add("commit-2");
                    return Task.CompletedTask;
                }
            });

            ServiceResult result = await coordinator.ApplyChangesAsync(s_sessionA, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(order, Is.EqualTo(s_expectedPrepareCommitOrder));
        }

        [Test]
        public async Task ApplyChangesWithFailingPrepareDiscardsTransactionWithoutCommittingAsync()
        {
            // A PrepareAsync that throws (for example DeleteCertificate
            // rejecting a still-referenced certificate, §7.10.7) must abort
            // the whole transaction before any operation commits, and every
            // staged operation must still be disposed.
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            bool firstCommitted = false;
            bool secondCommitted = false;
            bool firstDisposed = false;
            bool secondDisposed = false;

            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ =>
                {
                    firstCommitted = true;
                    return Task.CompletedTask;
                },
                DisposeStaged = () => firstDisposed = true
            });
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                AffectedTrustList = new NodeId(Guid.NewGuid(), 1),
                PrepareAsync = _ => throw new ServiceResultException(StatusCodes.BadInvalidState, "referenced"),
                CommitAsync = _ =>
                {
                    secondCommitted = true;
                    return Task.CompletedTask;
                },
                DisposeStaged = () => secondDisposed = true
            });

            ServiceResult result = await coordinator.ApplyChangesAsync(s_sessionA, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(firstCommitted, Is.False, "no operation may commit when a prepare step fails");
            Assert.That(secondCommitted, Is.False);
            Assert.That(firstDisposed, Is.True, "every staged operation must be disposed");
            Assert.That(secondDisposed, Is.True);
            Assert.That(coordinator.IsTransactionActive, Is.False);

            PushConfigurationTransactionSnapshot snapshot = coordinator.GetSnapshot();
            Assert.That(snapshot.Result, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(snapshot.Errors, Has.Count.EqualTo(1));
        }

        [Test]
        public void StagingSameSlotAfterAnInterleavedOperationKeepsTheSurvivorLastInRequestOrder()
        {
            // §7.10.2: the Server queues the changes "in the order that they
            // were requested". When a same-slot certificate operation is
            // superseded, the surviving (later) request must retain its later
            // request order relative to an operation staged in between, and
            // interleaved non-slot (TrustList) operations must never coalesce.
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            var group = new NodeId(Guid.NewGuid(), 1);
            var certType = new NodeId(Guid.NewGuid(), 1);
            var trustListId = new NodeId(Guid.NewGuid(), 1);

            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                AffectedCertificateGroup = group,
                AffectedCertificateType = certType,
                CommitAsync = _ => Task.CompletedTask
            });
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                AffectedTrustList = trustListId,
                CommitAsync = _ => Task.CompletedTask
            });
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                AffectedCertificateGroup = group,
                AffectedCertificateType = certType,
                LeavesCertificateSlotEmpty = true,
                CommitAsync = _ => Task.CompletedTask
            });

            ArrayOf<PushConfigurationOperation> staged = coordinator.GetStagedOperations();

            Assert.That(staged, Has.Count.EqualTo(2), "the superseded same-slot operation must be discarded");
            Assert.That(
                staged[0].AffectedTrustList,
                Is.EqualTo(trustListId),
                "the interleaved TrustList operation must keep its request-order position");
            Assert.That(
                staged[1].AffectedCertificateType,
                Is.EqualTo(certType),
                "the surviving same-slot operation must be applied after the earlier TrustList operation");
            Assert.That(
                staged[1].LeavesCertificateSlotEmpty,
                Is.True,
                "the surviving operation must be the later (delete) request, not the earlier one");
        }

        [Test]
        public void GetSnapshotStateIsNoneBeforeAnyTransaction()
        {
            // OPC 10000-12 §7.10.17: "If no transaction has started the values
            // of all Variables have a status of Bad_OutOfService." The snapshot
            // reports this via State == None.
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);

            PushConfigurationTransactionSnapshot snapshot = coordinator.GetSnapshot();

            Assert.That(snapshot.State, Is.EqualTo(PushConfigurationTransactionState.None));
            Assert.That(snapshot.IsActive, Is.False);
            Assert.That(snapshot.AffectedCertificateGroups, Is.Empty);
            Assert.That(snapshot.AffectedTrustLists, Is.Empty);
            Assert.That(snapshot.Errors, Is.Empty);
        }

        [Test]
        public void GetSnapshotStateIsActiveWhileTransactionInFlightAndReflectsStagedTargets()
        {
            // §7.10.17: while a transaction has started but not completed the
            // snapshot reports State == Active, and AffectedCertificateGroups /
            // AffectedTrustLists reflect the targets staged so far (updated "as
            // soon as" a group/TrustList is added to the transaction).
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            var group = new NodeId(Guid.NewGuid(), 1);
            var trustListId = new NodeId(Guid.NewGuid(), 1);

            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                AffectedCertificateGroup = group,
                AffectedCertificateType = new NodeId(Guid.NewGuid(), 1),
                CommitAsync = _ => Task.CompletedTask
            });
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                AffectedTrustList = trustListId,
                CommitAsync = _ => Task.CompletedTask
            });

            PushConfigurationTransactionSnapshot snapshot = coordinator.GetSnapshot();

            Assert.That(snapshot.State, Is.EqualTo(PushConfigurationTransactionState.Active));
            Assert.That(snapshot.IsActive, Is.True);
            Assert.That(snapshot.AffectedCertificateGroups.Contains(group), Is.True);
            Assert.That(snapshot.AffectedTrustLists.Contains(trustListId), Is.True);
            Assert.That(snapshot.Errors, Is.Empty, "no errors are recorded until a commit runs");
        }

        [Test]
        public async Task GetSnapshotStateIsCompletedWithGoodResultAfterApplyAsync()
        {
            // §7.10.17: once the transaction completes the status is Good and
            // the value is the StatusCode returned from ApplyChanges.
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ => Task.CompletedTask
            });

            await coordinator.ApplyChangesAsync(s_sessionA, CancellationToken.None).ConfigureAwait(false);

            PushConfigurationTransactionSnapshot snapshot = coordinator.GetSnapshot();
            Assert.That(snapshot.State, Is.EqualTo(PushConfigurationTransactionState.Completed));
            Assert.That(snapshot.Result, Is.EqualTo((StatusCode)StatusCodes.Good));
        }

        [Test]
        public void GetSnapshotStateIsCompletedAfterCancel()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ => Task.CompletedTask
            });

            coordinator.CancelChanges(s_sessionA);

            PushConfigurationTransactionSnapshot snapshot = coordinator.GetSnapshot();
            Assert.That(snapshot.State, Is.EqualTo(PushConfigurationTransactionState.Completed));
            Assert.That(snapshot.Result, Is.EqualTo((StatusCode)StatusCodes.BadRequestCancelledByClient));
        }

        [Test]
        public async Task GetSnapshotStateIsCompletedWithFailingResultAfterFailedCommitAsync()
        {
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            coordinator.Stage(s_sessionA, new PushConfigurationOperation
            {
                CommitAsync = _ => throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "bad")
            });

            await coordinator.ApplyChangesAsync(s_sessionA, CancellationToken.None).ConfigureAwait(false);

            PushConfigurationTransactionSnapshot snapshot = coordinator.GetSnapshot();
            Assert.That(snapshot.State, Is.EqualTo(PushConfigurationTransactionState.Completed));
            Assert.That(snapshot.Result, Is.EqualTo((StatusCode)StatusCodes.BadCertificateInvalid));
        }

        [Test]
        public async Task ApplyChangesWithOpenTrustListWriterAndNoActiveTransactionReturnsBadInvalidStateAsync()
        {
            // OPC 10000-12 §7.10.9: ApplyChanges returns Bad_InvalidState if any
            // TrustList is still open for writing. This precedence holds even
            // when no transaction is active, so the caller learns to close the
            // writer and retry rather than receiving BadNothingToDo.
            var coordinator = new PushConfigurationTransactionCoordinator(s_telemetry);
            var trustListId = new NodeId(Guid.NewGuid(), 1);
            coordinator.SetTrustListWriteOpen(trustListId, true);

            Assert.That(coordinator.IsTransactionActive, Is.False, "no transaction has been staged");

            ServiceResult result = await coordinator.ApplyChangesAsync(s_sessionA, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(
                result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidState),
                "an open TrustList writer takes precedence over BadNothingToDo");

            coordinator.SetTrustListWriteOpen(trustListId, false);
            ServiceResult afterClose = await coordinator.ApplyChangesAsync(s_sessionA, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(
                afterClose.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNothingToDo),
                "once every writer is closed and nothing is staged the result is BadNothingToDo");
        }
    }
}
