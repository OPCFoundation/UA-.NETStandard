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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings.Http;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    public sealed partial class HttpWotPayloadContractTests
    {
        [TestCase("i=10", "1e39", false)]
        [TestCase("i=11", "-1e309", false)]
        [TestCase("i=10", "[1,1e39]", true)]
        [TestCase("i=11", "[1,-1e309]", true)]
        public async Task EventNumericOverflowFailsWithoutPartialSelectedFields(
            string dataTypeId, string response, bool array)
        {
            string item = $$"""{"type":"number","uav:dataTypeId":"{{dataTypeId}}"}""";
            WotNotification notification = await ReadReviewedEventAsync(
                array ? ArraySchema(item) : item, response, NewContext()).ConfigureAwait(false);

            AssertRejectedEvent(notification, StatusCodes.BadDecodingError);
        }

        [TestCase("i=10", "NaN")]
        [TestCase("i=10", "Infinity")]
        [TestCase("i=11", "-Infinity")]
        public async Task DefaultEventCodecRetainsExplicitIeeeSpecialValues(string dataTypeId, string special)
        {
            string schema = $$"""{"type":"string","uav:dataTypeId":"{{dataTypeId}}"}""";
            WotNotification notification = await ReadReviewedEventAsync(
                schema, "\"" + special + "\"", NewContext()).ConfigureAwait(false);

            Assert.That(notification.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(notification.Data.TryGetValue(["Reading"], out DataValue reading), Is.True);
            double actual;
            if (dataTypeId == "i=10")
            {
                Assert.That(reading.WrappedValue.TryGetValue(out float single), Is.True);
                actual = single;
            }
            else
            {
                Assert.That(reading.WrappedValue.TryGetValue(out actual), Is.True);
            }
            if (special == "NaN")
            {
                Assert.That(double.IsNaN(actual), Is.True);
            }
            else
            {
                Assert.That(actual, Is.EqualTo(
                    special == "Infinity" ? double.PositiveInfinity : double.NegativeInfinity));
            }
            Assert.That(notification.EventFields["Reading"], Is.EqualTo(reading));
        }

        [TestCase("1e100", "1e101", false)]
        [TestCase("1e100", "1e100", true)]
        [TestCase("1e-100", "2e-100", false)]
        [TestCase("1e-100", "1e-100", true)]
        public async Task EventArrayElementsHonorExactNumericBounds(string maximum, string value, bool accepted)
        {
            string item = $$"""{"type":"number","uav:dataTypeId":"i=11","maximum":{{maximum}}}""";
            WotNotification notification = await ReadReviewedEventAsync(
                ArraySchema(item), "[" + value + "]", NewContext()).ConfigureAwait(false);

            if (accepted)
            {
                Assert.That(notification.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(notification.Data.TryGetValue(["Reading"], out DataValue reading), Is.True);
                Assert.That(reading.WrappedValue.TryGetValue(out ArrayOf<double> values), Is.True);
                Assert.That(values.Count, Is.EqualTo(1));
                Assert.That(values[0], Is.EqualTo(maximum == "1e100" ? 1e100 : 1e-100));
                Assert.That(notification.EventFields["Reading"], Is.EqualTo(reading));
                Assert.That(notification.Data.TryGetValue(["Prefix"], out DataValue prefix), Is.True);
                Assert.That(prefix.WrappedValue, Is.EqualTo(new Variant((ushort)12)));
            }
            else
            {
                AssertRejectedEvent(notification, StatusCodes.BadDecodingError);
            }
        }

        [TestCase("null", true, 0)]
        [TestCase("[]", true, 0)]
        [TestCase(/*lang=json,strict*/ "[{\"Value\":1}]", true, 1)]
        [TestCase(/*lang=json,strict*/ "[{\"Value\":1},{\"Value\":2}]", false, 0)]
        [TestCase(/*lang=json,strict*/ "[{\"Value\":\"invalid\"},{\"Value\":2}]", false, 0)]
        public async Task EventStructureArraysHonorTheNativeOuterLimit(string response, bool accepted, int count)
        {
            ServiceMessageContext context = NewContext();
            _ = RegisterReviewElement(context);
            context.MaxArrayLength = 1;
            WotNotification notification = await ReadReviewedEventAsync(
                ArraySchema(ReviewElementSchema, nullable: true, dataTypeId: "nsu=urn:http-payload;s=ReviewElement"),
                response, context).ConfigureAwait(false);

            if (accepted)
            {
                Assert.That(notification.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(notification.Data.TryGetValue(["Reading"], out DataValue reading), Is.True);
                Assert.That(reading.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> values), Is.True);
                Assert.That(values.IsNull, Is.EqualTo(response == "null"));
                Assert.That(values.Count, Is.EqualTo(count));
                if (count != 0)
                {
                    Assert.That(values[0].TypeId, Is.EqualTo(
                        new ExpandedNodeId("ReviewElement", PayloadNamespace)));
                }
                Assert.That(notification.EventFields["Reading"], Is.EqualTo(reading));
            }
            else
            {
                AssertRejectedEvent(notification, StatusCodes.BadEncodingLimitsExceeded);
            }
        }

        [TestCase("scalar", false)]
        [TestCase("scalar", true)]
        [TestCase("array", false)]
        [TestCase("array", true)]
        [TestCase("node", false)]
        [TestCase("node", true)]
        public async Task CustomEventCodecSelectedValuesMustMatchNativeContracts(string shape, bool invalid)
        {
            ServiceMessageContext context = NewContext();
            ushort ns = context.NamespaceUris.GetIndexOrAppend("urn:custom-event:review");
            ArrayOf<ushort> array = [12, 13];
            Variant value = shape switch
            {
                "scalar" => invalid ? new Variant("wrong kind") : new Variant((ushort)12),
                "array" => invalid ? new Variant((ushort)12) : new Variant(array),
                "node" => new Variant(new NodeId("Target", invalid ? (ushort)65000 : ns)),
                _ => throw new ArgumentOutOfRangeException(nameof(shape))
            };
            string item = shape == "node"
                ? /*lang=json,strict*/ """{"type":"string","uav:dataTypeId":"i=17"}"""
                : /*lang=json,strict*/ """{"type":"integer","uav:dataTypeId":"i=5"}""";
            string schema = shape == "array" ? ArraySchema(item) : item;
            var status = new StatusCode(StatusCodes.GoodClamped.Code | 0x0480u);
            var reading = new DataValue(value, status, new DateTimeUtc(2026, 9, 11), new DateTimeUtc(2026, 9, 11));
            var prefix = new DataValue(new Variant((ushort)12));
            var data = new WotEventDataBuilder();
            Assert.That(data.Add(["Prefix"], prefix), Is.True);
            Assert.That(data.Add(["Reading"], reading), Is.True);
            var fields = new Dictionary<string, DataValue>(StringComparer.Ordinal)
            {
                ["Prefix"] = prefix,
                ["Reading"] = reading
            };
            WotNotification expected = new WotNotification(
                new DataValue(new Variant("custom event semantics")), fields, data.Build()).WithContext(context);
            ByteString wire = new(Encoding.UTF8.GetBytes("custom:event-response"));
            var codec = new Mock<IWotInteractionPayloadCodec>(MockBehavior.Strict);
            codec.SetupGet(candidate => candidate.Id).Returns("event-native-review");
            codec.Setup(candidate => candidate.CanHandle("application/x-first-review")).Returns(true);
            codec.Setup(candidate => candidate.DecodeEvent(
                    wire, It.IsAny<WotPayloadDescriptor>(), It.IsAny<WotEventSelection>(),
                    context, It.IsAny<WotBindingBounds>()))
                .Returns(expected);
            codec.Setup(candidate => candidate.Decode(
                    It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<WotPayloadDescriptor>()))
                .Throws(new InvalidOperationException("No scalar or JSON fallback may decode this wire."));

            WotNotification result = await ReadReviewedEventAsync(
                schema, string.Empty, context, codec.Object).ConfigureAwait(false);

            if (invalid)
            {
                AssertRejectedEvent(
                    result, shape == "node" ? StatusCodes.BadNodeIdInvalid : StatusCodes.BadTypeMismatch);
            }
            else
            {
                Assert.That(result, Is.SameAs(expected));
                Assert.That(result.Value.WrappedValue, Is.EqualTo(new Variant("custom event semantics")));
                Assert.That(result.Data.TryGetValue(["Reading"], out DataValue actual), Is.True);
                Assert.That(actual.WrappedValue, Is.EqualTo(value));
                Assert.That(actual.StatusCode.Code, Is.EqualTo(status.Code));
                Assert.That(result.Context, Is.SameAs(context));
            }
            codec.Verify(candidate => candidate.DecodeEvent(
                wire, It.IsAny<WotPayloadDescriptor>(), It.IsAny<WotEventSelection>(),
                context, It.IsAny<WotBindingBounds>()), Times.Once);
            codec.Verify(candidate => candidate.Decode(
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<WotPayloadDescriptor>()), Times.Never);
        }

        private static async Task<WotNotification> ReadReviewedEventAsync(
            string schema, string response, IServiceMessageContext context, IWotPayloadCodec? codec = null)
        {
            string contentType = codec is null ? "application/json" : "application/x-first-review";
            string wire = codec is null ? $$"""{"Prefix":12,"Reading":{{response}}}""" : "custom:event-response";
            int sends = 0;
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected().Setup<Task<HttpResponseMessage>>(
                    "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns(() =>
                {
                    Interlocked.Increment(ref sends);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(wire, Encoding.UTF8, contentType)
                    });
                });
            using var client = new HttpClient(handler.Object);
            var retry = new Mock<IChannelReconnectPolicy>();
            retry.Setup(policy => policy.GetDelay(It.IsAny<int>())).Returns(TimeSpan.FromMilliseconds(-1));
            var registry = new WotProtocolBinderRegistry(
                [new HttpBindingPlanner()],
                [new HttpWotBindingExecutor(new HttpWotBindingOptions
                {
                    ClientFactory = () => client,
                    CallerClientHandlesRedirectSafety = true,
                    ObserveInterval = TimeSpan.FromHours(1),
                    RetryPolicy = retry.Object
                })], codecs: codec is null ? null : new WotPayloadCodecRegistry().Register(codec))
            {
                MessageContext = context
            };
            var selections = WotEventSelectionCatalog.Create(
                new Dictionary<string, ArrayOf<WotResolvedEventSelectClause>>(StringComparer.Ordinal)
                {
                    ["changed"] =
                    [
                        new WotResolvedEventSelectClause("i=2041", "Prefix"),
                        new WotResolvedEventSelectClause("i=2041", "Reading")
                    ]
                });
            string document = $$$$"""
                {"title":"Native event contracts","events":{"changed":{
                  "tm:ref":"urn:http-payload:review-event",
                  "data":{"type":"object","properties":{
                    "Prefix":{"type":"integer","uav:dataTypeId":"i=5"},"Reading":{{{{schema}}}}
                  }},
                  "forms":[{
                    "href":"https://payloads.example/event","op":"subscribeevent","contentType":"{{{{contentType}}}}"
                  }]
                }}}
                """;
            WotCompiledForm form = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "http-review", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(document), selections))
                .CompiledForms.Single();
            var arrived = new TaskCompletionSource<WotNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(form).ConfigureAwait(false);
            await using IWotSubscription subscription = await channel.SubscribeEventAsync(
                value => arrived.TrySetResult(value)).ConfigureAwait(false);
            WotNotification result = await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await subscription.DisposeAsync().ConfigureAwait(false);
            Assert.That(sends, Is.EqualTo(1));
            return result;
        }

        private static void AssertRejectedEvent(WotNotification notification, StatusCode status)
        {
            Assert.That(notification.Value.StatusCode, Is.EqualTo(status));
            Assert.That(notification.Data.Members, Is.Empty);
            Assert.That(notification.EventFields, Is.Empty);
        }
    }
}
