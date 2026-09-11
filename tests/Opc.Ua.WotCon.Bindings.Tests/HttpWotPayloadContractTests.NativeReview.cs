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
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Structure = Opc.Ua.Encoders.Structure;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    public sealed partial class HttpWotPayloadContractTests
    {
        [TestCase("i=5", -1, "UInt16", true)]
        [TestCase("i=5", -1, "String", false)]
        [TestCase("i=5", 1, "UInt16Array", true)]
        [TestCase("i=5", 1, "UInt16", false)]
        [TestCase("i=5", 1, "Matrix", false)]
        [TestCase("i=5", 2, "Matrix", true)]
        [TestCase("i=5", 0, "Matrix", true)]
        [TestCase("i=5", 0, "UInt16", false)]
        [TestCase("i=5", -2, "UInt16", true)]
        [TestCase("i=5", -2, "UInt16Array", true)]
        [TestCase("i=5", -2, "Matrix", true)]
        [TestCase("i=5", -3, "UInt16", true)]
        [TestCase("i=5", -3, "UInt16Array", true)]
        [TestCase("i=5", -3, "Matrix", false)]
        [TestCase("i=26", -1, "Double", true)]
        [TestCase("i=26", -1, "UInt64", true)]
        [TestCase("i=26", -1, "Int32", true)]
        [TestCase("i=26", -1, "String", false)]
        [TestCase("i=26", 1, "NumericVariants", true)]
        [TestCase("i=27", -1, "Int32", true)]
        [TestCase("i=27", -1, "UInt16", false)]
        [TestCase("i=27", -1, "Double", false)]
        [TestCase("i=28", -1, "UInt64", true)]
        [TestCase("i=28", -1, "Int32", false)]
        [TestCase("i=24", -1, "String", true)]
        [TestCase("i=24", 1, "NestedVariants", true)]
        [TestCase("i=29", -1, "Int32", true)]
        [TestCase("i=29", -1, "UInt16", false)]
        [TestCase("i=290", -1, "Double", true)]
        [TestCase("i=3", 1, "ByteString", true)]
        [TestCase("i=5", 1, "NullArray", true)]
        [TestCase("i=5", -1, "Null", false)]
        public async Task CustomCodecOutputsRespectUaNativeTypeAndRankSemantics(
            string dataTypeId, int rank, string valueKind, bool accepted)
        {
            ArrayOf<ushort> array = [12, 13];
            MatrixOf<ushort> matrix = new ushort[,] { { 12, 13 }, { 14, 15 } };
            ArrayOf<Variant> numericVariants = [new Variant(-1), new Variant(ulong.MaxValue)];
            ArrayOf<Variant> nestedVariants = [new Variant(array), new Variant("native nested array")];
            Variant value = valueKind switch
            {
                "UInt16" => new Variant((ushort)12),
                "String" => new Variant("not a UInt16"),
                "UInt16Array" => new Variant(array),
                "Matrix" => new Variant(matrix),
                "Int32" => new Variant(-12),
                "UInt64" => new Variant(ulong.MaxValue),
                "Double" => new Variant(1.25),
                "NumericVariants" => new Variant(numericVariants),
                "NestedVariants" => new Variant(nestedVariants),
                "ByteString" => new Variant(new ByteString(new byte[] { 12, 13 })),
                "NullArray" => new Variant(ArrayOf<ushort>.Null),
                "Null" => Variant.Null,
                _ => throw new ArgumentOutOfRangeException(nameof(valueKind))
            };
            ServiceMessageContext context = NewContext();
            WotInvokeResult supplied = NativeCodecResult(value, context);
            WotInvokeResult result = await InvokeReviewedCodecAsync(
                NativeSchema(dataTypeId, rank), supplied, context).ConfigureAwait(false);

            if (accepted)
            {
                AssertPreservedCodecOutput(result, supplied);
            }
            else
            {
                AssertRejectedOutput(result, StatusCodes.BadTypeMismatch);
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task CustomCodecStructureIdentityMustMatchEvenWhenFieldsAgree(bool wrongType, bool array)
        {
            ServiceMessageContext context = NewContext();
            ArrayOf<StructureField> fields =
                [new StructureField { Name = "Value", DataType = Ua.DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }];
            var fieldTypes = new Dictionary<string, BuiltInType> { ["Value"] = BuiltInType.Int32 };
            Structure declared = RegisterStructure(context, "Declared", fields, fieldTypes);
            Structure wrong = RegisterStructure(context, "Wrong", fields, fieldTypes);
            declared["Value"] = new Variant(12);
            wrong["Value"] = new Variant(12);
            ExtensionObject selected = new(wrongType ? wrong : declared);
            ArrayOf<ExtensionObject> elements = [new ExtensionObject(declared), selected];
            Variant value = array ? new Variant(elements) : new Variant(selected);
            WotInvokeResult supplied = NativeCodecResult(value, context);

            WotInvokeResult result = await InvokeReviewedCodecAsync(
                NativeSchema(declared.TypeId.ToString(), array ? ValueRanks.OneDimension : ValueRanks.Scalar),
                supplied, context).ConfigureAwait(false);

            if (wrongType)
            {
                AssertRejectedOutput(result, StatusCodes.BadTypeMismatch);
            }
            else
            {
                AssertPreservedCodecOutput(result, supplied);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CustomCodecRetainsRegisteredStructureSubtypeSemantics(bool structureBase)
        {
            ServiceMessageContext context = NewContext();
            ArrayOf<StructureField> fields =
                [new StructureField { Name = "Value", DataType = Ua.DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }];
            var fieldTypes = new Dictionary<string, BuiltInType> { ["Value"] = BuiltInType.Int32 };
            Structure declared = RegisterStructure(context, "Declared", fields, fieldTypes);
            Structure derived = RegisterStructure(context, "Derived", fields, fieldTypes,
                ExpandedNodeId.ToNodeId(declared.TypeId, context.NamespaceUris));
            derived["Value"] = new Variant(12);
            WotInvokeResult supplied = NativeCodecResult(new Variant(new ExtensionObject(derived)), context);

            WotInvokeResult result = await InvokeReviewedCodecAsync(
                NativeSchema(structureBase ? "i=22" : declared.TypeId.ToString()), supplied, context)
                .ConfigureAwait(false);

            AssertPreservedCodecOutput(result, supplied);
        }

        [TestCase("NodeId", false)]
        [TestCase("NodeId", true)]
        [TestCase("NodeIdArray", false)]
        [TestCase("NodeIdArray", true)]
        [TestCase("QualifiedName", false)]
        [TestCase("QualifiedName", true)]
        [TestCase("ExpandedNodeId", false)]
        [TestCase("ExpandedNodeId", true)]
        [TestCase("DataValue", false)]
        [TestCase("DataValue", true)]
        [TestCase("Structure", false)]
        [TestCase("Structure", true)]
        public async Task CustomCodecNamespaceIndexesMustResolveInTheirActualContext(string shape, bool invalid)
        {
            ServiceMessageContext context = NewContext();
            ushort mapped = context.NamespaceUris.GetIndexOrAppend("urn:http-review:mapped");
            ushort index = invalid ? (ushort)65000 : mapped;
            var node = new NodeId("Target", index);
            ArrayOf<NodeId> nodes = [new NodeId(2253u), node];
            string type;
            Variant value;
            switch (shape)
            {
                case "NodeId":
                case "NodeIdArray":
                    type = "i=17";
                    value = shape == "NodeIdArray" ? new Variant(nodes) : new Variant(node);
                    break;
                case "QualifiedName":
                    type = "i=20";
                    value = new Variant(new QualifiedName("Target", index));
                    break;
                case "ExpandedNodeId":
                    type = "i=18";
                    value = new Variant(new ExpandedNodeId(node));
                    break;
                case "DataValue":
                    type = "i=23";
                    value = new Variant(new DataValue(new Variant(node)));
                    break;
                case "Structure":
                    Structure structure = RegisterStructure(context, "ContextValue",
                        [new StructureField
                        {
                            Name = "Target", DataType = Ua.DataTypeIds.NodeId, ValueRank = ValueRanks.Scalar
                        }],
                        new Dictionary<string, BuiltInType> { ["Target"] = BuiltInType.NodeId });
                    structure["Target"] = new Variant(node);
                    type = structure.TypeId.ToString();
                    value = new Variant(new ExtensionObject(structure));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape));
            }
            int countBefore = context.NamespaceUris.Count;
            WotInvokeResult supplied = NativeCodecResult(value, context);

            WotInvokeResult result = await InvokeReviewedCodecAsync(
                NativeSchema(type, shape == "NodeIdArray" ? ValueRanks.OneDimension : ValueRanks.Scalar),
                supplied, NewContext()).ConfigureAwait(false);

            if (invalid)
            {
                AssertRejectedOutput(result, StatusCodes.BadNodeIdInvalid);
            }
            else
            {
                AssertPreservedCodecOutput(result, supplied);
            }
            Assert.That(context.NamespaceUris.Count, Is.EqualTo(countBefore));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task DefaultCodecRejectsUnmappedNativeNodeIds(bool invalid, bool array)
        {
            var context = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            string node = invalid ? "\"ns=65000;s=Unmapped\"" : "\"i=2253\"";
            const string item = /*lang=json,strict*/ """{"type":"string","uav:dataTypeId":"i=17"}""";
            using var harness = new Harness(null, array ? ArraySchema(item) : item, array ? "[" + node + "]" : node);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await ((IWotContextualBindingChannel)channel).InvokeAsync(
                new WotInvokeRequest([], context)).ConfigureAwait(false);

            if (invalid)
            {
                AssertRejectedOutput(result, StatusCodes.BadDecodingError);
            }
            else
            {
                Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
                Assert.That(result.Outputs, Has.Count.EqualTo(1));
                if (array)
                {
                    Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out ArrayOf<NodeId> nodes), Is.True);
                    Assert.That(nodes.Count, Is.EqualTo(1));
                    Assert.That(nodes[0], Is.EqualTo(new NodeId(2253u)));
                }
                else
                {
                    Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out NodeId actual), Is.True);
                    Assert.That(actual, Is.EqualTo(new NodeId(2253u)));
                }
                Assert.That(result.Context!.NamespaceUris.Count, Is.EqualTo(1));
            }
            Assert.That(context.NamespaceUris.Count, Is.EqualTo(1));
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [Test]
        public async Task CustomCodecNativeFailureDiscardsAllNamedOutputs()
        {
            const string schema = /*lang=json,strict*/ """
                {"type":"object","uav:argumentLayout":"named","uav:fieldOrder":["First","Second"],
                 "properties":{
                  "First":{"type":"integer","uav:dataTypeId":"i=5"},
                  "Second":{"type":"integer","uav:dataTypeId":"i=5"}
                 }}
                """;
            ServiceMessageContext context = NewContext();
            var supplied = new WotInvokeResult(StatusCodes.Good,
                [new DataValue(new Variant((ushort)12)), new DataValue(new Variant("wrong"))]);

            WotInvokeResult result = await InvokeReviewedCodecAsync(
                schema, supplied.WithContext(context), context).ConfigureAwait(false);

            AssertRejectedOutput(result, StatusCodes.BadTypeMismatch);
        }

        private static string NativeSchema(string dataTypeId, int rank = ValueRanks.Scalar)
        {
            return $$"""
                {"uav:argumentLayout":"single","uav:dataTypeId":"{{dataTypeId}}","uav:valueRank":{{rank}}}
                """;
        }

        private static WotInvokeResult NativeCodecResult(Variant value, IServiceMessageContext context)
        {
            var status = new StatusCode(StatusCodes.GoodClamped.Code | 0x0480u);
            var timestamp = new DateTimeUtc(2026, 9, 11, 12, 0, 0);
            var operation = new ServiceResult(
                "urn:http-review:diagnostics", status, new LocalizedText("en", "Native custom result"),
                "custom operation detail", innerResult: null);
            return new WotInvokeResult(status, [new DataValue(value, status, timestamp, timestamp)])
                .WithContext(context).WithResultDetails(operation, [ServiceResult.Good]);
        }

        private static async Task<WotInvokeResult> InvokeReviewedCodecAsync(
            string schema, WotInvokeResult supplied, IServiceMessageContext context)
        {
            const string contentType = "application/x-first-review";
            ByteString response = new(Encoding.UTF8.GetBytes("custom non-JSON response"));
            var codec = new Mock<IWotInteractionPayloadCodec>(MockBehavior.Strict);
            codec.SetupGet(value => value.Id).Returns("native-review");
            codec.Setup(value => value.CanHandle(contentType)).Returns(true);
            codec.Setup(value => value.EncodeArguments(
                    It.IsAny<WotInvokeRequest>(), It.IsAny<WotPayloadDescriptor>(), It.IsAny<WotBindingBounds>()))
                .Callback<WotInvokeRequest, WotPayloadDescriptor, WotBindingBounds>((request, _, _) =>
                {
                    Assert.That(request.Inputs.Count, Is.EqualTo(1));
                    Assert.That(request.Inputs[0], Is.EqualTo(new Variant((ushort)7)));
                    Assert.That(request.Context, Is.SameAs(context));
                })
                .Returns(WotEncodeResult.Ok(Encoding.UTF8.GetBytes("custom non-JSON request")));
            codec.Setup(value => value.DecodeArguments(
                    response, It.IsAny<WotPayloadDescriptor>(), context, It.IsAny<WotBindingBounds>()))
                .Returns(supplied);
            codec.Setup(value => value.Encode(It.IsAny<Variant>(), It.IsAny<WotPayloadDescriptor>()))
                .Throws(new InvalidOperationException("Scalar encoding fallback must not be used."));
            codec.Setup(value => value.Decode(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<WotPayloadDescriptor>()))
                .Throws(new InvalidOperationException("Scalar decoding fallback must not be used."));
            using var harness = new Harness("""{"type":"integer","uav:dataTypeId":"i=5"}""",
                schema, "custom non-JSON response",
                codecs: new WotPayloadCodecRegistry().Register(codec.Object), contentType: contentType);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await ((IWotContextualBindingChannel)channel).InvokeAsync(
                new WotInvokeRequest([new Variant((ushort)7)], context)).ConfigureAwait(false);

            Assert.That(harness.SendCount, Is.EqualTo(1));
            Assert.That(Encoding.UTF8.GetString(harness.Body.Span), Is.EqualTo("custom non-JSON request"));
            codec.Verify(value => value.DecodeArguments(
                response, It.IsAny<WotPayloadDescriptor>(), context, It.IsAny<WotBindingBounds>()), Times.Once);
            codec.Verify(value => value.Encode(It.IsAny<Variant>(), It.IsAny<WotPayloadDescriptor>()), Times.Never);
            codec.Verify(value => value.Decode(
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<WotPayloadDescriptor>()), Times.Never);
            return result;
        }

        private static void AssertPreservedCodecOutput(WotInvokeResult result, WotInvokeResult supplied)
        {
            Assert.That(result.Status.Code, Is.EqualTo(supplied.Status.Code));
            Assert.That(result.Outputs, Has.Count.EqualTo(1));
            Assert.That(result.Outputs[0].WrappedValue, Is.EqualTo(supplied.Outputs[0].WrappedValue));
            Assert.That(result.Outputs[0].StatusCode.Code, Is.EqualTo(supplied.Outputs[0].StatusCode.Code));
            Assert.That(result.Outputs[0].SourceTimestamp, Is.EqualTo(supplied.Outputs[0].SourceTimestamp));
            Assert.That(result.Outputs[0].ServerTimestamp, Is.EqualTo(supplied.Outputs[0].ServerTimestamp));
            Assert.That(result.OperationResult.Code, Is.EqualTo(supplied.OperationResult.Code));
            Assert.That(result.OperationResult.AdditionalInfo, Is.EqualTo("custom operation detail"));
            Assert.That(result.InputArgumentResults, Is.EqualTo(supplied.InputArgumentResults));
            Assert.That(result.Context, Is.SameAs(supplied.Context));
        }
    }
}
