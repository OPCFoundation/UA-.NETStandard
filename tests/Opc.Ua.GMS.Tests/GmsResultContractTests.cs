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
using NUnit.Framework;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery.Result;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Exercises immutable measurement contracts without a server dependency.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    public sealed class GmsResultContractTests
    {
        /// <summary>
        /// Mutation at either boundary cannot rewrite a captured result or its acquisition quality.
        /// </summary>
        [Test]
        public void ResultCapturesImmutableDataAndRawSample()
        {
            var data = new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType { ResultId = "inspection-17" },
                ResultContent = [new Variant(12.25)]
            };
            var acquired = new DateTimeUtc(2026, 1, 2, 3, 4, 5);
            var sample = new DataValue(Variant.FromStructure(data), StatusCodes.UncertainDataSubNormal, acquired);
            string[] tools = ["probe-7"];
            var result = new GmsResult(sample, tools);

            data.ResultMetaData.ResultId = "mutated";
            tools[0] = "mutated";
            ResultDataType returned = result.Data;
            returned.ResultMetaData!.ResultId = "mutated-again";
            returned.ResultContent = [];

            Assert.Multiple(() =>
            {
                Assert.That(result.Data.ResultMetaData!.ResultId, Is.EqualTo("inspection-17"));
                Assert.That(result.Data.ResultContent, Has.Count.EqualTo(1));
                Assert.That(result.UsedTools[0], Is.EqualTo("probe-7"));
                Assert.That(result.Value.StatusCode, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
                Assert.That(result.Value.SourceTimestamp, Is.EqualTo(acquired));
            });
        }

        /// <summary>
        /// Missing measurement payloads are not silently converted to successful empty results.
        /// </summary>
        [Test]
        public void ResultRejectsMissingEnvelopeOrIdentifier()
        {
            Assert.Throws<ArgumentException>(() => _ = new GmsResult(DataValue.Null));
            Assert.Throws<ArgumentException>(() => _ = new GmsResult(new DataValue(
                Variant.FromStructure(new ResultDataType()))));
        }

        /// <summary>
        /// Analog quality is independent of tolerance evaluation and geometric validity.
        /// </summary>
        [Test]
        public void AnalogSampleRetainsBadQualityAndAllowsMissingBadValue()
        {
            var timestamp = new DateTimeUtc(2026, 1, 2, 3, 4, 5);
            var units = new EUInformation { UnitId = 4408652, DisplayName = new LocalizedText("mm") };
            var sample = new GmsAnalogValue(new DataValue(Variant.Null, StatusCodes.BadSensorFailure, timestamp), units);
            units.UnitId = 0;

            Assert.Multiple(() =>
            {
                Assert.That(sample.Value.StatusCode, Is.EqualTo(StatusCodes.BadSensorFailure));
                Assert.That(sample.Value.SourceTimestamp, Is.EqualTo(timestamp));
                Assert.That(sample.Value.WrappedValue.IsNull, Is.True);
                Assert.That(sample.EngineeringUnits.UnitId, Is.EqualTo(4408652));
            });
            Assert.Throws<ArgumentException>(() => _ = new GmsAnalogValue(
                new DataValue(new Variant(42)), sample.EngineeringUnits));
            Assert.Throws<ArgumentException>(() => _ = new GmsAnalogValue(
                new DataValue(Variant.Null), sample.EngineeringUnits));
        }

        /// <summary>
        /// Corrections describe deviations, require a relative or absolute value, and are not commands.
        /// </summary>
        [Test]
        public void CorrectionRequiresAValueAndKeepsItsCharacteristicReference()
        {
            var value = new GmsAnalogValue(new DataValue(new Variant(-0.25)), new EUInformation());
            var correction = new GmsCorrection("offset-1", "diameter", relativeValue: value);
            Assert.Multiple(() =>
            {
                Assert.That(correction.Identifier, Is.EqualTo("offset-1"));
                Assert.That(correction.CharacteristicIdentifier, Is.EqualTo("diameter"));
                Assert.That(correction.RelativeValue, Is.SameAs(value));
                Assert.That(correction.AbsoluteValue, Is.Null);
            });
            Assert.Throws<ArgumentException>(() => _ = new GmsCorrection("offset-1", "diameter"));
            Assert.Throws<ArgumentException>(() => _ = new GmsCorrection(string.Empty, "diameter", value));
            Assert.Throws<ArgumentException>(() => _ = new GmsCorrection("offset-1", string.Empty, value));
        }

        /// <summary>
        /// A binary Variant containing "abc" is one type byte, a four-byte length and three UTF-8 bytes.
        /// </summary>
        [TestCase(7, false)]
        [TestCase(8, true)]
        [TestCase(9, true)]
        public void EncodedMetadataBudgetUsesExactByteBoundary(int bytes, bool accepted)
        {
            var limits = new GmsResultLimits { MaximumMetadataBytes = bytes };
            ServiceMessageContext context = ModelTestContext.CreateMessageContext();
            if (accepted)
            {
                Assert.That(limits.Measure(new Variant("abc"), context), Is.EqualTo(8));
            }
            else
            {
                ServiceResultException error = Assert.Throws<ServiceResultException>(
                    () => limits.Measure(new Variant("abc"), context))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
        }

        /// <summary>
        /// Invalid resource policies fail before provider or session work.
        /// </summary>
        [TestCase("Results")]
        [TestCase("Metadata")]
        [TestCase("File")]
        [TestCase("Storage")]
        [TestCase("Characteristics")]
        [TestCase("Tools")]
        [TestCase("Corrections")]
        public void InvalidBoundsAreRejected(string bound)
        {
            GmsResultLimits limits = bound switch
            {
                "Results" => new GmsResultLimits { MaximumResults = 0 },
                "Metadata" => new GmsResultLimits { MaximumMetadataBytes = 0 },
                "File" => new GmsResultLimits { MaximumFileBytes = 0 },
                "Storage" => new GmsResultLimits { MaximumStoredBytes = 0 },
                "Characteristics" => new GmsResultLimits { MaximumCharacteristics = 0 },
                "Tools" => new GmsResultLimits { MaximumUsedTools = 0 },
                "Corrections" => new GmsResultLimits { MaximumCorrections = 0 },
                _ => throw new ArgumentOutOfRangeException(nameof(bound))
            };
            Assert.Throws<ArgumentOutOfRangeException>(limits.Validate);
        }

        /// <summary>
        /// Intermediate results require characteristics, retain partial metadata and snapshot opaque payloads.
        /// </summary>
        [Test]
        public void IntermediateResultShapeAndPayloadAreValidatedAndImmutable()
        {
            DataValue value = GmsResultStoreTests.Result("partial").Value;
            Assert.Throws<ArgumentException>(() => _ = new GmsResult(value, isIntermediate: true));
            Assert.Throws<ArgumentException>(() => _ = new GmsResult(value, programName: "NotAnIntermediate"));
            Assert.Throws<ArgumentException>(() => _ = new GmsResult(
                value, characteristics: [new GmsCharacteristic("Bore")]));
            Assert.Throws<ArgumentException>(() => _ = new GmsResult(
                value, characteristics: [null!], isIntermediate: true));
            Assert.Throws<ArgumentException>(() => _ = new GmsResult(value, mimeType: string.Empty));
            byte[] bytes = [1, 2, 3];
            var result = new GmsResult(value, characteristics: [new GmsCharacteristic("Bore")],
                programName: "Measure", isIntermediate: true, fileContent: new ByteString(bytes));
            bytes[0] = 9;

            Assert.Multiple(() =>
            {
                Assert.That(result.Data.ResultMetaData!.IsPartial, Is.True);
                Assert.That(result.Data.ResultMetaData.HasTransferableDataOnFile, Is.True);
                Assert.That(result.ProgramName, Is.EqualTo("Measure"));
                Assert.That(result.Characteristics[0].Name, Is.EqualTo("Bore"));
                Assert.That(result.FileContent.Span[0], Is.EqualTo(1));
                Assert.That(result.MimeType, Is.EqualTo("application/octet-stream"));
            });
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _ = new GmsTolerance(GmsResultMeasurementTests.Analog(1), (ToleranceLimitEnum)3));
            Assert.Throws<ArgumentNullException>(() => _ = new GmsTolerance(null!, ToleranceLimitEnum.NoLimit));
            Assert.Throws<ArgumentException>(() => _ = new GmsCharacteristic(string.Empty));
            Assert.Throws<ArgumentNullException>(() => _ = new GmsAnalogValue(new DataValue(new Variant(1.0)), null!));
            Assert.Throws<ArgumentNullException>(() => _ = new GmsResultLimits().Measure(Variant.Null, null!));
        }
    }
}
