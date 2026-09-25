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
using System.IO;
using System.Linq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Vision;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using static UaLens.Tests.Companions.VisionWorkflowTestSupport;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class VisionWorkflowValuesTests
    {
        [TestCase(double.Epsilon)]
        [TestCase(29.5)]
        [TestCase(240d)]
        public void RatePreservesFinitePositiveValuesThroughInclusiveMaximum(double value)
        {
            Assert.That(VisionWorkflowValues.Rate(Variant.From(value)), Is.EqualTo(value));
        }

        [TestCase(0d)]
        [TestCase(-0.01)]
        [TestCase(double.NaN)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(double.PositiveInfinity)]
        public void RateRejectsNonpositiveOrNonfiniteValues(double value)
        {
            Assert.That(() => VisionWorkflowValues.Rate(Variant.From(value)), Throws.ArgumentException);
        }

        [Test]
        public void RateRejectsImmediatelyAboveMaximumAndOtherNumericTypes()
        {
            Assert.That(() => VisionWorkflowValues.Rate(Variant.From(Math.BitIncrement(240d))),
                Throws.ArgumentException);
            Assert.That(() => VisionWorkflowValues.Rate(Variant.From(30u)), Throws.ArgumentException);
            Assert.That(() => VisionWorkflowValues.Rate(Variant.From(30f)), Throws.ArgumentException);
        }

        [TestCase(1u, 1u)]
        [TestCase(8192u, 1u)]
        [TestCase(1u, 8192u)]
        [TestCase(4095u, 4097u)]
        [TestCase(4096u, 4096u)]
        public void DimensionsAcceptIndividualAndCombinedInclusiveBounds(uint width, uint height)
        {
            var fixture = new VisionWorkflowTestSupport();
            ArrayOf<CompanionValue> inputs = Replace(fixture.Inputs("configure-stream"), "width", Variant.From(width));
            inputs = Replace(inputs, "height", Variant.From(height));

            Assert.That(() => fixture.Validate("configure-stream", inputs), Throws.Nothing);
            Assert.That((ulong)width * height, Is.LessThanOrEqualTo(16_777_216UL));
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase(0u, 1u)]
        [TestCase(1u, 0u)]
        [TestCase(8193u, 1u)]
        [TestCase(1u, 8193u)]
        [TestCase(4096u, 4097u)]
        [TestCase(4097u, 4096u)]
        public void DimensionsRejectAdjacentIndividualOrCombinedOverflow(uint width, uint height)
        {
            var fixture = new VisionWorkflowTestSupport();
            ArrayOf<CompanionValue> inputs = Replace(fixture.Inputs("configure-stream"), "width", Variant.From(width));
            inputs = Replace(inputs, "height", Variant.From(height));

            Assert.That(() => fixture.Validate("configure-stream", inputs), Throws.ArgumentException);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase(1u, true)]
        [TestCase(100_000_000u, true)]
        [TestCase(0u, false)]
        [TestCase(100_000_001u, false)]
        public void ConfigureBitrateHasExactUnsignedBounds(uint bitrate, bool valid)
        {
            var fixture = new VisionWorkflowTestSupport();
            ArrayOf<CompanionValue> inputs = Replace(
                fixture.Inputs("configure-stream"), "bitrate", Variant.From(bitrate));
            if (valid)
            {
                Assert.That(() => fixture.Validate("configure-stream", inputs), Throws.Nothing);
            }
            else
            {
                Assert.That(() => fixture.Validate("configure-stream", inputs), Throws.ArgumentException);
            }
        }

        [TestCase(1u, true)]
        [TestCase(15u, true)]
        [TestCase(0u, false)]
        [TestCase(16u, false)]
        public void DurationHasExactInclusiveUnsignedBounds(uint seconds, bool valid)
        {
            var fixture = new VisionWorkflowTestSupport();
            ArrayOf<CompanionValue> inputs =
                Replace(fixture.Inputs("run-continuous-window"), "seconds", Variant.From(seconds));
            if (valid)
            {
                Assert.That(() => fixture.Validate("run-continuous-window", inputs), Throws.Nothing);
            }
            else
            {
                Assert.That(() => fixture.Validate("run-continuous-window", inputs), Throws.ArgumentException);
            }
        }

        [Test]
        public void DurationRejectsSignedOrFloatingRepresentations()
        {
            var fixture = new VisionWorkflowTestSupport();
            foreach (Variant value in (ArrayOf<Variant>)[Variant.From(1), Variant.From(1d), Variant.From("1")])
            {
                ArrayOf<CompanionValue> inputs = Replace(fixture.Inputs("run-continuous-window"), "seconds", value);
                Assert.That(() => fixture.Validate("run-continuous-window", inputs), Throws.ArgumentException);
            }
        }

        [TestCase("get-clip", "format")]
        [TestCase("probe-stream", "protocol")]
        [TestCase("configure-stream", "codec")]
        [TestCase("submit-detections", "purpose")]
        [TestCase("submit-inspection", "evaluation")]
        [TestCase("submit-correction", "purpose")]
        [TestCase("submit-image-reference", "purpose")]
        public void InvalidEnumsFailWithoutDispatch(string operation, string name)
        {
            var fixture = new VisionWorkflowTestSupport();
            ArrayOf<CompanionValue> inputs = Replace(fixture.Inputs(operation), name, Variant.From(int.MaxValue));
            Assert.That(() => fixture.Validate(operation, inputs), Throws.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadTypeMismatch));
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(1_048_576)]
        public void BytesEnforcesInclusiveLimitAndCopiesBackingStorage(int length)
        {
            byte[] original = new byte[length];
            if (length != 0)
            {
                original[0] = 37;
                original[^1] = 91;
            }
            var input = new ByteString(original);
            ByteString captured = VisionWorkflowValues.Bytes(Variant.From(input));
            Assert.That(captured.Length, Is.EqualTo(length));
            Assert.That(captured.ToArray(), Is.EqualTo(original));
            if (length != 0)
            {
                original[^1] = 12;
                Assert.That(captured[^1], Is.EqualTo(91));
                Assert.That(captured.Memory, Is.Not.EqualTo(input.Memory));
            }
        }

        [Test]
        public void BytesRejectsOneOverLimitAndWrongType()
        {
            Assert.That(() => VisionWorkflowValues.Bytes(Variant.From(new ByteString(new byte[1_048_577]))),
                Throws.ArgumentException);
            Assert.That(() => VisionWorkflowValues.Bytes(Variant.From("AQID")), Throws.ArgumentException);
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        public void CompleteEncodedInputsCountVariantOverheadAtTheExactBoundary(int delta)
        {
            var fixture = new VisionWorkflowTestSupport();
            ArrayOf<CompanionValue> inputs = fixture.Inputs("submit-correction");
            int overhead = EncodedLength(fixture, inputs);
            Assert.That(overhead, Is.GreaterThan(5), "The cap includes all fields, not only the ByteString length.");
            inputs = Replace(inputs, "inlineImage",
                Variant.From(new ByteString(new byte[1_048_576 - overhead + delta])));
            Assert.That(EncodedLength(fixture, inputs), Is.EqualTo(1_048_576 + delta));
            if (delta <= 0)
            {
                Assert.That(() => fixture.Validate("submit-correction", inputs), Throws.Nothing);
            }
            else
            {
                Assert.That(() => fixture.Validate("submit-correction", inputs),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(32)]
        public void StructuresPreserveTypedEmptySingletonAndMaximumArrays(int count)
        {
            ArrayOf<VisionDetectionDataType> original =
                [.. Enumerable.Range(0, count).Select(index => Detection("detection-" + index))];
            var fixture = new VisionWorkflowTestSupport();

            ArrayOf<VisionDetectionDataType> result = VisionWorkflowValues.Structures<VisionDetectionDataType>(
                fixture.Context, Variant.FromStructure(original));

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.Count, Is.EqualTo(count));
            for (int index = 0; index < count; index++)
            {
                Assert.That(result[index].DetectionId, Is.EqualTo("detection-" + index));
                Assert.That(result[index].Confidence, Is.EqualTo(0.875));
            }
        }

        [TestCase("missing")]
        [TestCase("typed-null")]
        [TestCase("scalar")]
        [TestCase("wrong-structure")]
        [TestCase("null-element")]
        [TestCase("33")]
        public void StructuresRejectMissingWrongTypeRankAndExcessCount(string fault)
        {
            var fixture = new VisionWorkflowTestSupport();
            Variant input = fault switch
            {
                "missing" => Variant.Null,
                "typed-null" => Variant.FromStructure(default(ArrayOf<VisionDetectionDataType>)),
                "scalar" => Variant.FromStructure(Detection()),
                "wrong-structure" => Variant.FromStructure([Characteristic()]),
                "null-element" => Variant.FromStructure<VisionDetectionDataType>([null!]),
                "33" => Variant.FromStructure(
                    [.. Enumerable.Range(0, 33).Select(index => Detection("detection-" + index))]),
                _ => throw new ArgumentOutOfRangeException(nameof(fault))
            };

            Assert.That(() => VisionWorkflowValues.Structures<VisionDetectionDataType>(fixture.Context, input),
                Throws.InstanceOf<ArgumentException>());
        }

        [TestCase(0d)]
        [TestCase(1d)]
        public void DetectionConfidenceAndIdentityBoundsAreInclusive(double confidence)
        {
            VisionDetectionDataType first = Detection(new string('A', 256));
            first.ClassLabel = new string('L', 256);
            first.Confidence = confidence;
            VisionDetectionDataType second = Detection(new string('a', 256));
            Assert.That(() => VisionWorkflowValues.ValidateDetections([first, second]), Throws.Nothing);
            Assert.That(first.DetectionId, Is.Not.EqualTo(second.DetectionId));
            Assert.That(first.Confidence, Is.EqualTo(confidence));
        }

        [TestCase("id-empty")]
        [TestCase("id-space")]
        [TestCase("id-long")]
        [TestCase("label-empty")]
        [TestCase("label-long")]
        [TestCase("negative-confidence")]
        [TestCase("above-confidence")]
        [TestCase("nan-confidence")]
        [TestCase("infinite-confidence")]
        [TestCase("duplicate")]
        public void DetectionsRequireUniqueBoundedIdentityAndFiniteConfidence(string fault)
        {
            VisionDetectionDataType value = Detection();
            switch (fault)
            {
                case "id-empty":
                    value.DetectionId = string.Empty;
                    break;
                case "id-space":
                    value.DetectionId = " ";
                    break;
                case "id-long":
                    value.DetectionId = new string('x', 257);
                    break;
                case "label-empty":
                    value.ClassLabel = string.Empty;
                    break;
                case "label-long":
                    value.ClassLabel = new string('x', 257);
                    break;
                case "negative-confidence":
                    value.Confidence = -double.Epsilon;
                    break;
                case "above-confidence":
                    value.Confidence = Math.BitIncrement(1d);
                    break;
                case "nan-confidence":
                    value.Confidence = double.NaN;
                    break;
                case "infinite-confidence":
                    value.Confidence = double.PositiveInfinity;
                    break;
                case "duplicate":
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }
            ArrayOf<VisionDetectionDataType> inputs = fault == "duplicate" ? [value, Detection()] : [value];
            Assert.That(() => VisionWorkflowValues.ValidateDetections(inputs), Throws.ArgumentException);
        }

        [TestCase("x")]
        [TestCase("y")]
        [TestCase("width-zero")]
        [TestCase("width-infinite")]
        [TestCase("height-negative")]
        [TestCase("height-nan")]
        [TestCase("rotation")]
        public void BoxesRequireFinitePositiveTwoDimensionalGeometry(string fault)
        {
            VisionDetectionDataType value = Detection();
            VisionBoundingBox2DDataType box = value.BoundingBox2D!;
            switch (fault)
            {
                case "x":
                    box.CenterX = double.NaN;
                    break;
                case "y":
                    box.CenterY = double.PositiveInfinity;
                    break;
                case "width-zero":
                    box.Width = 0;
                    break;
                case "width-infinite":
                    box.Width = double.PositiveInfinity;
                    break;
                case "height-negative":
                    box.Height = -1;
                    break;
                case "height-nan":
                    box.Height = double.NaN;
                    break;
                case "rotation":
                    box.Rotation = double.NegativeInfinity;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }
            Assert.That(() => VisionWorkflowValues.ValidateDetections([value]), Throws.ArgumentException);
        }

        [TestCase("2d")]
        [TestCase("3d")]
        [TestCase("pose")]
        [TestCase("3d-centre")]
        public void DetectionFlagsRequireTheirCorrespondingGeometry(string fault)
        {
            VisionDetectionDataType value = Detection();
            if (fault == "2d")
            {
                value.BoundingBox2D = null!;
            }
            else if (fault == "pose")
            {
                value.HasPose = true;
                value.Pose = null!;
            }
            else
            {
                value.HasBoundingBox3D = true;
                value.BoundingBox3D = fault == "3d" ? null! : new VisionBoundingBox3DDataType { Center = null! };
            }
            Assert.That(() => VisionWorkflowValues.ValidateDetections([value]), Throws.ArgumentException);
        }

        [TestCase("two")]
        [TestCase("four")]
        [TestCase("zero")]
        [TestCase("negative")]
        [TestCase("nan")]
        [TestCase("infinite")]
        public void ThreeDimensionalSizeRequiresExactlyThreePositiveFiniteValues(string fault)
        {
            VisionDetectionDataType value = Detection();
            value.HasBoundingBox3D = true;
            value.BoundingBox3D = new VisionBoundingBox3DDataType
            {
                Center = Pose(),
                Size = fault switch
                {
                    "two" => [1, 2],
                    "four" => [1, 2, 3, 4],
                    "zero" => [1, 0, 3],
                    "negative" => [1, 2, -3],
                    "nan" => [double.NaN, 2, 3],
                    "infinite" => [1, double.PositiveInfinity, 3],
                    _ => throw new ArgumentOutOfRangeException(nameof(fault))
                }
            };
            Assert.That(() => VisionWorkflowValues.ValidateDetections([value]), Throws.ArgumentException);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ValidPoseAndThreeDimensionalGeometryPreserveFrameAndCovariance(bool covariance)
        {
            VisionDetectionDataType value = Detection();
            value.HasPose = true;
            value.Pose = Pose();
            value.Pose.FrameId = new string('F', 256);
            value.Pose.Covariance = covariance ? new double[36] : [];
            value.HasBoundingBox3D = true;
            value.BoundingBox3D = new VisionBoundingBox3DDataType { Center = Pose(), Size = [0.01, 2, 3] };
            Assert.That(() => VisionWorkflowValues.ValidateDetections([value]), Throws.Nothing);
            Assert.That(value.Pose.Position.ToArray(), Is.EqualTo(sPosition));
            Assert.That(value.Pose.Orientation.ToArray(), Is.EqualTo(sOrientation));
            Assert.That(value.Pose.Covariance.Count, Is.EqualTo(covariance ? 36 : 0));
        }

        [TestCase("frame")]
        [TestCase("long-frame")]
        [TestCase("position-length")]
        [TestCase("position-nan")]
        [TestCase("orientation-length")]
        [TestCase("quaternion")]
        [TestCase("orientation-infinite")]
        [TestCase("covariance-length")]
        [TestCase("covariance-nan")]
        public void DetectionPosesRejectInvalidFrameQuaternionAndCovariance(string fault)
        {
            VisionDetectionDataType value = Detection();
            VisionPose3DDataType pose = Pose();
            value.HasPose = true;
            value.Pose = pose;
            switch (fault)
            {
                case "frame":
                    pose.FrameId = " ";
                    break;
                case "long-frame":
                    pose.FrameId = new string('F', 257);
                    break;
                case "position-length":
                    pose.Position = [1, 2];
                    break;
                case "position-nan":
                    pose.Position = [1, double.NaN, 3];
                    break;
                case "orientation-length":
                    pose.Orientation = [0, 0, 1];
                    break;
                case "quaternion":
                    pose.Orientation = [0, 0, 0, Math.Sqrt(1 + 2e-6)];
                    break;
                case "orientation-infinite":
                    pose.Orientation = [0, 0, 0, double.PositiveInfinity];
                    break;
                case "covariance-length":
                    pose.Covariance = [1];
                    break;
                case "covariance-nan":
                    double[] covariance = new double[36];
                    covariance[35] = double.NaN;
                    pose.Covariance = covariance;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }
            if (fault is "frame" or "long-frame")
            {
                Assert.That(() => VisionWorkflowValues.ValidateDetections([value]), Throws.ArgumentException);
            }
            else
            {
                Assert.That(() => VisionWorkflowValues.ValidateDetections([value]),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadTypeMismatch));
            }
        }

        [TestCase("id")]
        [TestCase("id-long")]
        [TestCase("name")]
        [TestCase("name-long")]
        [TestCase("duplicate")]
        [TestCase("nominal")]
        [TestCase("actual")]
        [TestCase("deviation")]
        [TestCase("inconsistent")]
        [TestCase("subtraction-overflow")]
        [TestCase("uncertainty")]
        [TestCase("negative-uncertainty")]
        [TestCase("lower")]
        [TestCase("upper")]
        [TestCase("tolerance-order")]
        [TestCase("status")]
        public void CharacteristicsRequireFiniteConsistentMeasurements(string fault)
        {
            VisionCharacteristicDataType value = Characteristic();
            switch (fault)
            {
                case "id":
                    value.CharacteristicId = " ";
                    break;
                case "id-long":
                    value.CharacteristicId = new string('i', 257);
                    break;
                case "name":
                    value.Name = string.Empty;
                    break;
                case "name-long":
                    value.Name = new string('n', 257);
                    break;
                case "duplicate":
                    break;
                case "nominal":
                    value.Nominal = double.NaN;
                    break;
                case "actual":
                    value.Actual = double.PositiveInfinity;
                    break;
                case "deviation":
                    value.Deviation = double.NaN;
                    break;
                case "inconsistent":
                    value.Deviation = 0.251;
                    break;
                case "subtraction-overflow":
                    value.Actual = double.MaxValue;
                    value.Nominal = -double.MaxValue;
                    value.Deviation = 0;
                    break;
                case "uncertainty":
                    value.Uncertainty = double.NaN;
                    break;
                case "negative-uncertainty":
                    value.Uncertainty = -double.Epsilon;
                    break;
                case "lower":
                    value.LowerTolerance = double.NegativeInfinity;
                    break;
                case "upper":
                    value.UpperTolerance = double.NaN;
                    break;
                case "tolerance-order":
                    value.LowerTolerance = 1;
                    break;
                case "status":
                    value.Status = (VisionToleranceStatusEnum)int.MaxValue;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }
            ArrayOf<VisionCharacteristicDataType> values =
                fault == "duplicate" ? [value, Characteristic()] : [value];
            Assert.That(() => VisionWorkflowValues.ValidateCharacteristics(values), Throws.ArgumentException);
        }

        [Test]
        public void CharacteristicsAcceptExactIdentityAndMeasurementBoundaries()
        {
            VisionCharacteristicDataType value = Characteristic(new string('i', 256));
            value.Name = new string('n', 256);
            value.Nominal = 10;
            value.Actual = 10;
            value.Deviation = 0;
            value.Uncertainty = 0;
            value.LowerTolerance = 0;
            value.UpperTolerance = 0;
            Assert.That(() => VisionWorkflowValues.ValidateCharacteristics([value]), Throws.Nothing);
            Assert.That(value.Deviation, Is.Zero);
            Assert.That(value.Uncertainty, Is.Zero);
        }

        [TestCase(0, true, true)]
        [TestCase(1, false, true)]
        [TestCase(0, false, false)]
        [TestCase(1, true, false)]
        public void DetectionSubmissionRequiresExplicitEmptyScene(int count, bool empty, bool valid)
        {
            var fixture = new VisionWorkflowTestSupport();
            ArrayOf<VisionDetectionDataType> detections = count == 0 ? [] : [Detection()];
            ArrayOf<CompanionValue> inputs = Replace(fixture.Inputs("submit-detections"), "detections",
                Variant.FromStructure(detections));
            inputs = Replace(inputs, "sceneIsEmpty", Variant.From(empty));
            if (valid)
            {
                Assert.That(() => fixture.Validate("submit-detections", inputs), Throws.Nothing);
            }
            else
            {
                Assert.That(() => fixture.Validate("submit-detections", inputs), Throws.ArgumentException);
            }
        }

        [Test]
        public void MissingDetectionsCannotMasqueradeAsAnExplicitEmptyScene()
        {
            var fixture = new VisionWorkflowTestSupport();
            ArrayOf<CompanionValue> inputs =
                Replace(fixture.Inputs("submit-detections"), "detections", Variant.Null);
            inputs = Replace(inputs, "sceneIsEmpty", Variant.From(true));
            Assert.That(() => fixture.Validate("submit-detections", inputs), Throws.ArgumentException);
        }

        [TestCase(0, 0, true, true)]
        [TestCase(1, 0, false, true)]
        [TestCase(0, 1, false, true)]
        [TestCase(0, 0, false, false)]
        [TestCase(1, 1, false, false)]
        [TestCase(1, 0, true, false)]
        [TestCase(0, 1, true, false)]
        [TestCase(1, 1, true, false)]
        public void CorrectionRequiresExactlyOneKindOrExplicitRetraction(
            int detectionCount, int measurementCount, bool retract, bool valid)
        {
            var fixture = new VisionWorkflowTestSupport();
            ArrayOf<VisionDetectionDataType> detections = detectionCount == 0 ? [] : [Detection()];
            ArrayOf<VisionCharacteristicDataType> measurements = measurementCount == 0 ? [] : [Characteristic()];
            ArrayOf<CompanionValue> inputs = Replace(fixture.Inputs("submit-correction"), "detections",
                Variant.FromStructure(detections));
            inputs = Replace(inputs, "characteristics", Variant.FromStructure(measurements));
            inputs = Replace(inputs, "retractAll", Variant.From(retract));
            if (valid)
            {
                Assert.That(() => fixture.Validate("submit-correction", inputs), Throws.Nothing);
            }
            else
            {
                Assert.That(() => fixture.Validate("submit-correction", inputs), Throws.ArgumentException);
            }
        }

        [TestCase("submit-inspection")]
        [TestCase("submit-correction")]
        public void ReviewedResultsRequireAnExactNonemptyIdentifier(string operation)
        {
            var fixture = new VisionWorkflowTestSupport();
            foreach (string value in new[] { string.Empty, " ", new string('r', 257) })
            {
                ArrayOf<CompanionValue> inputs = Replace(fixture.Inputs(operation), "resultId", Variant.From(value));
                Assert.That(() => fixture.Validate(operation, inputs), Throws.ArgumentException);
            }
            ArrayOf<CompanionValue> valid = Replace(fixture.Inputs(operation), "resultId",
                Variant.From(new string('r', 256)));
            Assert.That(() => fixture.Validate(operation, valid), Throws.Nothing);
        }

        [TestCase("get-clip")]
        [TestCase("submit-inspection")]
        [TestCase("submit-correction")]
        [TestCase("submit-image-reference")]
        public void CorrelatingResultIdentifiersHaveAnInclusive256CharacterLimit(string operation)
        {
            var fixture = new VisionWorkflowTestSupport();
            foreach (int length in new[] { 1, 256, 257, 4096 })
            {
                ArrayOf<CompanionValue> inputs = Replace(
                    fixture.Inputs(operation), "resultId", Variant.From(new string('r', length)));
                if (length <= 256)
                {
                    Assert.That(() => fixture.Validate(operation, inputs), Throws.Nothing,
                        $"The {operation} result ID has {length} characters.");
                }
                else
                {
                    Assert.That(() => fixture.Validate(operation, inputs), Throws.ArgumentException,
                        $"The {operation} result ID has {length} characters.");
                }
            }
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase(0, false)]
        [TestCase(4096, true)]
        [TestCase(4097, false)]
        public void CorrectionRequiresBoundedLocalizedReason(int length, bool valid)
        {
            var fixture = new VisionWorkflowTestSupport();
            ArrayOf<CompanionValue> inputs = Replace(fixture.Inputs("submit-correction"), "reason",
                Variant.From(new LocalizedText("en", new string('r', length))));
            if (valid)
            {
                Assert.That(() => fixture.Validate("submit-correction", inputs), Throws.Nothing);
            }
            else
            {
                Assert.That(() => fixture.Validate("submit-correction", inputs), Throws.ArgumentException);
            }
        }

        [Test]
        public void InspectionRequiresMeasuredCharacteristics()
        {
            var fixture = new VisionWorkflowTestSupport();
            ArrayOf<CompanionValue> inputs = Replace(fixture.Inputs("submit-inspection"), "characteristics",
                Variant.FromStructure(ArrayOf<VisionCharacteristicDataType>.Empty));
            Assert.That(() => fixture.Validate("submit-inspection", inputs), Throws.ArgumentException);
        }

        private static int EncodedLength(VisionWorkflowTestSupport fixture, ArrayOf<CompanionValue> inputs)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, fixture.Context.Session.MessageContext, leaveOpen: true))
            {
                foreach (CompanionValue input in inputs)
                {
                    encoder.WriteVariant(null, input.Value);
                }
            }
            return checked((int)stream.Length);
        }

        private static readonly double[] sPosition = [1, 2, 3];
        private static readonly double[] sOrientation = [0, 0, 0, 1];
    }
}
