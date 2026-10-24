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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Storage;

namespace Opc.Ua.Server.Tests.AsyncServices
{
    /// <summary>
    /// Tests local journal transitions against actual protected CAS bytes and explicitly test-qualified providers.
    /// No original service code runs here. These are neither physical power-loss nor full Durable-conformance tests.
    /// </summary>
    [TestFixture]
    [Category("AsyncServices")]
    [Parallelizable(ParallelScope.All)]
    public sealed class RecoverableExecutionJournalTests
    {
        [TestCase("store")]
        [TestCase("protector")]
        [TestCase("clock")]
        [TestCase("owners")]
        [TestCase("encoding")]
        public async Task ConstructorRequiresEveryDependency(string missing)
        {
            await using var test = new JournalScope();
            ArgumentNullException error = Assert.Throws<ArgumentNullException>(() =>
                new RecoverableExecutionJournal(
                    missing == "store" ? null! : test.Store,
                    missing == "protector" ? null! : test.Protection,
                    missing == "clock" ? null! : test.Clock,
                    missing == "owners" ? null! : test.Owners.Object,
                    RecoverableJournalTestData.LogicalServerId,
                    missing == "encoding" ? null! : RecoverableJournalTestData.Context(),
                    RecoverableJournalTestData.Witness))!;
            Assert.That(error.ParamName, Is.EqualTo(missing == "encoding" ? "context" : missing));
            Assert.That(test.Store.CasAttempts, Is.Zero);
            Assert.That(test.Clock.Reads, Is.Zero);
            test.Owners.VerifyNoOtherCalls();
        }

        [TestCase("durability")]
        [TestCase("consistency")]
        [TestCase("plaintext")]
        public async Task ConstructorRequiresQualifiedStorageAndAuthenticatedProtection(string missing)
        {
            await using var test = new JournalScope();
            var store = new Mock<ISharedKeyValueStore>(MockBehavior.Strict);
            if (missing != "durability")
            {
                _ = store.As<ISharedKeyValueStoreDurability>();
            }
            if (missing != "consistency")
            {
                _ = store.As<ISharedKeyValueStoreConsistency>();
            }
            Assert.That(() => new RecoverableExecutionJournal(
                store.Object, missing == "plaintext" ? NullRecordProtector.Instance : test.Protection,
                test.Clock, test.Owners.Object, RecoverableJournalTestData.LogicalServerId,
                RecoverableJournalTestData.Context(), RecoverableJournalTestData.Witness),
                Throws.ArgumentException);
            Assert.That(test.Clock.Reads, Is.Zero);
            store.VerifyNoOtherCalls();
            test.Owners.VerifyNoOtherCalls();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("relative/server")]
        [TestCase("not a server URI")]
        public async Task ConstructorRejectsInvalidLogicalServerUri(string? logicalId)
        {
            await using var test = new JournalScope();
            Assert.That(() => new RecoverableExecutionJournal(
                test.Store, test.Protection, test.Clock, test.Owners.Object, logicalId!,
                RecoverableJournalTestData.Context(), RecoverableJournalTestData.Witness),
                Throws.ArgumentException);
            Assert.That(test.Store.CasAttempts, Is.Zero);
            Assert.That(test.Clock.Reads, Is.Zero);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public async Task NonpositiveLoadBoundDoesNotScanOrQualify(int maximumRecords)
        {
            await using var test = new JournalScope();
            await UnavailableAsync(() => Complete(test.Journal.LoadAsync(maximumRecords, default)))
                .ConfigureAwait(false);
            Assert.That(test.Journal.IsReady, Is.False);
            Assert.That(test.Store.ScannedRecords, Is.Zero);
            Assert.That(test.Store.HealthChecks, Is.Zero);
            Assert.That(test.Clock.Reads, Is.Zero);
        }

        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(0, int.MaxValue)]
        public async Task LoadIsBoundedAndSingleUse(int recordCount, int maximumRecords)
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.At(1020);
            for (int index = 0; index < recordCount; index++)
            {
                RecoverableExecutionDurableRecord record = RecoverableJournalTestData.Available() with
                {
                    ClientId = new Uuid(new Guid(index + 1, 0, 0, new byte[8]))
                };
                await test.SeedAsync(record).ConfigureAwait(false);
            }
            Assert.That(test.Journal.IsReady, Is.False);
            ArrayOf<RecoverableExecutionJournal.Entry> loaded =
                await test.LoadAsync(maximumRecords: maximumRecords).ConfigureAwait(false);

            Assert.That(loaded.Count, Is.EqualTo(recordCount));
            Assert.That(test.Store.ScannedRecords, Is.EqualTo(recordCount));
            Assert.That(test.Store.Commits, Is.Zero);
            Assert.That(test.Journal.IsReady, Is.True);
            foreach (RecoverableExecutionJournal.Entry entry in loaded)
            {
                Assert.That(entry.Current.Kind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
                Assert.That(entry.Current.Response, Is.EqualTo(RecoverableJournalTestData.Response));
                Assert.That(entry.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            }
            await UnavailableAsync(() => Complete(test.Journal.LoadAsync(maximumRecords, default)))
                .ConfigureAwait(false);
            Assert.That(test.Store.ScannedRecords, Is.EqualTo(recordCount));
            Assert.That(test.Journal.IsReady, Is.True,
                "An invalid second initialization does not rewrite stored state.");
        }

        [Test]
        public async Task LoadStopsAtFirstRecordBeyondBoundAndCannotExposePartialReadiness()
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.At(1020);
            for (int index = 0; index < 3; index++)
            {
                await test.SeedAsync(RecoverableJournalTestData.Available() with
                {
                    ClientId = new Uuid(new Guid(index + 1, 0, 0, new byte[8]))
                }).ConfigureAwait(false);
            }

            await UnavailableAsync(() => test.LoadAsync(maximumRecords: 1)).ConfigureAwait(false);

            Assert.That(test.Store.ScannedRecords, Is.EqualTo(2));
            Assert.That(test.Store.Commits, Is.Zero);
            Assert.That(test.OwnerChecks, Is.EqualTo(1));
            await AssertPoisonedAsync(test.Journal).ConfigureAwait(false);
        }

        [Test]
        public async Task AdmissionRequiresLoadAndTheAdmissionPhase()
        {
            await using var test = new JournalScope();
            await UnavailableAsync(() => test.AdmitAsync()).ConfigureAwait(false);
            Assert.That(test.Store.CasAttempts, Is.Zero);
            await test.LoadAsync().ConfigureAwait(false);
            await ErrorAsync(() => test.AdmitAsync(RecoverableJournalTestData.Pending()),
                StatusCodes.BadInvalidState).ConfigureAwait(false);
            Assert.That(test.Store.CasAttempts, Is.Zero);
            Assert.That(test.Journal.IsReady, Is.True);
        }

        [Test]
        public async Task AdmissionStartsExecutionOnlyAfterFirstCommitReturns()
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            using CasGate gate = test.Store.Gate(1, afterCommit: true);
            Task<RecoverableExecutionJournal.Entry> admission = test.Start(test.AdmitAsync());
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            try
            {
                RecoverableExecutionDurableRecord stored = await test.StoredAsync().ConfigureAwait(false);
                Assert.That(stored.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.Admission));
                Assert.That(stored.AdmittedAt, Is.Default);
                Assert.That(stored.DispatchStarted, Is.False);
                Assert.That(admission.IsCompleted, Is.False);
                test.Clock.Now = RecoverableJournalTestData.At(1500);
            }
            finally
            {
                gate.Release();
            }
            RecoverableExecutionJournal.Entry entry = await admission.WaitAsync(s_watchdog).ConfigureAwait(false);
            (RecoverableExecutionDurableRecord record, RecoverableExecutionTime now) =
                await Complete(entry.ObserveAsync(default)).ConfigureAwait(false);

