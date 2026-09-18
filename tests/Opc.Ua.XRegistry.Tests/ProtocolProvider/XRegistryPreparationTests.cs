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
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryPreparationTests
    {
        [Test]
        public async Task ResponsePreviewAndAbortNeverPublishAnEntityOrOutcomeAsync()
        {
            var store = new InMemoryXRegistryTransactionStore();
            using var endpoint = new XRegistryTransactionalEndpoint(Options(), store);
            IXRegistryPreparedOperation operation = await endpoint.PrepareAsync(
                Change("preview") with { OperationId = "p" })
                .ConfigureAwait(false);
            XRegistryResponse before = await endpoint.ExecuteAsync(Read()).ConfigureAwait(false);
            ByteString stored = await store.LoadAsync().ConfigureAwait(false);
            await operation.DisposeAsync().ConfigureAwait(false);
            XRegistryOperationOutcome outcome = await endpoint.GetOperationOutcomeAsync("p", s_caller)
                .ConfigureAwait(false);
            XRegistryResponse after = await endpoint.ExecuteAsync(Read()).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(operation.Response.Metadata.GetProperty("name").GetString(), Is.EqualTo("preview"));
                Assert.That(operation.Response.Metadata.GetProperty("epoch").GetUInt64(), Is.EqualTo(1));
                Assert.That(before.Metadata.TryGetProperty("name", out _), Is.False);
                Assert.That(after.Metadata.TryGetProperty("name", out _), Is.False);
                Assert.That(after.Metadata.GetProperty("epoch").GetUInt64(), Is.Zero);
                Assert.That(stored.IsNull, Is.True);
                Assert.That(outcome.State, Is.EqualTo(XRegistryOperationState.Unknown));
            });
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await operation.CommitAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task SuccessfulCommitReturnsExactlyThePreparedResponseAndCannotTouchTwiceAsync()
        {
            using var endpoint = new XRegistryTransactionalEndpoint(Options(), new InMemoryXRegistryTransactionStore());
            IXRegistryPreparedOperation operation = await endpoint.PrepareAsync(Change("committed"))
                .ConfigureAwait(false);
            await using ConfiguredAsyncDisposable lifetime = operation.ConfigureAwait(false);
            XRegistryResponse committed = await operation.CommitAsync().ConfigureAwait(false);
            XRegistryResponse read = await endpoint.ExecuteAsync(Read()).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(committed, Is.SameAs(operation.Response));
                Assert.That(read.Metadata.GetRawText(), Is.EqualTo(committed.Metadata.GetRawText()));
                Assert.That(read.Metadata.GetProperty("epoch").GetUInt64(), Is.EqualTo(1));
            });
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await operation.CommitAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task InterveningCommitRejectsTheSecondPreviewInsteadOfRebasingItAsync()
        {
            using var endpoint = new XRegistryTransactionalEndpoint(Options(), new InMemoryXRegistryTransactionStore());
            IXRegistryPreparedOperation first = await endpoint.PrepareAsync(Change("first")).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable firstLifetime = first.ConfigureAwait(false);
            IXRegistryPreparedOperation second = await endpoint.PrepareAsync(Change("second")).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable secondLifetime = second.ConfigureAwait(false);
            XRegistryResponse committed = await first.CommitAsync().ConfigureAwait(false);
            XRegistryResponse rejected = await second.CommitAsync().ConfigureAwait(false);
            XRegistryResponse read = await endpoint.ExecuteAsync(Read()).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(committed.StatusCode, Is.EqualTo(200));
                Assert.That(rejected.StatusCode, Is.EqualTo(409));
                Assert.That(rejected.Error?.Code, Is.EqualTo("concurrent_change"));
                Assert.That(read.Metadata.GetProperty("name").GetString(), Is.EqualTo("first"));
                Assert.That(read.Metadata.GetProperty("epoch").GetUInt64(), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task RevokingAuthorizationBetweenPreparationAndCommitPreventsPublicationAsync()
        {
            bool allow = true;
            using var endpoint = new XRegistryTransactionalEndpoint(Options() with
            {
                AuthorizeAsync = (_, mutation, _) => new ValueTask<bool>(!mutation || allow)
            }, new InMemoryXRegistryTransactionStore());
            IXRegistryPreparedOperation operation = await endpoint.PrepareAsync(Change("revoked"))
                .ConfigureAwait(false);
            await using ConfiguredAsyncDisposable lifetime = operation.ConfigureAwait(false);
            allow = false;
            XRegistryResponse rejected = await operation.CommitAsync().ConfigureAwait(false);
            XRegistryResponse read = await endpoint.ExecuteAsync(Read()).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(403));
                Assert.That(read.Metadata.TryGetProperty("name", out _), Is.False);
                Assert.That(read.Metadata.GetProperty("epoch").GetUInt64(), Is.Zero);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedQuotaIsReleasedAfterAbortOrCommitAsync(bool commit)
        {
            using var endpoint = new XRegistryTransactionalEndpoint(
                Options() with { MaxPreparedOperations = 1 }, new InMemoryXRegistryTransactionStore());
            IXRegistryPreparedOperation held = await endpoint.PrepareAsync(Change("held")).ConfigureAwait(false);
            IXRegistryPreparedOperation denied = await endpoint.PrepareAsync(Change("denied")).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable deniedLifetime = denied.ConfigureAwait(false);
            Assert.That(denied.Response.StatusCode, Is.EqualTo(429));
            if (commit)
            {
                await held.CommitAsync().ConfigureAwait(false);
            }
            await held.DisposeAsync().ConfigureAwait(false);
            IXRegistryPreparedOperation released = await endpoint.PrepareAsync(Change("released"))
                .ConfigureAwait(false);
            await using ConfiguredAsyncDisposable releasedLifetime = released.ConfigureAwait(false);
            Assert.That(released.Response.StatusCode, Is.EqualTo(200));
        }

        [Test]
        public async Task CanceledCommitDoesNotPublishAndAbortReleasesItsReservationAsync()
        {
            using var endpoint = new XRegistryTransactionalEndpoint(
                Options() with { MaxPreparedOperations = 1 }, new InMemoryXRegistryTransactionStore());
            IXRegistryPreparedOperation operation = await endpoint.PrepareAsync(Change("canceled"))
                .ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await operation.CommitAsync(cancellation.Token).ConfigureAwait(false));
            await operation.DisposeAsync().ConfigureAwait(false);
            XRegistryResponse read = await endpoint.ExecuteAsync(Read()).ConfigureAwait(false);
            XRegistryResponse changed = await endpoint.ExecuteAsync(Change("next")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(read.Metadata.GetProperty("epoch").GetUInt64(), Is.Zero);
                Assert.That(changed.StatusCode, Is.EqualTo(200));
                Assert.That(changed.Metadata.GetProperty("epoch").GetUInt64(), Is.EqualTo(1));
            });
        }

        private static XRegistryTransactionalOptions Options()
        {
            using var model = JsonDocument.Parse("""{"groups":{}}""");
            return new XRegistryTransactionalOptions { Model = model.RootElement };
        }

        private static XRegistryRequest Change(string name)
        {
            using var document = JsonDocument.Parse($$"""{"name":"{{name}}"}""");
            return new XRegistryRequest(XRegistryAction.Merge, "/")
            {
                Metadata = document.RootElement,
                Context = s_caller
            };
        }

        private static XRegistryRequest Read()
        {
            return new XRegistryRequest(XRegistryAction.Read, "/") { Context = s_caller };
        }

        private static readonly XRegistryCallContext s_caller = new("writer")
        {
            IsAuthenticated = true,
            Roles = ["xregistry.write"]
        };
    }
}
