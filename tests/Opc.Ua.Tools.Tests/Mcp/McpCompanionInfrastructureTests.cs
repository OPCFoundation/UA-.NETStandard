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
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Proves shared companion bounds, cleanup and exact JSON/error contracts without a server.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class McpCompanionInfrastructureTests
    {
        /// <summary>
        /// Pages expose the requested slice and probe no more than one extra item.
        /// </summary>
        [TestCase(0, 2, 0, 1, 2, true, 3)]
        [TestCase(1, 2, 1, 2, 3, true, 4)]
        [TestCase(3, 2, 3, 4, -1, false, 6)]
        public async Task PagesReturnExactLiveSliceAndDisposeAsync(
            int offset, int limit, int first, int second, int next, bool more, int moves)
        {
            var source = new CountingSequence(5);
            JsonObject page = await McpCompanionTools.PageAsync(
                source, value => JsonValue.Create(value), offset, limit).ConfigureAwait(false);

            Assert.That(page["items"]!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(page["items"]![0]!.GetValue<int>(), Is.EqualTo(first));
            Assert.That(page["items"]![1]!.GetValue<int>(), Is.EqualTo(second));
            Assert.That(page["hasMore"]!.GetValue<bool>(), Is.EqualTo(more));
            Assert.That(page["nextOffset"]?.GetValue<int>(), Is.EqualTo(next < 0 ? null : (int?)next));
            Assert.That(page["consistency"]!.GetValue<string>(), Is.EqualTo("live"));
            Assert.That(source.Moves, Is.EqualTo(moves));
            Assert.That(source.Disposed, Is.True);
        }

        /// <summary>
        /// Empty and exhausted live sources do not fabricate continuation offsets.
        /// </summary>
        [TestCase(0, 0)]
        [TestCase(3, 5)]
        public async Task EmptyOrExhaustedPagesHaveNoContinuationAsync(int length, int offset)
        {
            var source = new CountingSequence(length);
            JsonObject page = await McpCompanionTools.PageAsync(
                source, value => JsonValue.Create(value), offset).ConfigureAwait(false);

            Assert.That(page["items"]!.AsArray(), Is.Empty);
            Assert.That(page["hasMore"]!.GetValue<bool>(), Is.False);
            Assert.That(page["nextOffset"], Is.Null);
            Assert.That(source.Disposed, Is.True);
        }

        /// <summary>
        /// Bounds are checked before allocating an enumerator.
        /// </summary>
        [TestCase(-1, 1)]
        [TestCase(1000001, 1)]
        [TestCase(0, 0)]
        [TestCase(0, 501)]
        public void PageBoundsRejectBeforeEnumeration(int offset, int count)
        {
            var source = new CountingSequence(2);
            Assert.That(async () => await McpCompanionTools.PageAsync(
                source, value => JsonValue.Create(value), offset, count).ConfigureAwait(false),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(source.Moves, Is.Zero);
        }

        /// <summary>
        /// The materialized page follows the same slice contract.
        /// </summary>
        [Test]
        public void MaterializedPagePreservesValuesAndContinuation()
        {
            ArrayOf<int> values = [10, 20, 30];
            JsonObject page = McpCompanionTools.Page(values, value => JsonValue.Create(value), 1, 1);
            Assert.That(page["items"]![0]!.GetValue<int>(), Is.EqualTo(20));
            Assert.That(page["nextOffset"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(page["hasMore"]!.GetValue<bool>(), Is.True);
        }

        /// <summary>
        /// The last allowed page size and offset are accepted, not rejected as out of range.
        /// </summary>
        [Test]
        public async Task PagesAcceptExactMaximumBoundsAsync()
        {
            JsonObject page = await McpCompanionTools.PageAsync(
                new CountingSequence(501), value => JsonValue.Create(value), maxResults: 500).ConfigureAwait(false);
            Assert.That(page["items"]!.AsArray(), Has.Count.EqualTo(500));
            Assert.That(page["items"]![499]!.GetValue<int>(), Is.EqualTo(499));
            Assert.That(page["hasMore"]!.GetValue<bool>(), Is.True);
            JsonObject exhausted = await McpCompanionTools.PageAsync(
                new CountingSequence(0), value => JsonValue.Create(value), offset: 1_000_000).ConfigureAwait(false);
            Assert.That(exhausted["items"]!.AsArray(), Is.Empty);
            Assert.That(exhausted["hasMore"]!.GetValue<bool>(), Is.False);
        }

        /// <summary>
        /// A bounded observation stops at its cap without consuming an extra event.
        /// </summary>
        [Test]
        public async Task ObservationStopsAtExactCapAndDisposesAsync()
        {
            var source = new CountingSequence(5);
            JsonObject result = await McpCompanionTools.ObserveAsync(
                _ => source, value => JsonValue.Create(value), maxItems: 2).ConfigureAwait(false);

            Assert.That(result["items"]!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(result["items"]![1]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(result["stoppedBy"]!.GetValue<string>(), Is.EqualTo("itemLimit"));
            Assert.That(result["complete"]!.GetValue<bool>(), Is.False);
            Assert.That(source.Moves, Is.EqualTo(2));
            Assert.That(source.Disposed, Is.True);
        }

        /// <summary>
        /// Natural completion is not mislabeled as timeout or truncation.
        /// </summary>
        [Test]
        public async Task ObservationReportsNaturalCompletionAsync()
        {
            var source = new CountingSequence(1);
            JsonObject result = await McpCompanionTools.ObserveAsync(
                _ => source, value => JsonValue.Create(value)).ConfigureAwait(false);

            Assert.That(result["items"]![0]!.GetValue<int>(), Is.Zero);
            Assert.That(result["stoppedBy"]!.GetValue<string>(), Is.EqualTo("sourceCompleted"));
            Assert.That(result["complete"]!.GetValue<bool>(), Is.True);
            Assert.That(source.Disposed, Is.True);
        }

        /// <summary>
        /// Only cancellation from the observation window is converted to a duration result.
        /// </summary>
        [Test]
        public async Task ObservationWindowIsExplicitAndDisposesAsync()
        {
            bool disposed = false;
            JsonObject result = await McpCompanionTools.ObserveAsync(
                token => WaitForCancellation(() => disposed = true, token),
                value => JsonValue.Create(value), durationMs: 1).ConfigureAwait(false);

            Assert.That(result["items"]!.AsArray(), Is.Empty);
            Assert.That(result["stoppedBy"]!.GetValue<string>(), Is.EqualTo("durationLimit"));
            Assert.That(disposed, Is.True);
        }

        /// <summary>
        /// Caller cancellation is never a successful partial observation.
        /// </summary>
        [Test]
        public void ObservationCallerCancellationPropagates()
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var source = new CountingSequence(3);
            Assert.That(async () => await McpCompanionTools.ObserveAsync(
                _ => source, value => JsonValue.Create(value), ct: cancelled.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(source.Disposed, Is.True);
        }

        /// <summary>
        /// Invalid duration and item limits never start an observation.
        /// </summary>
        [TestCase(0, 1)]
        [TestCase(-1, 1)]
        [TestCase(30001, 1)]
        [TestCase(1, 0)]
        [TestCase(1, -1)]
        [TestCase(1, 501)]
        public void ObservationRejectsBoundsBeforeStarting(int duration, int count)
        {
            bool started = false;
            Assert.That(async () => await McpCompanionTools.ObserveAsync(_ =>
            {
                started = true;
                return new CountingSequence(0);
            }, value => JsonValue.Create(value), duration, count).ConfigureAwait(false),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(started, Is.False);
        }

        /// <summary>
        /// An accepted maximum-duration observation can still complete immediately at its item cap.
        /// </summary>
        [Test]
        public async Task ObservationAcceptsExactMaximumBoundsAsync()
        {
            var source = new CountingSequence(501);
            JsonObject result = await McpCompanionTools.ObserveAsync(
                _ => source, value => JsonValue.Create(value), 30_000, 500).ConfigureAwait(false);
            Assert.That(result["items"]!.AsArray(), Has.Count.EqualTo(500));
            Assert.That(result["items"]![499]!.GetValue<int>(), Is.EqualTo(499));
            Assert.That(result["stoppedBy"]!.GetValue<string>(), Is.EqualTo("itemLimit"));
            Assert.That(source.Moves, Is.EqualTo(500));
            Assert.That(source.Disposed, Is.True);
        }

        /// <summary>
        /// Both parts of the original UA status and the error message survive.
        /// </summary>
        [Test]
        public async Task ExecutePreservesUaFailureAsync()
        {
            CallToolResult result = await McpCompanionTools.ExecuteAsync(
                _ => throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "locked by another session"))
                .ConfigureAwait(false);
            JsonElement json = result.StructuredContent!.Value;

            Assert.That(result.IsError, Is.True);
            Assert.That(json.GetProperty("statusCodeValue").GetUInt32(), Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(json.GetProperty("statusCode").GetString(), Does.Contain("BadUserAccessDenied"));
            Assert.That(json.GetProperty("message").GetString(), Does.Contain("locked by another session"));
            Assert.That(((TextContentBlock)result.Content[0]).Text, Is.EqualTo(json.GetRawText()));
        }

        /// <summary>
        /// A domain refusal does not become success merely because the service returned normally.
        /// </summary>
        [Test]
        public async Task ExecutePreservesDomainRefusalAsync()
        {
            CallToolResult result = await McpCompanionTools.ExecuteAsync(_ =>
                ValueTask.FromResult<JsonNode?>(new JsonObject
                {
                    ["error"] = true,
                    ["returnStatus"] = ulong.MaxValue,
                    ["message"] = "unknown job"
                })).ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            Assert.That(result.StructuredContent!.Value.GetProperty("returnStatus").GetUInt64(),
                Is.EqualTo(ulong.MaxValue));
            Assert.That(result.StructuredContent.Value.GetProperty("message").GetString(), Is.EqualTo("unknown job"));
        }

        /// <summary>
        /// Pre-cancelled invocations cannot issue a command.
        /// </summary>
        [Test]
        public void ExecutePreCancelledDoesNotInvokeCommand()
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            bool invoked = false;
            Assert.That(() => McpCompanionTools.ExecuteAsync(_ =>
            {
                invoked = true;
                return ValueTask.FromResult<JsonNode?>(null);
            }, cancelled.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(invoked, Is.False);
        }

        /// <summary>
        /// Cancellation arriving during an operation is not reported as success if the callee ignores it.
        /// </summary>
        [Test]
        public void ExecutePropagatesCancellationAfterOperation()
        {
            using var cancellation = new CancellationTokenSource();
            Assert.That(() => McpCompanionTools.ExecuteAsync(_ =>
            {
                cancellation.Cancel();
                return ValueTask.FromResult<JsonNode?>(new JsonObject { ["completed"] = true });
            }, cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        }

        /// <summary>
        /// Raw false, zero and null data are not mistaken for errors or omitted payloads.
        /// </summary>
        [TestCase("false")]
        [TestCase("0")]
        [TestCase("null")]
        public void ResultsPreserveFalsyPayloads(string raw)
        {
            CallToolResult result = McpCompanionTools.Result(JsonNode.Parse(raw));
            Assert.That(result.IsError, Is.False);
            Assert.That(result.StructuredContent!.Value.GetProperty("data").GetRawText(), Is.EqualTo(raw));
        }

        /// <summary>
        /// The closed array converter accepts a standard array, not the ArrayOf memory representation.
        /// </summary>
        [Test]
        public void StringArrayConverterPreservesValues()
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes("[\"i=1\",\"ns=2;s=a\"]"));
            Assert.That(reader.Read(), Is.True);
            var converter = new McpStringArrayJsonConverter();
            ArrayOf<string> result = converter.Read(ref reader, typeof(ArrayOf<string>), new JsonSerializerOptions());
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo("i=1"));
            Assert.That(result[1], Is.EqualTo("ns=2;s=a"));
        }

        /// <summary>
        /// Invalid shapes cannot bind to an empty success-shaped predecessor list.
        /// </summary>
        [TestCase("{}")]
        [TestCase("[null]")]
        [TestCase("[1]")]
        public void StringArrayConverterRejectsInvalidItems(string json)
        {
            Assert.That(() =>
            {
                var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
                reader.Read();
                new McpStringArrayJsonConverter().Read(
                    ref reader, typeof(ArrayOf<string>), new JsonSerializerOptions());
            }, Throws.TypeOf<JsonException>());
        }

        /// <summary>
        /// The converter permits exactly 500 strings and rejects the first excess element.
        /// </summary>
        [Test]
        public void StringArrayConverterEnforcesExactCapacity()
        {
            string entries = string.Join(",", System.Linq.Enumerable.Repeat("\"i=1\"", 500));
            ArrayOf<string> accepted = ReadStringArray("[" + entries + "]");
            Assert.That(accepted.Count, Is.EqualTo(500));
            Assert.That(accepted[499], Is.EqualTo("i=1"));
            Assert.That(() => ReadStringArray("[" + entries + ",\"i=2\"]"), Throws.TypeOf<JsonException>());
            Assert.That(ReadStringArray("null").IsNull, Is.True);
            Assert.That(ReadStringArray("[]").IsNull, Is.False);
            Assert.That(ReadStringArray("[]").Count, Is.Zero);
        }

        /// <summary>
        /// Non-finite outputs retain the correct sign and classification.
        /// </summary>
        [TestCase(double.NaN, "NaN")]
        [TestCase(double.PositiveInfinity, "Infinity")]
        [TestCase(double.NegativeInfinity, "-Infinity")]
        public void NumberProjectionPreservesNonFiniteClassification(double value, string expected)
        {
            Assert.That(McpCompanionJson.Number(value).GetValue<string>(), Is.EqualTo(expected));
            Assert.That(McpCompanionJson.Number(1.25).GetValue<double>(), Is.EqualTo(1.25));
        }

        /// <summary>
        /// Invokes the public converter without reflection-based serialization.
        /// </summary>
        private static ArrayOf<string> ReadStringArray(string json)
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
            if (!reader.Read())
            {
                throw new JsonException("Missing array.");
            }
            return new McpStringArrayJsonConverter().Read(
                ref reader, typeof(ArrayOf<string>), new JsonSerializerOptions());
        }

        /// <summary>
        /// A cancellation-driven stream avoids timing or polling assertions.
        /// </summary>
        private static async IAsyncEnumerable<int> WaitForCancellation(
            Action disposed,
            [EnumeratorCancellation] CancellationToken ct)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
                yield break;
            }
            finally
            {
                disposed();
            }
        }

        /// <summary>
        /// Records iteration and disposal without timing or I/O.
        /// </summary>
        private sealed class CountingSequence(int count) : IAsyncEnumerable<int>, IAsyncEnumerator<int>
        {
            public int Current { get; private set; } = -1;

            public int Moves { get; private set; }

            public bool Disposed { get; private set; }

            public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                m_token = cancellationToken;
                return this;
            }

            public ValueTask<bool> MoveNextAsync()
            {
                Moves++;
                m_token.ThrowIfCancellationRequested();
                return ValueTask.FromResult(++Current < count);
            }

            public ValueTask DisposeAsync()
            {
                Disposed = true;
                return ValueTask.CompletedTask;
            }

            private CancellationToken m_token;
        }
    }
}
#endif
