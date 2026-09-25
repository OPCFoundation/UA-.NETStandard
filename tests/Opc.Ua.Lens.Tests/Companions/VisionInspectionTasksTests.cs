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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Vision;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using static UaLens.Tests.Companions.CellProviderTestSession;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class VisionInspectionTasksTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task TypedCalibrationInspectionRetainsValuesAndDoesNotSolveOrWrite(bool extrinsic)
        {
            CellProviderTestSession fixture = Fixture(extrinsic);
            CompanionOperationResult result = await VisionInspectionTasks.ReadCalibrationsAsync(
                fixture.Context, sSensor, CancellationToken.None).ConfigureAwait(false);

            Assert.That(Field(result.Values, "Calibration 1 ID").TryGetValue(out string? id), Is.True);
            Assert.That(id, Is.EqualTo("calibration-1"));
            Assert.That(Field(result.Values, "Calibration 1 valid").TryGetValue(out bool valid), Is.True);
            Assert.That(valid, Is.True);
            Assert.That(Field(result.Values, "Calibration 1 residual error").TryGetValue(out double residual), Is.True);
            Assert.That(residual, Is.EqualTo(0.125));
            if (extrinsic)
            {
                Assert.That(Field(result.Values, "Calibration 1 position metres").TryGetValue(
                    out ArrayOf<double> position), Is.True);
                Assert.That(position.ToArray(), Is.EqualTo(sPosition));
            }
            else
            {
                Assert.That(Field(result.Values, "Calibration 1 focal X pixels").TryGetValue(out double focal),
                    Is.True);
                Assert.That(focal, Is.EqualTo(640));
                Assert.That(Field(result.Values, "Calibration 1 width").TryGetValue(out uint width), Is.True);
                Assert.That(width, Is.EqualTo(1280));
            }
            Assert.That(result.Summary, Does.Contain("1 published").And.Contain("no supported typed client contract"));
            fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase("Valid")]
        [TestCase("ResidualError")]
        [TestCase("Intrinsics")]
        public async Task DeniedCalibrationMembersCannotBeDisplayedAsSuccessfulDefaults(string member)
        {
            CellProviderTestSession fixture = Fixture(false);
            NodeId property = fixture.Children[(sCalibration, member)];
            fixture.Values[(property, Attributes.Value)] = fixture.Values[(property, Attributes.Value)]
                .WithStatus(StatusCodes.BadUserAccessDenied);

            await Assert.ThatAsync(() => VisionInspectionTasks.ReadCalibrationsAsync(
                fixture.Context, sSensor, CancellationToken.None).AsTask(),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadUserAccessDenied)).ConfigureAwait(false);

            fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase("fx")]
        [TestCase("fy")]
        [TestCase("cx")]
        [TestCase("cy")]
        [TestCase("skew")]
        [TestCase("width")]
        [TestCase("height")]
        [TestCase("coefficients")]
        [TestCase("nonfinite-coefficient")]
        [TestCase("model")]
        public void InvalidIntrinsicsCannotBePresentedAsUsable(string fault)
        {
            VisionIntrinsicsDataType value = Intrinsics();
            switch (fault)
            {
                case "fx":
                    value.Fx = 0;
                    break;
                case "fy":
                    value.Fy = -1;
                    break;
                case "cx":
                    value.Cx = double.NaN;
                    break;
                case "cy":
                    value.Cy = double.PositiveInfinity;
                    break;
                case "skew":
                    value.Skew = double.NaN;
                    break;
                case "width":
                    value.Width = 0;
                    break;
                case "height":
                    value.Height = 0;
                    break;
                case "coefficients":
                    value.DistortionCoefficients = new double[17];
                    break;
                case "nonfinite-coefficient":
                    value.DistortionCoefficients = [double.NaN];
                    break;
                case "model":
                    value.DistortionModel = (VisionDistortionModelEnum)99;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }
            Assert.That(() => VisionInspectionTasks.AddIntrinsics(new CellCompanionFields(32), "Calibration", value),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadTypeMismatch));
        }

        [TestCase(0)]
        [TestCase(16)]
        public void IntrinsicsPreserveCoefficientCountAndIndependentStorage(int count)
        {
            double[] coefficients = [.. Enumerable.Range(0, count).Select(index => index / 100d)];
            VisionIntrinsicsDataType value = Intrinsics();
            value.DistortionCoefficients = coefficients;
            var fields = new CellCompanionFields(16);

            VisionInspectionTasks.AddIntrinsics(fields, "Calibration", value);
            if (count > 0)
            {
                coefficients[0] = 999;
            }

            Assert.That(Field(fields.ToArray(), "Calibration distortion coefficients").TryGetValue(
                out ArrayOf<double> captured), Is.True);
            Assert.That(captured.Count, Is.EqualTo(count));
            if (count > 0)
            {
                Assert.That(captured[0], Is.Zero);
            }
        }

        [TestCase("position")]
        [TestCase("orientation")]
        [TestCase("covariance")]
        [TestCase("quaternion")]
        [TestCase("nan-position")]
        [TestCase("nan-orientation")]
        [TestCase("nan-covariance")]
        public void MalformedPosesAreRejected(string fault)
        {
            VisionPose3DDataType pose = Pose();
            switch (fault)
            {
                case "position":
                    pose.Position = [1, 2];
                    break;
                case "orientation":
                    pose.Orientation = [0, 0, 1];
                    break;
                case "covariance":
                    pose.Covariance = [1];
                    break;
                case "quaternion":
                    pose.Orientation = [0, 0, 0, 2];
                    break;
                case "nan-position":
                    pose.Position = [double.NaN, 2, 3];
                    break;
                case "nan-orientation":
                    pose.Orientation = [0, 0, 0, double.NaN];
                    break;
                case "nan-covariance":
                    double[] covariance = new double[36];
                    covariance[0] = double.NaN;
                    pose.Covariance = covariance;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }
            Assert.That(() => VisionInspectionTasks.ValidatePose(pose), Throws.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadTypeMismatch));
        }

        [TestCase("id")]
        [TestCase("performed")]
        [TestCase("negative-residual")]
        [TestCase("nonfinite-residual")]
        public async Task InvalidCalibrationIdentityNeverProducesASuccessfulInspection(string fault)
        {
            CellProviderTestSession fixture = Fixture(false);
            string property = fault == "id" ? "CalibrationId" : fault == "performed" ? "PerformedAt" : "ResidualError";
            Variant value = fault switch
            {
                "id" => Variant.From(string.Empty),
                "performed" => Variant.From(default(DateTimeUtc)),
                "negative-residual" => Variant.From(-1d),
                _ => Variant.From(double.NaN)
            };
            fixture.Values[(fixture.Children[(sCalibration, property)], Attributes.Value)] = new DataValue(value);

            await Assert.ThatAsync(() => VisionInspectionTasks.ReadCalibrationsAsync(
                fixture.Context, sSensor, CancellationToken.None).AsTask(),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadTypeMismatch)).ConfigureAwait(false);
        }

        [TestCase(16)]
        [TestCase(17)]
        public async Task CalibrationEnumerationHonorsTheExactCap(int count)
        {
            CellProviderTestSession fixture = Fixture(false, 16);
            fixture.Browses[sSensor] = Enumerable.Range(0, count).Select(index => fixture.Reference(
                new NodeId((uint)index + 9000, fixture.NamespaceIndex), "unknown" + index,
                new NodeId(Opc.Ua.Vision.ObjectTypes.VisionCalibrationType, fixture.NamespaceIndex))).ToArray();
            if (count == 16)
            {
                CompanionOperationResult result = await VisionInspectionTasks.ReadCalibrationsAsync(
                    fixture.Context, sSensor, CancellationToken.None).ConfigureAwait(false);
                Assert.That(result.Summary, Does.Contain("16 published"));
                Assert.That(result.Values.Count, Is.EqualTo(33));
            }
            else
            {
                await Assert.ThatAsync(() => VisionInspectionTasks.ReadCalibrationsAsync(
                    fixture.Context, sSensor, CancellationToken.None).AsTask(), Throws.TypeOf<ServiceResultException>()
                        .With.Property("StatusCode").EqualTo(StatusCodes.BadEncodingLimitsExceeded))
                        .ConfigureAwait(false);
            }
        }

        private static CellProviderTestSession Fixture(bool extrinsic, int maxTargets = 128)
        {
            var fixture = new CellProviderTestSession(Opc.Ua.Vision.Namespaces.Vision, maxTargets);
            var type = new NodeId(extrinsic ? Opc.Ua.Vision.ObjectTypes.ExtrinsicCalibrationType :
                Opc.Ua.Vision.ObjectTypes.IntrinsicCalibrationType, fixture.NamespaceIndex);
            var baseType = new NodeId(Opc.Ua.Vision.ObjectTypes.VisionCalibrationType, fixture.NamespaceIndex);
            fixture.NodeCache.Setup(cache => cache.IsTypeOfAsync(
                It.IsAny<NodeId>(), baseType, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            fixture.Browses[sSensor] = [fixture.Reference(sCalibration, "calibration", type)];
            fixture.AddValue(sCalibration, "CalibrationId", Variant.From("calibration-1"));
            fixture.AddValue(sCalibration, "PerformedAt", Variant.From(new DateTimeUtc(2026, 9, 13, 12)));
            fixture.AddValue(sCalibration, "Valid", Variant.From(true));
            fixture.AddValue(sCalibration, "ResidualError", Variant.From(0.125));
            fixture.AddValue(sCalibration, "Method", Variant.From("synthetic fixture"));
            if (extrinsic)
            {
                fixture.AddValue(sCalibration, "Mount", Variant.From((int)VisionCalibrationMountEnum.Fixed));
                fixture.AddValue(sCalibration, "SourceFrame",
                    Variant.From(new NodeId("camera", fixture.NamespaceIndex)));
                fixture.AddValue(sCalibration, "TargetFrame",
                    Variant.From(new NodeId("world", fixture.NamespaceIndex)));
                fixture.AddValue(sCalibration, "Transform", Variant.FromStructure(Pose()));
            }
            else
            {
                fixture.AddValue(sCalibration, "Intrinsics", Variant.FromStructure(Intrinsics()));
            }
            return fixture;
        }

        private static VisionIntrinsicsDataType Intrinsics()
        {
            return new()
            {
                Fx = 640,
                Fy = 640,
                Cx = 640.5,
                Cy = 360.5,
                Width = 1280,
                Height = 720,
                DistortionModel = VisionDistortionModelEnum.None,
                DistortionCoefficients = []
            };
        }

        private static VisionPose3DDataType Pose()
        {
            return new()
            {
                FrameId = "world",
                Position = sPosition,
                Orientation = [0, 0, 0, 1],
                Covariance = []
            };
        }

        private static readonly NodeId sSensor = new("sensor", 1);
        private static readonly NodeId sCalibration = new("calibration", 1);
        private static readonly double[] sPosition = [1, 2, 3];
    }
}
