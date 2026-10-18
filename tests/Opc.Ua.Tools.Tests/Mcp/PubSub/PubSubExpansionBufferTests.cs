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
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Exact buffer count, byte, quality, ordering and rate invariants.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionBufferTests
    {
        [Test]
        public async Task CountOverflowDropsOldestAndClearRemovesOnlyReturnedPageAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            var buffer = new McpPubSubDataSetBuffer(new McpPubSubOptions { MaxBufferedMessages = 2 },
                fixture.Remote.Context, fixture.Clock);
            await buffer.WriteAsync([Field("first")]).ConfigureAwait(false);
            await buffer.WriteAsync([Field("second")]).ConfigureAwait(false);
            await buffer.WriteAsync([Field("third")]).ConfigureAwait(false);

            JsonObject page = buffer.Read(1, clear: true);
            JsonObject rest = buffer.Read(2, clear: false);

            Assert.That(page["overflowDropped"]!.GetValue<long>(), Is.EqualTo(1));
            Assert.That(page["bufferedMessages"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(Value(page, fixture.Remote.Context), Is.EqualTo("second"));
            Assert.That(Value(rest, fixture.Remote.Context), Is.EqualTo("third"));
            Assert.That(rest["receivedDataSets"]!.GetValue<long>(), Is.EqualTo(3));
            Assert.That(rest["more"]!.GetValue<bool>(), Is.False);
        }

        [Test]
        public async Task ExactMessageByteCeilingAcceptsAndAdjacentCeilingRefusesAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            DataSetField field = Field(new string('x', 512));
            int bytes = McpPubSubDataSetBuffer.Encode([field], new McpPubSubOptions(), fixture.Remote.Context).Length;
            var exact = new McpPubSubDataSetBuffer(new McpPubSubOptions { MaxMessageBytes = bytes },
                fixture.Remote.Context, fixture.Clock);
            var below = new McpPubSubDataSetBuffer(new McpPubSubOptions { MaxMessageBytes = bytes - 1 },
                fixture.Remote.Context, fixture.Clock);

            await exact.WriteAsync([field]).ConfigureAwait(false);
            await below.WriteAsync([field]).ConfigureAwait(false);

            Assert.That(exact.Status()["bufferedBytes"]!.GetValue<int>(), Is.EqualTo(bytes));
            Assert.That(exact.Status()["receivedDataSets"]!.GetValue<long>(), Is.EqualTo(1));
            Assert.That(below.Status()["bufferedBytes"]!.GetValue<int>(), Is.Zero);
            Assert.That(below.Status()["invalidDropped"]!.GetValue<long>(), Is.EqualTo(1));
            Assert.That(below.Status()["receivedDataSets"]!.GetValue<long>(), Is.Zero);
        }

        [Test]
        public async Task AggregateBytesCanEvictBeforeMessageCountAndStatusSurvivesEncodingAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            DataSetField field = Field(new string('a', 512)) with { StatusCode = StatusCodes.UncertainInitialValue };
            int size = McpPubSubDataSetBuffer.Encode([field], new McpPubSubOptions(), fixture.Remote.Context).Length;
            var options = new McpPubSubOptions
            {
                MaxMessageBytes = size,
                MaxBufferedBytes = (size * 2) - 1,
                MaxBufferedMessages = 64
            };
            var buffer = new McpPubSubDataSetBuffer(options, fixture.Remote.Context, fixture.Clock);

            await buffer.WriteAsync([field]).ConfigureAwait(false);
            await buffer.WriteAsync([field]).ConfigureAwait(false);
            JsonObject result = buffer.Read(64, clear: true);
            using var decoder = new JsonDecoder(
                result["dataSets"]![0]!["fields"]!.ToJsonString(), fixture.Remote.Context);
            DataValue decoded = decoder.ReadDataValue("text");

            Assert.That(result["dataSets"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(result["overflowDropped"]!.GetValue<long>(), Is.EqualTo(1));
            Assert.That(result["bufferedBytes"]!.GetValue<int>(), Is.Zero);
            Assert.That(decoded.StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
            Assert.That(decoded.WrappedValue.TryGetValue(out string text), Is.True);
            Assert.That(text, Is.EqualTo(new string('a', 512)));
        }

        [Test]
        public async Task RateWindowIncludesExactBoundaryAndDoesNotExtendOnRefusalAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            var buffer = new McpPubSubDataSetBuffer(new McpPubSubOptions { MaxMessagesPerSecond = 1 },
                fixture.Remote.Context, fixture.Clock);
            await buffer.WriteAsync([Field("one")]).ConfigureAwait(false);
            fixture.Clock.Advance(TimeSpan.FromSeconds(1) - TimeSpan.FromTicks(1));
            await buffer.WriteAsync([Field("refused")]).ConfigureAwait(false);
            fixture.Clock.Advance(TimeSpan.FromTicks(1));
            await buffer.WriteAsync([Field("two")]).ConfigureAwait(false);

            JsonObject result = buffer.Read(64, clear: false);

            Assert.That(result["rateDropped"]!.GetValue<long>(), Is.EqualTo(1));
            Assert.That(result["receivedDataSets"]!.GetValue<long>(), Is.EqualTo(2));
            Assert.That(Value(result, fixture.Remote.Context), Is.EqualTo("one"));
            using var decoder = new JsonDecoder(
                result["dataSets"]![1]!["fields"]!.ToJsonString(), fixture.Remote.Context);
            Assert.That(decoder.ReadDataValue("text").WrappedValue.TryGetValue(out string value), Is.True);
            Assert.That(value, Is.EqualTo("two"));
        }

        private static DataSetField Field(string value)
        {
            return new DataSetField { Name = "text", Value = Variant.From(value) };
        }

        private static string Value(JsonObject result, IServiceMessageContext context)
        {
            using var decoder = new JsonDecoder(result["dataSets"]![0]!["fields"]!.ToJsonString(), context);
            DataValue value = decoder.ReadDataValue("text");
            Assert.That(value.WrappedValue.TryGetValue(out string text), Is.True);
            return text;
        }
    }
}
#endif
