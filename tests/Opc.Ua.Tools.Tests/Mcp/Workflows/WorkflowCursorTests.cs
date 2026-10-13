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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpCompanion.Workflows
{
    /// <summary>
    /// Proves exact server-continuation ownership, bounded lifetime and deterministic cleanup.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class WorkflowCursorTests
    {
        /// <summary>
        /// Stateless reads work without caller identity and release their own unconsumed server continuation.
        /// </summary>
        [Test]
        public async Task StatelessReadWorksWithoutCallerAndReleasesUnconsumedContinuationAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            Seed(fixture);
            CallToolResult result = await ReadAsync(fixture, retain: false).ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("hasMore").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("continuationRetained").GetBoolean(), Is.False);
            Assert.That(json.GetProperty("cursor").ValueKind, Is.EqualTo(JsonValueKind.Null));
            AssertRelease(fixture, kPoint);
        }

        /// <summary>
        /// Retention cannot create an unowned handle even if the first query would otherwise be harmless.
        /// </summary>
        [Test]
        public async Task RetainedReadRequiresTrustedCallerBeforeReadingServerAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            Seed(fixture);
            CallToolResult result = await ReadAsync(fixture, retain: true).ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.HistoryReads, Is.Empty);
        }

        /// <summary>
        /// Paging carries the existing continuation rather than replaying a query with an offset.
        /// </summary>
        [Test]
        public async Task ReadNextUsesExactServerContinuationAndExhaustionReleasesRegistryEntryAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.HistoryPages.Enqueue(WorkflowHistoryTests.Page(
                [WorkflowHistoryTests.Data(1), WorkflowHistoryTests.Data(2)], kPoint));
            fixture.HistoryPages.Enqueue(WorkflowHistoryTests.Page([WorkflowHistoryTests.Data(3)]));
            using IDisposable caller = fixture.Enter();
            string cursor = Handle(await ReadAsync(fixture, true).ConfigureAwait(false));
            CallToolResult next = await fixture.History.NextAsync(cursor, 2).ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(next);
            Assert.That(next.IsError, Is.False);
            Assert.That(json.GetProperty("items").GetArrayLength(), Is.EqualTo(2));
            Assert.That(json.GetProperty("complete").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("cursor").ValueKind, Is.EqualTo(JsonValueKind.Null));
            WorkflowHistoryRead[] reads = [.. fixture.HistoryReads.Where(read => !read.Release)];
            Assert.That(reads, Has.Length.EqualTo(2));
            Assert.That(reads[0].Nodes[0].ContinuationPoint.IsEmpty, Is.True);
            Assert.That(reads[1].Nodes[0].ContinuationPoint, Is.EqualTo(kPoint));
            Assert.That(reads[0].Details, Is.EqualTo(reads[1].Details));
            RemoteModelTestSession.Error(await fixture.History.NextAsync(cursor).ConfigureAwait(false),
                StatusCodes.BadContinuationPointInvalid);
        }

        /// <summary>
        /// Another authorized caller cannot advance or release this caller's cursor.
        /// </summary>
        [Test]
        public async Task ForeignCallerCannotReadOrReleaseOwnedHistoryCursorAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            Seed(fixture);
            using IDisposable owner = fixture.Enter();
            string cursor = Handle(await ReadAsync(fixture, true).ConfigureAwait(false));
            using (fixture.Enter(WorkflowTestContext.Other))
            {
                RemoteModelTestSession.Error(await fixture.History.NextAsync(cursor).ConfigureAwait(false),
                    StatusCodes.BadContinuationPointInvalid);
                RemoteModelTestSession.Error(await fixture.History.ReleaseAsync(cursor).ConfigureAwait(false),
                    StatusCodes.BadContinuationPointInvalid);
            }
            Assert.That(fixture.HistoryReads.Count(read => read.Release), Is.Zero);
            CallToolResult released = await fixture.History.ReleaseAsync(cursor).ConfigureAwait(false);
            Assert.That(RemoteModelTestSession.Payload(released).GetProperty("released").GetBoolean(), Is.True);
            AssertRelease(fixture, kPoint);
        }

        /// <summary>
        /// Exact idle expiry is enforced on both sides of the boundary without sleeping.
        /// </summary>
        [TestCase(-1, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        public async Task CursorIdleExpiryHasExactClockControlledBoundaryAsync(int relativeMilliseconds, bool expired)
        {
            await using WorkflowTestContext fixture = await WorkflowTestContext.CreateAsync(
                options: new WorkflowOptions
                {
                    HistoryCursorIdleTimeout = TimeSpan.FromSeconds(60),
                    HistoryCursorLifetime = TimeSpan.FromMinutes(2)
                }).ConfigureAwait(false);
            Seed(fixture);
            using IDisposable caller = fixture.Enter();
            string cursor = Handle(await ReadAsync(fixture, true).ConfigureAwait(false));
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(60000 + relativeMilliseconds));
            await fixture.Cursors.SweepAsync().ConfigureAwait(false);
            CallToolResult result = await fixture.History.NextAsync(cursor, 1).ConfigureAwait(false);
            Assert.That(result.IsError, Is.EqualTo(expired));
            if (expired)
            {
                RemoteModelTestSession.Error(result, StatusCodes.BadContinuationPointInvalid);
                AssertRelease(fixture, kPoint);
            }
            else
            {
                Assert.That(fixture.HistoryReads.Count(read => read.Release), Is.Zero);
            }
        }

        /// <summary>
        /// Active paging can extend idle time, but cannot extend the host's absolute lifetime.
        /// </summary>
        [Test]
        public async Task CursorUseDoesNotExtendAbsoluteLeaseAsync()
        {
            await using WorkflowTestContext fixture = await WorkflowTestContext.CreateAsync(
                options: new WorkflowOptions
                {
                    HistoryCursorIdleTimeout = TimeSpan.FromSeconds(40),
                    HistoryCursorLifetime = TimeSpan.FromSeconds(60)
                }).ConfigureAwait(false);
            fixture.HistoryPages.Enqueue(WorkflowHistoryTests.Page(
                Enumerable.Range(1, 8).Select(index => WorkflowHistoryTests.Data(index)).ToArrayOf(), kPoint));
            using IDisposable caller = fixture.Enter();
            string cursor = Handle(await ReadAsync(fixture, true).ConfigureAwait(false));
            fixture.Clock.Advance(TimeSpan.FromSeconds(39));
            Assert.That((await fixture.History.NextAsync(cursor, 1).ConfigureAwait(false)).IsError, Is.False);
            fixture.Clock.Advance(TimeSpan.FromSeconds(20));
            Assert.That((await fixture.History.NextAsync(cursor, 1).ConfigureAwait(false)).IsError, Is.False);
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
            RemoteModelTestSession.Error(await fixture.History.NextAsync(cursor, 1).ConfigureAwait(false),
                StatusCodes.BadContinuationPointInvalid);
            AssertRelease(fixture, kPoint);
        }

        /// <summary>
        /// A session generation change invalidates cursors without releasing an old point through its replacement.
        /// </summary>
        [Test]
        public async Task ChangedUaSessionGenerationRefusesCursorWithoutReleasingPointOnReplacementAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            Seed(fixture);
            using IDisposable caller = fixture.Enter();
            string cursor = Handle(await ReadAsync(fixture, true).ConfigureAwait(false));
            fixture.Remote.Session.SetupGet(session => session.SessionId).Returns(fixture.Remote.Id("new-generation"));
            RemoteModelTestSession.Error(await fixture.History.NextAsync(cursor).ConfigureAwait(false),
                StatusCodes.BadContinuationPointInvalid);
            Assert.That(fixture.HistoryReads.Count(read => read.Release), Is.Zero);
            Assert.That(fixture.SessionScopes.Scopes.Single().Disposed, Is.True);
        }

        /// <summary>
        /// Re-registering even the same session object under the same name creates a distinct ownership generation.
        /// </summary>
        [Test]
        public async Task ReplacedRegistrationInvalidatesCursorEvenWithUnchangedSessionIdAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            Seed(fixture);
            using IDisposable caller = fixture.Enter();
            string cursor = Handle(await ReadAsync(fixture, true).ConfigureAwait(false));
            await fixture.Manager.RegisterExistingSessionAsync("selected", fixture.Remote.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            RemoteModelTestSession.Error(await fixture.History.NextAsync(cursor).ConfigureAwait(false),
                StatusCodes.BadContinuationPointInvalid);
            AssertRelease(fixture, kPoint);
        }

        /// <summary>
        /// A per-caller quota does not consume another caller's independent capacity.
        /// </summary>
        [Test]
        public async Task PerCallerCursorQuotaIsIndependentWithinHostCapacityAsync()
        {
            await using WorkflowTestContext fixture = await WorkflowTestContext.CreateAsync(
                options: new WorkflowOptions { MaxHistoryCursors = 2, MaxHistoryCursorsPerCaller = 1 })
                .ConfigureAwait(false);
            Seed(fixture);
            using IDisposable owner = fixture.Enter();
            string first = Handle(await ReadAsync(fixture, true).ConfigureAwait(false));
            RemoteModelTestSession.Error(await ReadAsync(fixture, true).ConfigureAwait(false),
                StatusCodes.BadTooManyOperations);
            using (fixture.Enter(WorkflowTestContext.Other))
            {
                Seed(fixture);
                string second = Handle(await ReadAsync(fixture, true).ConfigureAwait(false));
                Assert.That(second, Is.Not.EqualTo(first));
                Assert.That((await fixture.History.ReleaseAsync(second).ConfigureAwait(false)).IsError, Is.False);
            }
            Assert.That((await fixture.History.ReleaseAsync(first).ConfigureAwait(false)).IsError, Is.False);
            Assert.That(fixture.HistoryReads.Count(read => read.Release), Is.EqualTo(2));
        }

        /// <summary>
        /// Cursor limits are checked before a second enumeration starts reading.
        /// </summary>
        [Test]
        public async Task CursorQuotaRefusesBeforeServerReadAndReleaseRestoresCapacityAsync()
        {
            await using WorkflowTestContext fixture = await WorkflowTestContext.CreateAsync(
                options: new WorkflowOptions { MaxHistoryCursors = 1, MaxHistoryCursorsPerCaller = 1 })
                .ConfigureAwait(false);
            Seed(fixture);
            using IDisposable caller = fixture.Enter();
            string first = Handle(await ReadAsync(fixture, true).ConfigureAwait(false));
            int before = fixture.HistoryReads.Count;
            RemoteModelTestSession.Error(await ReadAsync(fixture, true).ConfigureAwait(false),
                StatusCodes.BadTooManyOperations);
            Assert.That(fixture.HistoryReads, Has.Count.EqualTo(before));
            await fixture.History.ReleaseAsync(first).ConfigureAwait(false);
            Seed(fixture);
            Assert.That(Handle(await ReadAsync(fixture, true).ConfigureAwait(false)), Is.Not.EqualTo(first));
        }

        /// <summary>
        /// A bad subsequent page releases its actual current continuation, not a stale earlier point.
        /// </summary>
        [Test]
        public async Task HistoryFailureReleasesCurrentContinuationAndPreservesBadStatusAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.HistoryPages.Enqueue(WorkflowHistoryTests.Page([WorkflowHistoryTests.Data(1)], kPoint));
            var failedPoint = ByteString.From("\t\t"u8.ToArray());
            fixture.HistoryPages.Enqueue(new HistoryReadResult
            {
                StatusCode = StatusCodes.BadOutOfService,
                ContinuationPoint = failedPoint
            });
            CallToolResult result = await ReadAsync(fixture, false).ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadOutOfService);
            AssertRelease(fixture, failedPoint);
        }

        /// <summary>
        /// Repeated empty continuation pages cannot produce an unbounded reader.
        /// </summary>
        [Test]
        public async Task HistoryPageBudgetStopsEmptyContinuationLoopAndReleasesAsync()
        {
            await using WorkflowTestContext fixture = await WorkflowTestContext.CreateAsync(
                options: new WorkflowOptions { MaxHistoryPages = 2 }).ConfigureAwait(false);
            fixture.HistoryPages.Enqueue(WorkflowHistoryTests.Page([], kPoint));
            fixture.HistoryPages.Enqueue(WorkflowHistoryTests.Page([], kPoint));
            CallToolResult result = await ReadAsync(fixture, false).ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadTimeout);
            Assert.That(fixture.HistoryReads.Count(read => !read.Release), Is.EqualTo(2));
            AssertRelease(fixture, kPoint);
        }

        /// <summary>
        /// Host shutdown owns and releases all retained enumerators, without needing a subsequent MCP request.
        /// </summary>
        [Test]
        public async Task RegistryDisposalReleasesRetainedContinuationExactlyOnceAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            Seed(fixture);
            using IDisposable caller = fixture.Enter();
            _ = Handle(await ReadAsync(fixture, true).ConfigureAwait(false));
            await fixture.Cursors.DisposeAsync().ConfigureAwait(false);
            await fixture.Cursors.DisposeAsync().ConfigureAwait(false);
            AssertRelease(fixture, kPoint);
        }

        /// <summary>
        /// A caller-cancelled first read cannot retain an iterator after returning a cancellation error.
        /// </summary>
        [Test]
        public async Task CancelledActiveCursorReadDisposesSourceAndRestoresCapacityAsync()
        {
            await using WorkflowTestContext fixture = await WorkflowTestContext.CreateAsync(
                options: new WorkflowOptions { MaxHistoryCursors = 1, MaxHistoryCursorsPerCaller = 1 })
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            using var cancellation = new CancellationTokenSource();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<JsonObject> read = fixture.Cursors.ReadAsync(
                fixture.Manager.GetSessionInfoOrThrow("selected"),
                token => WaitingSource(started, disposed, token), 1, true, cancellation.Token).AsTask();
            await started.Task.ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            Assert.That(() => read, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(disposed.Task.IsCompletedSuccessfully, Is.True);
            Seed(fixture);
            Assert.That(Handle(await ReadAsync(fixture, true).ConfigureAwait(false)), Does.StartWith("history:"));
        }

        /// <summary>
        /// Host shutdown cancels a blocked page, joins it, then disposes its own primitives.
        /// </summary>
        [Test]
        public async Task RegistryShutdownCancelsAndJoinsActiveReadWithoutAbandonedSourceAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<JsonObject> read = fixture.Cursors.ReadAsync(
                fixture.Manager.GetSessionInfoOrThrow("selected"),
                token => WaitingSource(started, disposed, token), 1, true).AsTask();
            await started.Task.ConfigureAwait(false);
            await fixture.Cursors.DisposeAsync().ConfigureAwait(false);
            Assert.That(() => read, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(disposed.Task.IsCompletedSuccessfully, Is.True);
        }

        /// <summary>
        /// Models a blocked SDK enumeration with observable cancellation cleanup.
        /// </summary>
        private static async IAsyncEnumerable<JsonNode?> WaitingSource(
            TaskCompletionSource started, TaskCompletionSource disposed,
            [EnumeratorCancellation] CancellationToken ct)
        {
            try
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                yield break;
            }
            finally
            {
                disposed.TrySetResult();
            }
        }

        /// <summary>
        /// Supplies enough values to retain buffered data without an unnecessary second server page.
        /// </summary>
        private static void Seed(WorkflowTestContext fixture)
        {
            fixture.HistoryPages.Enqueue(WorkflowHistoryTests.Page(
                [
                    WorkflowHistoryTests.Data(1), WorkflowHistoryTests.Data(2),
                    WorkflowHistoryTests.Data(3), WorkflowHistoryTests.Data(4)
                ], kPoint));
        }

        /// <summary>
        /// Starts a one-item raw read through the real tool.
        /// </summary>
        private static Task<CallToolResult> ReadAsync(WorkflowTestContext fixture, bool retain)
        {
            return fixture.History.ReadRawAsync(fixture.Remote.Id("signal").ToString(),
                "2026-01-01T00:00:00Z", "2026-01-02T00:00:00Z", maxResults: 1,
                retainCursor: retain, sessionName: "selected");
        }

        /// <summary>
        /// Gets an actually returned opaque handle; no test manufactures or guesses one.
        /// </summary>
        private static string Handle(CallToolResult result)
        {
            Assert.That(result.IsError, Is.False);
            string cursor = RemoteModelTestSession.Payload(result).GetProperty("cursor").GetString()!;
            Assert.That(cursor, Does.StartWith("history:"));
            return cursor;
        }

        /// <summary>
        /// Proves cleanup used the real point, its original node and an uncancelled independent token.
        /// </summary>
        private static void AssertRelease(WorkflowTestContext fixture, ByteString expected)
        {
            WorkflowHistoryRead[] releases = [.. fixture.HistoryReads.Where(read => read.Release)];
            Assert.That(releases, Has.Length.EqualTo(1));
            Assert.That(releases[0].Nodes[0].ContinuationPoint, Is.EqualTo(expected));
            Assert.That(releases[0].Nodes[0].NodeId, Is.EqualTo(fixture.Remote.Id("signal")));
            Assert.That(releases[0].Token.CanBeCanceled, Is.False);
        }

        private static readonly ByteString kPoint = ByteString.From(new byte[] { 4, 2 });
    }
}
#endif
