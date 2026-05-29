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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Historian;

namespace Opc.Ua.Client.Tests.Historian
{
    /// <summary>
    /// Unit tests for <see cref="HistoryClient"/> that use a mocked
    /// <see cref="ISession"/> to exercise error paths that are difficult
    /// to trigger over the wire.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    public class HistoryClientUnitTests
    {
        [Test]
        public void ConstructorWithNullSessionThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new HistoryClient(null!));
        }

        [Test]
        public async Task ReadRawThrowsServiceResultExceptionOnBadStatusAsync()
        {
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.HistoryReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ExtensionObject>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                {
                    Results = new HistoryReadResult[]
                    {
                        new()
                        {
                            StatusCode = StatusCodes.BadHistoryOperationInvalid
                        }
                    }.ToArrayOf()
                }));

            var client = new HistoryClient(mockSession.Object);
            var nodeId = new NodeId("TestNode", 2);
            DateTime start = DateTime.UtcNow.AddHours(-1);
            DateTime end = DateTime.UtcNow;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await foreach (DataValue v in client.ReadRawAsync(nodeId, start, end).ConfigureAwait(false))
                {
                    // should not reach here
                }
            });

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationInvalid));
        }

        [Test]
        public async Task ReadRawWithEmptyResultsYieldsNothingAsync()
        {
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.HistoryReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ExtensionObject>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                {
                    Results = Array.Empty<HistoryReadResult>().ToArrayOf()
                }));

            var client = new HistoryClient(mockSession.Object);
            var values = new List<DataValue>();

            await foreach (DataValue v in client.ReadRawAsync(
                new NodeId("TestNode", 2),
                DateTime.UtcNow.AddHours(-1),
                DateTime.UtcNow).ConfigureAwait(false))
            {
                values.Add(v);
            }

            Assert.That(values, Is.Empty,
                "When HistoryRead returns an empty Results array the iterator should yield break.");
        }

        [Test]
        public async Task ReadRawWithThreeEmptyPagesAndContinuationThrowsBadInternalErrorAsync()
        {
            int callCount = 0;
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.HistoryReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ExtensionObject>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    callCount++;
                    return new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                    {
                        Results = new HistoryReadResult[]
                        {
                            new()
                            {
                                StatusCode = StatusCodes.Good,
                                ContinuationPoint = (ByteString)new byte[] { 0x01 }
                            }
                        }.ToArrayOf()
                    });
                });

            var client = new HistoryClient(mockSession.Object);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await foreach (DataValue v in client.ReadRawAsync(
                    new NodeId("TestNode", 2),
                    DateTime.UtcNow.AddHours(-1),
                    DateTime.UtcNow).ConfigureAwait(false))
                {
                    // should never yield
                }
            });

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
            Assert.That(callCount, Is.GreaterThanOrEqualTo(3),
                "The guard should trigger after three consecutive empty pages.");
        }

        [Test]
        public async Task PerformUpdateWithEmptyResultsReturnsEmptyListAsync()
        {
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.HistoryUpdateAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<ExtensionObject>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistoryUpdateResponse>(new HistoryUpdateResponse
                {
                    Results = Array.Empty<HistoryUpdateResult>().ToArrayOf()
                }));

            var client = new HistoryClient(mockSession.Object);
            DateTime now = DateTime.UtcNow;

            IList<StatusCode> result = await client.InsertAsync(
                new NodeId("TestNode", 2),
                new[]
                {
                    new DataValue(
                        new Variant(1.0),
                        StatusCodes.Good,
                        sourceTimestamp: now,
                        serverTimestamp: now)
                }).ConfigureAwait(false);

            Assert.That(result, Is.Empty,
                "When HistoryUpdate returns an empty Results array, InsertAsync should return an empty list.");
        }
    }
}