            Assert.That(record.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1500)));
            Assert.That(record.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.Pending));
            Assert.That(record.Revision, Is.EqualTo(2));
            Assert.That(record.Snapshot(now).ExecutionRemaining, Is.EqualTo(100));
            Assert.That(record.Snapshot(now).ExecutionAllowance, Is.EqualTo(100));
            Assert.That(record.Snapshot(now).ResultRetentionTime, Is.EqualTo(50));
            Assert.That(test.Store.Commits, Is.EqualTo(2));
            Assert.That(record.DispatchStarted, Is.False);
            Assert.That(record.WorkFinished, Is.False);
        }

        [Test]
        public async Task AdmissionOriginWriteMustCompleteBeforeEntryIsPublished()
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            using CasGate gate = test.Store.Gate(2, afterCommit: false);
            Task<RecoverableExecutionJournal.Entry> admission = test.Start(test.AdmitAsync());
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            try
            {
                Assert.That(admission.IsCompleted, Is.False);
                Assert.That((await test.StoredAsync().ConfigureAwait(false)).Phase,
                    Is.EqualTo(RecoverableExecutionCommitPhase.Admission));
                test.Clock.Now = RecoverableJournalTestData.At(1025);
            }
            finally
            {
                gate.Release();
            }
            RecoverableExecutionJournal.Entry entry = await admission.WaitAsync(s_watchdog).ConfigureAwait(false);
            (RecoverableExecutionDurableRecord record, RecoverableExecutionTime now) =
                await Complete(entry.ObserveAsync(default)).ConfigureAwait(false);
            Assert.That(record.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(record.Snapshot(now).ExecutionRemaining, Is.EqualTo(75));
            Assert.That(test.Store.Commits, Is.EqualTo(2));
            Assert.That(record.DispatchStarted, Is.False);
        }

        [Test]
        public async Task DelayedQualifiedClockSampleDoesNotReusePrecommitAdmissionTime()
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            using ClockGate gate = test.Clock.Gate(test.Clock.Reads + 2);
            Task<RecoverableExecutionJournal.Entry> admission = test.Start(test.AdmitAsync());
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            try
            {
                Assert.That(test.Store.Commits, Is.EqualTo(1));
                Assert.That((await test.StoredAsync().ConfigureAwait(false)).AdmittedAt, Is.Default);
                test.Clock.Now = RecoverableJournalTestData.At(1400);
                Assert.That(admission.IsCompleted, Is.False);
            }
            finally
            {
                gate.Release();
            }
            RecoverableExecutionJournal.Entry entry = await admission.WaitAsync(s_watchdog).ConfigureAwait(false);
            Assert.That(entry.Current.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1400)));
            Assert.That(entry.Current.Snapshot(test.Clock.Now).ExecutionRemaining, Is.EqualTo(100));
            Assert.That(test.Store.Commits, Is.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DuplicateAdmissionNeverReplacesCommittedBytes(bool changedPayload)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            ByteString before = await test.StoredBytesAsync().ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.At(1020);
            RecoverableExecutionDurableRecord duplicate = RecoverableJournalTestData.Admission() with
            {
                Request = changedPayload ? ByteString.From(0x99, 0x01) : RecoverableJournalTestData.Admission().Request
            };

            await ErrorAsync(() => test.AdmitAsync(duplicate), StatusCodes.BadInvalidState).ConfigureAwait(false);

            Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(before));
            Assert.That(test.Store.CasAttempts, Is.EqualTo(3));
            Assert.That(test.Store.Commits, Is.EqualTo(2));
            Assert.That(entry.Current.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(entry.Current.Snapshot(test.Clock.Now).ExecutionRemaining, Is.EqualTo(80));
            Assert.That(test.Journal.IsReady, Is.True);
        }

        [Test]
        public async Task DispatchMarkerCommitsOnceBeforePermissionReturns()
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            RecoverableExecutionDurableRecord old = entry.Current;
            using CasGate gate = test.Store.Gate(3, afterCommit: true);
            Task<bool> permission = test.Start(Complete(entry.BeginDispatchAsync()));
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            try
            {
                Assert.That(permission.IsCompleted, Is.False);
                Assert.That((await test.StoredAsync().ConfigureAwait(false)).DispatchStarted, Is.True);
                Assert.That(entry.Current.DispatchStarted, Is.False);
            }
            finally
            {
                gate.Release();
            }
            Assert.That(await permission.WaitAsync(s_watchdog).ConfigureAwait(false), Is.True);
            await ErrorAsync(() => Complete(entry.BeginDispatchAsync()), StatusCodes.BadInvalidState)
                .ConfigureAwait(false);
            Assert.That(test.Store.Commits, Is.EqualTo(3));
            Assert.That(entry.Current.Revision, Is.EqualTo(3));
            Assert.That(entry.Current.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(entry.Current.WorkFinished, Is.False);
            Assert.That(old.DispatchStarted, Is.False, "A previous immutable Current value must not change.");
            Assert.That(old.Revision, Is.EqualTo(2));
        }

        [TestCase(1, false, 0)]
        [TestCase(1, true, 1)]
        [TestCase(2, false, 1)]
        [TestCase(2, true, 2)]
        public async Task AdmissionFaultsReopenOnlyActualCommittedPhase(
            int attempt, bool afterCommit, int committedWrites)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            test.Store.FailAt = attempt;
            test.Store.FailAfterCommit = afterCommit;
            using CasGate gate = test.Store.Gate(attempt, afterCommit);
            Task<RecoverableExecutionJournal.Entry> admission = test.Start(test.AdmitAsync());
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            gate.Release();
            await UnavailableAsync(() => admission).ConfigureAwait(false);
            Assert.That(test.Store.Commits, Is.EqualTo(committedWrites));
            await AssertPoisonedAsync(test.Journal).ConfigureAwait(false);

            if (committedWrites > 0)
            {
                RecoverableExecutionDurableRecord stored = await test.StoredAsync().ConfigureAwait(false);
                Assert.That(stored.Phase, Is.EqualTo(committedWrites == 1
                    ? RecoverableExecutionCommitPhase.Admission : RecoverableExecutionCommitPhase.Pending));
                Assert.That(stored.DispatchStarted, Is.False);
                Assert.That(stored.AdmittedAt, Is.EqualTo(committedWrites == 1
                    ? default : RecoverableJournalTestData.At(1000)));
            }
            else
            {
                Assert.That(
                    (await Complete(test.Raw.TryGetAsync(JournalScope.Key)).ConfigureAwait(false)).Found, Is.False);
            }
            test.Store.FailAt = 0;
            test.Clock.Now = RecoverableJournalTestData.At(2000);
            RecoverableExecutionJournal reopened = test.NewJournal();
            ArrayOf<RecoverableExecutionJournal.Entry> recovered =
                await test.LoadAsync(reopened).ConfigureAwait(false);
            Assert.That(reopened.IsReady, Is.True);
            Assert.That(recovered.Count, Is.EqualTo(committedWrites == 0 ? 0 : 1));
            if (committedWrites > 0)
            {
                AssertIndeterminate(recovered[0].Current, RecoverableJournalTestData.At(2000), workFinished: true);
                Assert.That(await Complete(recovered[0].BeginDispatchAsync()).ConfigureAwait(false), Is.False);
                Assert.That(test.Store.Commits, Is.EqualTo(committedWrites + 2));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationBeforeAdmissionCommitLeavesNoOperation(bool cancelInsideCas)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            if (cancelInsideCas)
            {
                using CasGate gate = test.Store.Gate(1, afterCommit: false);
                Task<RecoverableExecutionJournal.Entry> admission =
                    test.Start(test.AdmitAsync(cancellationToken: cancellation.Token));
                await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
                await cancellation.CancelAsync().ConfigureAwait(false);
                gate.Release();
                // Cancellation of a possibly-started store call is conservatively unavailable.
                await UnavailableAsync(() => admission).ConfigureAwait(false);
                Assert.That(test.Journal.IsReady, Is.False);
            }
            else
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                _ = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await test.AdmitAsync(cancellationToken: cancellation.Token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(test.Journal.IsReady, Is.True);
            }
            Assert.That(test.Store.Commits, Is.Zero);
            Assert.That((await Complete(test.Raw.TryGetAsync(JournalScope.Key)).ConfigureAwait(false)).Found, Is.False);
            Assert.That((await test.LoadAsync(test.NewJournal()).ConfigureAwait(false)).Count, Is.Zero);
        }

        [Test]
        public async Task CancellationAfterAdmissionCommitCannotUndoAdmission()
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            using CasGate gate = test.Store.Gate(1, afterCommit: true);
            Task<RecoverableExecutionJournal.Entry> admission =
                test.Start(test.AdmitAsync(cancellationToken: cancellation.Token));
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.At(1200);
            gate.Release();
            RecoverableExecutionJournal.Entry entry = await admission.WaitAsync(s_watchdog).ConfigureAwait(false);

            Assert.That(entry.Current.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.Pending));
            Assert.That(entry.Current.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1200)));
            Assert.That(entry.Current.CancellationRequested, Is.False);
            Assert.That(entry.Current.Snapshot(test.Clock.Now).ExecutionRemaining, Is.EqualTo(100));
            Assert.That(test.Store.Commits, Is.EqualTo(2));
            Assert.That(test.Store.LastCasToken.CanBeCanceled, Is.False);
            Assert.That(test.Journal.IsReady, Is.True);
        }

        [TestCase("default")]
        [TestCase("rollback")]
        [TestCase("domain-reset")]
        [TestCase("provider-failure")]
        public async Task JournalTimeRejectsLostQualificationAndPoisonsReadiness(string failure)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            ByteString before = await test.StoredBytesAsync().ConfigureAwait(false);
            if (failure == "provider-failure")
            {
                test.Clock.Available = false;
            }
            else
            {
                test.Clock.Now = failure switch
                {
                    "default" => default,
                    "rollback" => RecoverableJournalTestData.At(999),
                    _ => new RecoverableExecutionTime(RecoverableJournalTestData.OtherId, 10_000_000)
                };
            }

            await UnavailableAsync(() => Complete(test.Journal.GetTimeAsync(default))).ConfigureAwait(false);
            await AssertPoisonedAsync(test.Journal, entry).ConfigureAwait(false);
            Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(before));
            Assert.That(test.Store.Commits, Is.EqualTo(2));
        }

        [Test]
        public async Task JournalTimeAcceptsQualifiedZeroAndZeroElapsedWithoutRenewal()
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.AtTicks(0);
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            for (int index = 0; index < 3; index++)
            {
                (RecoverableExecutionDurableRecord record, RecoverableExecutionTime now) =
                    await Complete(entry.ObserveAsync(default)).ConfigureAwait(false);
                Assert.That(now, Is.EqualTo(new RecoverableExecutionTime(RecoverableJournalTestData.Domain, 0)));
                Assert.That(record.AdmittedAt, Is.EqualTo(now));
                Assert.That(record.Snapshot(now).ExecutionRemaining, Is.EqualTo(100));
                Assert.That(record.Revision, Is.EqualTo(2));
            }
            Assert.That(test.Store.Commits, Is.EqualTo(2));
            Assert.That(test.Journal.IsReady, Is.True);
        }

        [Test]
        public async Task ExecutionOverflowBeforeAdmissionCannotCommitOrDispatch()
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.AtTicks(long.MaxValue - 999_999);
            await test.LoadAsync().ConfigureAwait(false);
            await UnavailableAsync(() => test.AdmitAsync()).ConfigureAwait(false);
            Assert.That(test.Store.CasAttempts, Is.Zero);
            Assert.That((await Complete(test.Raw.TryGetAsync(JournalScope.Key)).ConfigureAwait(false)).Found, Is.False);
        }

        [Test]
        public async Task ExactRepresentableExecutionDeadlineIsAccepted()
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.AtTicks(long.MaxValue - 1_000_000);
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            Assert.That(entry.Current.AdmittedAt.Ticks, Is.EqualTo(long.MaxValue - 1_000_000));
            Assert.That(entry.Current.Snapshot(test.Clock.Now).ExecutionRemaining, Is.EqualTo(100));
            Assert.That(test.Store.Commits, Is.EqualTo(2));
            Assert.That(test.Journal.IsReady, Is.True);
        }

        [Test]
        public async Task ExecutionOverflowAfterFirstCommitPoisonsAdmissionButCanRecoverIndeterminate()
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            using CasGate gate = test.Store.Gate(1, afterCommit: true);
            Task<RecoverableExecutionJournal.Entry> admission = test.Start(test.AdmitAsync());
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.AtTicks(long.MaxValue - 750_000);
            gate.Release();
            await UnavailableAsync(() => admission).ConfigureAwait(false);
            await AssertPoisonedAsync(test.Journal).ConfigureAwait(false);
            Assert.That((await test.StoredAsync().ConfigureAwait(false)).Phase,
                Is.EqualTo(RecoverableExecutionCommitPhase.Admission));

            ArrayOf<RecoverableExecutionJournal.Entry> loaded =
                await test.LoadAsync(test.NewJournal()).ConfigureAwait(false);
            Assert.That(loaded.Count, Is.EqualTo(1));
            AssertIndeterminate(loaded[0].Current, test.Clock.Now, workFinished: true);
            Assert.That(await Complete(loaded[0].BeginDispatchAsync()).ConfigureAwait(false), Is.False);
        }

        [Test]
        public async Task RetentionOverflowAfterCandidateCommitPoisonsLiveJournal()
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.AtTicks(long.MaxValue - 1_000_000);
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
            test.Clock.Now = RecoverableJournalTestData.AtTicks(long.MaxValue - 499_999);

            await UnavailableAsync(() => Complete(entry.CommitOutcomeAsync(
                ServiceOperationOutcome.ServiceResponse, StatusCodes.Good,
                RecoverableJournalTestData.Response, default))).ConfigureAwait(false);

            Assert.That((await test.StoredAsync().ConfigureAwait(false)).Phase,
                Is.EqualTo(RecoverableExecutionCommitPhase.OutcomePreparing));
            Assert.That(test.Store.Commits, Is.EqualTo(4));
            await AssertPoisonedAsync(test.Journal, entry).ConfigureAwait(false);
        }

        [TestCase("durability", false)]
        [TestCase("durability", true)]
        [TestCase("consistency", false)]
        [TestCase("consistency", true)]
        [TestCase("owner", false)]
        [TestCase("owner", true)]
        public async Task LostProofCannotReturnCachedSuccessOrUnknown(string proof, bool control)
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.At(1040);
            // An orphan's public history can expire without a delete/CAS that would incidentally check consistency.
            RecoverableExecutionDurableRecord orphan =
                RecoverableJournalTestData.Released(ServiceOperationState.Abandoned) with { WorkFinished = false };
            await test.SeedAsync(orphan).ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = (await test.LoadAsync().ConfigureAwait(false))[0];
            ByteString before = await test.StoredBytesAsync().ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.At(1090);
            if (proof == "durability")
            {
                test.Store.Available = false;
            }
            else if (proof == "consistency")
            {
                test.Store.Linearizable = false;
            }
            else
            {
                test.OwnerAvailable = false;
            }

            if (control)
            {
                await UnavailableAsync(() => Complete(entry.ControlAsync(AsyncServiceAccess.Acknowledge, default)))
                    .ConfigureAwait(false);
            }
            else
            {
                await UnavailableAsync(() => Complete(entry.ObserveAsync(default))).ConfigureAwait(false);
            }
            Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(before));
            Assert.That(test.Store.Commits, Is.Zero);
            await AssertPoisonedAsync(test.Journal, entry).ConfigureAwait(false);
        }

        [TestCase("durability")]
        [TestCase("clock")]
        [TestCase("owner")]
        [TestCase("consistency")]
        public async Task LoadRequiresEveryAvailableProofBeforeReportingReady(string failure)
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.At(1020);
            await test.SeedAsync(RecoverableJournalTestData.Available()).ConfigureAwait(false);
            ByteString before = await test.StoredBytesAsync().ConfigureAwait(false);
            test.Store.Available = failure != "durability";
            test.Clock.Available = failure != "clock";
            test.OwnerAvailable = failure != "owner";
            test.Store.Linearizable = failure != "consistency";

            await UnavailableAsync(() => test.LoadAsync()).ConfigureAwait(false);

            Assert.That(test.Journal.IsReady, Is.False);
            Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(before));
            Assert.That(test.Store.Commits, Is.Zero);
        }

        [TestCase("first-byte")]
        [TestCase("middle-byte")]
        [TestCase("last-byte")]
        [TestCase("ciphertext-byte")]
        [TestCase("truncate")]
        [TestCase("empty")]
        [TestCase("oversized")]
        [TestCase("wrong-key-context")]
        [TestCase("wrong-protection-key")]
        public async Task WholeProtectedRecordIntegrityFailuresNeverBecomeEmptyState(string corruption)
        {
            await using var test = new JournalScope();
            RecoverableExecutionDurableRecord record = RecoverableJournalTestData.Available();
            test.Clock.Now = RecoverableJournalTestData.At(1020);
            await test.SeedAsync(record).ConfigureAwait(false);
            byte[] bytes = (await test.StoredBytesAsync().ConfigureAwait(false)).Span.ToArray();
            ByteString damaged;
            switch (corruption)
            {
                case "empty":
                    damaged = ByteString.Empty;
                    break;
                case "oversized":
                    damaged = new ByteString(new byte[16393]);
                    break;
                case "truncate":
                    damaged = new ByteString(bytes.AsSpan(0, bytes.Length - 1).ToArray());
                    break;
                case "wrong-key-context":
                    damaged = RecoverableJournalTestData.Codec().Protect(
                        record, test.Protector, JournalScope.ProtectionContext(JournalScope.Key + "/other"));
                    break;
                case "wrong-protection-key":
                    using (AesCbcHmacRecordProtector other = CreateProtector())
                    {
                        damaged = RecoverableJournalTestData.Codec().Protect(
                            record, other, JournalScope.ProtectionContext(JournalScope.Key));
                    }
                    break;
                default:
                    int index = corruption switch
                    {
                        "first-byte" => 0,
                        "middle-byte" => bytes.Length / 2,
                        "ciphertext-byte" => 8 + (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)) / 2),
                        _ => bytes.Length - 1
                    };
                    bytes[index] ^= 0x40;
                    damaged = new ByteString(bytes);
                    break;
            }
            await Complete(test.Raw.SetAsync(JournalScope.Key, damaged)).ConfigureAwait(false);

            await UnavailableAsync(() => test.LoadAsync()).ConfigureAwait(false);

            Assert.That(test.Journal.IsReady, Is.False);
            Assert.That(test.Store.Commits, Is.Zero);
            Assert.That((await Complete(test.Raw.TryGetAsync(JournalScope.Key)).ConfigureAwait(false)).Value,
                Is.EqualTo(damaged));
        }

        [TestCase("format")]
        [TestCase("witness")]
        [TestCase("owner-authority")]
        [TestCase("owner-application")]
        [TestCase("owner-user")]
        [TestCase("owner-context")]
        [TestCase("negative-time")]
        [TestCase("empty-time-domain")]
        [TestCase("roles-count")]
        [TestCase("permissions-count")]
        [TestCase("payload-length")]
        [TestCase("trailing")]
        public async Task MalformedProtectedLocalRecordsFailAsResourceUnavailable(string corruption)
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.At(1020);
            var changes = new RecoverableJournalTestData.RawChanges();
            switch (corruption)
            {
                case "format":
                    changes.Format = 0;
                    break;
                case "witness":
                    changes.Witness = new ByteString(new byte[32]);
                    break;
                case "owner-authority":
                    changes.AuthorityId = Uuid.Empty;
                    break;
                case "owner-application":
                    changes.ApplicationId = Uuid.Empty;
                    break;
                case "owner-user":
                    changes.UserId = Uuid.Empty;
                    break;
                case "owner-context":
                    changes.OwnerContext = ByteString.Empty;
                    break;
                case "negative-time":
                    changes.TimeField = "admitted";
                    changes.TimeTicks = -1;
                    break;
                case "empty-time-domain":
                    changes.TimeField = "admitted";
                    changes.TimeDomain = Uuid.Empty;
                    break;
                case "roles-count":
                    changes.RoleCount = int.MaxValue;
                    break;
                case "permissions-count":
                    changes.PermissionCount = int.MaxValue;
                    break;
                case "payload-length":
                    changes.RequestLength = int.MaxValue;
                    break;
            }
            ByteString plaintext = RecoverableJournalTestData.Raw(RecoverableJournalTestData.Available(), changes);
            if (corruption == "trailing")
            {
                plaintext = ByteString.From([.. plaintext.Span.ToArray(), 0x5a]);
            }
            await test.SeedRawAsync(JournalScope.Key, plaintext).ConfigureAwait(false);

            await UnavailableAsync(() => test.LoadAsync()).ConfigureAwait(false);

            Assert.That(test.Journal.IsReady, Is.False);
            Assert.That(test.Store.Commits, Is.Zero);
            Assert.That(test.OwnerChecks, Is.Zero, "Corrupt local evidence is rejected before trusting ownership.");
        }

        [Test]
        public async Task PersistedIdentityMustMatchItsAuthenticatedStorageKey()
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.At(1020);
            RecoverableExecutionDurableRecord other = RecoverableJournalTestData.Available() with
            {
                ClientId = RecoverableJournalTestData.OtherId
            };
            await test.SeedRawAsync(JournalScope.Key, RecoverableJournalTestData.Raw(other)).ConfigureAwait(false);
            await UnavailableAsync(() => test.LoadAsync()).ConfigureAwait(false);
            Assert.That(test.Journal.IsReady, Is.False);
            Assert.That(test.OwnerChecks, Is.Zero);
            Assert.That(test.Store.Commits, Is.Zero);
        }

        [Test]
        public async Task ChangedClockDomainOnReopenCannotBecomeUnknownOrRenewDeadlines()
        {
            await using var test = new JournalScope();
            await test.SeedAsync(RecoverableJournalTestData.Available()).ConfigureAwait(false);
            ByteString before = await test.StoredBytesAsync().ConfigureAwait(false);
            test.Clock.Now = new RecoverableExecutionTime(RecoverableJournalTestData.OtherId, 20_000_000);
            await UnavailableAsync(() => test.LoadAsync()).ConfigureAwait(false);
            Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(before));
            Assert.That(test.Store.Commits, Is.Zero);
            Assert.That(test.Journal.IsReady, Is.False);
        }

        [Test]
        public async Task DisposePoisonsOnlyJournalNotBorrowedProviders()
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            test.Journal.Dispose();
            await AssertPoisonedAsync(test.Journal, entry).ConfigureAwait(false);
            ArrayOf<RecoverableExecutionJournal.Entry> restored =
                await test.LoadAsync(test.NewJournal()).ConfigureAwait(false);
            Assert.That(restored.Count, Is.EqualTo(1));
            AssertIndeterminate(restored[0].Current, RecoverableJournalTestData.At(1000), workFinished: true);
            Assert.That(await Complete(restored[0].BeginDispatchAsync()).ConfigureAwait(false), Is.False);
            Assert.That(test.Store.Commits, Is.EqualTo(4));
        }

        [Test]
        public async Task FinishedAdmissionCannotRenewExecutionOrigin()
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            ByteString before = await test.StoredBytesAsync().ConfigureAwait(false);

            await ErrorAsync(() => Complete(entry.FinishAdmissionAsync(RecoverableJournalTestData.At(1010))),
                StatusCodes.BadInvalidState).ConfigureAwait(false);

            Assert.That(entry.Current.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(entry.Current.Revision, Is.EqualTo(2));
            Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(before));
            Assert.That(test.Store.Commits, Is.EqualTo(2));
            Assert.That(test.Journal.IsReady, Is.True);
        }

        [TestCase(-1L)]
        [TestCase(0L)]
        [TestCase(1L)]
        public async Task OutcomeSelectionUsesPostCandidateCommitTimeAtExecutionBoundary(long deltaTicks)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
            test.Clock.Now = RecoverableJournalTestData.At(1090);
            using CasGate gate = test.Store.Gate(4, afterCommit: true);
            Task completion = test.Start(Complete(entry.CommitOutcomeAsync(
                ServiceOperationOutcome.ServiceResponse, StatusCodes.BadUserAccessDenied,
                RecoverableJournalTestData.Response, RecoverableJournalTestData.Available().Permissions)));
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            try
            {
                RecoverableExecutionDurableRecord candidate = await test.StoredAsync().ConfigureAwait(false);
                Assert.That(candidate.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.OutcomePreparing));
                Assert.That(candidate.Response, Is.EqualTo(ByteString.From(0x52, 0x50, 0x7f, 0x80)));
                Assert.That(candidate.OutcomeAt, Is.Default);
                Assert.That(completion.IsCompleted, Is.False);
                test.Clock.Now = RecoverableJournalTestData.AtTicks(11_000_000 + deltaTicks);
            }
            finally
            {
                gate.Release();
            }
            await completion.WaitAsync(s_watchdog).ConfigureAwait(false);
            RecoverableExecutionDurableRecord result = entry.Current;
            bool originalWins = deltaTicks < 0;

            Assert.That(result.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.OutcomeAvailable));
            Assert.That(result.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(result.Kind, Is.EqualTo(originalWins
                ? ServiceOperationOutcome.ServiceResponse : ServiceOperationOutcome.ExecutionDeadlineExceeded));
            Assert.That(result.Status, Is.EqualTo(originalWins
                ? StatusCodes.BadUserAccessDenied : StatusCodes.BadTimeout));
            Assert.That(result.Response, Is.EqualTo(originalWins ? RecoverableJournalTestData.Response : default));
            Assert.That(result.Permissions.Count, Is.EqualTo(originalWins ? 2 : 0));
            Assert.That(result.Permissions.Span.ToArray(), Is.EqualTo(originalWins
                ? RecoverableJournalTestData.Available().Permissions.Span.ToArray()
                : []));
            Assert.That(result.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.AtTicks(11_000_000 + deltaTicks)));
            Assert.That(result.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(result.WorkFinished, Is.True);
            Assert.That(result.Snapshot(test.Clock.Now).ResultRemaining, Is.EqualTo(50));
            Assert.That(test.Store.Commits, Is.EqualTo(originalWins ? 5 : 6));
        }

        [TestCase(0L)]
        [TestCase(1L)]
        public async Task CompletionAlreadyAtExecutionDeadlineNeverWritesOriginalPayload(long deltaTicks)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
            test.Clock.Now = RecoverableJournalTestData.AtTicks(11_000_000 + deltaTicks);
            using CasGate gate = test.Store.Gate(4, afterCommit: true);
            Task completion = test.Start(Complete(entry.CommitOutcomeAsync(
                ServiceOperationOutcome.ServiceResponse, StatusCodes.Good,
                RecoverableJournalTestData.Response, RecoverableJournalTestData.Available().Permissions)));
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            try
            {
                RecoverableExecutionDurableRecord candidate = await test.StoredAsync().ConfigureAwait(false);
                Assert.That(candidate.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.OutcomePreparing));
                Assert.That(candidate.Kind, Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
                Assert.That(candidate.Status, Is.EqualTo(StatusCodes.BadTimeout));
                Assert.That(candidate.Response.IsNull, Is.True);
                Assert.That(candidate.Permissions.Count, Is.Zero);
                test.Clock.Now = RecoverableJournalTestData.At(1105);
            }
            finally
            {
                gate.Release();
            }
            await completion.WaitAsync(s_watchdog).ConfigureAwait(false);
            Assert.That(entry.Current.Kind, Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
            Assert.That(entry.Current.Response.IsNull, Is.True);
            Assert.That(entry.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1105)));
            Assert.That(entry.Current.Snapshot(test.Clock.Now).ResultRemaining, Is.EqualTo(50));
            Assert.That(test.Store.Commits, Is.EqualTo(5));
        }

        [TestCase(15)]
        [TestCase(50)]
        [TestCase(51)]
        public async Task SelectedOutcomeCannotBeReplacedWhileRetentionOriginWriteIsPending(int writeDelayMilliseconds)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
            test.Clock.Now = RecoverableJournalTestData.At(1090);
            using CasGate gate = test.Store.Gate(5, afterCommit: false);
            Task completion = test.Start(Complete(entry.CommitOutcomeAsync(
                ServiceOperationOutcome.ServiceResponse, StatusCodes.BadUserAccessDenied,
                RecoverableJournalTestData.Response, RecoverableJournalTestData.Available().Permissions)));
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            Task<(RecoverableExecutionDurableRecord Record, RecoverableExecutionTime Now)> observation =
                test.Start(Complete(entry.ObserveAsync(default)));
            try
            {
                Assert.That(completion.IsCompleted, Is.False);
                Assert.That(observation.IsCompleted, Is.False,
                    "An E-expiry observer must wait for the same serializer.");
                Assert.That(entry.Current.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.OutcomePreparing));
                ServiceResultException error = Assert.Throws<ServiceResultException>(
                    () => entry.Current.Snapshot(test.Clock.Now))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
                test.Clock.Now = RecoverableJournalTestData.At(1090 + writeDelayMilliseconds);
            }
            finally
            {
                gate.Release();
            }
            await completion.WaitAsync(s_watchdog).ConfigureAwait(false);
            (RecoverableExecutionDurableRecord record, RecoverableExecutionTime now) =
                await observation.WaitAsync(s_watchdog).ConfigureAwait(false);
            bool resultExpired = writeDelayMilliseconds >= 50;

            Assert.That(record.Kind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(record.Status, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(record.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1090)));
            Assert.That(record.State, Is.EqualTo(resultExpired
                ? ServiceOperationState.Expired : ServiceOperationState.OutcomeAvailable));
            Assert.That(record.Response, Is.EqualTo(resultExpired ? default : RecoverableJournalTestData.Response));
            Assert.That(record.ReleasedAt, Is.EqualTo(resultExpired
                ? RecoverableJournalTestData.At(1140) : default));
            Assert.That(record.Snapshot(now).ResultRemaining, Is.EqualTo(resultExpired ? 0 : 35));
            Assert.That(record.Snapshot(now).HistoryRemaining, Is.EqualTo(resultExpired
                ? 100 - writeDelayMilliseconds : 0));
            Assert.That(record.CancellationRequested, Is.False);
            Assert.That(record.WorkFinished, Is.True);
            Assert.That(test.Store.Commits, Is.EqualTo(resultExpired ? 6 : 5));
        }

        [Test]
        public async Task DeadlineCandidateGetsItsOwnPostcommitRetentionOrigin()
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
            test.Clock.Now = RecoverableJournalTestData.At(1090);
            using CasGate originalGate = test.Store.Gate(4, afterCommit: true);
            using CasGate timeoutGate = test.Store.Gate(5, afterCommit: true);
            Task completion = test.Start(Complete(entry.CommitOutcomeAsync(
                ServiceOperationOutcome.ServiceResponse, StatusCodes.Good, RecoverableJournalTestData.Response,
                RecoverableJournalTestData.Available().Permissions)));
            await originalGate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.At(1100);
            originalGate.Release();
            await timeoutGate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            try
            {
                RecoverableExecutionDurableRecord stored = await test.StoredAsync().ConfigureAwait(false);
                Assert.That(stored.Kind, Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
                Assert.That(stored.Status, Is.EqualTo(StatusCodes.BadTimeout));
                Assert.That(stored.Response.IsNull, Is.True);
                Assert.That(stored.Permissions.Count, Is.Zero);
                Assert.That(stored.OutcomeAt, Is.Default);
                Assert.That(completion.IsCompleted, Is.False);
                test.Clock.Now = RecoverableJournalTestData.At(1125);
            }
            finally
            {
                timeoutGate.Release();
            }
            await completion.WaitAsync(s_watchdog).ConfigureAwait(false);

            Assert.That(entry.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1125)));
            Assert.That(entry.Current.Snapshot(test.Clock.Now).ResultRemaining, Is.EqualTo(50));
            Assert.That(entry.Current.Kind, Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
            Assert.That(entry.Current.Response.IsNull, Is.True);
            Assert.That(entry.Current.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(test.Store.Commits, Is.EqualTo(6));
        }

        [TestCase(4, false, 3)]
        [TestCase(4, true, 4)]
        [TestCase(5, false, 4)]
        [TestCase(5, true, 5)]
        public async Task OutcomeFaultsReconcileActualCommittedBytesWithoutReplay(
            int attempt, bool afterCommit, int committedWrites)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
            test.Clock.Now = RecoverableJournalTestData.At(1020);
            test.Store.FailAt = attempt;
            test.Store.FailAfterCommit = afterCommit;
            using CasGate gate = test.Store.Gate(attempt, afterCommit);
            Task completion = test.Start(Complete(entry.CommitOutcomeAsync(
                ServiceOperationOutcome.ServiceResponse, StatusCodes.BadUserAccessDenied,
                RecoverableJournalTestData.Response, RecoverableJournalTestData.Available().Permissions)));
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            gate.Release();
            await UnavailableAsync(() => completion).ConfigureAwait(false);
            await AssertPoisonedAsync(test.Journal, entry).ConfigureAwait(false);
            RecoverableExecutionDurableRecord stored = await test.StoredAsync().ConfigureAwait(false);
            Assert.That(stored.Phase, Is.EqualTo(committedWrites switch
            {
                3 => RecoverableExecutionCommitPhase.Pending,
                4 => RecoverableExecutionCommitPhase.OutcomePreparing,
                _ => RecoverableExecutionCommitPhase.OutcomeAvailable
            }));
            Assert.That(stored.DispatchStarted, Is.True);
            Assert.That(test.Store.Commits, Is.EqualTo(committedWrites));
            ByteString committedBytes = await test.StoredBytesAsync().ConfigureAwait(false);
            test.Store.FailAt = 0;
            test.Clock.Now = RecoverableJournalTestData.At(1030);

            RecoverableExecutionJournal reopened = test.NewJournal();
            RecoverableExecutionJournal.Entry recovered = (await test.LoadAsync(reopened).ConfigureAwait(false))[0];

            Assert.That(await Complete(recovered.BeginDispatchAsync()).ConfigureAwait(false), Is.False);
            Assert.That(reopened.IsReady, Is.True);
            Assert.That(recovered.Current.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            if (committedWrites == 5)
            {
                Assert.That(recovered.Current.Kind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
                Assert.That(recovered.Current.Status, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(recovered.Current.Response, Is.EqualTo(ByteString.From(0x52, 0x50, 0x7f, 0x80)));
                Assert.That(recovered.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
                Assert.That(recovered.Current.Snapshot(test.Clock.Now).ResultRemaining, Is.EqualTo(40));
                Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(committedBytes));
                Assert.That(test.Store.Commits, Is.EqualTo(5));
            }
            else
            {
                AssertIndeterminate(recovered.Current, RecoverableJournalTestData.At(1030), committedWrites == 4);
                Assert.That(test.Store.Commits, Is.EqualTo(committedWrites + 2));
                Assert.That(recovered.Current.Response.IsNull, Is.True);
            }
        }

        [Test]
        public async Task IncompleteDeadlineOutcomeReopensAsIndeterminateWithNewRetention()
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
            test.Clock.Now = RecoverableJournalTestData.At(1090);
            using CasGate candidateGate = test.Store.Gate(4, afterCommit: true);
            test.Store.FailAt = 5;
            test.Store.FailAfterCommit = true;
            Task completion = test.Start(Complete(entry.CommitOutcomeAsync(
                ServiceOperationOutcome.ServiceResponse, StatusCodes.Good, RecoverableJournalTestData.Response,
                default)));
            await candidateGate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.At(1100);
            candidateGate.Release();
            await UnavailableAsync(() => completion).ConfigureAwait(false);
            RecoverableExecutionDurableRecord stored = await test.StoredAsync().ConfigureAwait(false);
            Assert.That(stored.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.OutcomePreparing));
            Assert.That(stored.Kind, Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
            Assert.That(stored.OutcomeAt, Is.Default);
            Assert.That(stored.Response.IsNull, Is.True);
            test.Store.FailAt = 0;
            test.Clock.Now = RecoverableJournalTestData.At(2000);

            RecoverableExecutionJournal.Entry recovered =
                (await test.LoadAsync(test.NewJournal()).ConfigureAwait(false))[0];

            AssertIndeterminate(recovered.Current, RecoverableJournalTestData.At(2000), workFinished: true);
            Assert.That(recovered.Current.Response.IsNull, Is.True);
            Assert.That(await Complete(recovered.BeginDispatchAsync()).ConfigureAwait(false), Is.False);
            Assert.That(test.Store.Commits, Is.EqualTo(7));
        }

        [TestCase(0L)]
        [TestCase(1L)]
        [TestCase(499_999L)]
        public async Task CommittedOutcomeReopensByteIdenticalWithOriginalRetention(long downtimeTicks)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
            test.Clock.Now = RecoverableJournalTestData.At(1020);
            await Complete(entry.CommitOutcomeAsync(
                ServiceOperationOutcome.ServiceResponse, StatusCodes.BadUserAccessDenied,
                RecoverableJournalTestData.Response, RecoverableJournalTestData.Available().Permissions))
                .ConfigureAwait(false);
            ByteString protectedBytes = await test.StoredBytesAsync().ConfigureAwait(false);
            test.Journal.Dispose();
            test.Clock.Now = RecoverableJournalTestData.AtTicks(10_200_000 + downtimeTicks);

            RecoverableExecutionJournal.Entry restored =
                (await test.LoadAsync(test.NewJournal()).ConfigureAwait(false))[0];
            (RecoverableExecutionDurableRecord record, RecoverableExecutionTime now) =
                await Complete(restored.ObserveAsync(default)).ConfigureAwait(false);

            Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(protectedBytes));
            Assert.That(record.Response, Is.EqualTo(ByteString.From(0x52, 0x50, 0x7f, 0x80)));
            Assert.That(record.Request, Is.EqualTo(ByteString.From(0x52, 0x51, 0x00, 0xff)));
            Assert.That(record.Permissions.Span.ToArray(),
                Is.EqualTo(RecoverableJournalTestData.Available().Permissions.Span.ToArray()));
            Assert.That(record.Status, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(record.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            Assert.That(record.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(record.Snapshot(now).ResultRemaining, Is.EqualTo(downtimeTicks switch
            {
                0 => 50,
                1 => 49,
                _ => 0
            }));
            Assert.That(record.Revision, Is.EqualTo(5));
            Assert.That(test.Store.Commits, Is.EqualTo(5));
            Assert.That(await Complete(restored.BeginDispatchAsync()).ConfigureAwait(false), Is.False);
        }

        [Test]
        public async Task RepeatedRecoveryDoesNotRenewAlreadyReconciledRetention()
        {
            await using var test = new JournalScope();
            await test.SeedAsync(RecoverableJournalTestData.Pending()).ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = (await test.LoadAsync().ConfigureAwait(false))[0];
            ByteString before = await test.StoredBytesAsync().ConfigureAwait(false);
            AssertIndeterminate(entry.Current, RecoverableJournalTestData.At(1000), workFinished: true);
            test.Clock.Now = RecoverableJournalTestData.At(1020);

            await Complete(entry.RecoverAsync(default)).ConfigureAwait(false);

            Assert.That(entry.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(entry.Current.Snapshot(test.Clock.Now).ResultRemaining, Is.EqualTo(30));
            Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(before));
            Assert.That(test.Store.Commits, Is.EqualTo(2));
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.False);
        }

        [TestCase("original")]
        [TestCase("timeout")]
        [TestCase("indeterminate")]
        [TestCase("released")]
        public async Task LateCompletionCannotReplaceSelectedOrReleasedOutcome(string selected)
        {
            await using var test = new JournalScope();
            RecoverableExecutionJournal.Entry entry;
            if (selected == "indeterminate")
            {
                await test.SeedAsync(RecoverableJournalTestData.Pending() with
                {
                    DispatchStarted = true,
                    Revision = 3
                }).ConfigureAwait(false);
                entry = (await test.LoadAsync().ConfigureAwait(false))[0];
            }
            else
            {
                await test.LoadAsync().ConfigureAwait(false);
                entry = await test.AdmitAsync().ConfigureAwait(false);
                Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
                test.Clock.Now = RecoverableJournalTestData.At(selected == "timeout" ? 1100 : 1020);
                if (selected == "timeout")
                {
                    _ = await Complete(entry.ObserveAsync(default)).ConfigureAwait(false);
                }
                else if (selected == "released")
                {
                    Assert.That(await Complete(entry.ControlAsync(AsyncServiceAccess.Abandon, default))
                        .ConfigureAwait(false), Is.EqualTo(ServiceOperationControlResult.Applied));
                }
                else
                {
                    await Complete(entry.CommitOutcomeAsync(
                        ServiceOperationOutcome.ServiceResponse, StatusCodes.BadUserAccessDenied,
                        RecoverableJournalTestData.Response, RecoverableJournalTestData.Available().Permissions))
                        .ConfigureAwait(false);
                }
            }
            RecoverableExecutionDurableRecord before = entry.Current;
            int commits = test.Store.Commits;
            test.Clock.Now = RecoverableJournalTestData.AtTicks(test.Clock.Now.Ticks + 100_000);
            for (int index = 0; index < 2; index++)
            {
                await Complete(entry.CommitOutcomeAsync(
                    ServiceOperationOutcome.ServiceResponse, StatusCodes.BadInternalError,
                    ByteString.From(0x99, 0x88),
                    [new RecoverableNodePermission(new NodeId(999), PermissionType.Write)]))
                    .ConfigureAwait(false);
            }

            Assert.That(entry.Current.Kind, Is.EqualTo(before.Kind));
            Assert.That(entry.Current.Status, Is.EqualTo(before.Status));
            Assert.That(entry.Current.State, Is.EqualTo(before.State));
            Assert.That(entry.Current.Response, Is.EqualTo(before.Response));
            Assert.That(entry.Current.Permissions.Span.ToArray(), Is.EqualTo(before.Permissions.Span.ToArray()));
            Assert.That(entry.Current.OutcomeAt, Is.EqualTo(before.OutcomeAt));
            Assert.That(entry.Current.ReleasedAt, Is.EqualTo(before.ReleasedAt));
            Assert.That(entry.Current.CancellationRequested, Is.EqualTo(before.CancellationRequested));
            Assert.That(entry.Current.WorkFinished, Is.True);
            Assert.That(test.Store.Commits, Is.EqualTo(commits + (before.WorkFinished ? 0 : 1)));
            Assert.That(entry.Current.Revision, Is.EqualTo(before.Revision + (before.WorkFinished ? 0UL : 1UL)));
            if (selected != "original")
            {
                Assert.That(entry.Current.Response.IsNull, Is.True);
            }
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.False);
        }

        [TestCase("protect-throw")]
        [TestCase("protect-empty")]
        [TestCase("protect-oversized")]
        [TestCase("invalid-permission")]
        [TestCase("cas-false")]
        [TestCase("cas-before")]
        [TestCase("cas-after")]
        public async Task SerializationOrCasFailurePoisonsEveryLiveAccessPath(string fault)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
            ByteString before = await test.StoredBytesAsync().ConfigureAwait(false);
            if (fault.StartsWith("protect-", StringComparison.Ordinal))
            {
                test.Protection.FailAt = test.Protection.Calls + 1;
                test.Protection.Failure = fault["protect-".Length..];
            }
            else if (fault.StartsWith("cas-", StringComparison.Ordinal))
            {
                test.Store.FailAt = test.Store.CasAttempts + 1;
                test.Store.FailAfterCommit = fault == "cas-after";
                test.Store.ReturnFalse = fault == "cas-false";
            }
            if (fault == "invalid-permission")
            {
                var duplicate = new RecoverableNodePermission(new NodeId(201), PermissionType.Read);
                await UnavailableAsync(() => Complete(entry.CommitOutcomeAsync(
                    ServiceOperationOutcome.ServiceResponse, StatusCodes.Good,
                    RecoverableJournalTestData.Response, [duplicate, duplicate]))).ConfigureAwait(false);
            }
            else
            {
                await UnavailableAsync(() => Complete(entry.ControlAsync(AsyncServiceAccess.Cancel, default)))
                    .ConfigureAwait(false);
            }

            await AssertPoisonedAsync(test.Journal, entry).ConfigureAwait(false);
            bool committed = fault == "cas-after";
            RecoverableExecutionDurableRecord persisted = await test.StoredAsync().ConfigureAwait(false);
            Assert.That(persisted.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.Pending));
            Assert.That(persisted.CancellationRequested, Is.EqualTo(committed));
            Assert.That(persisted.DispatchStarted, Is.True);
            Assert.That(test.Store.Commits, Is.EqualTo(committed ? 4 : 3));
            if (!committed)
            {
                Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(before));
            }
            test.Store.FailAt = 0;
            test.Protection.FailAt = 0;
            test.Clock.Now = RecoverableJournalTestData.At(1010);
            RecoverableExecutionJournal.Entry restored =
                (await test.LoadAsync(test.NewJournal()).ConfigureAwait(false))[0];
            AssertIndeterminate(restored.Current, RecoverableJournalTestData.At(1010), workFinished: false);
            Assert.That(restored.Current.CancellationRequested, Is.EqualTo(committed));
            Assert.That(await Complete(restored.BeginDispatchAsync()).ConfigureAwait(false), Is.False);
        }

        [Test]
        public async Task CompetingJournalWriterRejectsStaleExpectedBytesAndPoisonsOldCache()
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry stale = await test.AdmitAsync().ConfigureAwait(false);
            Assert.That(await Complete(stale.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
            test.Clock.Now = RecoverableJournalTestData.At(1010);
            RecoverableExecutionJournal.Entry recovered =
                (await test.LoadAsync(test.NewJournal()).ConfigureAwait(false))[0];
            ByteString currentBytes = await test.StoredBytesAsync().ConfigureAwait(false);

            await UnavailableAsync(() => Complete(stale.ControlAsync(AsyncServiceAccess.Cancel, default)))
                .ConfigureAwait(false);

            await AssertPoisonedAsync(test.Journal, stale).ConfigureAwait(false);
            Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(currentBytes));
            Assert.That(test.Store.CasAttempts, Is.EqualTo(6));
            Assert.That(test.Store.Commits, Is.EqualTo(5));
            AssertIndeterminate(recovered.Current, RecoverableJournalTestData.At(1010), workFinished: false);
            Assert.That(recovered.Current.CancellationRequested, Is.False);
        }

        [Test]
        public async Task RevisionOverflowCannotWrapOrExposeCachedControlSuccess()
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.At(1020);
            await test.SeedAsync(RecoverableJournalTestData.Available() with { Revision = ulong.MaxValue })
                .ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = (await test.LoadAsync().ConfigureAwait(false))[0];
            ByteString before = await test.StoredBytesAsync().ConfigureAwait(false);

            await UnavailableAsync(() => Complete(entry.ControlAsync(AsyncServiceAccess.Acknowledge, default)))
                .ConfigureAwait(false);

            Assert.That(entry.Current.Revision, Is.EqualTo(ulong.MaxValue));
            Assert.That(entry.Current.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(before));
            Assert.That(test.Store.CasAttempts, Is.Zero);
            await AssertPoisonedAsync(test.Journal, entry).ConfigureAwait(false);
        }

        [TestCaseSource(nameof(ControlCases))]
        public async Task ControlDispositionMatrixPreservesUnrelatedStateAndDeadlines(
            string phase, AsyncServiceAccess action, ServiceOperationControlResult expected)
        {
            await using var test = new JournalScope();
            RecoverableExecutionJournal.Entry entry;
            if (phase == "Pending")
            {
                await test.LoadAsync().ConfigureAwait(false);
                entry = await test.AdmitAsync().ConfigureAwait(false);
                test.Clock.Now = RecoverableJournalTestData.At(1040);
            }
            else
            {
                RecoverableExecutionDurableRecord seed = phase switch
                {
                    "Available" => RecoverableJournalTestData.Available(),
                    "Acknowledged" => RecoverableJournalTestData.Released(ServiceOperationState.Acknowledged),
                    "Abandoned" => RecoverableJournalTestData.Released(ServiceOperationState.Abandoned),
                    "Expired" => RecoverableJournalTestData.Released(ServiceOperationState.Expired),
                    _ => RecoverableJournalTestData.Released(ServiceOperationState.Abandoned) with
                    {
                        WorkFinished = false
                    }
                };
                test.Clock.Now = RecoverableJournalTestData.At(phase == "Expired" ? 1080 : 1040);
                await test.SeedAsync(seed).ConfigureAwait(false);
                entry = (await test.LoadAsync().ConfigureAwait(false))[0];
                if (phase == "Unknown")
                {
                    test.Clock.Now = RecoverableJournalTestData.At(1090);
                }
            }
            RecoverableExecutionDurableRecord before = entry.Current;
            ByteString protectedBefore = await test.StoredBytesAsync().ConfigureAwait(false);
            int commitsBefore = test.Store.Commits;

            ServiceOperationControlResult disposition =
                await Complete(entry.ControlAsync(action, default)).ConfigureAwait(false);

            Assert.That(disposition, Is.EqualTo(expected));
            bool applied = expected == ServiceOperationControlResult.Applied;
            bool released = applied && action != AsyncServiceAccess.Cancel;
            ServiceOperationState expectedState = released
                ? action == AsyncServiceAccess.Acknowledge
                    ? ServiceOperationState.Acknowledged : ServiceOperationState.Abandoned
                : before.State;
            Assert.That(entry.Current.State, Is.EqualTo(expectedState));
            Assert.That(entry.Current.Phase, Is.EqualTo(released
                ? RecoverableExecutionCommitPhase.Released : before.Phase));
            Assert.That(entry.Current.Kind, Is.EqualTo(before.Kind));
            Assert.That(entry.Current.Status, Is.EqualTo(before.Status));
            Assert.That(entry.Current.AdmittedAt, Is.EqualTo(before.AdmittedAt));
            Assert.That(entry.Current.OutcomeAt, Is.EqualTo(before.OutcomeAt));
            Assert.That(entry.Current.ReleasedAt, Is.EqualTo(released ? test.Clock.Now : before.ReleasedAt));
            Assert.That(entry.Current.CancellationRequested,
                Is.EqualTo(applied && action == AsyncServiceAccess.Cancel));
            Assert.That(entry.Current.WorkFinished, Is.EqualTo(before.WorkFinished));
            Assert.That(entry.Current.DispatchStarted, Is.EqualTo(before.DispatchStarted));
            Assert.That(entry.Current.Request, Is.EqualTo(released ? default : before.Request));
            Assert.That(entry.Current.Response, Is.EqualTo(released ? default : before.Response));
            Assert.That(entry.Current.Permissions.Count, Is.EqualTo(released ? 0 : before.Permissions.Count));
            Assert.That(entry.Current.Permissions.Span.ToArray(), Is.EqualTo(released
                ? [] : before.Permissions.Span.ToArray()));
            Assert.That(test.Store.Commits, Is.EqualTo(commitsBefore + (applied ? 1 : 0)));
            Assert.That(entry.Current.Revision, Is.EqualTo(before.Revision + (applied ? 1UL : 0UL)));
            Assert.That(entry.Forgotten, Is.False);
            if (!applied)
            {
                Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(protectedBefore));
            }
        }

        [TestCase(AsyncServiceAccess.Submit)]
        [TestCase(AsyncServiceAccess.Collect)]
        [TestCase((AsyncServiceAccess)(-1))]
        [TestCase((AsyncServiceAccess)int.MaxValue)]
        public async Task UnsupportedControlNeverMutatesRecord(AsyncServiceAccess action)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            ByteString before = await test.StoredBytesAsync().ConfigureAwait(false);
            int ownerChecks = test.OwnerChecks;
            int clockReads = test.Clock.Reads;
            int healthChecks = test.Store.HealthChecks;

            await ErrorAsync(() => Complete(entry.ControlAsync(action, default)), StatusCodes.BadInvalidArgument)
                .ConfigureAwait(false);

            Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(before));
            Assert.That(test.Store.Commits, Is.EqualTo(2));
            Assert.That(test.OwnerChecks, Is.EqualTo(ownerChecks));
            Assert.That(test.Clock.Reads, Is.EqualTo(clockReads));
            Assert.That(test.Store.HealthChecks, Is.EqualTo(healthChecks));
            Assert.That(test.Journal.IsReady, Is.True);
        }

        [TestCase(AsyncServiceAccess.Cancel)]
        [TestCase(AsyncServiceAccess.Acknowledge)]
        [TestCase(AsyncServiceAccess.Abandon)]
        public async Task ConcurrentSameActionRetriesCommitOnceWithoutRenewal(AsyncServiceAccess action)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            long originMilliseconds = 1000;
            if (action == AsyncServiceAccess.Acknowledge)
            {
                Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
                test.Clock.Now = RecoverableJournalTestData.At(1020);
                await Complete(entry.CommitOutcomeAsync(
                    ServiceOperationOutcome.ServiceResponse, StatusCodes.Good,
                    RecoverableJournalTestData.Response, RecoverableJournalTestData.Available().Permissions))
                    .ConfigureAwait(false);
                originMilliseconds = 1020;
            }
            RecoverableExecutionDurableRecord before = entry.Current;
            int commits = test.Store.Commits;
            using CasGate gate = test.Store.Gate(test.Store.CasAttempts + 1, afterCommit: true);
            Task<ServiceOperationControlResult> first =
                test.Start(Complete(entry.ControlAsync(action, default)));
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.At(originMilliseconds + 20);
            Task<ServiceOperationControlResult> retry =
                test.Start(Complete(entry.ControlAsync(action, default)));
            try
            {
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(retry.IsCompleted, Is.False);
                Assert.That(test.Store.Commits, Is.EqualTo(commits + 1));
                Assert.That(entry.Current.Revision, Is.EqualTo(before.Revision));
            }
            finally
            {
                gate.Release();
            }
            ServiceOperationControlResult[] results =
                await Task.WhenAll(first, retry).WaitAsync(s_watchdog).ConfigureAwait(false);

            Assert.That(results, Is.EqualTo(
            [
                ServiceOperationControlResult.Applied, ServiceOperationControlResult.AlreadyApplied
            ]));
            Assert.That(test.Store.Commits, Is.EqualTo(commits + 1));
            Assert.That(entry.Current.Revision, Is.EqualTo(before.Revision + 1));
            Assert.That(entry.Current.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(entry.Current.OutcomeAt, Is.EqualTo(before.OutcomeAt));
            Assert.That(entry.Current.ReleasedAt, Is.EqualTo(action == AsyncServiceAccess.Cancel
                ? default : RecoverableJournalTestData.At(originMilliseconds)));
            Assert.That(entry.Current.Snapshot(test.Clock.Now).HistoryRemaining,
                Is.EqualTo(action == AsyncServiceAccess.Cancel ? 0 : 30));
            Assert.That(entry.Current.CancellationRequested, Is.EqualTo(action == AsyncServiceAccess.Cancel));
            Assert.That(entry.Current.WorkFinished, Is.EqualTo(before.WorkFinished));
            test.Clock.Now = RecoverableJournalTestData.At(originMilliseconds + 21);
            Assert.That(await Complete(entry.ControlAsync(action, default)).ConfigureAwait(false),
                Is.EqualTo(ServiceOperationControlResult.AlreadyApplied));
            Assert.That(test.Store.Commits, Is.EqualTo(commits + 1));
            Assert.That(entry.Current.ReleasedAt, Is.EqualTo(action == AsyncServiceAccess.Cancel
                ? default : RecoverableJournalTestData.At(originMilliseconds)));
        }

        [TestCase(AsyncServiceAccess.Acknowledge)]
        [TestCase(AsyncServiceAccess.Abandon)]
        public async Task ReleaseSamplesLogicalTimeInsideSerializerAndCommitLatencyConsumesHistory(
            AsyncServiceAccess action)
        {
            await using var test = new JournalScope();
            RecoverableExecutionJournal.Entry entry;
            if (action == AsyncServiceAccess.Acknowledge)
            {
                test.Clock.Now = RecoverableJournalTestData.At(1020);
                await test.SeedAsync(RecoverableJournalTestData.Available()).ConfigureAwait(false);
                entry = (await test.LoadAsync().ConfigureAwait(false))[0];
            }
            else
            {
                await test.LoadAsync().ConfigureAwait(false);
                entry = await test.AdmitAsync().ConfigureAwait(false);
            }
            RecoverableExecutionDurableRecord before = entry.Current;
            long logicalRelease = action == AsyncServiceAccess.Acknowledge ? 1030 : 1010;
            using ClockGate serializerGate = test.Clock.Gate(test.Clock.Reads + 1);
            using CasGate releaseGate = test.Store.Gate(test.Store.CasAttempts + 1, afterCommit: true);
            Task<(RecoverableExecutionDurableRecord Record, RecoverableExecutionTime Now)> observation =
                test.Start(Complete(entry.ObserveAsync(default)));
            await serializerGate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            Task<ServiceOperationControlResult> control =
                test.Start(Complete(entry.ControlAsync(action, default)));
            Assert.That(control.IsCompleted, Is.False);
            test.Clock.Now = RecoverableJournalTestData.At(logicalRelease);
            serializerGate.Release();
            await releaseGate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            try
            {
                RecoverableExecutionDurableRecord persisted = await test.StoredAsync().ConfigureAwait(false);
                Assert.That(persisted.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.Released));
                Assert.That(persisted.State, Is.EqualTo(action == AsyncServiceAccess.Acknowledge
                    ? ServiceOperationState.Acknowledged : ServiceOperationState.Abandoned));
                Assert.That(persisted.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(logicalRelease)));
                Assert.That(persisted.Request.IsNull, Is.True);
                Assert.That(persisted.Response.IsNull, Is.True);
                Assert.That(persisted.Permissions.Count, Is.Zero);
                Assert.That(persisted.Kind, Is.EqualTo(before.Kind));
                Assert.That(persisted.Status, Is.EqualTo(before.Status));
                Assert.That(entry.Current.State, Is.EqualTo(before.State));
                Assert.That(control.IsCompleted, Is.False, "A committed bundle is not confirmed before CAS returns.");
                test.Clock.Now = RecoverableJournalTestData.At(logicalRelease + 30);
            }
            finally
            {
                releaseGate.Release();
            }
            _ = await observation.WaitAsync(s_watchdog).ConfigureAwait(false);
            Assert.That(await control.WaitAsync(s_watchdog).ConfigureAwait(false),
                Is.EqualTo(ServiceOperationControlResult.Applied));
            (RecoverableExecutionDurableRecord released, RecoverableExecutionTime now) =
                await Complete(entry.ObserveAsync(default)).ConfigureAwait(false);

            Assert.That(released.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(logicalRelease)));
            Assert.That(released.Snapshot(now).HistoryRemaining, Is.EqualTo(20));
            Assert.That(released.AdmittedAt, Is.EqualTo(before.AdmittedAt));
            Assert.That(released.OutcomeAt, Is.EqualTo(before.OutcomeAt));
            Assert.That(released.WorkFinished, Is.EqualTo(before.WorkFinished));
            Assert.That(released.CancellationRequested, Is.False);
            Assert.That(now, Is.EqualTo(RecoverableJournalTestData.At(logicalRelease + 30)));
        }

        [TestCase(AsyncServiceAccess.Acknowledge, false)]
        [TestCase(AsyncServiceAccess.Acknowledge, true)]
        [TestCase(AsyncServiceAccess.Abandon, false)]
        [TestCase(AsyncServiceAccess.Abandon, true)]
        public async Task ReleaseFaultReopensBeforeOrAfterAtomicReleaseBundle(
            AsyncServiceAccess action, bool afterCommit)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
            test.Clock.Now = RecoverableJournalTestData.At(1020);
            if (action == AsyncServiceAccess.Acknowledge)
            {
                await Complete(entry.CommitOutcomeAsync(
                    ServiceOperationOutcome.ServiceResponse, StatusCodes.Good, RecoverableJournalTestData.Response,
                    default)).ConfigureAwait(false);
            }
            int committedBefore = test.Store.Commits;
            test.Store.FailAt = test.Store.CasAttempts + 1;
            test.Store.FailAfterCommit = afterCommit;
            using CasGate gate = test.Store.Gate(test.Store.FailAt, afterCommit);
            Task<ServiceOperationControlResult> control =
                test.Start(Complete(entry.ControlAsync(action, default)));
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            gate.Release();
            await UnavailableAsync(() => control).ConfigureAwait(false);
            await AssertPoisonedAsync(test.Journal, entry).ConfigureAwait(false);
            RecoverableExecutionDurableRecord stored = await test.StoredAsync().ConfigureAwait(false);
            Assert.That(stored.IsReleased, Is.EqualTo(afterCommit));
            Assert.That(stored.ReleasedAt, Is.EqualTo(afterCommit ? RecoverableJournalTestData.At(1020) : default));
            Assert.That(test.Store.Commits, Is.EqualTo(committedBefore + (afterCommit ? 1 : 0)));
            test.Store.FailAt = 0;
            test.Clock.Now = RecoverableJournalTestData.At(1030);

            RecoverableExecutionJournal.Entry reopened =
                (await test.LoadAsync(test.NewJournal()).ConfigureAwait(false))[0];

            if (afterCommit)
            {
                Assert.That(reopened.Current.State, Is.EqualTo(action == AsyncServiceAccess.Acknowledge
                    ? ServiceOperationState.Acknowledged : ServiceOperationState.Abandoned));
                Assert.That(reopened.Current.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
                Assert.That(reopened.Current.Snapshot(test.Clock.Now).HistoryRemaining, Is.EqualTo(40));
                Assert.That(reopened.Current.Request.IsNull, Is.True);
                Assert.That(reopened.Current.Response.IsNull, Is.True);
                Assert.That(reopened.Current.Permissions.Count, Is.Zero);
                Assert.That(test.Store.Commits, Is.EqualTo(committedBefore + 1));
            }
            else if (action == AsyncServiceAccess.Acknowledge)
            {
                Assert.That(reopened.Current.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
                Assert.That(reopened.Current.Response, Is.EqualTo(RecoverableJournalTestData.Response));
                Assert.That(reopened.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
                Assert.That(test.Store.Commits, Is.EqualTo(committedBefore));
            }
            else
            {
                AssertIndeterminate(reopened.Current, RecoverableJournalTestData.At(1030), workFinished: false);
                Assert.That(test.Store.Commits, Is.EqualTo(committedBefore + 2));
            }
        }

        [TestCase("result", -1L)]
        [TestCase("result", 0L)]
        [TestCase("result", 1L)]
        [TestCase("history", -1L)]
        [TestCase("history", 0L)]
        [TestCase("history", 1L)]
        public async Task ResultAndHistoryExpiryUseOriginalExactBoundaries(string timer, long deltaTicks)
        {
            await using var test = new JournalScope();
            bool resultTimer = timer == "result";
            RecoverableExecutionDurableRecord seed = resultTimer
                ? RecoverableJournalTestData.Available()
                : RecoverableJournalTestData.Released(ServiceOperationState.Acknowledged);
            test.Clock.Now = RecoverableJournalTestData.At(resultTimer ? 1020 : 1040);
            await test.SeedAsync(seed).ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = (await test.LoadAsync().ConfigureAwait(false))[0];
            ByteString before = await test.StoredBytesAsync().ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.AtTicks(
                (resultTimer ? 10_700_000 : 10_900_000) + deltaTicks);

            (RecoverableExecutionDurableRecord record, RecoverableExecutionTime now) =
                await Complete(entry.ObserveAsync(default)).ConfigureAwait(false);

            bool elapsed = deltaTicks >= 0;
            Assert.That(now, Is.EqualTo(test.Clock.Now));
            Assert.That(record.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(record.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            Assert.That(record.Snapshot(now).ExecutionAllowance, Is.EqualTo(100));
            Assert.That(record.Snapshot(now).ResultRetentionTime, Is.EqualTo(50));
            Assert.That(record.Snapshot(now).ResultRemaining, Is.Zero);
            Assert.That(entry.Forgotten, Is.EqualTo(!resultTimer && elapsed));
            Assert.That(test.Store.Commits, Is.EqualTo(elapsed ? 1 : 0));
            Assert.That(test.Store.Deletes, Is.EqualTo(!resultTimer && elapsed ? 1 : 0));
            if (resultTimer)
            {
                Assert.That(record.State, Is.EqualTo(elapsed
                    ? ServiceOperationState.Expired : ServiceOperationState.OutcomeAvailable));
                Assert.That(record.ReleasedAt, Is.EqualTo(elapsed ? RecoverableJournalTestData.At(1070) : default));
                Assert.That(record.Response, Is.EqualTo(elapsed ? default : RecoverableJournalTestData.Response));
                Assert.That(record.Snapshot(now).HistoryRemaining,
                    Is.EqualTo(elapsed ? deltaTicks == 0 ? 50 : 49 : 0));
                Assert.That(
                    (await Complete(test.Raw.TryGetAsync(JournalScope.Key)).ConfigureAwait(false)).Found, Is.True);
            }
            else
            {
                Assert.That(record.State, Is.EqualTo(ServiceOperationState.Acknowledged));
                Assert.That(record.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(1040)));
                Assert.That(record.Request.IsNull, Is.True);
                Assert.That(record.Response.IsNull, Is.True);
                Assert.That(record.Snapshot(now).HistoryRemaining, Is.Zero);
                Assert.That((await Complete(test.Raw.TryGetAsync(JournalScope.Key)).ConfigureAwait(false)).Found,
                    Is.EqualTo(!elapsed));
                Assert.That(await Complete(entry.ControlAsync(AsyncServiceAccess.Acknowledge, default))
                    .ConfigureAwait(false), Is.EqualTo(elapsed
                    ? ServiceOperationControlResult.UnknownOperation : ServiceOperationControlResult.AlreadyApplied));
            }
            if (!elapsed)
            {
                Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(before));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DelayedObservationIncludesDowntimeWithoutRenewingHistory(bool restart)
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.At(1020);
            await test.SeedAsync(RecoverableJournalTestData.Available()).ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = (await test.LoadAsync().ConfigureAwait(false))[0];
            test.Clock.Now = RecoverableJournalTestData.At(1200);
            if (restart)
            {
                test.Journal.Dispose();
                entry = (await test.LoadAsync(test.NewJournal()).ConfigureAwait(false))[0];
            }
            (RecoverableExecutionDurableRecord record, RecoverableExecutionTime now) =
                await Complete(entry.ObserveAsync(default)).ConfigureAwait(false);

            Assert.That(record.State, Is.EqualTo(ServiceOperationState.Expired));
            Assert.That(record.Kind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(record.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(record.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            Assert.That(record.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(1070)));
            Assert.That(record.Request.IsNull, Is.True);
            Assert.That(record.Response.IsNull, Is.True);
            Assert.That(record.Snapshot(now).ResultRemaining, Is.Zero);
            Assert.That(record.Snapshot(now).HistoryRemaining, Is.Zero);
            Assert.That(entry.Forgotten, Is.True);
            Assert.That(test.Store.Commits, Is.EqualTo(2));
            Assert.That(test.Store.Deletes, Is.EqualTo(1));
            Assert.That((await Complete(test.Raw.TryGetAsync(JournalScope.Key)).ConfigureAwait(false)).Found, Is.False);
            Assert.That(await Complete(entry.ControlAsync(AsyncServiceAccess.Cancel, default)).ConfigureAwait(false),
                Is.EqualTo(ServiceOperationControlResult.UnknownOperation));
            Assert.That((await test.LoadAsync(test.NewJournal()).ConfigureAwait(false)).Count, Is.Zero);
        }

        [Test]
        public async Task ExpiryCommitLatencyConsumesHistoryBeforeObservationReturns()
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.At(1020);
            await test.SeedAsync(RecoverableJournalTestData.Available()).ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = (await test.LoadAsync().ConfigureAwait(false))[0];
            test.Clock.Now = RecoverableJournalTestData.At(1070);
            using CasGate gate = test.Store.Gate(1, afterCommit: true);
            Task<(RecoverableExecutionDurableRecord Record, RecoverableExecutionTime Now)> observation =
                test.Start(Complete(entry.ObserveAsync(default)));
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            try
            {
                RecoverableExecutionDurableRecord stored = await test.StoredAsync().ConfigureAwait(false);
                Assert.That(stored.State, Is.EqualTo(ServiceOperationState.Expired));
                Assert.That(stored.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(1070)));
                Assert.That(stored.Response.IsNull, Is.True);
                test.Clock.Now = RecoverableJournalTestData.At(1120);
            }
            finally
            {
                gate.Release();
            }
            (RecoverableExecutionDurableRecord record, RecoverableExecutionTime now) =
                await observation.WaitAsync(s_watchdog).ConfigureAwait(false);
            Assert.That(now, Is.EqualTo(RecoverableJournalTestData.At(1120)));
            Assert.That(record.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(1070)));
            Assert.That(record.Snapshot(now).HistoryRemaining, Is.Zero);
            Assert.That(entry.Forgotten, Is.True);
            Assert.That(test.Store.Commits, Is.EqualTo(2));
            Assert.That(test.Store.Deletes, Is.EqualTo(1));
        }

        [TestCase(ServiceOperationState.Acknowledged)]
        [TestCase(ServiceOperationState.Abandoned)]
        [TestCase(ServiceOperationState.Expired)]
        public async Task ReleasedRecordsStayReleasedAcrossRestartWithoutRenewal(ServiceOperationState state)
        {
            await using var test = new JournalScope();
            RecoverableExecutionDurableRecord released = RecoverableJournalTestData.Released(state);
            await test.SeedAsync(released).ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.At(state == ServiceOperationState.Expired ? 1080 : 1050);
            ByteString before = await test.StoredBytesAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = (await test.LoadAsync().ConfigureAwait(false))[0];

            Assert.That(entry.Current.State, Is.EqualTo(state));
            Assert.That(entry.Current.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.Released));
            Assert.That(entry.Current.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(
                state == ServiceOperationState.Expired ? 1070 : 1040)));
            Assert.That(entry.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            Assert.That(entry.Current.Request.IsNull, Is.True);
            Assert.That(entry.Current.Response.IsNull, Is.True);
            Assert.That(entry.Current.Permissions.Count, Is.Zero);
            Assert.That(entry.Current.Snapshot(test.Clock.Now).HistoryRemaining, Is.EqualTo(40));
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.False);
            Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(before));
            Assert.That(test.Store.Commits, Is.Zero);
            test.Clock.Now = RecoverableJournalTestData.At(state == ServiceOperationState.Expired ? 1090 : 1060);
            RecoverableExecutionJournal.Entry again =
                (await test.LoadAsync(test.NewJournal()).ConfigureAwait(false))[0];
            Assert.That(again.Current.State, Is.EqualTo(state));
            Assert.That(again.Current.ReleasedAt, Is.EqualTo(released.ReleasedAt));
            Assert.That(again.Current.Snapshot(test.Clock.Now).HistoryRemaining, Is.EqualTo(30));
            Assert.That(test.Store.Commits, Is.Zero);
        }

        [Test]
        public async Task DispatchedOrphanSurvivesExpiredHistoryAndRejectsDuplicateAdmission()
        {
            await using var test = new JournalScope();
            await test.SeedAsync(RecoverableJournalTestData.Pending() with
            {
                DispatchStarted = true,
                Revision = 3
            }).ConfigureAwait(false);
            RecoverableExecutionJournal.Entry orphan = (await test.LoadAsync().ConfigureAwait(false))[0];
            AssertIndeterminate(orphan.Current, RecoverableJournalTestData.At(1000), workFinished: false);
            Assert.That(await Complete(orphan.BeginDispatchAsync()).ConfigureAwait(false), Is.False);
            test.Clock.Now = RecoverableJournalTestData.At(1100);
            _ = await Complete(orphan.ObserveAsync(default)).ConfigureAwait(false);
            Assert.That(orphan.Current.State, Is.EqualTo(ServiceOperationState.Expired));
            Assert.That(orphan.Current.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(1050)));
            Assert.That(orphan.Current.CancellationRequested, Is.True);
            Assert.That(orphan.Current.WorkFinished, Is.False);
            Assert.That(orphan.Forgotten, Is.False);
            Assert.That((await Complete(test.Raw.TryGetAsync(JournalScope.Key)).ConfigureAwait(false)).Found, Is.True);
            int commits = test.Store.Commits;
            ByteString orphanBytes = await test.StoredBytesAsync().ConfigureAwait(false);
            orphan = (await test.LoadAsync(test.NewJournal()).ConfigureAwait(false))[0];
            Assert.That(orphan.Forgotten, Is.False);
            Assert.That(orphan.Current.DispatchStarted, Is.True);
            Assert.That(orphan.Current.WorkFinished, Is.False);
            Assert.That(orphan.Current.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(1050)));
            Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(orphanBytes));
            Assert.That(test.Store.Commits, Is.EqualTo(commits));
            foreach (AsyncServiceAccess action in new[]
            {
                AsyncServiceAccess.Acknowledge, AsyncServiceAccess.Cancel, AsyncServiceAccess.Abandon
            })
            {
                Assert.That(await Complete(orphan.ControlAsync(action, default)).ConfigureAwait(false),
                    Is.EqualTo(ServiceOperationControlResult.UnknownOperation));
            }
            await ErrorAsync(() => test.AdmitAsync(), StatusCodes.BadInvalidState).ConfigureAwait(false);
            Assert.That(test.Store.Commits, Is.EqualTo(commits));
            Assert.That(test.Store.Deletes, Is.Zero);
            Assert.That(orphan.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));

            // This API call supplies independently known work completion; the test never executes original work.
            await Complete(orphan.CommitOutcomeAsync(
                ServiceOperationOutcome.ServiceResponse, StatusCodes.Good, ByteString.From(0x99, 0x88), default))
                .ConfigureAwait(false);

            Assert.That(orphan.Current.Kind, Is.EqualTo(ServiceOperationOutcome.ExecutionIndeterminate));
            Assert.That(orphan.Current.Status, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(orphan.Current.Response.IsNull, Is.True);
            Assert.That(orphan.Current.WorkFinished, Is.True);
            Assert.That(orphan.Current.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(1050)));
            Assert.That(orphan.Forgotten, Is.True);
            Assert.That(test.Store.Commits, Is.EqualTo(commits + 2));
            Assert.That(test.Store.Deletes, Is.EqualTo(1));
            Assert.That((await Complete(test.Raw.TryGetAsync(JournalScope.Key)).ConfigureAwait(false)).Found, Is.False);
            RecoverableExecutionJournal.Entry fresh = await test.AdmitAsync().ConfigureAwait(false);
            Assert.That(fresh.Current.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.Pending));
            Assert.That(fresh.Current.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1100)));
            Assert.That(fresh.Current.DispatchStarted, Is.False);
            Assert.That(fresh.Current.Revision, Is.EqualTo(2));
        }

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public async Task RecoveryUsesDispatchAndWorkFinishedProofConservatively(
            bool released, bool dispatchStarted, bool workFinished)
        {
            await using var test = new JournalScope();
            RecoverableExecutionDurableRecord record = RecoverableJournalTestData.Pending() with
            {
                DispatchStarted = dispatchStarted,
                WorkFinished = workFinished,
                Revision = 3
            };
            if (released)
            {
                record = record with
                {
                    Phase = RecoverableExecutionCommitPhase.Released,
                    State = ServiceOperationState.Abandoned,
                    Request = default,
                    ReleasedAt = RecoverableJournalTestData.At(1010)
                };
            }
            test.Clock.Now = RecoverableJournalTestData.At(1010);
            await test.SeedAsync(record).ConfigureAwait(false);

            RecoverableExecutionJournal.Entry entry = (await test.LoadAsync().ConfigureAwait(false))[0];

            bool completionProved = workFinished || !dispatchStarted;
            Assert.That(entry.Current.DispatchStarted, Is.EqualTo(dispatchStarted));
            Assert.That(entry.Current.WorkFinished, Is.EqualTo(completionProved));
            Assert.That((await test.StoredAsync().ConfigureAwait(false)).WorkFinished, Is.EqualTo(completionProved));
            Assert.That(entry.Current.State, Is.EqualTo(released
                ? ServiceOperationState.Abandoned : ServiceOperationState.OutcomeAvailable));
            Assert.That(entry.Current.Kind, Is.EqualTo(released
                ? ServiceOperationOutcome.None : ServiceOperationOutcome.ExecutionIndeterminate));
            if (released && !completionProved)
            {
                await ErrorAsync(() => Complete(entry.BeginDispatchAsync()), StatusCodes.BadInvalidState)
                    .ConfigureAwait(false);
            }
            else
            {
                Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.False);
            }
            int commits = test.Store.Commits;
            test.Clock.Now = RecoverableJournalTestData.At(1200);
            _ = await Complete(entry.ObserveAsync(default)).ConfigureAwait(false);

            Assert.That(entry.Forgotten, Is.EqualTo(completionProved));
            Assert.That(test.Store.Deletes, Is.EqualTo(completionProved ? 1 : 0));
            Assert.That((await Complete(test.Raw.TryGetAsync(JournalScope.Key)).ConfigureAwait(false)).Found,
                Is.EqualTo(!completionProved));
            Assert.That(test.Store.Commits, Is.EqualTo(commits + (released ? 1 : 2)));
            Assert.That(entry.Current.Response.IsNull, Is.True);
            Assert.That(await Complete(entry.ControlAsync(AsyncServiceAccess.Abandon, default)).ConfigureAwait(false),
                Is.EqualTo(ServiceOperationControlResult.UnknownOperation));
        }

        [TestCase(1006)]
        [TestCase(1060)]
        [TestCase(1100)]
        [TestCase(1101)]
        public async Task AbandonBeforeDispatchDoesNotCancelOrSuppressPredeadlineWork(int dispatchMilliseconds)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.At(1005);
            Assert.That(await Complete(entry.ControlAsync(AsyncServiceAccess.Abandon, default)).ConfigureAwait(false),
                Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(entry.Current.State, Is.EqualTo(ServiceOperationState.Abandoned));
            Assert.That(entry.Current.Request.IsNull, Is.True);
            Assert.That(entry.Current.Response.IsNull, Is.True);
            Assert.That(entry.Current.CancellationRequested, Is.False);
            Assert.That(entry.Current.WorkFinished, Is.False);
            test.Clock.Now = RecoverableJournalTestData.At(dispatchMilliseconds);

            bool permission = await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false);

            bool beforeDeadline = dispatchMilliseconds < 1100;
            Assert.That(permission, Is.EqualTo(beforeDeadline));
            Assert.That(entry.Current.DispatchStarted, Is.EqualTo(beforeDeadline));
            Assert.That(entry.Current.WorkFinished, Is.EqualTo(!beforeDeadline));
            Assert.That(entry.Current.CancellationRequested, Is.False);
            Assert.That(entry.Current.State, Is.EqualTo(ServiceOperationState.Abandoned));
            Assert.That(entry.Current.Kind, Is.EqualTo(ServiceOperationOutcome.None));
            Assert.That(entry.Current.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(entry.Current.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(1005)));
            Assert.That(entry.Forgotten, Is.EqualTo(!beforeDeadline));
            Assert.That(test.Store.Commits, Is.EqualTo(beforeDeadline ? 4 : 5));
            Assert.That(test.Store.Deletes, Is.EqualTo(beforeDeadline ? 0 : 1));
            if (beforeDeadline)
            {
                await ErrorAsync(() => Complete(entry.BeginDispatchAsync()), StatusCodes.BadInvalidState)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task CancelBeforeExecutionDeadlineRecordsIntentWithoutInventingTimeout()
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.At(1005);
            Assert.That(await Complete(entry.ControlAsync(AsyncServiceAccess.Cancel, default)).ConfigureAwait(false),
                Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(entry.Current.CancellationRequested, Is.True);
            Assert.That(entry.Current.WorkFinished, Is.False);
            Assert.That(entry.Current.Request, Is.EqualTo(ByteString.From(0x52, 0x51, 0x00, 0xff)));
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
            test.Clock.Now = RecoverableJournalTestData.AtTicks(11_000_000 - 1);
            (RecoverableExecutionDurableRecord pending, _) =
                await Complete(entry.ObserveAsync(default)).ConfigureAwait(false);
            Assert.That(pending.State, Is.EqualTo(ServiceOperationState.Pending));
            Assert.That(pending.Kind, Is.EqualTo(ServiceOperationOutcome.None));
            Assert.That(pending.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(pending.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(pending.WorkFinished, Is.False);
            Assert.That(test.Store.Commits, Is.EqualTo(4));
            test.Clock.Now = RecoverableJournalTestData.At(1100);

            _ = await Complete(entry.ObserveAsync(default)).ConfigureAwait(false);

            Assert.That(entry.Current.Kind, Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
            Assert.That(entry.Current.Status, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(entry.Current.Response.IsNull, Is.True);
            Assert.That(entry.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1100)));
            Assert.That(entry.Current.CancellationRequested, Is.True);
            Assert.That(entry.Current.WorkFinished, Is.False);
            Assert.That(test.Store.Commits, Is.EqualTo(6));
            await Complete(entry.CommitOutcomeAsync(
                ServiceOperationOutcome.ServiceResponse, StatusCodes.Good,
                RecoverableJournalTestData.Response, default))
                .ConfigureAwait(false);
            Assert.That(await Complete(entry.ControlAsync(AsyncServiceAccess.Cancel, default)).ConfigureAwait(false),
                Is.EqualTo(ServiceOperationControlResult.NotApplicable));
            Assert.That(entry.Current.WorkFinished, Is.True);
            Assert.That(entry.Current.Response.IsNull, Is.True);
            Assert.That(entry.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1100)));
        }

        [Test]
        public async Task CancelCanRecordIntentOnReleasedButStillActiveWork()
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.At(1040);
            await test.SeedAsync(RecoverableJournalTestData.Released(ServiceOperationState.Abandoned) with
            {
                WorkFinished = false
            }).ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = (await test.LoadAsync().ConfigureAwait(false))[0];
            Assert.That(await Complete(entry.ControlAsync(AsyncServiceAccess.Cancel, default)).ConfigureAwait(false),
                Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(await Complete(entry.ControlAsync(AsyncServiceAccess.Cancel, default)).ConfigureAwait(false),
                Is.EqualTo(ServiceOperationControlResult.AlreadyApplied));
            Assert.That(entry.Current.CancellationRequested, Is.True);
            Assert.That(entry.Current.WorkFinished, Is.False);
            Assert.That(entry.Current.State, Is.EqualTo(ServiceOperationState.Abandoned));
            Assert.That(entry.Current.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(1040)));
            Assert.That(entry.Current.Response.IsNull, Is.True);
            Assert.That(test.Store.Commits, Is.EqualTo(1));

            await Complete(entry.CommitOutcomeAsync(
                ServiceOperationOutcome.ServiceResponse, StatusCodes.Good,
                RecoverableJournalTestData.Response, default))
                .ConfigureAwait(false);

            Assert.That(await Complete(entry.ControlAsync(AsyncServiceAccess.Cancel, default)).ConfigureAwait(false),
                Is.EqualTo(ServiceOperationControlResult.NotApplicable));
            Assert.That(entry.Current.WorkFinished, Is.True);
            Assert.That(entry.Current.CancellationRequested, Is.True);
            Assert.That(entry.Current.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(1040)));
            Assert.That(entry.Current.Response.IsNull, Is.True);
            Assert.That(test.Store.Commits, Is.EqualTo(2));
        }

        [TestCase(false, 0L)]
        [TestCase(false, 1L)]
        [TestCase(true, 0L)]
        [TestCase(true, 1L)]
        public async Task DispatchCommitLatencyCannotPermitWorkAtOrAfterExecutionDeadline(
            bool afterCommit, long deltaTicks)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            using CasGate gate = test.Store.Gate(3, afterCommit);
            Task<bool> permission = test.Start(Complete(entry.BeginDispatchAsync()));
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.AtTicks(11_000_000 + deltaTicks);
            gate.Release();

            Assert.That(await permission.WaitAsync(s_watchdog).ConfigureAwait(false), Is.False);

            Assert.That(entry.Current.DispatchStarted, Is.True);
            Assert.That(entry.Current.WorkFinished, Is.True, "The journal withheld its permission to start.");
            Assert.That(entry.Current.Kind, Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
            Assert.That(entry.Current.Status, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(entry.Current.Response.IsNull, Is.True);
            Assert.That(entry.Current.OutcomeAt,
                Is.EqualTo(RecoverableJournalTestData.AtTicks(11_000_000 + deltaTicks)));
            Assert.That(entry.Current.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(test.Store.Commits, Is.EqualTo(5));
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DispatchMarkerReplyLossRetainsOnlyActualCommittedWorkEvidence(bool afterCommit)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            test.Store.FailAt = 3;
            test.Store.FailAfterCommit = afterCommit;
            using CasGate gate = test.Store.Gate(3, afterCommit);
            Task<bool> permission = test.Start(Complete(entry.BeginDispatchAsync()));
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            gate.Release();
            await UnavailableAsync(() => permission).ConfigureAwait(false);
            await AssertPoisonedAsync(test.Journal, entry).ConfigureAwait(false);
            Assert.That((await test.StoredAsync().ConfigureAwait(false)).DispatchStarted, Is.EqualTo(afterCommit));
            test.Store.FailAt = 0;
            test.Clock.Now = RecoverableJournalTestData.At(1030);

            RecoverableExecutionJournal.Entry restored =
                (await test.LoadAsync(test.NewJournal()).ConfigureAwait(false))[0];

            AssertIndeterminate(restored.Current, RecoverableJournalTestData.At(1030), workFinished: !afterCommit);
            Assert.That(restored.Current.DispatchStarted, Is.EqualTo(afterCommit));
            Assert.That(await Complete(restored.BeginDispatchAsync()).ConfigureAwait(false), Is.False);
            Assert.That(test.Store.Commits, Is.EqualTo(afterCommit ? 5 : 4));
        }

        [Test]
        public async Task WorkCompletionCommitLatencyCannotRenewAbandonedHistory()
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync(
                RecoverableJournalTestData.Admission() with { Retention = TimeSpan.FromMilliseconds(200) })
                .ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.At(1040);
            Assert.That(await Complete(entry.ControlAsync(AsyncServiceAccess.Abandon, default)).ConfigureAwait(false),
                Is.EqualTo(ServiceOperationControlResult.Applied));
            test.Clock.Now = RecoverableJournalTestData.At(1100);
            using CasGate gate = test.Store.Gate(4, afterCommit: true);
            Task<(RecoverableExecutionDurableRecord Record, RecoverableExecutionTime Now)> observation =
                test.Start(Complete(entry.ObserveAsync(default)));
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.At(1240);
            gate.Release();
            (RecoverableExecutionDurableRecord record, RecoverableExecutionTime now) =
                await observation.WaitAsync(s_watchdog).ConfigureAwait(false);

            Assert.That(record.State, Is.EqualTo(ServiceOperationState.Abandoned));
            Assert.That(record.DispatchStarted, Is.False);
            Assert.That(record.WorkFinished, Is.True);
            Assert.That(record.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(1040)));
            Assert.That(record.Snapshot(now).HistoryRemaining, Is.Zero);
            Assert.That(record.Snapshot(now).ResultRetentionTime, Is.EqualTo(200));
            Assert.That(now, Is.EqualTo(RecoverableJournalTestData.At(1240)));
            Assert.That(entry.Forgotten, Is.True);
            Assert.That(test.Store.Commits, Is.EqualTo(5));
            Assert.That(test.Store.Deletes, Is.EqualTo(1));
        }

        [Test]
        public async Task HistoryDeleteReplyLossPoisonsLiveEntryAndReopenObservesAbsence()
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.At(1040);
            await test.SeedAsync(RecoverableJournalTestData.Released(ServiceOperationState.Acknowledged))
                .ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = (await test.LoadAsync().ConfigureAwait(false))[0];
            test.Clock.Now = RecoverableJournalTestData.At(1090);
            test.Store.FailAt = 1;
            test.Store.FailAfterCommit = true;
            using CasGate gate = test.Store.Gate(1, afterCommit: true);
            Task<(RecoverableExecutionDurableRecord Record, RecoverableExecutionTime Now)> observation =
                test.Start(Complete(entry.ObserveAsync(default)));
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            gate.Release();
            await UnavailableAsync(() => observation).ConfigureAwait(false);

            Assert.That(entry.Forgotten, Is.False, "The live entry cannot confirm a deletion whose reply was lost.");
            Assert.That(test.Store.Deletes, Is.EqualTo(1));
            Assert.That((await Complete(test.Raw.TryGetAsync(JournalScope.Key)).ConfigureAwait(false)).Found, Is.False);
            await AssertPoisonedAsync(test.Journal, entry).ConfigureAwait(false);
            test.Store.FailAt = 0;
            RecoverableExecutionJournal reopened = test.NewJournal();
            Assert.That((await test.LoadAsync(reopened).ConfigureAwait(false)).Count, Is.Zero);
            Assert.That(reopened.IsReady, Is.True);
        }

        [Test]
        public async Task DifferentEntriesDoNotShareTransitionSerializer()
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry first = await test.AdmitAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry second = await test.AdmitAsync(
                RecoverableJournalTestData.Admission() with { ClientId = RecoverableJournalTestData.OtherId })
                .ConfigureAwait(false);
            using CasGate gate = test.Store.Gate(5, afterCommit: false);
            Task<ServiceOperationControlResult> blocked =
                test.Start(Complete(first.ControlAsync(AsyncServiceAccess.Cancel, default)));
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            try
            {
                Assert.That(await Complete(second.ControlAsync(AsyncServiceAccess.Cancel, default))
                    .ConfigureAwait(false), Is.EqualTo(ServiceOperationControlResult.Applied));
                Assert.That(blocked.IsCompleted, Is.False);
                Assert.That(first.Current.CancellationRequested, Is.False);
                Assert.That(second.Current.CancellationRequested, Is.True);
                Assert.That(second.Current.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
                Assert.That(test.Store.Commits, Is.EqualTo(5));
            }
            finally
            {
                gate.Release();
            }
            Assert.That(await blocked.WaitAsync(s_watchdog).ConfigureAwait(false),
                Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(first.Current.CancellationRequested, Is.True);
            Assert.That(test.Store.Commits, Is.EqualTo(6));
        }

        [Test]
        public async Task FileStoreCompositionReopensOriginalOutcomeWithoutRenewingRetention()
        {
            await using var test = new JournalScope();
            string parent = Path.Combine(Path.GetTempPath(), "rj-" + Guid.NewGuid().ToString("N"));
            string directory = Path.Combine(parent, "store");
            Directory.CreateDirectory(parent);
            try
            {
                ByteString protectedRecord;
                await using (var first = new FileSharedKeyValueStore(
                    directory, "synthetic-journal-composition", test.Protector, 1_048_576))
                {
                    RecoverableExecutionJournal journal = test.NewJournal(first);
                    Assert.That((await test.LoadAsync(journal).ConfigureAwait(false)).Count, Is.Zero);
                    RecoverableExecutionJournal.Entry entry =
                        await test.AdmitAsync(journal: journal).ConfigureAwait(false);
                    Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
                    test.Clock.Now = RecoverableJournalTestData.At(1020);
                    await Complete(entry.CommitOutcomeAsync(
                        ServiceOperationOutcome.ServiceResponse, StatusCodes.BadUserAccessDenied,
                        RecoverableJournalTestData.Response, RecoverableJournalTestData.Available().Permissions))
                        .ConfigureAwait(false);
                    (bool found, ByteString bytes) =
                        await Complete(first.TryGetAsync(JournalScope.Key)).ConfigureAwait(false);
                    Assert.That(found, Is.True);
                    protectedRecord = bytes;
                    Assert.That(entry.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
                    journal.Dispose();
                }
                test.Clock.Now = RecoverableJournalTestData.At(1030);
                await using var reopenedStore = new FileSharedKeyValueStore(
                    directory, "synthetic-journal-composition", test.Protector, 1_048_576);
                RecoverableExecutionJournal reopenedJournal = test.NewJournal(reopenedStore);
                ArrayOf<RecoverableExecutionJournal.Entry> entries =
                    await test.LoadAsync(reopenedJournal).ConfigureAwait(false);
                Assert.That(entries.Count, Is.EqualTo(1));
                (RecoverableExecutionDurableRecord record, RecoverableExecutionTime now) =
                    await Complete(entries[0].ObserveAsync(default)).ConfigureAwait(false);

                Assert.That(record.Kind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
                Assert.That(record.Status, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(record.Response, Is.EqualTo(ByteString.From(0x52, 0x50, 0x7f, 0x80)));
                Assert.That(record.Request, Is.EqualTo(ByteString.From(0x52, 0x51, 0x00, 0xff)));
                Assert.That(record.Permissions.Span.ToArray(),
                    Is.EqualTo(RecoverableJournalTestData.Available().Permissions.Span.ToArray()));
                Assert.That(record.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
                Assert.That(record.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
                Assert.That(record.Snapshot(now).ResultRemaining, Is.EqualTo(40));
                Assert.That(record.Revision, Is.EqualTo(5));
                Assert.That(record.DispatchStarted, Is.True);
                Assert.That(record.WorkFinished, Is.True);
                Assert.That(await Complete(entries[0].BeginDispatchAsync()).ConfigureAwait(false), Is.False);
                (bool stillFound, ByteString sameBytes) =
                    await Complete(reopenedStore.TryGetAsync(JournalScope.Key)).ConfigureAwait(false);
                Assert.That(stillFound, Is.True);
                Assert.That(sameBytes, Is.EqualTo(protectedRecord));
                Assert.That(reopenedJournal.IsReady, Is.True);
            }
            finally
            {
                if (Directory.Exists(parent))
                {
                    Directory.Delete(parent, recursive: true);
                }
            }
        }

        [TestCase(-1L)]
        [TestCase(0L)]
        [TestCase(1L)]
        public async Task UnstartedPendingOperationAtDeadlineNeverCommitsDispatchMarker(long deltaTicks)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.AtTicks(11_000_000 + deltaTicks);

            bool permission = await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false);

            bool beforeDeadline = deltaTicks < 0;
            Assert.That(permission, Is.EqualTo(beforeDeadline));
            Assert.That(entry.Current.DispatchStarted, Is.EqualTo(beforeDeadline));
            Assert.That(entry.Current.WorkFinished, Is.EqualTo(!beforeDeadline));
            Assert.That(entry.Current.State, Is.EqualTo(beforeDeadline
                ? ServiceOperationState.Pending : ServiceOperationState.OutcomeAvailable));
            Assert.That(entry.Current.Kind, Is.EqualTo(beforeDeadline
                ? ServiceOperationOutcome.None : ServiceOperationOutcome.ExecutionDeadlineExceeded));
            Assert.That(entry.Current.Status, Is.EqualTo(beforeDeadline ? StatusCodes.Good : StatusCodes.BadTimeout));
            Assert.That(entry.Current.OutcomeAt, Is.EqualTo(beforeDeadline ? default : test.Clock.Now));
            Assert.That(entry.Current.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(entry.Current.CancellationRequested, Is.False);
            Assert.That(entry.Current.Response.IsNull, Is.True);
            Assert.That(test.Store.Commits, Is.EqualTo(beforeDeadline ? 3 : 4));
            Assert.That((await test.StoredAsync().ConfigureAwait(false)).DispatchStarted, Is.EqualTo(beforeDeadline));
        }

        [Test]
        public async Task CompletedCancelledOperationCanRetainOriginalResponseBeforeExecutionDeadline()
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.At(1005);
            Assert.That(await Complete(entry.ControlAsync(AsyncServiceAccess.Cancel, default)).ConfigureAwait(false),
                Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
            test.Clock.Now = RecoverableJournalTestData.At(1020);

            await Complete(entry.CommitOutcomeAsync(
                ServiceOperationOutcome.ServiceResponse, StatusCodes.Good,
                RecoverableJournalTestData.Response, RecoverableJournalTestData.Available().Permissions))
                .ConfigureAwait(false);

            Assert.That(entry.Current.Kind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(entry.Current.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(entry.Current.Response, Is.EqualTo(ByteString.From(0x52, 0x50, 0x7f, 0x80)));
            Assert.That(entry.Current.Permissions.Span.ToArray(),
                Is.EqualTo(RecoverableJournalTestData.Available().Permissions.Span.ToArray()));
            Assert.That(entry.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            Assert.That(entry.Current.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(entry.Current.CancellationRequested, Is.True);
            Assert.That(entry.Current.WorkFinished, Is.True);
            Assert.That(await Complete(entry.ControlAsync(AsyncServiceAccess.Cancel, default)).ConfigureAwait(false),
                Is.EqualTo(ServiceOperationControlResult.NotApplicable));
            Assert.That(entry.Current.Revision, Is.EqualTo(6));
            Assert.That(test.Store.Commits, Is.EqualTo(6));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancelledObservationOrControlDoesNotMutateOrPoisonJournal(bool control)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            ByteString before = await test.StoredBytesAsync().ConfigureAwait(false);
            int reads = test.Clock.Reads;
            int ownerChecks = test.OwnerChecks;
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            Func<Task> operation = control
                ? () => Complete(entry.ControlAsync(AsyncServiceAccess.Cancel, cancellation.Token))
                : () => Complete(entry.ObserveAsync(cancellation.Token));

            OperationCanceledException error = (await Assert.CatchAsync<OperationCanceledException>(async () =>
                await operation().ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(before));
            Assert.That(entry.Current.CancellationRequested, Is.False);
            Assert.That(entry.Current.Revision, Is.EqualTo(2));
            Assert.That(test.Store.Commits, Is.EqualTo(2));
            Assert.That(test.Clock.Reads, Is.EqualTo(reads));
            Assert.That(test.OwnerChecks, Is.EqualTo(ownerChecks));
            Assert.That(test.Journal.IsReady, Is.True);
            Assert.That((await Complete(entry.ObserveAsync(default)).ConfigureAwait(false)).Record.State,
                Is.EqualTo(ServiceOperationState.Pending));
        }

        [Test]
        public async Task CancelledLoadCannotPublishReadinessOrFabricateAnEmptySuccessfulLoad()
        {
            await using var test = new JournalScope();
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);

            OperationCanceledException error = (await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await Complete(test.Journal.LoadAsync(4, cancellation.Token)).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(test.Journal.IsReady, Is.False);
            Assert.That(test.Store.HealthChecks, Is.Zero);
            Assert.That(test.Store.ScannedRecords, Is.Zero);
            Assert.That(test.Store.Commits, Is.Zero);
            Assert.That(test.Clock.Reads, Is.Zero);
            await AssertPoisonedAsync(test.Journal).ConfigureAwait(false);
            RecoverableExecutionJournal reopened = test.NewJournal();
            Assert.That((await test.LoadAsync(reopened).ConfigureAwait(false)).Count, Is.Zero);
            Assert.That(reopened.IsReady, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LostClockProofAfterCandidateIsIndeterminateOnQualifiedReopen(bool defaultCoordinate)
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = await test.AdmitAsync().ConfigureAwait(false);
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
            test.Clock.Now = RecoverableJournalTestData.At(1020);
            using CasGate gate = test.Store.Gate(4, afterCommit: true);
            Task completion = test.Start(Complete(entry.CommitOutcomeAsync(
                ServiceOperationOutcome.ServiceResponse, StatusCodes.Good, RecoverableJournalTestData.Response,
                default)));
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            test.Clock.Available = defaultCoordinate;
            if (defaultCoordinate)
            {
                test.Clock.Now = default;
            }
            gate.Release();
            await UnavailableAsync(() => completion).ConfigureAwait(false);
            await AssertPoisonedAsync(test.Journal, entry).ConfigureAwait(false);
            RecoverableExecutionDurableRecord candidate = await test.StoredAsync().ConfigureAwait(false);
            Assert.That(candidate.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.OutcomePreparing));
            Assert.That(candidate.Kind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(candidate.Response, Is.EqualTo(RecoverableJournalTestData.Response));
            Assert.That(candidate.OutcomeAt, Is.Default);
            Assert.That(test.Store.Commits, Is.EqualTo(4));
            test.Clock.Available = true;
            test.Clock.Now = RecoverableJournalTestData.At(1030);

            RecoverableExecutionJournal.Entry reopened =
                (await test.LoadAsync(test.NewJournal()).ConfigureAwait(false))[0];

            AssertIndeterminate(reopened.Current, RecoverableJournalTestData.At(1030), workFinished: true);
            Assert.That(reopened.Current.Response.IsNull, Is.True);
            Assert.That(test.Store.Commits, Is.EqualTo(6));
            Assert.That(await Complete(reopened.BeginDispatchAsync()).ConfigureAwait(false), Is.False);
        }

        [Test]
        public async Task RestoreCallbackSeesReconciledEntriesBeforeReadinessAndNextScan()
        {
            await using var test = new JournalScope();
            await test.SeedAsync(RecoverableJournalTestData.Pending()).ConfigureAwait(false);
            await test.SeedAsync(RecoverableJournalTestData.Pending() with
            {
                ClientId = RecoverableJournalTestData.OtherId,
                DispatchStarted = true,
                Revision = 3
            }).ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.At(1030);
            using var cancellation = new CancellationTokenSource();
            var restored = new List<RecoverableExecutionJournal.Entry>();
            var captured = new List<RecoverableExecutionDurableRecord>();
            var tokens = new List<CancellationToken>();
            var readiness = new List<bool>();
            TaskCompletionSource<bool> entered = Signal();
            TaskCompletionSource<bool> release = Signal();
            async ValueTask RestoreAsync(RecoverableExecutionJournal.Entry entry, CancellationToken ct)
            {
                restored.Add(entry);
                captured.Add(entry.Current);
                tokens.Add(ct);
                readiness.Add(test.Journal.IsReady);
                if (restored.Count == 1)
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(s_watchdog, ct).ConfigureAwait(false);
                }
            }
            Task<ArrayOf<RecoverableExecutionJournal.Entry>> loading =
                test.Start(test.LoadWithRestoreAsync(RestoreAsync, cancellationToken: cancellation.Token));
            await entered.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            try
            {
                Assert.That(loading.IsCompleted, Is.False);
                Assert.That(test.Journal.IsReady, Is.False);
                Assert.That(restored, Has.Count.EqualTo(1));
                Assert.That(test.Store.ScannedRecords, Is.EqualTo(1),
                    "The next record must not be scanned before this reservation callback returns.");
                Assert.That(test.Store.Commits, Is.EqualTo(2));
                AssertIndeterminate(captured[0], RecoverableJournalTestData.At(1030),
                    workFinished: !captured[0].DispatchStarted);
                await UnavailableAsync(() => Complete(restored[0].BeginDispatchAsync())).ConfigureAwait(false);
                test.Clock.Now = RecoverableJournalTestData.At(1040);
            }
            finally
            {
                release.TrySetResult(true);
            }
            ArrayOf<RecoverableExecutionJournal.Entry> loaded =
                await loading.WaitAsync(s_watchdog).ConfigureAwait(false);
            RecoverableExecutionJournal.Entry[] loadedEntries = loaded.Span.ToArray();

            Assert.That(loaded.Count, Is.EqualTo(2));
            Assert.That(restored, Has.Count.EqualTo(2));
            Assert.That(loadedEntries.Select(value => value.Current.ClientId), Is.EquivalentTo(
            [
                RecoverableJournalTestData.ClientId, RecoverableJournalTestData.OtherId
            ]));
            Assert.That(tokens, Is.EqualTo([cancellation.Token, cancellation.Token]));
            Assert.That(readiness, Is.EqualTo(s_restoreReadiness));
            Assert.That(test.Journal.IsReady, Is.True);
            Assert.That(test.Store.ScannedRecords, Is.EqualTo(2));
            Assert.That(test.Store.Commits, Is.EqualTo(4));
            Assert.That(loaded[0], Is.SameAs(restored[0]));
            Assert.That(loaded[1], Is.SameAs(restored[1]));
            Assert.That(loaded[0].Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1030)));
            Assert.That(loaded[0].Current.Snapshot(test.Clock.Now).ResultRemaining, Is.EqualTo(40));
            Assert.That(loaded[1].Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1040)));
            Assert.That(loaded[1].Current.Snapshot(test.Clock.Now).ResultRemaining, Is.EqualTo(50));
            foreach (RecoverableExecutionJournal.Entry entry in loadedEntries)
            {
                Assert.That(entry.Current.Kind, Is.EqualTo(ServiceOperationOutcome.ExecutionIndeterminate));
                Assert.That(entry.Current.Status, Is.EqualTo(StatusCodes.BadUnexpectedError));
                Assert.That(entry.Current.Response.IsNull, Is.True);
                Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.False);
            }
            Assert.That(test.Store.Commits, Is.EqualTo(4));
        }

        [TestCase(1, false)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(2, true)]
        public async Task ReservationRestoreFailureWithdrawsReadinessAndNeverAuthorizesDispatch(
            int failureAt, bool ioFailure)
        {
            await using var test = new JournalScope();
            Uuid[] ids =
            [
                RecoverableJournalTestData.ClientId,
                RecoverableJournalTestData.OtherId,
                new Uuid("78000000-0000-0000-0000-000000000008")
            ];
            foreach (Uuid id in ids)
            {
                await test.SeedAsync(RecoverableJournalTestData.Pending() with { ClientId = id })
                    .ConfigureAwait(false);
            }
            test.Clock.Now = RecoverableJournalTestData.At(2000);
            var restoredRecords = new List<RecoverableExecutionDurableRecord>();
            var dispatchFailures = new List<StatusCode>();
            var permissionsToStart = new List<bool>();
            int reservationsRestored = 0;
            async ValueTask RestoreAsync(RecoverableExecutionJournal.Entry entry, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                restoredRecords.Add(entry.Current);
                // Capture observations here; assert outside the callback so Load's wrapping cannot hide a test failure.
                try
                {
                    permissionsToStart.Add(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false));
                }
                catch (ServiceResultException exception)
                {
                    dispatchFailures.Add(exception.StatusCode);
                }
                if (restoredRecords.Count == failureAt)
                {
                    if (ioFailure)
                    {
                        throw new IOException("Synthetic reservation provider unavailable.");
                    }
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations);
                }
                reservationsRestored++;
            }

            await UnavailableAsync(() => test.LoadWithRestoreAsync(RestoreAsync, maximumRecords: 3))
                .ConfigureAwait(false);

            Assert.That(restoredRecords, Has.Count.EqualTo(failureAt));
            Assert.That(reservationsRestored, Is.EqualTo(failureAt - 1));
            Assert.That(permissionsToStart, Is.Empty);
            Assert.That(dispatchFailures,
                Is.EqualTo(Enumerable.Repeat(StatusCodes.BadResourceUnavailable, failureAt).ToArray()));
            Assert.That(test.Store.ScannedRecords, Is.EqualTo(failureAt));
            Assert.That(test.OwnerChecks, Is.EqualTo(failureAt));
            Assert.That(test.Store.Commits, Is.EqualTo(2 * failureAt));
            await AssertPoisonedAsync(test.Journal).ConfigureAwait(false);
            foreach (RecoverableExecutionDurableRecord restored in restoredRecords)
            {
                AssertIndeterminate(restored, RecoverableJournalTestData.At(2000), workFinished: true);
                Assert.That(restored.DispatchStarted, Is.False);
                RecoverableExecutionDurableRecord persisted =
                    await test.StoredAsync(JournalScope.KeyFor(restored)).ConfigureAwait(false);
                Assert.That(persisted.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(2000)));
                Assert.That(persisted.DispatchStarted, Is.False);
            }
            test.Clock.Now = RecoverableJournalTestData.At(2010);
            int retryReservations = 0;
            ValueTask RetryRestore(RecoverableExecutionJournal.Entry entry, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                retryReservations++;
                return default;
            }
            RecoverableExecutionJournal reopened = test.NewJournal();
            ArrayOf<RecoverableExecutionJournal.Entry> recovered =
                await test.LoadWithRestoreAsync(RetryRestore, reopened, 3).ConfigureAwait(false);

            Assert.That(recovered.Count, Is.EqualTo(3));
            Assert.That(retryReservations, Is.EqualTo(3));
            Assert.That(reopened.IsReady, Is.True);
            Assert.That(test.Store.Commits, Is.EqualTo(6));
            foreach (RecoverableExecutionJournal.Entry entry in recovered.Span.ToArray())
            {
                bool previouslyRestored = restoredRecords.Any(value => value.ClientId == entry.Current.ClientId);
                AssertIndeterminate(entry.Current,
                    RecoverableJournalTestData.At(previouslyRestored ? 2000 : 2010), workFinished: true);
                Assert.That(entry.Current.DispatchStarted, Is.False);
                Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.False);
            }
        }

        [Test]
        public async Task CancelledRestoreCallbackPoisonsPartialLoadWithoutDispatch()
        {
            await using var test = new JournalScope();
            await test.SeedAsync(RecoverableJournalTestData.Pending()).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            RecoverableExecutionDurableRecord? captured = null;
            CancellationToken received = default;
            int callbacks = 0;
            async ValueTask Restore(RecoverableExecutionJournal.Entry entry, CancellationToken ct)
            {
                callbacks++;
                captured = entry.Current;
                received = ct;
                await cancellation.CancelAsync().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            }

            OperationCanceledException error = (await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await test.LoadWithRestoreAsync(Restore, cancellationToken: cancellation.Token).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(received, Is.EqualTo(cancellation.Token));
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(test.Store.ScannedRecords, Is.EqualTo(1));
            Assert.That(test.Store.Commits, Is.EqualTo(2));
            AssertIndeterminate(captured!, RecoverableJournalTestData.At(1000), workFinished: true);
            Assert.That(captured!.DispatchStarted, Is.False);
            await AssertPoisonedAsync(test.Journal).ConfigureAwait(false);
            ByteString committed = await test.StoredBytesAsync().ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.At(1010);
            RecoverableExecutionJournal.Entry restored =
                (await test.LoadAsync(test.NewJournal()).ConfigureAwait(false))[0];
            Assert.That(restored.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(restored.Current.Snapshot(test.Clock.Now).ResultRemaining, Is.EqualTo(40));
            Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(committed));
            Assert.That(await Complete(restored.BeginDispatchAsync()).ConfigureAwait(false), Is.False);
            Assert.That(test.Store.Commits, Is.EqualTo(2));
        }

        [Test]
        public async Task EmptyLoadDoesNotInvokeRestoreCallback()
        {
            await using var test = new JournalScope();
            int callbacks = 0;
            ValueTask Restore(RecoverableExecutionJournal.Entry entry, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                callbacks++;
                return default;
            }

            ArrayOf<RecoverableExecutionJournal.Entry> records =
                await test.LoadWithRestoreAsync(Restore).ConfigureAwait(false);

            Assert.That(records.Count, Is.Zero);
            Assert.That(callbacks, Is.Zero);
            Assert.That(test.Store.ScannedRecords, Is.Zero);
            Assert.That(test.Store.Commits, Is.Zero);
            Assert.That(test.Journal.IsReady, Is.True);
        }

        [Test]
        public async Task RestoreCallbackCanConfirmTerminationBeforeReadiness()
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.At(1030);
            await test.SeedAsync(SelectedOrphan("available")).ConfigureAwait(false);
            bool? readyDuringRestore = null;
            bool? finishedBeforeProof = null;
            int callbacks = 0;
            async ValueTask RestoreAsync(RecoverableExecutionJournal.Entry entry, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                callbacks++;
                readyDuringRestore = test.Journal.IsReady;
                finishedBeforeProof = entry.Current.WorkFinished;
                await Complete(entry.ConfirmTerminationAsync()).ConfigureAwait(false);
            }

            RecoverableExecutionJournal.Entry entry =
                (await test.LoadWithRestoreAsync(RestoreAsync).ConfigureAwait(false))[0];

            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(readyDuringRestore, Is.False);
            Assert.That(finishedBeforeProof, Is.False);
            Assert.That(test.Journal.IsReady, Is.True);
            Assert.That(entry.Current.WorkFinished, Is.True);
            Assert.That(entry.Current.DispatchStarted, Is.True);
            Assert.That(entry.Current.CancellationRequested, Is.False);
            Assert.That(entry.Current.Kind, Is.EqualTo(ServiceOperationOutcome.ExecutionIndeterminate));
            Assert.That(entry.Current.Status, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(entry.Current.Response.IsNull, Is.True);
            Assert.That(entry.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            Assert.That(entry.Current.Snapshot(test.Clock.Now).ResultRemaining, Is.EqualTo(40));
            Assert.That(test.Store.Commits, Is.EqualTo(1));
            Assert.That((await test.StoredAsync().ConfigureAwait(false)).WorkFinished, Is.True);
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.False);
        }

        [TestCase("available")]
        [TestCase("abandoned")]
        [TestCase("expired")]
        public async Task ConfirmTerminationPreservesSelectedOutcomeAndDeadlines(string phase)
        {
            await using var test = new JournalScope();
            long confirmationTime = phase switch
            {
                "available" => 1040,
                "abandoned" => 1050,
                _ => 1080
            };
            test.Clock.Now = RecoverableJournalTestData.At(confirmationTime);
            await test.SeedAsync(SelectedOrphan(phase) with { CancellationRequested = true }).ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = (await test.LoadAsync().ConfigureAwait(false))[0];
            RecoverableExecutionDurableRecord before = entry.Current;
            Assert.That(before.WorkFinished, Is.False);
            Assert.That(before.DispatchStarted, Is.True);
            Assert.That(test.Store.Commits, Is.Zero);

            await Complete(entry.ConfirmTerminationAsync()).ConfigureAwait(false);
            test.Clock.Now = RecoverableJournalTestData.At(confirmationTime + 5);
            await Complete(entry.ConfirmTerminationAsync()).ConfigureAwait(false);

            Assert.That(entry.Current.WorkFinished, Is.True);
            Assert.That(entry.Current.DispatchStarted, Is.True);
            Assert.That(entry.Current.CancellationRequested, Is.True);
            Assert.That(entry.Current.State, Is.EqualTo(before.State));
            Assert.That(entry.Current.Kind, Is.EqualTo(ServiceOperationOutcome.ExecutionIndeterminate));
            Assert.That(entry.Current.Status, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(entry.Current.Request, Is.EqualTo(before.Request));
            Assert.That(entry.Current.Response, Is.EqualTo(before.Response));
            Assert.That(entry.Current.Permissions.Span.ToArray(), Is.EqualTo(before.Permissions.Span.ToArray()));
            Assert.That(entry.Current.Token, Is.EqualTo(before.Token));
            Assert.That(entry.Current.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(entry.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            Assert.That(entry.Current.ReleasedAt, Is.EqualTo(before.ReleasedAt));
            Assert.That(entry.Current.Execution, Is.EqualTo(TimeSpan.FromMilliseconds(100)));
            Assert.That(entry.Current.Retention, Is.EqualTo(TimeSpan.FromMilliseconds(50)));
            Assert.That(entry.Current.Snapshot(test.Clock.Now).ResultRemaining,
                Is.EqualTo(phase == "available" ? 25 : 0));
            Assert.That(entry.Current.Snapshot(test.Clock.Now).HistoryRemaining,
                Is.EqualTo(phase == "available" ? 0 : 35));
            Assert.That(entry.Current.Revision, Is.EqualTo(before.Revision + 1));
            Assert.That(test.Store.Commits, Is.EqualTo(1));
            Assert.That(test.Store.Deletes, Is.Zero);
            Assert.That(entry.Forgotten, Is.False);
            Assert.That((await test.StoredAsync().ConfigureAwait(false)).WorkFinished, Is.True);
            Assert.That(await Complete(entry.ControlAsync(AsyncServiceAccess.Cancel, default)).ConfigureAwait(false),
                Is.EqualTo(ServiceOperationControlResult.NotApplicable));
            Assert.That(await Complete(entry.BeginDispatchAsync()).ConfigureAwait(false), Is.False);
        }

        [Test]
        public async Task AlreadyFinishedConfirmationDoesNotRewriteOriginalResponse()
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.At(1030);
            await test.SeedAsync(RecoverableJournalTestData.Available()).ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = (await test.LoadAsync().ConfigureAwait(false))[0];
            ByteString before = await test.StoredBytesAsync().ConfigureAwait(false);

            await Complete(entry.ConfirmTerminationAsync()).ConfigureAwait(false);
            await Complete(entry.ConfirmTerminationAsync()).ConfigureAwait(false);

            Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(before));
            Assert.That(entry.Current.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(entry.Current.Kind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(entry.Current.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(entry.Current.Response, Is.EqualTo(ByteString.From(0x52, 0x50, 0x7f, 0x80)));
            Assert.That(entry.Current.Request, Is.EqualTo(ByteString.From(0x52, 0x51, 0x00, 0xff)));
            Assert.That(entry.Current.Permissions.Span.ToArray(),
                Is.EqualTo(RecoverableJournalTestData.Available().Permissions.Span.ToArray()));
            Assert.That(entry.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            Assert.That(entry.Current.Snapshot(test.Clock.Now).ResultRemaining, Is.EqualTo(40));
            Assert.That(entry.Current.WorkFinished, Is.True);
            Assert.That(entry.Current.CancellationRequested, Is.False);
            Assert.That(entry.Current.Revision, Is.EqualTo(5));
            Assert.That(test.Store.Commits, Is.Zero);
        }

        [TestCase("abandoned")]
        [TestCase("expired")]
        public async Task ConfirmTerminationDeletesExpiredOrphanHistoryOnlyAfterProof(string phase)
        {
            await using var test = new JournalScope();
            RecoverableExecutionDurableRecord seed = SelectedOrphan(phase);
            test.Clock.Now = seed.ReleasedAt;
            await test.SeedAsync(seed).ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = (await test.LoadAsync().ConfigureAwait(false))[0];
            long historyDeadline = phase == "expired" ? 11_200_000 : 10_900_000;
            test.Clock.Now = RecoverableJournalTestData.AtTicks(historyDeadline - 1);
            _ = await Complete(entry.ObserveAsync(default)).ConfigureAwait(false);
            Assert.That(entry.Forgotten, Is.False);
            Assert.That(entry.Current.WorkFinished, Is.False);
            test.Clock.Now = RecoverableJournalTestData.AtTicks(historyDeadline);
            _ = await Complete(entry.ObserveAsync(default)).ConfigureAwait(false);
            Assert.That(entry.Forgotten, Is.False);
            Assert.That(entry.Current.WorkFinished, Is.False);
            Assert.That((await Complete(test.Raw.TryGetAsync(JournalScope.Key)).ConfigureAwait(false)).Found, Is.True);
            Assert.That(await Complete(entry.ControlAsync(AsyncServiceAccess.Abandon, default)).ConfigureAwait(false),
                Is.EqualTo(ServiceOperationControlResult.UnknownOperation));
            Assert.That(test.Store.Deletes, Is.Zero,
                "Elapsed public history does not establish the proof needed to release an orphan's identity charge.");
            int commits = test.Store.Commits;
            bool cancellationRequested = entry.Current.CancellationRequested;
            using CasGate gate = test.Store.Gate(test.Store.CasAttempts + 1, afterCommit: false);
            Task proof = test.Start(Complete(entry.ConfirmTerminationAsync()));
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            Task retry = test.Start(Complete(entry.ConfirmTerminationAsync()));
            try
            {
                Assert.That(proof.IsCompleted, Is.False);
                Assert.That(retry.IsCompleted, Is.False);
                Assert.That((await test.StoredAsync().ConfigureAwait(false)).WorkFinished, Is.False);
                Assert.That(entry.Forgotten, Is.False);
                Assert.That(test.Store.Commits, Is.EqualTo(commits));
                Assert.That(test.Store.Deletes, Is.Zero);
            }
            finally
            {
                gate.Release();
            }
            await Task.WhenAll(proof, retry).WaitAsync(s_watchdog).ConfigureAwait(false);

            Assert.That(entry.Current.WorkFinished, Is.True);
            Assert.That(entry.Current.CancellationRequested, Is.EqualTo(cancellationRequested));
            Assert.That(entry.Current.State, Is.EqualTo(seed.State));
            Assert.That(entry.Current.Kind, Is.EqualTo(ServiceOperationOutcome.ExecutionIndeterminate));
            Assert.That(entry.Current.Status, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(entry.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            Assert.That(entry.Current.ReleasedAt, Is.EqualTo(seed.ReleasedAt));
            Assert.That(entry.Current.Response.IsNull, Is.True);
            Assert.That(entry.Forgotten, Is.True);
            Assert.That(test.Store.Commits, Is.EqualTo(commits + 2));
            Assert.That(test.Store.Deletes, Is.EqualTo(1));
            Assert.That((await Complete(test.Raw.TryGetAsync(JournalScope.Key)).ConfigureAwait(false)).Found, Is.False);
            await Complete(entry.ConfirmTerminationAsync()).ConfigureAwait(false);
            Assert.That(test.Store.Commits, Is.EqualTo(commits + 2));
            Assert.That(test.Store.Deletes, Is.EqualTo(1));
            Assert.That((await test.LoadAsync(test.NewJournal()).ConfigureAwait(false)).Count, Is.Zero);
        }

        [TestCase(1070)]
        [TestCase(1120)]
        public async Task ConfirmTerminationAdvancesElapsedResultAndHistoryWithoutRenewal(int nowMilliseconds)
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.At(1020);
            await test.SeedAsync(SelectedOrphan("available")).ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = (await test.LoadAsync().ConfigureAwait(false))[0];
            Assert.That(entry.Current.WorkFinished, Is.False);
            Assert.That(entry.Current.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            test.Clock.Now = RecoverableJournalTestData.At(nowMilliseconds);

            await Complete(entry.ConfirmTerminationAsync()).ConfigureAwait(false);

            bool historyExpired = nowMilliseconds == 1120;
            Assert.That(entry.Current.WorkFinished, Is.True);
            Assert.That(entry.Current.State, Is.EqualTo(ServiceOperationState.Expired));
            Assert.That(entry.Current.Kind, Is.EqualTo(ServiceOperationOutcome.ExecutionIndeterminate));
            Assert.That(entry.Current.Status, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(entry.Current.CancellationRequested, Is.False);
            Assert.That(entry.Current.Request.IsNull, Is.True);
            Assert.That(entry.Current.Response.IsNull, Is.True);
            Assert.That(entry.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            Assert.That(entry.Current.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(1070)));
            Assert.That(entry.Current.Snapshot(test.Clock.Now).HistoryRemaining,
                Is.EqualTo(historyExpired ? 0 : 50));
            Assert.That(entry.Forgotten, Is.EqualTo(historyExpired));
            Assert.That(test.Store.Commits, Is.EqualTo(historyExpired ? 3 : 2));
            Assert.That(test.Store.Deletes, Is.EqualTo(historyExpired ? 1 : 0));
            Assert.That((await Complete(test.Raw.TryGetAsync(JournalScope.Key)).ConfigureAwait(false)).Found,
                Is.EqualTo(!historyExpired));
            await Complete(entry.ConfirmTerminationAsync()).ConfigureAwait(false);
            Assert.That(test.Store.Commits, Is.EqualTo(historyExpired ? 3 : 2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TerminationProofWriteFailurePoisonsLiveJournalAndReopenUsesCommittedProof(bool afterCommit)
        {
            await using var test = new JournalScope();
            test.Clock.Now = RecoverableJournalTestData.At(1040);
            await test.SeedAsync(SelectedOrphan("abandoned")).ConfigureAwait(false);
            RecoverableExecutionJournal.Entry entry = (await test.LoadAsync().ConfigureAwait(false))[0];
            test.Store.FailAt = 1;
            test.Store.FailAfterCommit = afterCommit;
            using CasGate gate = test.Store.Gate(1, afterCommit);
            Task proof = test.Start(Complete(entry.ConfirmTerminationAsync()));
            await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            gate.Release();
            await UnavailableAsync(() => proof).ConfigureAwait(false);

            await AssertPoisonedAsync(test.Journal, entry).ConfigureAwait(false);
            Assert.That(entry.Current.WorkFinished, Is.False);
            RecoverableExecutionDurableRecord persisted = await test.StoredAsync().ConfigureAwait(false);
            Assert.That(persisted.WorkFinished, Is.EqualTo(afterCommit));
            Assert.That(persisted.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(1040)));
            Assert.That(persisted.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            Assert.That(test.Store.Commits, Is.EqualTo(afterCommit ? 1 : 0));
            test.Store.FailAt = 0;
            test.Clock.Now = RecoverableJournalTestData.At(1050);
            RecoverableExecutionJournal.Entry reopened =
                (await test.LoadAsync(test.NewJournal()).ConfigureAwait(false))[0];

            Assert.That(reopened.Current.WorkFinished, Is.EqualTo(afterCommit));
            Assert.That(reopened.Current.Kind, Is.EqualTo(ServiceOperationOutcome.ExecutionIndeterminate));
            Assert.That(reopened.Current.Status, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(reopened.Current.Response.IsNull, Is.True);
            Assert.That(reopened.Current.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(1040)));
            Assert.That(reopened.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            Assert.That(reopened.Current.Snapshot(test.Clock.Now).HistoryRemaining, Is.EqualTo(40));
            test.Clock.Now = RecoverableJournalTestData.At(1090);
            _ = await Complete(reopened.ObserveAsync(default)).ConfigureAwait(false);
            Assert.That(reopened.Forgotten, Is.EqualTo(afterCommit));
            Assert.That(test.Store.Deletes, Is.EqualTo(afterCommit ? 1 : 0));
            Assert.That((await Complete(test.Raw.TryGetAsync(JournalScope.Key)).ConfigureAwait(false)).Found,
                Is.EqualTo(!afterCommit));
        }

        [Test]
        public async Task JournalReleaseShrinksSlotAndHistoryUpdatesKeepItsSize()
        {
            await using var test = new JournalScope();
            await test.LoadAsync().ConfigureAwait(false);
            RecoverableExecutionJournal.Entry admitted = await test.AdmitAsync().ConfigureAwait(false);
            Assert.That((await test.StoredBytesAsync().ConfigureAwait(false)).Length, Is.EqualTo(16392));
            Assert.That(await Complete(admitted.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
            Assert.That((await test.StoredBytesAsync().ConfigureAwait(false)).Length, Is.EqualTo(16392));
            test.Journal.Dispose();
            test.Clock.Now = RecoverableJournalTestData.At(1010);
            RecoverableExecutionJournal.Entry recovered =
                (await test.LoadAsync(test.NewJournal()).ConfigureAwait(false))[0];
            AssertIndeterminate(recovered.Current, RecoverableJournalTestData.At(1010), workFinished: false);
            Assert.That((await test.StoredBytesAsync().ConfigureAwait(false)).Length, Is.EqualTo(16392));
            test.Clock.Now = RecoverableJournalTestData.At(1020);

            Assert.That(await Complete(recovered.ControlAsync(AsyncServiceAccess.Abandon, default))
                .ConfigureAwait(false),
                Is.EqualTo(ServiceOperationControlResult.Applied));

            int historySize = RecoverableJournalTestData.Raw(
                RecoverableJournalTestData.Released(ServiceOperationState.Abandoned)).Length +
                4104;
            Assert.That((await test.StoredBytesAsync().ConfigureAwait(false)).Length,
                Is.EqualTo(historySize).And.LessThan(16392));
            test.Clock.Now = RecoverableJournalTestData.At(1025);
            Assert.That(await Complete(recovered.ControlAsync(AsyncServiceAccess.Cancel, default))
                .ConfigureAwait(false),
                Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That((await test.StoredBytesAsync().ConfigureAwait(false)).Length, Is.EqualTo(historySize));
            test.Clock.Now = RecoverableJournalTestData.At(1030);
            await Complete(recovered.ConfirmTerminationAsync()).ConfigureAwait(false);

            Assert.That((await test.StoredBytesAsync().ConfigureAwait(false)).Length, Is.EqualTo(historySize));
            RecoverableExecutionDurableRecord persisted = await test.StoredAsync().ConfigureAwait(false);
            Assert.That(persisted.State, Is.EqualTo(ServiceOperationState.Abandoned));
            Assert.That(persisted.Kind, Is.EqualTo(ServiceOperationOutcome.ExecutionIndeterminate));
            Assert.That(persisted.Status, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(persisted.Request.IsNull, Is.True);
            Assert.That(persisted.Response.IsNull, Is.True);
            Assert.That(persisted.Permissions.Count, Is.Zero);
            Assert.That(persisted.CancellationRequested, Is.True);
            Assert.That(persisted.WorkFinished, Is.True);
            Assert.That(persisted.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1010)));
            Assert.That(persisted.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            Assert.That(persisted.Snapshot(test.Clock.Now).HistoryRemaining, Is.EqualTo(40));
            Assert.That(test.Store.Commits, Is.EqualTo(8));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CapacityRejectedAdmissionLeavesJournalReadyForReleaseAndRetry(bool expiry)
        {
            await using var test = new JournalScope();
            int historyBytes = RecoverableJournalTestData.Raw(
                RecoverableJournalTestData.Released(ServiceOperationState.Acknowledged)).Length +
                4104;
            using var capacity = new TestCapacityStore(test.Store, 16392 + historyBytes);
            RecoverableExecutionJournal journal = test.NewJournal(capacity);
            await test.LoadAsync(journal).ConfigureAwait(false);
            RecoverableExecutionJournal.Entry first = await test.AdmitAsync(journal: journal).ConfigureAwait(false);
            Assert.That(await Complete(first.BeginDispatchAsync()).ConfigureAwait(false), Is.True);
            test.Clock.Now = RecoverableJournalTestData.At(1020);
            var response = new ByteString(Enumerable.Repeat((byte)0xb6, 1024).ToArray());
            await Complete(first.CommitOutcomeAsync(
                ServiceOperationOutcome.ServiceResponse, StatusCodes.Good,
                response, RecoverableJournalTestData.Available().Permissions)).ConfigureAwait(false);
            ByteString committed = await test.StoredBytesAsync().ConfigureAwait(false);
            Assert.That(committed.Length, Is.EqualTo(16392));
            Assert.That(await capacity.ValueBytesAsync().ConfigureAwait(false), Is.EqualTo(16392));
            RecoverableExecutionDurableRecord secondAdmission = RecoverableJournalTestData.Admission() with
            {
                ClientId = RecoverableJournalTestData.OtherId
            };

            await UnavailableAsync(() => test.AdmitAsync(secondAdmission, journal)).ConfigureAwait(false);

            Assert.That(journal.IsReady, Is.True,
                "Proven pre-publication capacity rejection is not ambiguous failure.");
            Assert.That(capacity.CapacityAttempts, Is.EqualTo(2));
            Assert.That(capacity.CapacityRejections, Is.EqualTo(1));
            Assert.That(test.Store.Commits, Is.EqualTo(5));
            Assert.That(test.Store.CasAttempts, Is.EqualTo(5));
            Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(committed));
            Assert.That((await Complete(test.Raw.TryGetAsync(JournalScope.KeyFor(secondAdmission)))
                .ConfigureAwait(false)).Found, Is.False);
            (RecoverableExecutionDurableRecord available, _) =
                await Complete(first.ObserveAsync(default)).ConfigureAwait(false);
            Assert.That(available.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(available.Response, Is.EqualTo(response));
            Assert.That(available.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            test.Clock.Now = RecoverableJournalTestData.At(expiry ? 1070 : 1030);
            if (expiry)
            {
                _ = await Complete(first.ObserveAsync(default)).ConfigureAwait(false);
            }
            else
            {
                Assert.That(await Complete(first.ControlAsync(AsyncServiceAccess.Acknowledge, default))
                    .ConfigureAwait(false), Is.EqualTo(ServiceOperationControlResult.Applied));
            }
            Assert.That(first.Current.State, Is.EqualTo(expiry
                ? ServiceOperationState.Expired : ServiceOperationState.Acknowledged));
            Assert.That(first.Current.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(expiry ? 1070 : 1030)));
            Assert.That(first.Current.Response.IsNull, Is.True);
            Assert.That((await test.StoredBytesAsync().ConfigureAwait(false)).Length, Is.EqualTo(historyBytes));
            Assert.That(await capacity.ValueBytesAsync().ConfigureAwait(false), Is.EqualTo(historyBytes));

            RecoverableExecutionJournal.Entry second =
                await test.AdmitAsync(secondAdmission, journal).ConfigureAwait(false);

            Assert.That(second.Current.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.Pending));
            Assert.That(second.Current.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(expiry ? 1070 : 1030)));
            Assert.That(second.Current.DispatchStarted, Is.False);
            Assert.That(second.Current.Snapshot(test.Clock.Now).ExecutionRemaining, Is.EqualTo(100));
            Assert.That(first.Current.OutcomeAt, Is.EqualTo(RecoverableJournalTestData.At(1020)));
            Assert.That(first.Current.ReleasedAt, Is.EqualTo(RecoverableJournalTestData.At(expiry ? 1070 : 1030)));
            Assert.That(await capacity.ValueBytesAsync().ConfigureAwait(false), Is.EqualTo(capacity.MaximumValueBytes));
            Assert.That(capacity.CapacityAttempts, Is.EqualTo(3));
            Assert.That(capacity.CapacityRejections, Is.EqualTo(1));
            Assert.That(test.Store.Commits, Is.EqualTo(8));
            Assert.That(journal.IsReady, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CapacityAdmissionExceptionStillPoisonsAndReopenSeesActualBytes(bool afterCommit)
        {
            await using var test = new JournalScope();
            using var capacity = new TestCapacityStore(test.Store, 16392);
            RecoverableExecutionJournal journal = test.NewJournal(capacity);
            await test.LoadAsync(journal).ConfigureAwait(false);
            test.Store.FailAt = 1;
            test.Store.FailAfterCommit = afterCommit;
            using CasGate gate = test.Store.Gate(1, afterCommit);
            // CA2025 needs this direct await to recognize the borrowed journal lifetime.
            // TODO: Remove when CA2025 recognizes the enclosing finally-await ownership.
#pragma warning disable RCS1174
            async Task<RecoverableExecutionJournal.Entry> AdmitBeforeStoreDisposalAsync() =>
                await test.AdmitAsync(journal: journal).ConfigureAwait(false);
#pragma warning restore RCS1174
            Task<RecoverableExecutionJournal.Entry> admission = AdmitBeforeStoreDisposalAsync();
            try
            {
                await gate.Reached.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
                gate.Release();
                await UnavailableAsync(async () =>
                    await admission.ConfigureAwait(false)).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
                try
                {
                    // Complete the bounded admission before disposing its borrowed store.
                    _ = await admission.ConfigureAwait(false);
                }
                catch (ServiceResultException exception) when (
                    exception.StatusCode == StatusCodes.BadResourceUnavailable)
                {
                    // The exact failure is asserted above; this also drains earlier failing paths.
                }
            }

            await AssertPoisonedAsync(journal).ConfigureAwait(false);
            Assert.That(capacity.CapacityAttempts, Is.EqualTo(1));
            Assert.That(capacity.CapacityRejections, Is.Zero);
            Assert.That(test.Store.Commits, Is.EqualTo(afterCommit ? 1 : 0));
            Assert.That((await Complete(test.Raw.TryGetAsync(JournalScope.Key)).ConfigureAwait(false)).Found,
                Is.EqualTo(afterCommit));
            if (afterCommit)
            {
                RecoverableExecutionDurableRecord committed = await test.StoredAsync().ConfigureAwait(false);
                Assert.That(committed.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.Admission));
                Assert.That(committed.AdmittedAt, Is.Default);
                Assert.That(committed.DispatchStarted, Is.False);
                Assert.That((await test.StoredBytesAsync().ConfigureAwait(false)).Length, Is.EqualTo(16392));
            }
            test.Store.FailAt = 0;
            test.Clock.Now = RecoverableJournalTestData.At(2000);
            RecoverableExecutionJournal reopened = test.NewJournal(capacity);
            ArrayOf<RecoverableExecutionJournal.Entry> records =
                await test.LoadAsync(reopened).ConfigureAwait(false);
            Assert.That(records.Count, Is.EqualTo(afterCommit ? 1 : 0));
            Assert.That(reopened.IsReady, Is.True);
            if (afterCommit)
            {
                AssertIndeterminate(records[0].Current, RecoverableJournalTestData.At(2000), workFinished: true);
                Assert.That(await Complete(records[0].BeginDispatchAsync()).ConfigureAwait(false), Is.False);
                Assert.That((await test.StoredBytesAsync().ConfigureAwait(false)).Length, Is.EqualTo(16392));
                Assert.That(test.Store.Commits, Is.EqualTo(3));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CapacityAwareDuplicateIsStillBadInvalidState(bool changedPayload)
        {
            await using var test = new JournalScope();
            using var capacity = new TestCapacityStore(test.Store, 16392);
            RecoverableExecutionJournal journal = test.NewJournal(capacity);
            await test.LoadAsync(journal).ConfigureAwait(false);
            RecoverableExecutionJournal.Entry original = await test.AdmitAsync(journal: journal).ConfigureAwait(false);
            ByteString before = await test.StoredBytesAsync().ConfigureAwait(false);
            RecoverableExecutionDurableRecord duplicate = RecoverableJournalTestData.Admission() with
            {
                Request = changedPayload ? ByteString.From(0x19, 0x28) : RecoverableJournalTestData.Admission().Request
            };
            test.Clock.Now = RecoverableJournalTestData.At(1010);

            await ErrorAsync(() => test.AdmitAsync(duplicate, journal), StatusCodes.BadInvalidState)
                .ConfigureAwait(false);

            Assert.That(await test.StoredBytesAsync().ConfigureAwait(false), Is.EqualTo(before));
            Assert.That(journal.IsReady, Is.True);
            Assert.That(original.Current.AdmittedAt, Is.EqualTo(RecoverableJournalTestData.At(1000)));
            Assert.That(original.Current.Snapshot(test.Clock.Now).ExecutionRemaining, Is.EqualTo(90));
            Assert.That(capacity.CapacityAttempts, Is.EqualTo(2));
            Assert.That(capacity.CapacityRejections, Is.Zero);
            Assert.That(test.Store.Commits, Is.EqualTo(2));
            Assert.That(await capacity.ValueBytesAsync().ConfigureAwait(false), Is.EqualTo(16392));
        }

        [Test]
        public async Task UnrecognizedCapacityDispositionAfterCommitIsNotAdmissionSuccess()
        {
            await using var test = new JournalScope();
            using var capacity = new TestCapacityStore(test.Store, 16392)
            {
                ReportAppliedAs = (SharedKeyValueStoreWriteResult)int.MaxValue
            };
            RecoverableExecutionJournal journal = test.NewJournal(capacity);
            await test.LoadAsync(journal).ConfigureAwait(false);

            await UnavailableAsync(() => test.AdmitAsync(journal: journal)).ConfigureAwait(false);

            await AssertPoisonedAsync(journal).ConfigureAwait(false);
            Assert.That(test.Store.Commits, Is.EqualTo(1));
            RecoverableExecutionDurableRecord committed = await test.StoredAsync().ConfigureAwait(false);
            Assert.That(committed.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.Admission));
            Assert.That(committed.AdmittedAt, Is.Default);
            Assert.That(committed.DispatchStarted, Is.False);
            capacity.ReportAppliedAs = null;
            RecoverableExecutionJournal.Entry restored =
                (await test.LoadAsync(test.NewJournal(capacity)).ConfigureAwait(false))[0];
            AssertIndeterminate(restored.Current, RecoverableJournalTestData.At(1000), workFinished: true);
            Assert.That(await Complete(restored.BeginDispatchAsync()).ConfigureAwait(false), Is.False);
        }

        private static RecoverableExecutionDurableRecord SelectedOrphan(string phase)
        {
            RecoverableExecutionDurableRecord record = RecoverableJournalTestData.Available() with
            {
                Kind = ServiceOperationOutcome.ExecutionIndeterminate,
                Status = StatusCodes.BadUnexpectedError,
                Response = default,
                Permissions = default,
                WorkFinished = false
            };
            return phase == "available" ? record : record with
            {
                Phase = RecoverableExecutionCommitPhase.Released,
                State = phase == "expired" ? ServiceOperationState.Expired : ServiceOperationState.Abandoned,
                Request = default,
                ReleasedAt = RecoverableJournalTestData.At(phase == "expired" ? 1070 : 1040),
                Revision = 6
            };
        }

        private static IEnumerable<TestCaseData> ControlCases()
        {
            foreach (string phase in new[]
            {
                "Pending", "Available", "Acknowledged", "Abandoned", "Expired", "Unknown"
            })
            {
                foreach (AsyncServiceAccess action in new[]
                {
                    AsyncServiceAccess.Acknowledge, AsyncServiceAccess.Cancel, AsyncServiceAccess.Abandon
                })
                {
                    ServiceOperationControlResult expected = ServiceOperationControlResult.NotApplicable;
                    if (phase == "Unknown")
                    {
                        expected = ServiceOperationControlResult.UnknownOperation;
                    }
                    else if ((action == AsyncServiceAccess.Cancel && phase == "Pending") ||
                        (action == AsyncServiceAccess.Acknowledge && phase == "Available") ||
                        (action == AsyncServiceAccess.Abandon && phase is "Pending" or "Available"))
                    {
                        expected = ServiceOperationControlResult.Applied;
                    }
                    else if ((action == AsyncServiceAccess.Acknowledge && phase == "Acknowledged") ||
                        (action == AsyncServiceAccess.Abandon && phase == "Abandoned"))
                    {
                        expected = ServiceOperationControlResult.AlreadyApplied;
                    }
                    yield return new TestCaseData(phase, action, expected);
                }
            }
        }

        private static async Task AssertPoisonedAsync(
            RecoverableExecutionJournal journal, RecoverableExecutionJournal.Entry? entry = null)
        {
            Assert.That(journal.IsReady, Is.False);
            await UnavailableAsync(() => Complete(journal.GetTimeAsync(default))).ConfigureAwait(false);
            await UnavailableAsync(() => Complete(journal.AdmitAsync(
                RecoverableJournalTestData.Admission(), default))).ConfigureAwait(false);
            if (entry != null)
            {
                await UnavailableAsync(() => Complete(entry.ObserveAsync(default))).ConfigureAwait(false);
                await UnavailableAsync(() => Complete(entry.ControlAsync(AsyncServiceAccess.Cancel, default)))
                    .ConfigureAwait(false);
                await UnavailableAsync(() => Complete(entry.BeginDispatchAsync())).ConfigureAwait(false);
            }
        }

        private static void AssertIndeterminate(
            RecoverableExecutionDurableRecord record, RecoverableExecutionTime origin, bool workFinished)
        {
            Assert.That(record.Phase, Is.EqualTo(RecoverableExecutionCommitPhase.OutcomeAvailable));
            Assert.That(record.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(record.Kind, Is.EqualTo(ServiceOperationOutcome.ExecutionIndeterminate));
            Assert.That(record.Status, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(record.Response.IsNull, Is.True);
            Assert.That(record.OutcomeAt, Is.EqualTo(origin));
            Assert.That(record.WorkFinished, Is.EqualTo(workFinished));
            Assert.That(record.Snapshot(origin).ResultRemaining, Is.EqualTo(50));
        }

        private static Task UnavailableAsync(Func<Task> action)
        {
            return ErrorAsync(action, StatusCodes.BadResourceUnavailable);
        }

        private static async Task ErrorAsync(Func<Task> action, StatusCode status)
        {
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await action().WaitAsync(s_watchdog).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(status));
        }

        private static Task Complete(ValueTask operation)
        {
            return operation.AsTask().WaitAsync(s_watchdog);
        }

        private static Task<T> Complete<T>(ValueTask<T> operation)
        {
            return operation.AsTask().WaitAsync(s_watchdog);
        }

        private static AesCbcHmacRecordProtector CreateProtector()
        {
            byte[] key = new byte[32];
            using var random = RandomNumberGenerator.Create();
            random.GetBytes(key);
            try
            {
                return new AesCbcHmacRecordProtector(key);
            }
            finally
            {
                CryptoUtils.ZeroMemory(key);
            }
        }

        private sealed class JournalScope : IAsyncDisposable
        {
            public JournalScope()
            {
                Store = new TestQualifiedStore(Raw);
                Protector = CreateProtector();
                Protection = new FaultingProtector(Protector);
                Owners.Setup(value => value.ValidatePersistedOwnerAsync(
                    It.IsAny<RecoverableExecutionOwner>(), It.IsAny<CancellationToken>()))
                    .Returns((RecoverableExecutionOwner owner, CancellationToken ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        OwnerChecks++;
                        Assert.That(owner.Id, Is.EqualTo(RecoverableJournalTestData.Owner.Id));
                        Assert.That(owner.ContinuityContext, Is.EqualTo(ByteString.From(0x6f, 0x77, 0x6e, 0x01)));
                        return OwnerAvailable ? default :
                            throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                    });
                Journal = NewJournal();
            }

            internal InMemorySharedKeyValueStore Raw { get; } = new();
            internal TestQualifiedStore Store { get; }
            internal AesCbcHmacRecordProtector Protector { get; }
            internal FaultingProtector Protection { get; }
            internal QualifiedClock Clock { get; } = new();
            internal Mock<IRecoverableExecutionOwnerProvider> Owners { get; } = new(MockBehavior.Strict);
            internal RecoverableExecutionJournal Journal { get; }
            internal bool OwnerAvailable { get; set; } = true;
            internal int OwnerChecks { get; private set; }
            internal static string Key => KeyFor(RecoverableJournalTestData.Admission());

            internal RecoverableExecutionJournal NewJournal(ISharedKeyValueStore? store = null)
            {
                var journal = new RecoverableExecutionJournal(store ?? Store, Protection, Clock, Owners.Object,
                    RecoverableJournalTestData.LogicalServerId, RecoverableJournalTestData.Context(),
                    RecoverableJournalTestData.Witness);
                m_journals.Add(journal);
                return journal;
            }

            internal async Task<ArrayOf<RecoverableExecutionJournal.Entry>> LoadAsync(
                RecoverableExecutionJournal? journal = null, int maximumRecords = 4)
            {
                ArrayOf<RecoverableExecutionJournal.Entry> entries =
                    await Complete((journal ?? Journal).LoadAsync(maximumRecords, default)).ConfigureAwait(false);
                foreach (RecoverableExecutionJournal.Entry entry in entries)
                {
                    m_entries.Add(entry);
                }
                return entries;
            }

            internal async Task<ArrayOf<RecoverableExecutionJournal.Entry>> LoadWithRestoreAsync(
                Func<RecoverableExecutionJournal.Entry, CancellationToken, ValueTask> restore,
                RecoverableExecutionJournal? journal = null, int maximumRecords = 4,
                CancellationToken cancellationToken = default)
            {
                ArrayOf<RecoverableExecutionJournal.Entry> entries = await Complete(
                    (journal ?? Journal).LoadAsync(maximumRecords, restore, cancellationToken)).ConfigureAwait(false);
                foreach (RecoverableExecutionJournal.Entry entry in entries)
                {
                    m_entries.Add(entry);
                }
                return entries;
            }

            internal async Task<RecoverableExecutionJournal.Entry> AdmitAsync(
                RecoverableExecutionDurableRecord? record = null, RecoverableExecutionJournal? journal = null,
                CancellationToken cancellationToken = default)
            {
                RecoverableExecutionJournal.Entry entry = await Complete((journal ?? Journal).AdmitAsync(
                    record ?? RecoverableJournalTestData.Admission(), cancellationToken)).ConfigureAwait(false);
                m_entries.Add(entry);
                return entry;
            }

            internal Task<T> Start<T>(Task<T> operation)
            {
                m_operations.Add(operation);
                return operation;
            }

            internal Task Start(Task operation)
            {
                m_operations.Add(operation);
                return operation;
            }

            internal Task SeedAsync(RecoverableExecutionDurableRecord record)
            {
                string key = KeyFor(record);
                return Complete(Raw.SetAsync(key,
                    RecoverableJournalTestData.Codec().Protect(record, Protector, ProtectionContext(key))));
            }

            internal Task SeedRawAsync(string key, ByteString plaintext)
            {
                // Only corruption tests substitute plaintext. The real codec still constructs the actual envelope.
                var corruptingProtector = new PlaintextSubstitutingProtector(Protector, plaintext);
                ByteString envelope = RecoverableJournalTestData.Codec().Protect(
                    RecoverableJournalTestData.Admission(), corruptingProtector, ProtectionContext(key));
                return Complete(Raw.SetAsync(key, envelope));
            }

            internal async Task<ByteString> StoredBytesAsync(string? key = null)
            {
                (bool found, ByteString value) = await Complete(Raw.TryGetAsync(key ?? Key)).ConfigureAwait(false);
                Assert.That(found, Is.True, "The oracle reads actual backing bytes, never Entry.Current.");
                return value;
            }

            internal async Task<RecoverableExecutionDurableRecord> StoredAsync(string? key = null)
            {
                ByteString bytes = await StoredBytesAsync(key).ConfigureAwait(false);
                return RecoverableJournalTestData.Codec().Unprotect(
                    bytes, Protector, ProtectionContext(key ?? Key));
            }

            internal static string KeyFor(RecoverableExecutionDurableRecord record)
            {
                return RecoverableExecutionRecordCodec.GetKey(
                    RecoverableJournalTestData.KeyPrefix, record.Owner.Id, record.ClientId);
            }

            internal static ByteString ProtectionContext(string key)
            {
                return RecordProtectionContext.Create("OpcUa.RSE.Server/0.4/v1", key);
            }

            public async ValueTask DisposeAsync()
            {
                Store.ReleaseGates();
                Clock.ReleaseGates();
                foreach (Task operation in m_operations)
                {
                    try
                    {
                        await operation.WaitAsync(s_watchdog).ConfigureAwait(false);
                    }
                    catch (Exception) when (operation.IsFaulted || operation.IsCanceled)
                    {
                        // The test owns the failure assertion. Observe faults so cleanup cannot strand a gate.
                    }
                }
                foreach (RecoverableExecutionJournal.Entry entry in m_entries)
                {
                    entry.Dispose();
                }
                foreach (RecoverableExecutionJournal journal in m_journals)
                {
                    journal.Dispose();
                }
                Protector.Dispose();
                Raw.Dispose();
            }

            private readonly List<RecoverableExecutionJournal> m_journals = [];
            private readonly List<RecoverableExecutionJournal.Entry> m_entries = [];
            private readonly List<Task> m_operations = [];
        }

        /// <summary>
        /// Qualification is explicitly a test assertion. The inner store holds real protected committed bytes,
        /// but this wrapper makes no claim that memory survives process or device power loss.
        /// </summary>
        private sealed class TestQualifiedStore(ISharedKeyValueStore inner) :
            ISharedKeyValueStore, ISharedKeyValueStoreConsistency, ISharedKeyValueStoreDurability
        {
            internal bool Available { get; set; } = true;
            internal bool Linearizable { get; set; } = true;
            internal int FailAt { get; set; }
            internal bool FailAfterCommit { get; set; }
            internal bool ReturnFalse { get; set; }
            internal int CasAttempts => Volatile.Read(ref m_attempts);
            internal int Commits => Volatile.Read(ref m_commits);
            internal int Deletes => Volatile.Read(ref m_deletes);
            internal int ScannedRecords => Volatile.Read(ref m_scanned);
            internal int HealthChecks => Volatile.Read(ref m_healthChecks);
            internal CancellationToken LastCasToken { get; private set; }

            internal CasGate Gate(int attempt, bool afterCommit)
            {
                var gate = new CasGate(attempt, afterCommit);
                m_gates.Add(gate);
                return gate;
            }

            internal void ReleaseGates()
            {
                foreach (CasGate gate in m_gates)
                {
                    gate.Release();
                }
            }

            public ValueTask VerifyDurabilityAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref m_healthChecks);
                return Available ? default : throw new IOException("Synthetic storage qualification lost.");
            }

            public bool IsLinearizable(string key)
            {
                return Linearizable;
            }

            public bool IsProcessLocal(string key)
            {
                return true;
            }

            public ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return inner.TryGetAsync(key, ct);
            }

            public ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return inner.SetAsync(key, value, ct);
            }

            public async ValueTask<bool> CompareAndSwapAsync(
                string key, ByteString expected, ByteString value, CancellationToken ct = default)
            {
                int attempt = Interlocked.Increment(ref m_attempts);
                LastCasToken = ct;
                CasGate? before = m_gates.Find(gate => gate.Attempt == attempt && !gate.AfterCommit);
                if (before != null)
                {
                    await before.EnterAsync().ConfigureAwait(false);
                }
                ct.ThrowIfCancellationRequested();
                if (FailAt == attempt && !FailAfterCommit)
                {
                    if (ReturnFalse)
                    {
                        return false;
                    }
                    throw new IOException("Synthetic failure before the inner CAS.");
                }
                bool committed = await inner.CompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false);
                if (committed)
                {
                    Interlocked.Increment(ref m_commits);
                    if (value.IsNull)
                    {
                        Interlocked.Increment(ref m_deletes);
                    }
                }
                CasGate? after = m_gates.Find(gate => gate.Attempt == attempt && gate.AfterCommit);
                if (after != null)
                {
                    await after.EnterAsync().ConfigureAwait(false);
                }
                if (FailAt == attempt && FailAfterCommit)
                {
                    throw new IOException("Synthetic reply loss after the inner CAS actually committed.");
                }
                return committed;
            }

            public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return inner.DeleteAsync(key, ct);
            }

            public async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix, [EnumeratorCancellation] CancellationToken ct = default)
            {
                await foreach (KeyValuePair<string, ByteString> item in inner.ScanAsync(keyPrefix, ct)
                    .ConfigureAwait(false))
                {
                    Interlocked.Increment(ref m_scanned);
                    yield return item;
                }
            }

            public IAsyncEnumerable<KeyValueChange> WatchAsync(string keyPrefix, CancellationToken ct = default)
            {
                return inner.WatchAsync(keyPrefix, ct);
            }

            private readonly List<CasGate> m_gates = [];
            private int m_attempts;
            private int m_commits;
            private int m_deletes;
            private int m_scanned;
            private int m_healthChecks;
        }

        /// <summary>
        /// A test-qualified value-byte budget, not the real file backend's snapshot/device capacity model.
        /// Capacity decisions and the existing inner CAS are serialized; rejected writes never touch committed bytes.
        /// </summary>
        private sealed class TestCapacityStore(TestQualifiedStore inner, int maximumValueBytes) :
            ISharedKeyValueStore, ISharedKeyValueStoreConsistency, ISharedKeyValueStoreDurability,
            ISharedKeyValueStoreCapacity, IDisposable
        {
            internal int MaximumValueBytes { get; } = maximumValueBytes;
            internal int CapacityAttempts { get; private set; }
            internal int CapacityRejections { get; private set; }
            internal SharedKeyValueStoreWriteResult? ReportAppliedAs { get; set; }

            public ValueTask VerifyDurabilityAsync(CancellationToken cancellationToken = default)
            {
                return inner.VerifyDurabilityAsync(cancellationToken);
            }

            public bool IsLinearizable(string key)
            {
                return inner.IsLinearizable(key);
            }

            public bool IsProcessLocal(string key)
            {
                return inner.IsProcessLocal(key);
            }

            public async ValueTask<SharedKeyValueStoreWriteResult> TryCompareAndSwapAsync(
                string key, ByteString expected, ByteString value, CancellationToken cancellationToken = default)
            {
                CapacityAttempts++;
                SharedKeyValueStoreWriteResult result =
                    await WriteAsync(key, expected, value, cancellationToken).ConfigureAwait(false);
                return result == SharedKeyValueStoreWriteResult.Applied ? ReportAppliedAs ?? result : result;
            }

            public async ValueTask<bool> CompareAndSwapAsync(
                string key, ByteString expected, ByteString value, CancellationToken ct = default)
            {
                SharedKeyValueStoreWriteResult result =
                    await WriteAsync(key, expected, value, ct).ConfigureAwait(false);
                return result switch
                {
                    SharedKeyValueStoreWriteResult.Applied => true,
                    SharedKeyValueStoreWriteResult.ConditionNotMet => false,
                    _ => throw new IOException("A legacy mutation exceeded the synthetic value-byte budget.")
                };
            }

            private async ValueTask<SharedKeyValueStoreWriteResult> WriteAsync(
                string key, ByteString expected, ByteString value, CancellationToken ct)
            {
                await m_writes.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    (bool found, ByteString current) = await inner.TryGetAsync(key, ct).ConfigureAwait(false);
                    if (expected.IsNull ? found : !found || current != expected)
                    {
                        return SharedKeyValueStoreWriteResult.ConditionNotMet;
                    }
                    long bytes = await ValueBytesAsync(ct).ConfigureAwait(false);
                    bytes -= found ? current.Length : 0;
                    bytes += value.IsNull ? 0 : value.Length;
                    if (bytes > MaximumValueBytes)
                    {
                        CapacityRejections++;
                        return SharedKeyValueStoreWriteResult.CapacityRejected;
                    }
                    return await inner.CompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false)
                        ? SharedKeyValueStoreWriteResult.Applied : SharedKeyValueStoreWriteResult.ConditionNotMet;
                }
                finally
                {
                    m_writes.Release();
                }
            }

            internal async Task<long> ValueBytesAsync(CancellationToken ct = default)
            {
                long bytes = 0;
                await foreach (KeyValuePair<string, ByteString> item in inner.ScanAsync(string.Empty, ct)
                    .ConfigureAwait(false))
                {
                    bytes += item.Value.Length;
                }
                return bytes;
            }

            public ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, CancellationToken ct = default)
            {
                return inner.TryGetAsync(key, ct);
            }

            public ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
            {
                throw new NotSupportedException("The journal must publish through its existing CAS contract.");
            }

            public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                throw new NotSupportedException("The journal must delete with the exact committed CAS predecessor.");
            }

            public IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix, CancellationToken ct = default)
            {
                return inner.ScanAsync(keyPrefix, ct);
            }

            public IAsyncEnumerable<KeyValueChange> WatchAsync(string keyPrefix, CancellationToken ct = default)
            {
                return inner.WatchAsync(keyPrefix, ct);
            }

            public void Dispose()
            {
                m_writes.Dispose();
            }

            private readonly SemaphoreSlim m_writes = new(1, 1);
        }

        private sealed class CasGate(int attempt, bool afterCommit) : IDisposable
        {
            internal int Attempt { get; } = attempt;
            internal bool AfterCommit { get; } = afterCommit;
            internal TaskCompletionSource<bool> Reached { get; } = Signal();

            internal async Task EnterAsync()
            {
                Reached.TrySetResult(true);
                await m_release.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            }

            internal void Release()
            {
                m_release.TrySetResult(true);
            }

            public void Dispose()
            {
                Release();
            }

            private readonly TaskCompletionSource<bool> m_release = Signal();
        }

        private sealed class ClockGate(int read) : IDisposable
        {
            internal int Read { get; } = read;
            internal TaskCompletionSource<bool> Reached { get; } = Signal();

            internal async Task EnterAsync()
            {
                Reached.TrySetResult(true);
                await m_release.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
            }

            internal void Release()
            {
                m_release.TrySetResult(true);
            }

            public void Dispose()
            {
                Release();
            }

            private readonly TaskCompletionSource<bool> m_release = Signal();
        }

        private sealed class QualifiedClock : IRecoverableExecutionClock
        {
            internal RecoverableExecutionTime Now { get; set; } = RecoverableJournalTestData.At(1000);
            internal bool Available { get; set; } = true;
            internal int Reads => Volatile.Read(ref m_reads);

            internal ClockGate Gate(int read)
            {
                var gate = new ClockGate(read);
                m_gates.Add(gate);
                return gate;
            }

            internal void ReleaseGates()
            {
                foreach (ClockGate gate in m_gates)
                {
                    gate.Release();
                }
            }

            public async ValueTask<RecoverableExecutionTime> GetTimeAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = Interlocked.Increment(ref m_reads);
                ClockGate? gate = m_gates.Find(value => value.Read == read);
                if (gate != null)
                {
                    await gate.EnterAsync().ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                return Available ? Now : throw new IOException("Synthetic elapsed-time proof unavailable.");
            }

            private readonly List<ClockGate> m_gates = [];
            private int m_reads;
        }

        private sealed class PlaintextSubstitutingProtector(IRecordProtector inner, ByteString substitute) :
            IRecordProtector
        {
            public ByteString Protect(ByteString context, ByteString plaintext)
            {
                return inner.Protect(context, substitute);
            }

            public bool TryUnprotect(ByteString context, ByteString protectedRecord, out ByteString plaintext)
            {
                return inner.TryUnprotect(context, protectedRecord, out plaintext);
            }
        }

        private sealed class FaultingProtector(IRecordProtector inner) : IRecordProtector
        {
            internal int Calls { get; private set; }
            internal int FailAt { get; set; }
            internal string Failure { get; set; } = "throw";

            public ByteString Protect(ByteString context, ByteString plaintext)
            {
                Calls++;
                if (Calls == FailAt)
                {
                    return Failure switch
                    {
                        "empty" => ByteString.Empty,
                        // Ciphertext capacity is 16384; the enclosing 8-byte header is not protection overhead.
                        "oversized" => new ByteString(new byte[16385]),
                        _ => throw new IOException("Synthetic serialization/protection failure.")
                    };
                }
                return inner.Protect(context, plaintext);
            }

            public bool TryUnprotect(ByteString context, ByteString protectedRecord, out ByteString plaintext)
            {
                return inner.TryUnprotect(context, protectedRecord, out plaintext);
            }
        }

        private static TaskCompletionSource<bool> Signal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static readonly bool[] s_restoreReadiness = [false, false];
        private static readonly TimeSpan s_watchdog = TimeSpan.FromSeconds(15);
    }
}
