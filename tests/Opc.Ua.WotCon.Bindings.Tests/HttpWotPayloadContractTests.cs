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
using System.Xml;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings.Http;
using Opc.Ua.WotCon.Bindings.Planners;
using Structure = Opc.Ua.Encoders.Structure;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    [TestFixture]
    public sealed class HttpWotPayloadContractTests
    {
        [TestCase("{")]
        [TestCase("")]
        [TestCase("{}")]
        [TestCase("""{"Minimum":-7}""")]
        [TestCase("""{"Minimum":-7,"Maximum":42,"Unexpected":1}""")]
        [TestCase("""{"Minimum":"wrong","Maximum":42}""")]
        [TestCase("""{"Minimum":-7,"Maximum":42,"Minimum":8}""")]
        [TestCase("[1,2]")]
        [TestCase("null")]
        public async Task InvalidNamedOutputFailsTheOperationWithoutPartialGoodValues(string response)
        {
            using var harness = new Harness(null, NamedSchema, response);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(result.Success, Is.False);
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public async Task WrongInputCountFailsBeforeSending(int count)
        {
            using var harness = new Harness(NamedSchema, null, string.Empty);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);
            Variant[] values = Enumerable.Repeat(new Variant(1L), count).ToArray();

            WotInvokeResult result = await channel.InvokeAsync(values).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(harness.SendCount, Is.Zero);
        }

        [TestCase(1)]
        [TestCase(3)]
        public async Task NamedContractsPreserveEveryPositionForSingletonAndMultipleArguments(int count)
        {
            string[] names = Enumerable.Range(0, count).Select(index => "Argument" + index).ToArray();
            string order = string.Join(",", Enumerable.Reverse(names).Select(name => "\"" + name + "\""));
            string properties = string.Join(",", names.Select(name => "\"" + name + "\":{\"type\":\"integer\"}"));
            string schema = "{\"type\":\"object\",\"uav:argumentLayout\":\"named\",\"uav:fieldOrder\":[" +
                order + "],\"properties\":{" + properties + "}}";
            string response = "{" + string.Join(",", names.Select((name, index) =>
                "\"" + name + "\":" + (100 + index))) + "}";
            using var harness = new Harness(schema, schema, response);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);
            Variant[] inputs = Enumerable.Range(0, count).Select(index => new Variant(-1L - index)).ToArray();

            WotInvokeResult result = await channel.InvokeAsync(inputs).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(result.Outputs, Has.Count.EqualTo(count));
            using System.Text.Json.JsonDocument body = System.Text.Json.JsonDocument.Parse(harness.Body.Memory);
            Assert.That(body.RootElement.EnumerateObject().Select(property => property.Name),
                Is.EqualTo(Enumerable.Reverse(names)));
            for (int index = 0; index < count; index++)
            {
                Assert.That(body.RootElement.GetProperty(names[count - 1 - index]).GetInt64(),
                    Is.EqualTo(-1L - index));
                Assert.That(result.Outputs[index].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(result.Outputs[index].WrappedValue.TryGetValue(out long value), Is.True);
                Assert.That(value, Is.EqualTo(100L + count - 1 - index));
            }
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [Test]
        public async Task AbsentInputRejectsUnexpectedValuesBeforeSending()
        {
            using var harness = new Harness(null, null, string.Empty);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([Variant.Null]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(harness.SendCount, Is.Zero);
        }

        [Test]
        public async Task AbsentContractsSendNoBodyAndReturnNoValues()
        {
            using var harness = new Harness(null, null, string.Empty);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(harness.Body.IsNull, Is.True);
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [TestCase("null")]
        [TestCase("1")]
        [TestCase("{}")]
        public async Task UndeclaredOutputCannotSilentlyIntroduceAResult(string response)
        {
            using var harness = new Harness(null, null, response);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ExplicitEmptyNamedContractsKeepTheirEmptyObjectPayload()
        {
            const string empty = """
                {"type":"object","uav:argumentLayout":"named","uav:fieldOrder":[],"properties":{}}
                """;
            using var harness = new Harness(empty, empty, "{}");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(Encoding.UTF8.GetString(harness.Body.Span), Is.EqualTo("{}"));
        }

        [TestCase("")]
        [TestCase("null")]
        [TestCase("[]")]
        [TestCase("""{"Unexpected":1}""")]
        public async Task ExplicitEmptyNamedOutputStillRequiresItsDeclaredObject(string response)
        {
            const string schema = """
                {"type":"object","uav:argumentLayout":"named","uav:fieldOrder":[],"properties":{}}
                """;
            using var harness = new Harness(null, schema, response);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ExplicitNullIsOneArgumentAndOneOutputRatherThanAbsence()
        {
            const string schema = """{"type":"null","uav:argumentLayout":"single"}""";
            using var harness = new Harness(schema, schema, "null");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([Variant.Null]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Outputs, Has.Count.EqualTo(1));
            Assert.That(result.Outputs[0].WrappedValue.IsNull, Is.True);
            Assert.That(Encoding.UTF8.GetString(harness.Body.Span), Is.EqualTo("null"));
        }

        [Test]
        public async Task ArrayContractRemainsOneTypedArgumentAndOutput()
        {
            const string schema = """{"type":"array","items":{"type":"integer"},"uav:valueRank":1}""";
            using var harness = new Harness(schema, schema, "[8,9]");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);
            ArrayOf<long> input = [-7, 42];

            WotInvokeResult result = await channel.InvokeAsync([new Variant(input)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Outputs, Has.Count.EqualTo(1));
            Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out ArrayOf<long> output), Is.True);
            ArrayOf<long> expected = [8, 9];
            Assert.That(output, Is.EqualTo(expected));
            Assert.That(Encoding.UTF8.GetString(harness.Body.Span), Is.EqualTo("[-7,42]"));
        }

        [TestCase(7, true)]
        [TestCase(8, true)]
        [TestCase(9, false)]
        public async Task RequestByteLimitIsExactAndFailureDoesNotSend(int bytes, bool accepted)
        {
            using var harness = new Harness(
                """{"type":"string"}""", null, string.Empty, new WotBindingBounds { MaxPayloadBytes = 8 });
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);
            string text = new('x', bytes - 2);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(text)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(accepted ? StatusCodes.Good : StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(harness.SendCount, Is.EqualTo(accepted ? 1 : 0));
            if (accepted)
            {
                Assert.That(harness.Body.Length, Is.EqualTo(bytes));
                Assert.That(Encoding.UTF8.GetString(harness.Body.Span), Is.EqualTo("\"" + text + "\""));
            }
        }

        [TestCase(7, true)]
        [TestCase(8, true)]
        [TestCase(9, false)]
        public async Task ResponseByteLimitIsExactAndCannotHideFailure(int bytes, bool accepted)
        {
            string text = new('x', bytes - 2);
            using var harness = new Harness(
                null, """{"type":"string"}""", "\"" + text + "\"", new WotBindingBounds { MaxPayloadBytes = 8 });
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(accepted ? StatusCodes.Good : StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(harness.SendCount, Is.EqualTo(1));
            Assert.That(result.Outputs, Has.Count.EqualTo(accepted ? 1 : 0));
            if (accepted)
            {
                Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out string? output), Is.True);
                Assert.That(output, Is.EqualTo(text));
            }
        }

        [Test]
        public async Task NonFiniteInputHasAnExplicitEncodingFailureBeforeSending()
        {
            using var harness = new Harness("""{"type":"number"}""", null, string.Empty);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(double.NaN)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadEncodingError));
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(harness.SendCount, Is.Zero);
        }

        [Test]
        public async Task ContextualInvocationKeepsPortableNodeIdsAndTheOutputContext()
        {
            const string schema = """{"type":"string","uav:dataTypeId":"i=17"}""";
            using var harness = new Harness(schema, schema, "\"nsu=urn:payload;s=returned\"");
            ServiceMessageContext source = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            source.NamespaceUris.Append("urn:unrelated");
            ushort index = source.NamespaceUris.GetIndexOrAppend("urn:payload");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);
            Assert.That(channel, Is.InstanceOf<IWotContextualBindingChannel>());

            WotInvokeResult result = await ((IWotContextualBindingChannel)channel).InvokeAsync(
                new WotInvokeRequest([new Variant(new NodeId("sent", index))], source)).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(Encoding.UTF8.GetString(harness.Body.Span), Is.EqualTo("\"nsu=urn:payload;s=sent\""));
            Assert.That(result.Outputs, Has.Count.EqualTo(1));
            Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out NodeId returned), Is.True);
            Assert.That(result.Context, Is.Not.Null);
            Assert.That(NodeId.ToExpandedNodeId(returned, result.Context!.NamespaceUris),
                Is.EqualTo(new ExpandedNodeId("returned", "urn:payload")));
            Assert.That(source.NamespaceUris.GetString(index), Is.EqualTo("urn:payload"));
        }

        [Test]
        public async Task ScopedDataTypeNameIsResolvedBeforeTheDocumentIsDisposed()
        {
            const string outputSchema = """
                {
                  "@context":{"native":"http://opcfoundation.org/UA/"},
                  "type":"integer","uav:dataTypeName":"native:UInt16"
                }
                """;
            using var harness = new Harness(null, outputSchema, "65535");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Outputs, Has.Count.EqualTo(1));
            Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out ushort output), Is.True);
            Assert.That(output, Is.EqualTo(ushort.MaxValue));
        }

        [TestCase("0", true)]
        [TestCase("65535", true)]
        [TestCase("-1", false)]
        [TestCase("65536", false)]
        [TestCase("1.5", false)]
        [TestCase("\"65535\"", false)]
        [TestCase("null", false)]
        public async Task DeclaredUInt16OutputEnforcesNativeRangeAndJsonKind(string response, bool accepted)
        {
            using var harness = new Harness(null, """{"type":"integer","uav:dataTypeId":"i=5"}""", response);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(accepted ? StatusCodes.Good : StatusCodes.BadDecodingError),
                result.Error);
            Assert.That(result.Outputs, Has.Count.EqualTo(accepted ? 1 : 0));
            Assert.That(harness.SendCount, Is.EqualTo(1));
            if (accepted)
            {
                Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out ushort value), Is.True);
                Assert.That(value, Is.EqualTo(response == "0" ? (ushort)0 : ushort.MaxValue));
            }
            else
            {
                Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        public async Task ReturnedNamespaceContextDoesNotMutateTheCaller()
        {
            ServiceMessageContext context = NewContext();
            int count = context.NamespaceUris.Count;
            using var harness = new Harness(null, """{"type":"string","uav:dataTypeId":"i=17"}""",
                "\"nsu=urn:returned-only;s=Output\"");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await ((IWotContextualBindingChannel)channel).InvokeAsync(
                new WotInvokeRequest([], context)).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(result.Context, Is.Not.Null.And.Not.SameAs(context));
            Assert.That(context.NamespaceUris.Count, Is.EqualTo(count));
            Assert.That(context.NamespaceUris.GetIndex("urn:returned-only"), Is.EqualTo(-1));
            Assert.That(result.Outputs, Has.Count.EqualTo(1));
            Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out NodeId output), Is.True);
            Assert.That(NodeId.ToExpandedNodeId(output, result.Context!.NamespaceUris),
                Is.EqualTo(new ExpandedNodeId("Output", "urn:returned-only")));
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [Test]
        public async Task SingleStructureRetainsNestedFieldsTypedArraysAndCustomTypeAsOneValue()
        {
            ServiceMessageContext context = NewContext();
            Structure nested = RegisterStructure(context, "Nested",
                [new StructureField { Name = "Value", DataType = Ua.DataTypeIds.Int64, ValueRank = ValueRanks.Scalar }],
                new Dictionary<string, BuiltInType> { ["Value"] = BuiltInType.Int64 });
            ushort ns = context.NamespaceUris.GetIndexOrAppend(PayloadNamespace);
            Structure root = RegisterStructure(context, "Sample",
                [
                    new StructureField
                    {
                        Name = "Count", DataType = Ua.DataTypeIds.Int32, ValueRank = ValueRanks.Scalar
                    },
                    new StructureField
                    {
                        Name = "Nested", DataType = new NodeId("Nested", ns), ValueRank = ValueRanks.Scalar
                    },
                    new StructureField
                    {
                        Name = "Ids", DataType = Ua.DataTypeIds.NodeId, ValueRank = ValueRanks.OneDimension
                    }
                ],
                new Dictionary<string, BuiltInType>
                {
                    ["Count"] = BuiltInType.Int32,
                    ["Nested"] = BuiltInType.Null,
                    ["Ids"] = BuiltInType.NodeId
                });
            nested["Value"] = new Variant(9000000000000L);
            root["Count"] = new Variant(7);
            root["Nested"] = new Variant(new ExtensionObject(nested));
            ArrayOf<NodeId> ids = [new NodeId("one", ns), new NodeId("two", ns)];
            root["Ids"] = new Variant(ids);
            const string schema = """
                {
                  "type":"object","uav:argumentLayout":"single","uav:dataTypeId":"nsu=urn:http-payload;s=Sample",
                  "properties":{
                    "Ids":{"type":"array","items":{"type":"string","uav:dataTypeId":"i=17"}},
                    "Nested":{"type":"object","uav:dataTypeId":"nsu=urn:http-payload;s=Nested",
                      "properties":{"Value":{"type":"integer"}}},
                    "Count":{"type":"integer"}
                  }
                }
                """;
            using var harness = new Harness(schema, schema,
                """{"Ids":["nsu=urn:http-payload;s=three"],"Nested":{"Value":-8},"Count":9}""");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);
            Assert.That(channel, Is.InstanceOf<IWotContextualBindingChannel>());

            WotInvokeResult result = await ((IWotContextualBindingChannel)channel).InvokeAsync(
                new WotInvokeRequest([new Variant(new ExtensionObject(root))], context)).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(harness.SendCount, Is.EqualTo(1));
            using System.Text.Json.JsonDocument body = System.Text.Json.JsonDocument.Parse(harness.Body.Memory);
            Assert.That(body.RootElement.ValueKind, Is.EqualTo(System.Text.Json.JsonValueKind.Object));
            Assert.That(body.RootElement.GetProperty("Count").GetInt32(), Is.EqualTo(7));
            Assert.That(body.RootElement.GetProperty("Nested").GetProperty("Value").GetInt64(),
                Is.EqualTo(9000000000000L));
            Assert.That(body.RootElement.GetProperty("Ids")[1].GetString(), Is.EqualTo("nsu=urn:http-payload;s=two"));
            Assert.That(body.RootElement.TryGetProperty("UaTypeId", out _), Is.False);
            Assert.That(result.Outputs, Has.Count.EqualTo(1));
            Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out ExtensionObject output), Is.True);
            Assert.That(output.TryGetValue(out IEncodeable? encodeable, result.Context), Is.True);
            Assert.That(encodeable!.TypeId, Is.EqualTo(new ExpandedNodeId("Sample", PayloadNamespace)));
            Assert.That(encodeable, Is.InstanceOf<IStructure>());
            var returned = (IStructure)encodeable;
            Assert.That(returned["Count"].TryGetValue(out int count), Is.True);
            Assert.That(count, Is.EqualTo(9));
            Assert.That(returned["Nested"].TryGetValue(out ExtensionObject child), Is.True);
            Assert.That(child.TryGetValue(out IEncodeable? childValue, result.Context), Is.True);
            Assert.That(((IStructure)childValue!)["Value"].TryGetValue(out long nestedValue), Is.True);
            Assert.That(nestedValue, Is.EqualTo(-8));
            Assert.That(returned["Ids"].TryGetValue(out ArrayOf<NodeId> returnedIds), Is.True);
            Assert.That(returnedIds, Has.Count.EqualTo(1));
            Assert.That(NodeId.ToExpandedNodeId(returnedIds[0], result.Context!.NamespaceUris),
                Is.EqualTo(new ExpandedNodeId("three", PayloadNamespace)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EmptyAndNullStructuresAreSingleValuesNotMissingArguments(bool nullValue)
        {
            ServiceMessageContext context = NewContext();
            Structure empty = RegisterStructure(context, "Empty", [], []);
            const string schema = """
                {"uav:argumentLayout":"single","uav:dataTypeId":"nsu=urn:http-payload;s=Empty"}
                """;
            using var harness = new Harness(schema, schema, nullValue ? "null" : "{}");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);
            Assert.That(channel, Is.InstanceOf<IWotContextualBindingChannel>());
            Variant input = new(nullValue ? ExtensionObject.Null : new ExtensionObject(empty));

            WotInvokeResult result = await ((IWotContextualBindingChannel)channel).InvokeAsync(
                new WotInvokeRequest([input], context)).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(result.Outputs, Has.Count.EqualTo(1));
            Assert.That(Encoding.UTF8.GetString(harness.Body.Span), Is.EqualTo(nullValue ? "null" : "{}"));
            Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out ExtensionObject output), Is.True);
            Assert.That(output.IsNull, Is.EqualTo(nullValue));
            if (!nullValue)
            {
                Assert.That(output.TryGetValue(out IEncodeable? value, result.Context), Is.True);
                Assert.That(value!.TypeId, Is.EqualTo(empty.TypeId));
                Assert.That(((IStructure)value).GetFields(), Is.Empty);
            }
        }

        [Test]
        public async Task StructureArrayPreservesElementTypesAndExplicitNulls()
        {
            ServiceMessageContext context = NewContext();
            Structure first = RegisterStructure(context, "Element",
                [new StructureField { Name = "Value", DataType = Ua.DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }],
                new Dictionary<string, BuiltInType> { ["Value"] = BuiltInType.Int32 });
            first["Value"] = new Variant(7);
            const string schema = """
                {"type":"array","uav:valueRank":1,"items":{
                  "type":["object","null"],"uav:dataTypeId":"nsu=urn:http-payload;s=Element",
                  "properties":{"Value":{"type":"integer"}}
                }}
                """;
            using var harness = new Harness(schema, schema, """[{"Value":11},null,{"Value":-5}]""");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);
            ArrayOf<ExtensionObject> input = [new ExtensionObject(first), ExtensionObject.Null];

            WotInvokeResult result = await ((IWotContextualBindingChannel)channel).InvokeAsync(
                new WotInvokeRequest([new Variant(input)], context)).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(Encoding.UTF8.GetString(harness.Body.Span), Is.EqualTo("""[{"Value":7},null]"""));
            Assert.That(result.Outputs, Has.Count.EqualTo(1));
            Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> output), Is.True);
            Assert.That(output, Has.Count.EqualTo(3));
            Assert.That(output[1].IsNull, Is.True);
            Assert.That(output[0].TryGetValue(out IEncodeable? firstValue, result.Context), Is.True);
            Assert.That(firstValue!.TypeId, Is.EqualTo(first.TypeId));
            Assert.That(((IStructure)firstValue)["Value"].TryGetValue(out int firstNumber), Is.True);
            Assert.That(firstNumber, Is.EqualTo(11));
            Assert.That(output[2].TryGetValue(out IEncodeable? lastValue, result.Context), Is.True);
            Assert.That(lastValue!.TypeId, Is.EqualTo(first.TypeId));
            Assert.That(((IStructure)lastValue)["Value"].TryGetValue(out int lastNumber), Is.True);
            Assert.That(lastNumber, Is.EqualTo(-5));
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task WrongStructureIdentityFailsBeforeSendingEvenWhenTheFieldsMatch(bool array)
        {
            ServiceMessageContext context = NewContext();
            ArrayOf<StructureField> fields =
                [new StructureField { Name = "Value", DataType = Ua.DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }];
            var fieldTypes = new Dictionary<string, BuiltInType> { ["Value"] = BuiltInType.Int32 };
            _ = RegisterStructure(context, "Declared", fields, fieldTypes);
            Structure wrong = RegisterStructure(context, "Wrong", fields, fieldTypes);
            wrong["Value"] = new Variant(7);
            const string item = """
                {"type":"object","uav:dataTypeId":"nsu=urn:http-payload;s=Declared","uav:argumentLayout":"single"}
                """;
            string schema = array ? "{\"type\":\"array\",\"uav:valueRank\":1,\"items\":" + item + "}" : item;
            using var harness = new Harness(schema, null, string.Empty);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);
            ArrayOf<ExtensionObject> wrongArray = [new ExtensionObject(wrong)];
            Variant input = array
                ? new Variant(wrongArray)
                : new Variant(new ExtensionObject(wrong));

            WotInvokeResult result = await ((IWotContextualBindingChannel)channel).InvokeAsync(
                new WotInvokeRequest([input], context)).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadEncodingError));
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(harness.SendCount, Is.Zero);
        }

        [TestCase("{}")]
        [TestCase("""{"Value":"wrong"}""")]
        [TestCase("""{"Value":1,"Extra":2}""")]
        [TestCase("\"{\\\"Value\\\":1}\"")]
        public async Task InvalidStructureOutputFailsInsteadOfReturningAnOpaqueExtensionObject(string response)
        {
            ServiceMessageContext context = NewContext();
            _ = RegisterStructure(context, "Number",
                [new StructureField { Name = "Value", DataType = Ua.DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }],
                new Dictionary<string, BuiltInType> { ["Value"] = BuiltInType.Int32 });
            const string schema = """
                {"type":"object","uav:dataTypeId":"nsu=urn:http-payload;s=Number","uav:argumentLayout":"single"}
                """;
            using var harness = new Harness(null, schema, response);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);
            Assert.That(channel, Is.InstanceOf<IWotContextualBindingChannel>());

            WotInvokeResult result = await ((IWotContextualBindingChannel)channel).InvokeAsync(
                new WotInvokeRequest([], context)).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [Test]
        public async Task UInt64ContractUsesJsonNumbersWithoutLosingPrecision()
        {
            const string schema = """{"type":"integer","uav:dataTypeId":"i=9"}""";
            using var harness = new Harness(schema, schema, "18446744073709551615");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(ulong.MaxValue)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(Encoding.UTF8.GetString(harness.Body.Span), Is.EqualTo("18446744073709551615"));
            Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out ulong output), Is.True);
            Assert.That(output, Is.EqualTo(ulong.MaxValue));
        }

        [Test]
        public async Task LocalizedTextStringContractDoesNotLeakTheUaJsonObjectEnvelope()
        {
            const string schema = """{"type":"string","uav:dataTypeId":"i=21"}""";
            using var harness = new Harness(schema, schema, "\"Recovered\"");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(new LocalizedText("Alarm"))])
                .ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(Encoding.UTF8.GetString(harness.Body.Span), Is.EqualTo("\"Alarm\""));
            Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out LocalizedText output), Is.True);
            Assert.That(output.Text, Is.EqualTo("Recovered"));
        }

        [TestCase(2, true)]
        [TestCase(3, true)]
        [TestCase(4, false)]
        public async Task RequestJsonDepthLimitIncludesTheRootAndRejectsBeforeSending(int depth, bool accepted)
        {
            (ServiceMessageContext context, Variant input, string schema, string json) = DeepStructure(depth);
            using var harness = new Harness(schema, null, string.Empty, new WotBindingBounds { MaxPayloadDepth = 3 });
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);
            Assert.That(channel, Is.InstanceOf<IWotContextualBindingChannel>());

            WotInvokeResult result = await ((IWotContextualBindingChannel)channel).InvokeAsync(
                new WotInvokeRequest([input], context)).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(accepted ? StatusCodes.Good : StatusCodes.BadEncodingLimitsExceeded),
                result.Error);
            Assert.That(harness.SendCount, Is.EqualTo(accepted ? 1 : 0));
            if (accepted)
            {
                Assert.That(Encoding.UTF8.GetString(harness.Body.Span), Is.EqualTo(json));
            }
        }

        [TestCase(2, true)]
        [TestCase(3, true)]
        [TestCase(4, false)]
        public async Task ResponseJsonDepthLimitIncludesTheRootAndFailsTheWholeOperation(int depth, bool accepted)
        {
            (ServiceMessageContext context, _, string schema, string json) = DeepStructure(depth);
            using var harness = new Harness(null, schema, json, new WotBindingBounds { MaxPayloadDepth = 3 });
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);
            Assert.That(channel, Is.InstanceOf<IWotContextualBindingChannel>());

            WotInvokeResult result = await ((IWotContextualBindingChannel)channel).InvokeAsync(
                new WotInvokeRequest([], context)).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(accepted ? StatusCodes.Good : StatusCodes.BadEncodingLimitsExceeded),
                result.Error);
            Assert.That(result.Outputs, Has.Count.EqualTo(accepted ? 1 : 0));
            Assert.That(harness.SendCount, Is.EqualTo(1));
            if (accepted)
            {
                Variant value = result.Outputs[0].WrappedValue;
                for (int index = 0; index < depth; index++)
                {
                    Assert.That(value.TryGetValue(out ExtensionObject extension), Is.True);
                    Assert.That(extension.TryGetValue(out IEncodeable? body, result.Context), Is.True);
                    value = ((IStructure)body!)[index == depth - 1 ? "Value" : "Child"];
                }
                Assert.That(value.TryGetValue(out int leaf), Is.True);
                Assert.That(leaf, Is.EqualTo(7));
            }
        }

        [Test]
        public async Task LegacyScalarCodecKeepsItsOwnWireAndValueMeaning()
        {
            var codec = new Mock<IWotPayloadCodec>(MockBehavior.Strict);
            codec.SetupGet(value => value.Id).Returns("legacy-test");
            codec.Setup(value => value.CanHandle("application/x-payload-test")).Returns(true);
            codec.Setup(value => value.Encode(It.Is<Variant>(input => input == new Variant("input")),
                    It.IsAny<WotPayloadDescriptor>()))
                .Returns(WotEncodeResult.Ok(Encoding.UTF8.GetBytes("legacy-wire")));
            codec.Setup(value => value.Decode(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<WotPayloadDescriptor>()))
                .Returns(WotDecodeResult.Ok(new Variant("semantic-output")));
            using var harness = new Harness(
                """{"type":"string"}""", """{"type":"string"}""", "legacy-response",
                codecs: new WotPayloadCodecRegistry().Register(codec.Object),
                contentType: "application/x-payload-test");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant("input")]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(Encoding.UTF8.GetString(harness.Body.Span), Is.EqualTo("legacy-wire"));
            Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out string? output), Is.True);
            Assert.That(output, Is.EqualTo("semantic-output"));
            Assert.That(harness.SendCount, Is.EqualTo(1));
            codec.Verify(value => value.Encode(It.IsAny<Variant>(), It.IsAny<WotPayloadDescriptor>()), Times.Once);
            codec.Verify(value => value.Decode(
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<WotPayloadDescriptor>()), Times.Once);
        }

        [Test]
        public async Task LegacyCodecRejectsNamedArgumentsBeforeCallingItsScalarEncoderOrSending()
        {
            var codec = new Mock<IWotPayloadCodec>(MockBehavior.Strict);
            codec.SetupGet(value => value.Id).Returns("legacy-test");
            codec.Setup(value => value.CanHandle("application/x-payload-test")).Returns(true);
            using var harness = new Harness(
                NamedSchema, null, string.Empty, codecs: new WotPayloadCodecRegistry().Register(codec.Object),
                contentType: "application/x-payload-test");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(-7L), new Variant(42L)])
                .ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(result.Error, Does.Contain("legacy codec"));
            Assert.That(harness.SendCount, Is.Zero);
            codec.Verify(value => value.Encode(It.IsAny<Variant>(), It.IsAny<WotPayloadDescriptor>()), Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LegacyCodecRejectsNullableArraysBeforeCallingItsScalarEncoderOrSending(bool outputOnly)
        {
            const string schema = """
                {"type":["array","null"],"uav:argumentLayout":"single","uav:dataTypeId":"i=8",
                 "uav:valueRank":1,"items":{"type":"integer"}}
                """;
            var codec = new Mock<IWotPayloadCodec>(MockBehavior.Strict);
            codec.SetupGet(value => value.Id).Returns("legacy-test");
            codec.Setup(value => value.CanHandle("application/x-payload-test")).Returns(true);
            using var harness = new Harness(outputOnly ? null : schema, outputOnly ? schema : null, string.Empty,
                codecs: new WotPayloadCodecRegistry().Register(codec.Object),
                contentType: "application/x-payload-test");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);
            ArrayOf<long> array = [1, 2];
            ArrayOf<Variant> inputs = outputOnly ? [] : [new Variant(array)];

            WotInvokeResult result = await channel.InvokeAsync(inputs.Span.ToArray()).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(harness.SendCount, Is.Zero);
            codec.Verify(value => value.Encode(It.IsAny<Variant>(), It.IsAny<WotPayloadDescriptor>()), Times.Never);
        }

        [Test]
        public async Task InteractionCodecReceivesAllArgumentsAndTheOriginalContextWithoutScalarAdaptation()
        {
            ServiceMessageContext context = NewContext();
            var codec = new Mock<IWotInteractionPayloadCodec>(MockBehavior.Strict);
            codec.SetupGet(value => value.Id).Returns("interaction-test");
            codec.Setup(value => value.CanHandle("application/x-payload-test")).Returns(true);
            WotInvokeRequest? received = null;
            codec.Setup(value => value.EncodeArguments(
                    It.IsAny<WotInvokeRequest>(), It.IsAny<WotPayloadDescriptor>(), It.IsAny<WotBindingBounds>()))
                .Callback<WotInvokeRequest, WotPayloadDescriptor, WotBindingBounds>(
                    (request, _, _) => received = request)
                .Returns(WotEncodeResult.Ok(Encoding.UTF8.GetBytes("both-native-arguments")));
            codec.Setup(value => value.DecodeArguments(
                    It.IsAny<ByteString>(), It.IsAny<WotPayloadDescriptor>(), context, It.IsAny<WotBindingBounds>()))
                .Returns(new WotInvokeResult(StatusCodes.Good,
                    [new DataValue(new Variant(-8L)), new DataValue(new Variant(43L))]).WithContext(context));
            using var harness = new Harness(
                NamedSchema, NamedSchema, "custom-output", codecs: new WotPayloadCodecRegistry().Register(codec.Object),
                contentType: "application/x-payload-test");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);
            Assert.That(channel, Is.InstanceOf<IWotContextualBindingChannel>());
            var request = new WotInvokeRequest([new Variant(-7L), new Variant(42L)], context);

            WotInvokeResult result = await ((IWotContextualBindingChannel)channel).InvokeAsync(request)
                .ConfigureAwait(false);

            Assert.That(received, Is.SameAs(request));
            Assert.That(received!.Inputs, Has.Count.EqualTo(2));
            Assert.That(received.Inputs[1], Is.EqualTo(new Variant(42L)));
            Assert.That(received.Context, Is.SameAs(context));
            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Outputs, Has.Count.EqualTo(2));
            Assert.That(result.Outputs[0].WrappedValue, Is.EqualTo(new Variant(-8L)));
            Assert.That(result.Outputs[1].WrappedValue, Is.EqualTo(new Variant(43L)));
            Assert.That(result.Context, Is.SameAs(context));
            Assert.That(Encoding.UTF8.GetString(harness.Body.Span), Is.EqualTo("both-native-arguments"));
            Assert.That(harness.SendCount, Is.EqualTo(1));
            codec.Verify(value => value.Encode(It.IsAny<Variant>(), It.IsAny<WotPayloadDescriptor>()), Times.Never);
            codec.Verify(value => value.Decode(
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<WotPayloadDescriptor>()), Times.Never);
        }

        [Test]
        public async Task InteractionCodecIsNotCalledWhenTheDeclaredInputCountIsWrong()
        {
            var codec = new Mock<IWotInteractionPayloadCodec>(MockBehavior.Strict);
            codec.SetupGet(value => value.Id).Returns("interaction-test");
            codec.Setup(value => value.CanHandle("application/x-payload-test")).Returns(true);
            using var harness = new Harness(NamedSchema, null, string.Empty,
                codecs: new WotPayloadCodecRegistry().Register(codec.Object),
                contentType: "application/x-payload-test");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(1L)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(harness.SendCount, Is.Zero);
            codec.Verify(value => value.EncodeArguments(
                It.IsAny<WotInvokeRequest>(), It.IsAny<WotPayloadDescriptor>(), It.IsAny<WotBindingBounds>()),
                Times.Never);
        }

        [Test]
        public async Task InteractionCodecCannotReturnNamespaceIndexesWithoutTheirContext()
        {
            var codec = new Mock<IWotInteractionPayloadCodec>(MockBehavior.Strict);
            codec.SetupGet(value => value.Id).Returns("interaction-test");
            codec.Setup(value => value.CanHandle("application/x-payload-test")).Returns(true);
            codec.Setup(value => value.EncodeArguments(
                    It.IsAny<WotInvokeRequest>(), It.IsAny<WotPayloadDescriptor>(), It.IsAny<WotBindingBounds>()))
                .Returns(WotEncodeResult.Ok(ReadOnlyMemory<byte>.Empty));
            codec.Setup(value => value.DecodeArguments(
                    It.IsAny<ByteString>(), It.IsAny<WotPayloadDescriptor>(),
                    It.IsAny<IServiceMessageContext>(), It.IsAny<WotBindingBounds>()))
                .Returns(new WotInvokeResult(StatusCodes.Good, [new DataValue(new Variant(new NodeId("Unknown", 2)))]));
            using var harness = new Harness(null, """{"type":"string","uav:dataTypeId":"i=17"}""", "custom-output",
                codecs: new WotPayloadCodecRegistry().Register(codec.Object),
                contentType: "application/x-payload-test");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(result.Error, Does.Contain("context"));
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [Test]
        public async Task PreCancelledInvocationDoesNotEncodeOrSend()
        {
            var codec = new Mock<IWotInteractionPayloadCodec>(MockBehavior.Strict);
            codec.SetupGet(value => value.Id).Returns("cancel-test");
            codec.Setup(value => value.CanHandle("application/x-payload-test")).Returns(true);
            using var harness = new Harness(null, null, string.Empty,
                codecs: new WotPayloadCodecRegistry().Register(codec.Object),
                contentType: "application/x-payload-test");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);

            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await channel.InvokeAsync([], cancellation.Token).ConfigureAwait(false));

            Assert.That(harness.SendCount, Is.Zero);
            codec.Verify(value => value.EncodeArguments(
                It.IsAny<WotInvokeRequest>(), It.IsAny<WotPayloadDescriptor>(), It.IsAny<WotBindingBounds>()),
                Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task InFlightActionCancellationOrTimeoutNeverDecodesOrRetries(bool timeout)
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var codec = new Mock<IWotInteractionPayloadCodec>(MockBehavior.Strict);
            codec.SetupGet(value => value.Id).Returns("cancel-test");
            codec.Setup(value => value.CanHandle("application/x-payload-test")).Returns(true);
            codec.Setup(value => value.EncodeArguments(
                    It.IsAny<WotInvokeRequest>(), It.IsAny<WotPayloadDescriptor>(), It.IsAny<WotBindingBounds>()))
                .Returns(WotEncodeResult.Ok(ReadOnlyMemory<byte>.Empty));
            using var harness = new Harness(null, null, string.Empty,
                new WotBindingBounds
                {
                    DefaultTimeout = timeout ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromMinutes(1)
                },
                new WotPayloadCodecRegistry().Register(codec.Object), "application/x-payload-test",
                responder: async (_, token) =>
                {
                    entered.TrySetResult(true);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                    throw new InvalidOperationException("The in-flight request must be cancelled.");
                });
            using var cancellation = new CancellationTokenSource();
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);
            Task<WotInvokeResult> invocation = channel.InvokeAsync([], cancellation.Token).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            if (timeout)
            {
                WotInvokeResult result = await invocation.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadTimeout));
                Assert.That(result.Outputs, Is.Empty);
                Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            }
            else
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                Assert.CatchAsync<OperationCanceledException>(async () =>
                    await invocation.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
                Assert.That(invocation.IsCanceled, Is.True);
            }

            Assert.That(harness.SendCount, Is.EqualTo(1));
            codec.Verify(value => value.EncodeArguments(
                It.IsAny<WotInvokeRequest>(), It.IsAny<WotPayloadDescriptor>(), It.IsAny<WotBindingBounds>()),
                Times.Once);
            codec.Verify(value => value.DecodeArguments(
                It.IsAny<ByteString>(), It.IsAny<WotPayloadDescriptor>(),
                It.IsAny<IServiceMessageContext>(), It.IsAny<WotBindingBounds>()), Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EmptyAndNullArraysRemainOneTypedOutput(bool nullArray)
        {
            const string schema = """
                {"type":["array","null"],"uav:argumentLayout":"single","uav:dataTypeId":"i=8",
                 "uav:valueRank":1,"items":{"type":"integer"}}
                """;
            using var harness = new Harness(null, schema, nullArray ? "null" : "[]");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(result.Outputs, Has.Count.EqualTo(1));
            Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out ArrayOf<long> output), Is.True);
            Assert.That(output.Count, Is.Zero);
            Assert.That(output.IsNull, Is.EqualTo(nullArray));
        }

        [Test]
        public async Task LegacyTextCodecKeepsAnEmptyScalarDistinctFromAbsentContracts()
        {
            using var harness = new Harness("""{"type":"string"}""", """{"type":"string"}""", string.Empty,
                contentType: "text/plain");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(string.Empty)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(harness.Body.IsNull, Is.False);
            Assert.That(harness.Body.Length, Is.Zero);
            Assert.That(result.Outputs, Has.Count.EqualTo(1));
            Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out string? output), Is.True);
            Assert.That(output, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CustomCodecCannotHideMissingOrBadOutputsInAGoodOperation(bool badValue)
        {
            var codec = new Mock<IWotInteractionPayloadCodec>(MockBehavior.Strict);
            codec.SetupGet(value => value.Id).Returns("bad-output-test");
            codec.Setup(value => value.CanHandle("application/x-payload-test")).Returns(true);
            codec.Setup(value => value.EncodeArguments(
                    It.IsAny<WotInvokeRequest>(), It.IsAny<WotPayloadDescriptor>(), It.IsAny<WotBindingBounds>()))
                .Returns(WotEncodeResult.Ok(ReadOnlyMemory<byte>.Empty));
            codec.Setup(value => value.DecodeArguments(
                    It.IsAny<ByteString>(), It.IsAny<WotPayloadDescriptor>(),
                    It.IsAny<IServiceMessageContext>(), It.IsAny<WotBindingBounds>()))
                .Returns(new WotInvokeResult(StatusCodes.Good,
                    badValue ? [DataValue.FromStatusCode(StatusCodes.BadOutOfRange)] : []));
            using var harness = new Harness(null, """{"type":"integer"}""", "custom-response",
                codecs: new WotPayloadCodecRegistry().Register(codec.Object),
                contentType: "application/x-payload-test");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(badValue ? StatusCodes.BadOutOfRange : StatusCodes.BadDecodingError));
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DirectAndDiActivationUseTheProvidedTypeAndNamespaceContext(bool dependencyInjection)
        {
            ServiceMessageContext context = NewContext();
            ushort ns = context.NamespaceUris.GetIndexOrAppend("urn:injected");
            string? body = null;
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected().Setup<Task<HttpResponseMessage>>(
                    "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, token) =>
                {
                    body = await request.Content!.ReadAsStringAsync(token).ConfigureAwait(false);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("\"nsu=urn:injected;s=Output\"", Encoding.UTF8, "application/json")
                    };
                });
            using var client = new HttpClient(handler.Object);
            var planner = new HttpBindingPlanner();
            var executor = new HttpWotBindingExecutor(new HttpWotBindingOptions
            {
                ClientFactory = () => client,
                CallerClientHandlesRedirectSafety = true
            });
            var services = new ServiceCollection();
            services.AddSingleton<IWotProtocolBinder>(planner);
            services.AddSingleton<IWotBindingExecutor>(executor);
            services.AddSingleton<IServiceMessageContext>(context);
            services.EnsureWotBinderRegistry();
            using ServiceProvider provider = services.BuildServiceProvider();
            WotProtocolBinderRegistry registry = dependencyInjection
                ? provider.GetRequiredService<WotProtocolBinderRegistry>()
                : new WotProtocolBinderRegistry([planner], [executor]) { MessageContext = context };
            const string td = """
                {"title":"Injected","actions":{"reference":{
                  "input":{"type":"string","uav:dataTypeId":"i=17"},
                  "output":{"type":"string","uav:dataTypeId":"i=17"},
                  "forms":[{"href":"https://injected.example/reference","op":"invokeaction"}]
                }}}
                """;
            WotCompiledForm form = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "injected", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td))).CompiledForms.Single();
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(form).ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(new NodeId("Input", ns))])
                .ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(body, Is.EqualTo("\"nsu=urn:injected;s=Input\""));
            Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out NodeId output), Is.True);
            Assert.That(NodeId.ToExpandedNodeId(output, result.Context!.NamespaceUris),
                Is.EqualTo(new ExpandedNodeId("Output", "urn:injected")));
            if (dependencyInjection)
            {
                Assert.That(provider.GetRequiredService<IWotContextualBindingChannelFactory>(), Is.SameAs(registry));
                Assert.That(provider.GetRequiredService<IWotBindingChannelFactory>(), Is.SameAs(registry));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DirectAndDiActivationUseTheRegisteredNativeStructureFactory(bool dependencyInjection)
        {
            ServiceMessageContext context = NewContext();
            Structure type = RegisterStructure(context, "Injected",
                [new StructureField { Name = "Value", DataType = Ua.DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }],
                new Dictionary<string, BuiltInType> { ["Value"] = BuiltInType.Int32 });
            int sends = 0;
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected().Setup<Task<HttpResponseMessage>>(
                    "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>((_, _) =>
                {
                    sends++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"Value":73}""", Encoding.UTF8, "application/json")
                    });
                });
            using var client = new HttpClient(handler.Object);
            var planner = new HttpBindingPlanner();
            var executor = new HttpWotBindingExecutor(new HttpWotBindingOptions
            {
                ClientFactory = () => client,
                CallerClientHandlesRedirectSafety = true
            });
            var services = new ServiceCollection();
            services.AddSingleton<IWotProtocolBinder>(planner);
            services.AddSingleton<IWotBindingExecutor>(executor);
            services.AddSingleton<IServiceMessageContext>(context);
            services.EnsureWotBinderRegistry();
            using ServiceProvider provider = services.BuildServiceProvider();
            WotProtocolBinderRegistry registry = dependencyInjection
                ? provider.GetRequiredService<WotProtocolBinderRegistry>()
                : new WotProtocolBinderRegistry([planner], [executor]) { MessageContext = context };
            const string td = """
                {"title":"Injected structure","actions":{"read":{
                  "output":{
                    "type":"object","uav:argumentLayout":"single","uav:dataTypeId":"nsu=urn:http-payload;s=Injected"
                  },
                  "forms":[{"href":"https://injected.example/structure","op":"invokeaction"}]
                }}}
                """;
            WotCompiledForm form = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "injected-structure", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)))
                .CompiledForms.Single();
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(form).ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(result.Outputs, Has.Count.EqualTo(1));
            Assert.That(result.Context, Is.Not.Null);
            Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out ExtensionObject output), Is.True);
            Assert.That(output.TryGetValue(out IEncodeable? value, result.Context), Is.True);
            Assert.That(value!.TypeId, Is.EqualTo(type.TypeId));
            Assert.That(value, Is.InstanceOf<IStructure>());
            Assert.That(((IStructure)value)["Value"].TryGetValue(out int number), Is.True);
            Assert.That(number, Is.EqualTo(73));
            Assert.That(sends, Is.EqualTo(1));
        }

        private static (ServiceMessageContext Context, Variant Value, string Schema, string Json) DeepStructure(
            int depth)
        {
            ServiceMessageContext context = NewContext();
            Structure current = RegisterStructure(context, "Leaf",
                [new StructureField { Name = "Value", DataType = Ua.DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }],
                new Dictionary<string, BuiltInType> { ["Value"] = BuiltInType.Int32 });
            current["Value"] = new Variant(7);
            string json = "{\"Value\":7}";
            for (int index = 1; index < depth; index++)
            {
                Structure parent = RegisterStructure(context, "Depth" + index,
                    [new StructureField
                    {
                        Name = "Child",
                        DataType = ExpandedNodeId.ToNodeId(current.TypeId, context.NamespaceUris),
                        ValueRank = ValueRanks.Scalar
                    }],
                    new Dictionary<string, BuiltInType> { ["Child"] = BuiltInType.Null });
                parent["Child"] = new Variant(new ExtensionObject(current));
                current = parent;
                json = "{\"Child\":" + json + "}";
            }
            string schema = "{\"type\":\"object\",\"uav:argumentLayout\":\"single\",\"uav:dataTypeId\":\"" +
                current.TypeId + "\"}";
            return (context, new Variant(new ExtensionObject(current)), schema, json);
        }

        private static ServiceMessageContext NewContext()
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            context.NamespaceUris.Append("urn:before-payload");
            return context;
        }

        private static Structure RegisterStructure(
            ServiceMessageContext context,
            string name,
            ArrayOf<StructureField> fields,
            Dictionary<string, BuiltInType> fieldTypes)
        {
            ushort ns = context.NamespaceUris.GetIndexOrAppend(PayloadNamespace);
            var structure = new Structure(
                new XmlQualifiedName(name, PayloadNamespace),
                new ExpandedNodeId(name, PayloadNamespace),
                new ExpandedNodeId(name + ".Binary", PayloadNamespace),
                new ExpandedNodeId(name + ".Xml", PayloadNamespace),
                new StructureDefinition
                {
                    BaseDataType = Ua.DataTypeIds.Structure,
                    DefaultEncodingId = new NodeId(name + ".Binary", ns),
                    StructureType = StructureType.Structure,
                    Fields = fields
                },
                fieldTypes);
            IEncodeableFactoryBuilder builder = context.Factory.Builder;
            builder.AddEncodeableType(structure);
            builder.Commit();
            return structure;
        }

        private sealed class Harness : IDisposable
        {
            public Harness(
                string? input,
                string? output,
                string response,
                WotBindingBounds? bounds = null,
                IWotCodecRegistry? codecs = null,
                string contentType = "application/json",
                Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? responder = null)
            {
                var handler = new Mock<HttpMessageHandler>();
                handler.Protected().Setup<Task<HttpResponseMessage>>(
                    "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                    .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                    {
                        SendCount++;
                        Body = request.Content is null ? default :
                            new ByteString(await request.Content.ReadAsByteArrayAsync(cancellationToken)
                                .ConfigureAwait(false));
                        if (responder is not null)
                        {
                            return await responder(request, cancellationToken).ConfigureAwait(false);
                        }
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(response, Encoding.UTF8, "application/json")
                        };
                    });
                m_client = new HttpClient(handler.Object);
                m_registry = new WotProtocolBinderRegistry(
                    [new HttpBindingPlanner()],
                    [new HttpWotBindingExecutor(new HttpWotBindingOptions
                    {
                        ClientFactory = () => m_client,
                        CallerClientHandlesRedirectSafety = true
                    })],
                    codecs: codecs, bounds: bounds);
                string inputMember = input is null ? string.Empty : "\"input\":" + input + ",";
                string outputMember = output is null ? string.Empty : "\"output\":" + output + ",";
                string td = $$"""
                    {
                      "@context":"https://www.w3.org/2022/wot/td/v1.1",
                      "title":"HTTP contract",
                      "actions":{"act":{
                        {{inputMember}}{{outputMember}}
                        "forms":[{
                          "href":"https://payloads.example/action","op":"invokeaction","contentType":"{{contentType}}"
                        }]
                      }
                      }
                    }
                    """;
                WotBindingPlan plan = m_registry.Prepare(WotBindingPlanRequest.FromDocument(
                    "payloads", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));
                m_form = plan.CompiledForms.Single();
            }

            public int SendCount { get; private set; }

            public ByteString Body { get; private set; }

            public ValueTask<IWotBindingChannel> OpenAsync()
            {
                return m_registry.OpenChannelAsync(m_form);
            }

            public void Dispose()
            {
                m_client.Dispose();
            }

            private readonly HttpClient m_client;
            private readonly WotProtocolBinderRegistry m_registry;
            private readonly WotCompiledForm m_form;
        }

        private const string NamedSchema = """
            {
              "type":"object","uav:argumentLayout":"named","uav:fieldOrder":["Minimum","Maximum"],
              "properties":{"Maximum":{"type":"integer"},"Minimum":{"type":"integer"}}
            }
            """;
        private const string PayloadNamespace = "urn:http-payload";
    }
}
