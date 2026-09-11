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

using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Structure = Opc.Ua.Encoders.Structure;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    public sealed partial class HttpWotPayloadContractTests
    {
        [TestCase("i=10", "1e39", false)]
        [TestCase("i=10", "-1e39", false)]
        [TestCase("i=11", "1e309", false)]
        [TestCase("i=11", "-1e309", false)]
        [TestCase("i=10", "[1,1e39]", true)]
        [TestCase("i=10", "[1,-1e39]", true)]
        [TestCase("i=11", "[1,1e309]", true)]
        [TestCase("i=11", "[1,-1e309]", true)]
        public async Task FiniteJsonNumbersCannotOverflowNativeFloatingPoint(
            string dataTypeId, string response, bool array)
        {
            string item = $$"""{"type":"number","uav:dataTypeId":"{{dataTypeId}}"}""";
            string schema = array ? ArraySchema(item) : item;
            using var harness = new Harness(null, schema, response);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            AssertRejectedOutput(result, StatusCodes.BadDecodingError);
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [TestCase("i=10", "3.4028234663852886e38", (double)float.MaxValue)]
        [TestCase("i=10", "-3.4028234663852886e38", (double)-float.MaxValue)]
        [TestCase("i=11", "1.7976931348623157e308", double.MaxValue)]
        [TestCase("i=11", "-1.7976931348623157e308", -double.MaxValue)]
        [TestCase("i=11", "1e100", 1e100)]
        public async Task NativeFloatingPointAcceptsFiniteExtremes(
            string dataTypeId, string response, double expected)
        {
            string schema = $$"""{"type":"number","uav:dataTypeId":"{{dataTypeId}}"}""";
            using var harness = new Harness(null, schema, response);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(result.Outputs, Has.Count.EqualTo(1));
            Assert.That(result.Outputs[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            if (dataTypeId == "i=10")
            {
                Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out float actual), Is.True);
                Assert.That(actual, Is.EqualTo((float)expected));
                Assert.That(float.IsInfinity(actual), Is.False);
            }
            else
            {
                Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out double actual), Is.True);
                Assert.That(actual, Is.EqualTo(expected));
                Assert.That(double.IsInfinity(actual), Is.False);
            }
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [TestCase("i=10", "NaN")]
        [TestCase("i=10", "Infinity")]
        [TestCase("i=10", "-Infinity")]
        [TestCase("i=11", "NaN")]
        [TestCase("i=11", "Infinity")]
        [TestCase("i=11", "-Infinity")]
        public async Task StringSchemasRetainExplicitNativeIeeeSpecialValues(string dataTypeId, string special)
        {
            string schema = $$"""{"type":"string","uav:dataTypeId":"{{dataTypeId}}"}""";
            using var harness = new Harness(null, schema, "\"" + special + "\"");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(result.Outputs, Has.Count.EqualTo(1));
            double actual;
            if (dataTypeId == "i=10")
            {
                Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out float single), Is.True);
                actual = single;
            }
            else
            {
                Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out actual), Is.True);
            }
            if (special == "NaN")
            {
                Assert.That(double.IsNaN(actual), Is.True);
            }
            else
            {
                Assert.That(actual, Is.EqualTo(special == "Infinity"
                    ? double.PositiveInfinity : double.NegativeInfinity));
            }
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [TestCase("1e39")]
        [TestCase("-1e39")]
        public async Task NestedStructureFloatOverflowDiscardsEarlierNamedOutputs(string number)
        {
            ServiceMessageContext context = NewContext();
            _ = RegisterStructure(context, "FloatReading",
                [new StructureField { Name = "Value", DataType = Ua.DataTypeIds.Float, ValueRank = ValueRanks.Scalar }],
                new Dictionary<string, BuiltInType> { ["Value"] = BuiltInType.Float });
            const string schema = /*lang=json,strict*/ """
                {"type":"object","uav:argumentLayout":"named","uav:fieldOrder":["Valid","Reading"],
                 "properties":{
                  "Valid":{"type":"integer","uav:dataTypeId":"i=5"},
                  "Reading":{"type":"object","uav:dataTypeId":"nsu=urn:http-payload;s=FloatReading",
                             "properties":{"Value":{"type":"number"}}}
                 }}
                """;
            using var harness = new Harness(null, schema, "{\"Valid\":12,\"Reading\":{\"Value\":" + number + "}}");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await ((IWotContextualBindingChannel)channel).InvokeAsync(
                new WotInvokeRequest([], context)).ConfigureAwait(false);

            AssertRejectedOutput(result, StatusCodes.BadDecodingError);
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [TestCase("maximum", "1000", "1e100", false, 0)]
        [TestCase("maximum", "1000", "1000", true, 1000)]
        [TestCase("maximum", "1000", "1001", false, 0)]
        [TestCase("minimum", "-1000", "-1e100", false, 0)]
        [TestCase("minimum", "-1000", "-1000", true, -1000)]
        [TestCase("minimum", "-1000", "-1001", false, 0)]
        [TestCase("maximum", "1e100", "1e101", false, 0)]
        [TestCase("minimum", "1e100", "1000", false, 0)]
        [TestCase("minimum", "-1e100", "-1e101", false, 0)]
        [TestCase("maximum", "-1e100", "-1000", false, 0)]
        [TestCase("maximum", "1e100", "1e100", true, 1e100)]
        [TestCase("minimum", "1e100", "1e100", true, 1e100)]
        [TestCase("maximum", "-1e100", "-1e100", true, -1e100)]
        [TestCase("minimum", "-1e100", "-1e100", true, -1e100)]
        [TestCase("maximum", "10e99", "1.0e100", true, 1e100)]
        [TestCase("maximum", "1e-100", "2e-100", false, 0)]
        [TestCase("minimum", "1e-100", "0", false, 0)]
        [TestCase("minimum", "-1e-100", "-2e-100", false, 0)]
        [TestCase("maximum", "1e-100", "1e-100", true, 1e-100)]
        [TestCase("maximum", "0", "-0e100", true, 0)]
        [TestCase("minimum", "-0", "0.000", true, 0)]
        [TestCase("maximum", "1.00000000000000000001e100", "1.00000000000000000002e100", false, 0)]
        [TestCase("minimum", "1.00000000000000000002e100", "1.00000000000000000001e100", false, 0)]
        [TestCase("maximum", "1e2147483647", "1e100", true, 1e100)]
        [TestCase("minimum", "1e2147483647", "1e100", false, 0)]
        [TestCase("maximum", "1e-2147483648", "1e-100", false, 0)]
        public async Task NumericBoundsCompareExactJsonMagnitudes(
            string boundName, string bound, string response, bool accepted, double expected)
        {
            string schema = $$"""
                {"type":"number","uav:dataTypeId":"i=11","{{boundName}}":{{bound}}}
                """;
            using var harness = new Harness(null, schema, response);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            if (accepted)
            {
                Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
                Assert.That(result.Outputs, Has.Count.EqualTo(1));
                Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out double actual), Is.True);
                Assert.That(actual, Is.EqualTo(expected));
            }
            else
            {
                AssertRejectedOutput(result, StatusCodes.BadDecodingError);
            }
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [TestCase("maximum", "18446744073709551614", "18446744073709551615", false)]
        [TestCase("minimum", "18446744073709551615", "18446744073709551614", false)]
        [TestCase("maximum", "18446744073709551615", "18446744073709551615", true)]
        [TestCase("minimum", "18446744073709551615", "18446744073709551615", true)]
        public async Task NumericBoundsPreserveAdjacentUInt64Values(
            string boundName, string bound, string response, bool accepted)
        {
            string schema = $$"""
                {"type":"integer","uav:dataTypeId":"i=9","{{boundName}}":{{bound}}}
                """;
            using var harness = new Harness(null, schema, response);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            if (accepted)
            {
                Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
                Assert.That(result.Outputs, Has.Count.EqualTo(1));
                Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out ulong actual), Is.True);
                Assert.That(actual, Is.EqualTo(ulong.MaxValue));
            }
            else
            {
                AssertRejectedOutput(result, StatusCodes.BadDecodingError);
            }
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [TestCase("1e999999999999999999999999999999")]
        [TestCase("1e-999999999999999999999999999999")]
        [TestCase("\"not-numeric\"")]
        public async Task UnsupportedNumericBoundComparisonsFailExplicitly(string bound)
        {
            string schema = $$"""{"type":"number","uav:dataTypeId":"i=11","maximum":{{bound}}}""";
            using var harness = new Harness(null, schema, "1e100");
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            AssertRejectedOutput(result, StatusCodes.BadDecodingError);
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [TestCase("null", 1, true, 0)]
        [TestCase("[]", 1, true, 0)]
        [TestCase(/*lang=json,strict*/ "[{\"Value\":1}]", 1, true, 1)]
        [TestCase(/*lang=json,strict*/ "[{\"Value\":1},{\"Value\":2}]", 1, false, 0)]
        [TestCase(/*lang=json,strict*/ "[{\"Value\":\"not-an-integer\"},{\"Value\":2}]", 1, false, 0)]
        [TestCase(/*lang=json,strict*/ "[{\"Value\":1},{\"Value\":2}]", 0, true, 2)]
        [TestCase(/*lang=json,strict*/ "[{\"Value\":1},{\"Value\":2}]", -1, true, 2)]
        public async Task StructureArrayLimitIsCheckedBeforeDecodingElements(
            string response, int limit, bool accepted, int count)
        {
            ServiceMessageContext context = NewContext();
            Structure template = RegisterReviewElement(context);
            context.MaxArrayLength = limit;
            string schema = ArraySchema(
                ReviewElementSchema, nullable: true, dataTypeId: "nsu=urn:http-payload;s=ReviewElement");
            using var harness = new Harness(null, schema, response);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await ((IWotContextualBindingChannel)channel).InvokeAsync(
                new WotInvokeRequest([], context)).ConfigureAwait(false);

            if (accepted)
            {
                Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
                Assert.That(result.Outputs, Has.Count.EqualTo(1));
                Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> values), Is.True);
                Assert.That(values.IsNull, Is.EqualTo(response == "null"));
                Assert.That(values.Count, Is.EqualTo(count));
                for (int index = 0; index < values.Count; index++)
                {
                    Assert.That(values[index].TryGetValue(out IEncodeable? decoded, result.Context), Is.True);
                    Assert.That(decoded!.TypeId, Is.EqualTo(template.TypeId));
                    Assert.That(decoded, Is.InstanceOf<IStructure>());
                    Assert.That(((IStructure)decoded)["Value"], Is.EqualTo(new Variant(index + 1)));
                }
            }
            else
            {
                AssertRejectedOutput(result, StatusCodes.BadEncodingLimitsExceeded);
            }
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [TestCase("[]", true, 0)]
        [TestCase("[1]", true, 1)]
        [TestCase("[1,2]", false, 0)]
        public async Task OrdinaryArrayRetainsTheSameNativeLengthLimit(string response, bool accepted, int count)
        {
            ServiceMessageContext context = NewContext();
            context.MaxArrayLength = 1;
            string schema = ArraySchema(/*lang=json,strict*/ """{"type":"integer","uav:dataTypeId":"i=6"}""");
            using var harness = new Harness(null, schema, response);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await ((IWotContextualBindingChannel)channel).InvokeAsync(
                new WotInvokeRequest([], context)).ConfigureAwait(false);

            if (accepted)
            {
                Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
                Assert.That(result.Outputs, Has.Count.EqualTo(1));
                Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out ArrayOf<int> values), Is.True);
                Assert.That(values.IsNull, Is.False);
                Assert.That(values.Count, Is.EqualTo(count));
                if (count == 1)
                {
                    Assert.That(values[0], Is.EqualTo(1));
                }
            }
            else
            {
                AssertRejectedOutput(result, StatusCodes.BadEncodingLimitsExceeded);
            }
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        private static string ArraySchema(string element, bool nullable = false, string? dataTypeId = null)
        {
            string type = nullable ? "[\"array\",\"null\"]" : "\"array\"";
            string identity = dataTypeId is null ? string.Empty : "\"uav:dataTypeId\":\"" + dataTypeId + "\",";
            return $$"""
                {"type":{{type}},"uav:argumentLayout":"single","uav:valueRank":1,{{identity}}"items":{{element}}}
                """;
        }

        private static Structure RegisterReviewElement(ServiceMessageContext context)
        {
            return RegisterStructure(context, "ReviewElement",
                [new StructureField { Name = "Value", DataType = Ua.DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }],
                new Dictionary<string, BuiltInType> { ["Value"] = BuiltInType.Int32 });
        }

        private static void AssertRejectedOutput(WotInvokeResult result, StatusCode expectedStatus)
        {
            Assert.That(result.Status, Is.EqualTo(expectedStatus), result.Error);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
        }

        private const string ReviewElementSchema = /*lang=json,strict*/ """
            {"type":"object","uav:dataTypeId":"nsu=urn:http-payload;s=ReviewElement",
             "properties":{"Value":{"type":"integer"}}}
            """;
    }
}
