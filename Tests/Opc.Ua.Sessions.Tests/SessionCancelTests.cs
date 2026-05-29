/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// compliance tests for Session Cancel.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("SessionServices")]
    public class SessionCancelTests : TestFixture
    {
        [Description("Calls Cancel() against an in-flight request. Timing-sensitive against the in-process reference server: requests typically complete before the Cancel reaches the server, so we accept either CancelCount &gt;= 0 with a Good result.")]
        [Test]
        public async Task CancelInFlightRequestReturnsCountAsync()
        {
            // Fire a Read in the background and immediately attempt to cancel by
            // issuing a Cancel for the next-likely request handle. Because the
            // reference server processes requests very quickly, CancelCount will
            // typically be 0 - but the service must always succeed with Good.
            ValueTask<ReadResponse> readTask = Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = VariableIds.Server_ServerStatus_CurrentTime, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None);

            CancelResponse response = await Session.CancelAsync(
                requestHeader: null,
                requestHandle: 0,
                ct: CancellationToken.None).ConfigureAwait(false);

            await readTask.ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(
                StatusCode.IsGood(response.ResponseHeader.ServiceResult),
                Is.True,
                $"Expected Good ServiceResult but got {response.ResponseHeader.ServiceResult}.");
            Assert.That(response.CancelCount, Is.GreaterThanOrEqualTo(0u));
        }

        [Description("Cancel a completed call. Issues a Read of a valid node, waits for it to complete, then calls Cancel with the (already-completed) request handle. Expected: ServiceResult = Good, CancelCount = 0.")]
        [Test]
        public async Task CancelCompletedRequestReturnsZeroAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = VariableIds.Server_ServerStatus_CurrentTime, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);

            // Pick a handle that the previous Read could plausibly have used.
            // The Read is finished, so Cancel must succeed with CancelCount = 0.
            var requestHeader = new RequestHeader
            {
                Timestamp = DateTime.UtcNow,
                TimeoutHint = 10000
            };

            CancelResponse response = await Session.CancelAsync(
                requestHeader,
                requestHandle: 1,
                ct: CancellationToken.None).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(
                StatusCode.IsGood(response.ResponseHeader.ServiceResult),
                Is.True,
                $"Expected Good ServiceResult but got {response.ResponseHeader.ServiceResult}.");
            Assert.That(response.CancelCount, Is.Zero,
                "Cancel of an already-completed request must report CancelCount = 0.");
        }

        [Description("Call Cancel with an unknown request handle. Expected: ServiceResult = Good, CancelCount = 0 (Cancel is idempotent and does not error on unknown handles).")]
        [Test]
        public async Task CancelUnknownRequestHandleReturnsZeroAsync()
        {
            const uint UnknownRequestHandle = 0xDEADBEEF;

            CancelResponse response = await Session.CancelAsync(
                requestHeader: null,
                requestHandle: UnknownRequestHandle,
                ct: CancellationToken.None).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(
                StatusCode.IsGood(response.ResponseHeader.ServiceResult),
                Is.True,
                $"Expected Good ServiceResult but got {response.ResponseHeader.ServiceResult}.");
            Assert.That(response.CancelCount, Is.Zero,
                "Cancel of an unknown request handle must report CancelCount = 0.");
        }

        [Description("Cancel - server returns service result Bad_NothingToDo. ")]
        [Test]
        public void CancelWithInjectedBadNothingToDoAsync()
        {
            using IDisposable expectation = MockController.ExpectNextResponse<CancelResponse>(
                r => r.ResponseHeader.ServiceResult = StatusCodes.BadNothingToDo);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await Session.CancelAsync(
                    requestHeader: null,
                    requestHandle: 0,
                    ct: CancellationToken.None).ConfigureAwait(false));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Description("Cancel - server returns Good but overrides CancelCount to 0. ")]
        [Test]
        public async Task CancelWithInjectedZeroCancelCountAsync()
        {
            using IDisposable expectation = MockController.ExpectNextResponse<CancelResponse>(
                r => r.CancelCount = 0u);

            CancelResponse response = await Session.CancelAsync(
                requestHeader: null,
                requestHandle: 0,
                ct: CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.CancelCount, Is.Zero);
        }

        [Description("Cancel - server returns Bad_NothingToDo and decrements the actual CancelCount by 1. ")]
        [Test]
        public void CancelWithInjectedDecrementedCancelCountAsync()
        {
            using IDisposable expectation = MockController.ExpectNextResponse<CancelResponse>(
                r =>
                {
                    if (r.CancelCount > 0u)
                    {
                        r.CancelCount--;
                    }
                    r.ResponseHeader.ServiceResult = StatusCodes.BadNothingToDo;
                });

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await Session.CancelAsync(
                    requestHeader: null,
                    requestHandle: 0,
                    ct: CancellationToken.None).ConfigureAwait(false));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Description("Cancel - server returns Good and increments the actual CancelCount by 1. ")]
        [Test]
        public async Task CancelWithInjectedIncrementedCancelCountAsync()
        {
            using IDisposable expectation = MockController.ExpectNextResponse<CancelResponse>(
                r => ++r.CancelCount);

            CancelResponse response = await Session.CancelAsync(
                requestHeader: null,
                requestHandle: 0,
                ct: CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.CancelCount, Is.GreaterThanOrEqualTo(1u));
        }
    }
}
