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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryCandidateSnapshotTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task CandidateIsImmutableAndInvisibleUntilSuccessfulCommitAsync(bool concurrentChange)
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            IXRegistryPreparedOperation prepared = await endpoint.PrepareAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g/schemas/r/versions/v1",
                /*lang=json,strict*/ """{"name":"candidate","schemabase64":"AP8B"}""")).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable lifetime = prepared.ConfigureAwait(false);
            var candidate = (IXRegistryPreparedSnapshot)prepared;
            XRegistryEndpointDescription description = await candidate.InspectCandidateAsync().ConfigureAwait(false);
            XRegistryResponse preview = await candidate.ReadCandidateAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Read, "/groups/g/schemas/r/versions/v1") with
            {
                View = XRegistryView.Default,
                ExpectedGeneration = description.Generation
            }).ConfigureAwait(false);
            XRegistryResponse absent = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/groups/g")).ConfigureAwait(false);
            if (concurrentChange)
            {
                XRegistryResponse changed = await endpoint.ExecuteAsync(
                    XRegistryProviderCoverage.Request(XRegistryAction.Merge, "/", "{}")).ConfigureAwait(false);
                Assert.That(changed.StatusCode, Is.EqualTo(200));
            }
            XRegistryEndpointDescription again = await candidate.InspectCandidateAsync().ConfigureAwait(false);
            XRegistryResponse committed = await prepared.CommitAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(absent.StatusCode, Is.EqualTo(404));
                Assert.That(preview.Document.ToArray(), Is.EqualTo(new byte[] { 0, 255, 1 }));
                Assert.That(preview.Metadata.GetProperty("name").GetString(), Is.EqualTo("candidate"));
                Assert.That(preview.Generation, Is.EqualTo(description.Generation));
                Assert.That(again.Generation, Is.EqualTo(description.Generation));
                Assert.That(committed.StatusCode, Is.EqualTo(concurrentChange ? 409 : 201));
                Assert.That(candidate.HasCandidate, Is.False);
            });
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await candidate.InspectCandidateAsync().ConfigureAwait(false));
        }

        [TestCase(XRegistryAction.Replace)]
        [TestCase(XRegistryAction.Merge)]
        [TestCase(XRegistryAction.Create)]
        [TestCase(XRegistryAction.Delete)]
        public async Task CandidateCannotBeMutatedOrUsedAsAnotherOperationJournalAsync(XRegistryAction action)
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            IXRegistryPreparedOperation prepared = await endpoint.PrepareAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Merge, "/", "{}")).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable lifetime = prepared.ConfigureAwait(false);
            var candidate = (IXRegistryPreparedSnapshot)prepared;
            XRegistryResponse mutation = await candidate.ReadCandidateAsync(
                XRegistryProviderCoverage.Request(action, "/", "{}")).ConfigureAwait(false);
            XRegistryResponse journal = await candidate.ReadCandidateAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/") with { OperationId = "not-a-journal" })
                .ConfigureAwait(false);
            XRegistryResponse stale = await candidate.ReadCandidateAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/") with { ExpectedGeneration = "other" })
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(mutation.StatusCode, Is.EqualTo(405));
                Assert.That(journal.StatusCode, Is.EqualTo(405));
                Assert.That(stale.Error?.Code, Is.EqualTo("concurrent_change"));
            });
        }

        [Test]
        public async Task CandidateUsesOriginalIdentityAndRechecksAuthorizationAsync()
        {
            bool allowed = true;
            using var endpoint = new XRegistryTransactionalEndpoint(XRegistryProviderCoverage.Options() with
            {
                AuthorizeAsync = (context, _, _) => new ValueTask<bool>(
                    allowed && context.Subject == XRegistryProviderCoverage.Writer.Subject)
            }, new InMemoryXRegistryTransactionStore());
            IXRegistryPreparedOperation prepared = await endpoint.PrepareAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Merge, "/", "{}")).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable lifetime = prepared.ConfigureAwait(false);
            var candidate = (IXRegistryPreparedSnapshot)prepared;
            XRegistryResponse first = await candidate.ReadCandidateAsync(
                new XRegistryRequest(XRegistryAction.Read, "/") { Context = new XRegistryCallContext("other") })
                .ConfigureAwait(false);
            allowed = false;
            XRegistryResponse denied = await candidate.ReadCandidateAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.StatusCode, Is.EqualTo(200));
                Assert.That(denied.StatusCode, Is.EqualTo(403));
            });
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await candidate.InspectCandidateAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task ReplayedAndRejectedOutcomesNeverExposeAnotherCandidateAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            XRegistryRequest change = XRegistryProviderCoverage.Request(XRegistryAction.Merge, "/", "{}") with
            {
                OperationId = "committed"
            };
            Assert.That((await endpoint.ExecuteAsync(change).ConfigureAwait(false)).StatusCode, Is.EqualTo(200));
            IXRegistryPreparedOperation replay = await endpoint.PrepareAsync(change).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable replayLifetime = replay.ConfigureAwait(false);
            IXRegistryPreparedOperation rejected = await endpoint.PrepareAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Merge, "/",
                    /*lang=json,strict*/ """{"epoch":0}""")).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable rejectedLifetime = rejected.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(((IXRegistryPreparedSnapshot)replay).HasCandidate, Is.False);
                Assert.That(((IXRegistryPreparedSnapshot)rejected).HasCandidate, Is.False);
                Assert.That(replay.Response.StatusCode, Is.EqualTo(200));
                Assert.That(rejected.Response.StatusCode, Is.EqualTo(400));
            });
        }
    }
}
