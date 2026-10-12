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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;
using static Opc.Ua.XRegistry.Tests.ProtocolProvider.XRegistryProviderCoverage;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryJournalMaintenanceTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetiredOutcomesKeepTheirOriginalStateAndCannotBeReappliedAfterRestartAsync(bool rejected)
        {
            var store = new InMemoryXRegistryTransactionStore();
            const string operationId = "acknowledged-operation";
            XRegistryRequest request = Request(XRegistryAction.Merge, "/",
                rejected ? /*lang=json,strict*/ """{"epoch":17}""" : /*lang=json,strict*/ """{"name":"once"}""") with
            {
                OperationId = operationId
            };
            using (var endpoint = new XRegistryTransactionalEndpoint(Options(), store))
            {
                XRegistryResponse response = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
                Assert.That(response.StatusCode, Is.EqualTo(rejected ? 400 : 200));
                Assert.That(
                    await endpoint.RetireOutcomesAsync([operationId], Writer).ConfigureAwait(false), Is.EqualTo(1));
                Assert.That(await endpoint.RetireOutcomesAsync([operationId], Writer).ConfigureAwait(false), Is.Zero);
            }
            using var reopened = new XRegistryTransactionalEndpoint(Options(), store);
            XRegistryResponse replay = await reopened.ExecuteAsync(request).ConfigureAwait(false);
            XRegistryOperationOutcome outcome = await reopened.GetOperationOutcomeAsync(operationId, Writer)
                .ConfigureAwait(false);
            XRegistryResponse root =
                await reopened.ExecuteAsync(Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(replay.StatusCode, Is.EqualTo(410));
                Assert.That(replay.Error?.Code, Is.EqualTo("operation_retired"));
                Assert.That(outcome.State,
                    Is.EqualTo(rejected ? XRegistryOperationState.Rejected : XRegistryOperationState.Committed));
                Assert.That(outcome.Response, Is.Null);
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(rejected ? 0 : 1));
            });
        }

        [Test]
        public async Task InvalidOrUnauthorizedAcknowledgmentsCannotPartiallyRetireAJournalAsync()
        {
            var store = new InMemoryXRegistryTransactionStore();
            using var endpoint = new XRegistryTransactionalEndpoint(Options(), store);
            XRegistryRequest request = Request(XRegistryAction.Merge, "/", "{}") with { OperationId = "retained" };
            Assert.That((await endpoint.ExecuteAsync(request).ConfigureAwait(false)).StatusCode, Is.EqualTo(200));
            ByteString before = await store.LoadAsync().ConfigureAwait(false);
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await endpoint.RetireOutcomesAsync(["retained", "unknown"], Writer).ConfigureAwait(false));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await endpoint.RetireOutcomesAsync(["retained"], XRegistryCallContext.Anonymous).ConfigureAwait(false));
            Assert.That(await store.LoadAsync().ConfigureAwait(false), Is.EqualTo(before));
            XRegistryOperationOutcome retained = await endpoint.GetOperationOutcomeAsync("retained", Writer)
                .ConfigureAwait(false);
            Assert.That(retained.Response?.StatusCode, Is.EqualTo(200));
        }

        [Test]
        public async Task MaintenanceCannotInvalidateAnOutstandingPreparationAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            IXRegistryPreparedOperation operation =
                await endpoint.PrepareAsync(Request(XRegistryAction.Merge, "/", "{}"))
                .ConfigureAwait(false);
            await using (operation.ConfigureAwait(false))
            {
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await endpoint.RetireOutcomesAsync([], Writer).ConfigureAwait(false));
                Assert.That((await operation.CommitAsync().ConfigureAwait(false)).StatusCode, Is.EqualTo(200));
            }
        }
    }
}
