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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Historian;
using Opc.Ua.Client.Tests.SessionContexts;

namespace Opc.Ua.Client.Tests.Historian
{
    /// <summary>
    /// Verifies decoded-page bounds before values are copied or exposed.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class HistoryClientPageLimitTests
    {
        /// <summary>
        /// Zero keeps the legacy limit; an exact limit accepts all values, including empty pages.
        /// </summary>
        [TestCase(0, 0, 3)]
        [TestCase(0, 3, 3)]
        [TestCase(0, 1, 0)]
        [TestCase(1, 0, 3)]
        [TestCase(1, 3, 3)]
        [TestCase(1, 1, 0)]
        [TestCase(2, 0, 3)]
        [TestCase(2, 3, 3)]
        [TestCase(2, 1, 0)]
        public async Task DecodedPageLimitAcceptsZeroExactAndEmpty(int kind, int limit, int count)
        {
            await using var fixture = new SessionRequestContextTestContext();
            fixture.Handler = (_, _) => new ValueTask<IServiceResponse>(Page(kind, count));
            var client = new HistoryClient(fixture.ManagedSession, new HistoryClientOptions
            {
                MaxDecodedPageItems = limit
            });
            List<int> values = await CollectAsync(ReadAsync(client, kind)).ConfigureAwait(false);
            Assert.That(values, Is.EqualTo(Enumerable.Range(0, count)));
            Assert.That(fixture.Requests, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// The first excess item rejects the entire page before yielding and releases its point.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task DecodedPageLimitRejectsExcessBeforeYieldAndReleasesPoint(int kind)
        {
            await using var fixture = new SessionRequestContextTestContext();
            var point = ByteString.From("owned"u8);
            fixture.Handler = (request, _) =>
            {
                var history = (HistoryReadRequest)request;
                return new ValueTask<IServiceResponse>(history.ReleaseContinuationPoints
                    ? new HistoryReadResponse { Results = [new HistoryReadResult()] }
                    : Page(kind, 3, point));
            };
            var client = new HistoryClient(fixture.ManagedSession, new HistoryClientOptions
            {
                MaxDecodedPageItems = 2
            });
            int yielded = 0;
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await foreach (int value in ReadAsync(client, kind).ConfigureAwait(false))
                {
                    yielded += value + 1;
                }
            })!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(yielded, Is.Zero);
            Assert.That(fixture.Requests, Has.Count.EqualTo(2));
            var release = (HistoryReadRequest)fixture.Requests[1];
            Assert.That(release.ReleaseContinuationPoints, Is.True);
            Assert.That(release.NodesToRead[0].ContinuationPoint, Is.EqualTo(point));
        }

        /// <summary>
        /// Oversized modified arrays are rejected before a destination array or metadata traversal.
        /// </summary>
        [Test]
        public async Task ModifiedLimitPrecedesCopyAndMalformedMetadataTraversal()
        {
            await using var fixture = new SessionRequestContextTestContext();
            fixture.Handler = static (_, _) => new ValueTask<IServiceResponse>(new HistoryReadResponse
            {
                Results =
                [
                    new HistoryReadResult
                    {
                        HistoryData = new ExtensionObject(new HistoryModifiedData
                        {
                            DataValues = [new DataValue(Variant.From(1)), new DataValue(Variant.From(2))],
                            ModificationInfos = [null!, null!]
                        })
                    }
                ]
            });
            var client = new HistoryClient(fixture.ManagedSession, new HistoryClientOptions
            {
                MaxDecodedPageItems = 1
            });
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await CollectAsync(ReadAsync(client, 1)).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// The modification-info array has its own count check even when the values array is small.
        /// </summary>
        [Test]
        public async Task ModifiedLimitBoundsBothIncomingArrays()
        {
            await using var fixture = new SessionRequestContextTestContext();
            fixture.Handler = static (_, _) => new ValueTask<IServiceResponse>(new HistoryReadResponse
            {
                Results =
                [
                    new HistoryReadResult
                    {
                        HistoryData = new ExtensionObject(new HistoryModifiedData
                        {
                            DataValues = [],
                            ModificationInfos = [new ModificationInfo(), new ModificationInfo()]
                        })
                    }
                ]
            });
            var client = new HistoryClient(fixture.ManagedSession, new HistoryClientOptions
            {
                MaxDecodedPageItems = 1
            });
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await CollectAsync(ReadAsync(client, 1)).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// A server refusal takes precedence over the untrusted payload's size.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task DecodedPageLimitPreservesServiceAndNodeFailures(bool serviceFailure)
        {
            await using var fixture = new SessionRequestContextTestContext();
            fixture.Handler = (_, _) =>
            {
                HistoryReadResponse response = Page(0, 3);
                response.ResponseHeader = new ResponseHeader
                {
                    ServiceResult = serviceFailure ? StatusCodes.BadUserAccessDenied : StatusCodes.Good
                };
                response.Results[0].StatusCode = serviceFailure
                    ? StatusCodes.Good : StatusCodes.BadHistoryOperationUnsupported;
                return new ValueTask<IServiceResponse>(response);
            };
            var client = new HistoryClient(fixture.ManagedSession, new HistoryClientOptions
            {
                MaxDecodedPageItems = 1
            });
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await CollectAsync(ReadAsync(client, 0)).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(serviceFailure
                ? StatusCodes.BadUserAccessDenied : StatusCodes.BadHistoryOperationUnsupported));
        }

        /// <summary>
        /// Invalid limits fail before the first service request.
        /// </summary>
        [Test]
        public void NegativeDecodedPageLimitIsRejected()
        {
            var session = new Mock<ISession>();
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new HistoryClient(session.Object, new HistoryClientOptions { MaxDecodedPageItems = -1 }));
            session.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Creates one representative page in each of the three Part 11 wire payload shapes.
        /// </summary>
        private static HistoryReadResponse Page(int kind, int count, ByteString point = default)
        {
            DataValue[] values = [.. Enumerable.Range(0, count).Select(i => new DataValue(Variant.From(i)))];
            ExtensionObject payload = kind switch
            {
                0 => new ExtensionObject(new HistoryData { DataValues = values }),
                1 => new ExtensionObject(new HistoryModifiedData
                {
                    DataValues = values,
                    ModificationInfos = Enumerable.Range(0, count).Select(_ => new ModificationInfo()).ToArray()
                }),
                _ => new ExtensionObject(new HistoryEvent
                {
                    Events = Enumerable.Range(0, count)
                        .Select(i => new HistoryEventFieldList { EventFields = [Variant.From(i)] }).ToArray()
                })
            };
            return new HistoryReadResponse
            {
                Results = [new HistoryReadResult { HistoryData = payload, ContinuationPoint = point }]
            };
        }

        /// <summary>
        /// Reads typed values without buffering inside the assertion loop.
        /// </summary>
        private static async IAsyncEnumerable<int> ReadAsync(HistoryClient client, int kind)
        {
            NodeId node = new(20u, 1);
            DateTime start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            if (kind == 1)
            {
                await foreach (ModifiedHistoryValue value in client.ReadModifiedAsync(
                    node, start, start.AddHours(1)).ConfigureAwait(false))
                {
                    Assert.That(value.Value.WrappedValue.TryGetValue(out int number), Is.True);
                    yield return number;
                }
            }
            else if (kind == 2)
            {
                var filter = new EventFilter { SelectClauses = [new SimpleAttributeOperand()] };
                await foreach (HistoryEventFieldList value in client.ReadEventsAsync(
                    node, start, start.AddHours(1), filter).ConfigureAwait(false))
                {
                    Assert.That(value.EventFields[0].TryGetValue(out int number), Is.True);
                    yield return number;
                }
            }
            else
            {
                await foreach (DataValue value in client.ReadRawAsync(
                    node, start, start.AddHours(1)).ConfigureAwait(false))
                {
                    Assert.That(value.WrappedValue.TryGetValue(out int number), Is.True);
                    yield return number;
                }
            }
        }

        /// <summary>
        /// Collects the small deterministic expected result.
        /// </summary>
        private static async Task<List<int>> CollectAsync(IAsyncEnumerable<int> values)
        {
            var result = new List<int>();
            await foreach (int value in values.ConfigureAwait(false))
            {
                result.Add(value);
            }
            return result;
        }
    }
}
