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
 *
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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

#nullable enable

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// A DataType field's <c>uav:arrayDimensions</c> is a list of OPC 10000-3
    /// <c>UInt32</c> bounds, and an entry that is not one is rejected rather
    /// than dropped.
    /// </summary>
    /// <remarks>
    /// Dropping a malformed entry silently changes the rank the remaining ones
    /// describe: three authored dimensions of which one is <c>-1</c> would
    /// materialize as a two-dimensional array nobody wrote, and no later reader
    /// could tell that from an authored pair.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotDataTypeDimensionTests
    {
        [TestCase("-1", TestName = "ANegativeDimensionIsRejected")]
        [TestCase("1.5", TestName = "AFractionalDimensionIsRejected")]
        [TestCase("\"3\"", TestName = "ATextualDimensionIsRejected")]
        [TestCase("4294967296", TestName = "AnOverflowingDimensionIsRejected")]
        [TestCase("null", TestName = "ANullDimensionIsRejected")]
        [TestCase("true", TestName = "ABooleanDimensionIsRejected")]
        [TestCase("[2]", TestName = "ANestedDimensionIsRejected")]
        public void AMalformedFieldDimensionIsRejected(string dimension)
        {
            WotConversionResult<UANodeSet> result = Convert(
                "[2," + dimension + "]", valueRank: 2);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Diagnostics.Any(d =>
                        d.Severity == WotDiagnosticSeverity.Error &&
                        d.Code == WotDiagnosticCode.InvalidValueRank &&
                        d.Message.Contains(
                            "rejected rather than dropped", StringComparison.Ordinal)),
                    Is.True,
                    string.Join("; ", result.Diagnostics.Select(d => d.Message)));
                Assert.That(
                    FieldDimensions(result),
                    Is.Null.Or.Empty,
                    "A rejected term materializes nothing, so no truncated dimension list " +
                    "reaches the NodeSet.");
            });
        }

        /// <summary>
        /// The diagnostic names the offending entry by index, because a term
        /// with several entries is only actionable if the reader is told which
        /// one is wrong.
        /// </summary>
        [Test]
        public void TheDiagnosticNamesTheOffendingEntryByIndex()
        {
            WotConversionResult<UANodeSet> result = Convert("[2,3,-7]", valueRank: 3);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Message.Contains("at index 2", StringComparison.Ordinal) &&
                    d.Message.Contains("'-7'", StringComparison.Ordinal)),
                Is.True,
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void ATermThatIsNotAnArrayIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert("\"2,3\"", valueRank: 2);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Code == WotDiagnosticCode.InvalidValueRank),
                Is.True,
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        }

        /// <summary>
        /// Zero is the bound OPC 10000-3 uses for a dimension whose length is
        /// not fixed, so it is the one value that looks wrong and is not.
        /// </summary>
        [Test]
        public void AZeroDimensionIsAccepted()
        {
            WotConversionResult<UANodeSet> result = Convert("[0,4]", valueRank: 2);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                    Is.Empty,
                    string.Join("; ", result.Diagnostics.Select(d => d.Message)));
                Assert.That(FieldDimensions(result), Is.EqualTo("0,4"));
            });
        }

        [Test]
        public void WellFormedDimensionsSurviveUnchanged()
        {
            WotConversionResult<UANodeSet> result = Convert("[2,3]", valueRank: 2);

            Assert.That(FieldDimensions(result), Is.EqualTo("2,3"));
        }

        /// <summary>
        /// The rank is the number of bounds, so a list whose length disagrees
        /// with the stated ValueRank describes an array of a different shape.
        /// </summary>
        [Test]
        public void ARankMismatchedDimensionListIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert("[2,3]", valueRank: 3);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                    d.Message.Contains("array dimension", StringComparison.Ordinal)),
                Is.True,
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        }

        private static string? FieldDimensions(WotConversionResult<UANodeSet> result)
        {
            return result.Value?.Items?
                .OfType<UADataType>()
                .FirstOrDefault()?
                .Definition?
                .Field?
                .FirstOrDefault(f => string.Equals(f.Name, "Samples", StringComparison.Ordinal))?
                .ArrayDimensions;
        }

        private static WotConversionResult<UANodeSet> Convert(
            string dimensions, int valueRank)
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"," +
                "\"uav:dataTypeDefinitions\":[{" +
                "\"@id\":\"urn:test:pump#SampleSet\"," +
                "\"@type\":\"uav:StructureDefinition\"," +
                "\"uav:dataTypeName\":\"pump:SampleSet\"," +
                "\"uav:structureType\":\"Structure\"," +
                "\"uav:fields\":[{" +
                "\"@type\":\"uav:StructureField\"," +
                "\"uav:fieldName\":\"Samples\"," +
                "\"uav:fieldDataTypeName\":\"ua:Double\"," +
                "\"uav:fieldDataTypeId\":\"i=11\"," +
                "\"uav:valueRank\":" +
                valueRank.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                "\"uav:arrayDimensions\":" + dimensions + "}]}]}");

            using WotDocument document = WotDocument.Parse(json);
            return WotNodeSetConverter.ToNodeSetResult(document);
        }
    }
}
