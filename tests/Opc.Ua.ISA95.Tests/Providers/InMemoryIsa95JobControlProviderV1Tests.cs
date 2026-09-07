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

using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.ISA95.Server.Providers;
using V1 = Opc.Ua.ISA95.JobControl.V1;

namespace Opc.Ua.ISA95.Tests.Providers
{
    [TestFixture]
    public class InMemoryIsa95JobControlProviderV1Tests
    {
        [Test]
        public async Task StoreCreatesOrderAndReturnsNoError()
        {
            using var provider = new InMemoryIsa95JobControlProvider();

            Isa95JobOrderReceiptV1 receipt = await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Store,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(receipt.Result), Is.True);
            Assert.That(
                receipt.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.Success));
        }

        [Test]
        public async Task StoreDuplicateReturnsUnableToAcceptJobOrder()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Store,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Isa95JobOrderReceiptV1 receipt = await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Store,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsUncertain(receipt.Result), Is.True);
            Assert.That(
                receipt.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.UnableToAccept));
        }

        [Test]
        public async Task StoreWithEmptyIdentifierReturnsInvalidJobOrderId()
        {
            using var provider = new InMemoryIsa95JobControlProvider();

            Isa95JobOrderReceiptV1 receipt = await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Store,
                Isa95TestData.V1Order(string.Empty)).ConfigureAwait(false);

            Assert.That(ServiceResult.IsUncertain(receipt.Result), Is.True);
            Assert.That(
                receipt.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.InvalidRequest));
        }

        [Test]
        public async Task UndefinedCommandReturnsUnableToAcceptCommand()
        {
            using var provider = new InMemoryIsa95JobControlProvider();

            Isa95JobOrderReceiptV1 receipt = await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Undefined,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsUncertain(receipt.Result), Is.True);
            Assert.That(
                receipt.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.InvalidCommand));
        }

        [Test]
        public async Task StoreAndStartThenStartAgainIsInvalidState()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.StoreAndStart,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Isa95JobOrderReceiptV1 receipt = await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Start,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsUncertain(receipt.Result), Is.True);
            Assert.That(
                receipt.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.InvalidCommand));
        }

        [Test]
        public async Task StartUnknownOrderReturnsInvalidJobOrderId()
        {
            using var provider = new InMemoryIsa95JobControlProvider();

            Isa95JobOrderReceiptV1 receipt = await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Start,
                Isa95TestData.V1Order("missing")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsUncertain(receipt.Result), Is.True);
            Assert.That(
                receipt.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.UnknownJobOrderId));
        }

        [Test]
        public async Task UpdateBeforeStartSucceedsAndUpdateAfterStartIsInvalid()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Store,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Isa95JobOrderReceiptV1 beforeStart = await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Update,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);
            await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Start,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);
            await provider.TransitionAsync(
                "job1",
                Isa95JobExecutionTransition.BeginExecution).ConfigureAwait(false);
            Isa95JobOrderReceiptV1 afterStart = await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Update,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(beforeStart.Result), Is.True);
            Assert.That(ServiceResult.IsUncertain(afterStart.Result), Is.True);
        }

        [Test]
        public async Task CancelBeforeStartRemovesOrder()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Store,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Isa95JobOrderReceiptV1 cancel = await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Cancel,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);
            Isa95JobOrderReceiptV1 startAfterCancel = await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Start,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(cancel.Result), Is.True);
            Assert.That(
                startAfterCancel.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.UnknownJobOrderId));
        }

        [Test]
        public async Task CancelRunningOrderIsInvalidState()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.StoreAndStart,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);
            await provider.TransitionAsync(
                "job1",
                Isa95JobExecutionTransition.BeginExecution).ConfigureAwait(false);

            Isa95JobOrderReceiptV1 receipt = await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Cancel,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsUncertain(receipt.Result), Is.True);
        }

        [Test]
        public async Task StopRemovesStartedOrderAsV1Requires()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.StoreAndStart,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);
            await provider.TransitionAsync(
                "job1",
                Isa95JobExecutionTransition.BeginExecution).ConfigureAwait(false);

            Isa95JobOrderReceiptV1 stop = await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Stop,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);
            // V1 Stop removes the stored information, so the order is no longer known.
            Isa95JobOrderReceiptV1 startAfterStop = await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Start,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(stop.Result), Is.True);
            Assert.That(
                stop.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.Success));
            Assert.That(
                startAfterStop.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.UnknownJobOrderId));
        }

        [Test]
        public async Task StopOnNotStartedOrderIsInvalidState()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Store,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Isa95JobOrderReceiptV1 stop = await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Stop,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsUncertain(stop.Result), Is.True);
            Assert.That(
                stop.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.InvalidCommand));
        }

        [Test]
        public async Task ClearNonTerminalOrderIsInvalidState()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Store,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Isa95JobOrderReceiptV1 receipt = await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Clear,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsUncertain(receipt.Result), Is.True);
        }

        [Test]
        public async Task StoreBeyondCapacityIsRejected()
        {
            using var provider = new InMemoryIsa95JobControlProvider(
                new Isa95JobControlProviderOptions { MaxJobOrders = 1 });
            await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Store,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);

            Isa95JobOrderReceiptV1 receipt = await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Store,
                Isa95TestData.V1Order("job2")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsUncertain(receipt.Result), Is.True);
            Assert.That(
                receipt.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.UnableToAccept));
        }

        [Test]
        public async Task ReceiveJobResponseThenRequestReturnsResponse()
        {
            using var provider = new InMemoryIsa95JobControlProvider();

            Isa95JobResponseReceiptV1 receive = await provider.ReceiveJobResponseAsync(
                Isa95TestData.V1Response("r1", "job1")).ConfigureAwait(false);
            Isa95JobResponseQueryV1 query = await provider.RequestJobResponseAsync(
                "job1",
                V1.ISA95JobOrderStateEnum.Undefined).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(receive.Result), Is.True);
            Assert.That(ServiceResult.IsGood(query.Result), Is.True);
            Assert.That(query.Responses.Count, Is.EqualTo(1));
            Assert.That(query.Responses[0].ID, Is.EqualTo("r1"));
            Assert.That(query.Responses[0].JobState, Is.EqualTo(V1.ISA95JobOrderStateEnum.Completed));
        }

        [Test]
        public async Task RequestJobResponseByStateOnlySelectorReturnsMatches()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobResponseAsync(
                Isa95TestData.V1Response("done", "job1", V1.ISA95JobOrderStateEnum.Completed)).ConfigureAwait(false);
            await provider.ReceiveJobResponseAsync(
                Isa95TestData.V1Response("running", "job2", V1.ISA95JobOrderStateEnum.Running)).ConfigureAwait(false);

            // State-only selector: no job order id, a specific state.
            Isa95JobResponseQueryV1 completed = await provider.RequestJobResponseAsync(
                jobOrderId: null,
                V1.ISA95JobOrderStateEnum.Completed).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(completed.Result), Is.True);
            Assert.That(completed.Responses.Count, Is.EqualTo(1));
            Assert.That(completed.Responses[0].ID, Is.EqualTo("done"));
        }

        [Test]
        public async Task RequestJobResponseWithBothSelectorsIsInvalidRequest()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobResponseAsync(
                Isa95TestData.V1Response("done", "job1", V1.ISA95JobOrderStateEnum.Completed)).ConfigureAwait(false);

            Isa95JobResponseQueryV1 query = await provider.RequestJobResponseAsync(
                "job1",
                V1.ISA95JobOrderStateEnum.Completed).ConfigureAwait(false);

            Assert.That(ServiceResult.IsUncertain(query.Result), Is.True);
            Assert.That(
                query.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.InvalidRequest));
            Assert.That(query.Responses.Count, Is.Zero);
        }

        [Test]
        public async Task RequestJobResponseWithNeitherSelectorIsInvalidRequest()
        {
            using var provider = new InMemoryIsa95JobControlProvider();

            Isa95JobResponseQueryV1 query = await provider.RequestJobResponseAsync(
                jobOrderId: null,
                V1.ISA95JobOrderStateEnum.Undefined).ConfigureAwait(false);

            Assert.That(ServiceResult.IsUncertain(query.Result), Is.True);
            Assert.That(
                query.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.InvalidRequest));
        }

        [Test]
        public async Task RequestJobResponseForUnknownOrderReturnsInvalidJobOrderId()
        {
            using var provider = new InMemoryIsa95JobControlProvider();

            Isa95JobResponseQueryV1 query = await provider.RequestJobResponseAsync(
                "missing",
                V1.ISA95JobOrderStateEnum.Undefined).ConfigureAwait(false);

            Assert.That(ServiceResult.IsUncertain(query.Result), Is.True);
            Assert.That(
                query.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.UnknownJobOrderId));
            Assert.That(query.Responses.Count, Is.Zero);
        }

        [Test]
        public async Task RequestJobResponseWithEmptyIdReturnsInvalidArgument()
        {
            using var provider = new InMemoryIsa95JobControlProvider();

            Isa95JobResponseQueryV1 query = await provider.RequestJobResponseAsync(
                string.Empty,
                V1.ISA95JobOrderStateEnum.Undefined).ConfigureAwait(false);

            Assert.That(ServiceResult.IsUncertain(query.Result), Is.True);
            Assert.That(
                query.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.InvalidRequest));
        }

        [Test]
        public async Task DuplicateJobResponseIsRejected()
        {
            using var provider = new InMemoryIsa95JobControlProvider();
            await provider.ReceiveJobResponseAsync(Isa95TestData.V1Response("r1", "job1")).ConfigureAwait(false);

            Isa95JobResponseReceiptV1 receipt = await provider.ReceiveJobResponseAsync(
                Isa95TestData.V1Response("r1", "job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsUncertain(receipt.Result), Is.True);
            Assert.That(
                receipt.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.InvalidRequest));
        }

        [Test]
        public async Task JobResponseWithEmptyIdIsRejected()
        {
            using var provider = new InMemoryIsa95JobControlProvider();

            Isa95JobResponseReceiptV1 receipt = await provider.ReceiveJobResponseAsync(
                Isa95TestData.V1Response(string.Empty, "job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsUncertain(receipt.Result), Is.True);
            Assert.That(
                receipt.ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.InvalidRequest));
        }

        [Test]
        public async Task JobResponseBeyondCapacityIsRejected()
        {
            using var provider = new InMemoryIsa95JobControlProvider(
                new Isa95JobControlProviderOptions { MaxJobResponses = 1 });
            await provider.ReceiveJobResponseAsync(Isa95TestData.V1Response("r1", "job1")).ConfigureAwait(false);

            Isa95JobResponseReceiptV1 receipt = await provider.ReceiveJobResponseAsync(
                Isa95TestData.V1Response("r2", "job1")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsUncertain(receipt.Result), Is.True);
        }

        [Test]
        public async Task ExpiredJobResponsesArePurgedByRetention()
        {
            var time = new FakeTimeProvider();
            using var provider = new InMemoryIsa95JobControlProvider(
                new Isa95JobControlProviderOptions
                {
                    ResponseRetention = System.TimeSpan.FromMinutes(1)
                },
                time);
            await provider.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Store,
                Isa95TestData.V1Order("job1")).ConfigureAwait(false);
            await provider.ReceiveJobResponseAsync(Isa95TestData.V1Response("old", "job1")).ConfigureAwait(false);

            time.Advance(System.TimeSpan.FromMinutes(2));
            Isa95JobResponseQueryV1 query = await provider.RequestJobResponseAsync(
                "job1",
                V1.ISA95JobOrderStateEnum.Undefined).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(query.Result), Is.True);
            Assert.That(query.Responses.Count, Is.Zero);
        }

        [Test]
        public void DisposedProviderThrowsOnUse()
        {
            var provider = new InMemoryIsa95JobControlProvider();
            provider.Dispose();

            Assert.That(
                async () => await provider.ReceiveJobOrderAsync(
                    V1.ISA95JobOrderCommandEnum.Store,
                    Isa95TestData.V1Order("job1")).ConfigureAwait(false),
                Throws.TypeOf<System.ObjectDisposedException>());
        }

        [Test]
        public void NullJobOrderThrows()
        {
            using var provider = new InMemoryIsa95JobControlProvider();

            Assert.That(
                async () => await provider.ReceiveJobOrderAsync(
                    V1.ISA95JobOrderCommandEnum.Store,
                    null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }
    }
}
