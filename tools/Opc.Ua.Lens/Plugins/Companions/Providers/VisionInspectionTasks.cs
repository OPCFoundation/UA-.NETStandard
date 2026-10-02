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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// Bounded calibration inspection on the borrowed session; never changes a calibration or moves a device.
    /// </summary>
    internal static class VisionInspectionTasks
    {
        public static async ValueTask<CompanionOperationResult> ReadCalibrationsAsync(
            CompanionContext context,
            NodeId sensorId,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime =
                CellCompanionSupport.BeginOperation(context, Opc.Ua.Vision.Namespaces.Vision, cancellationToken);
            CancellationToken token = lifetime.Token;
            var client = new VisionClient(context.Session, context.Telemetry);
            VisionSensorClient sensor = client.Sensor(sensorId);
            var fields = new CellCompanionFields(context.MaxFields);
            fields.Add("Sensor", Variant.From(sensorId));
            int count = 0;
            await foreach (VisionNodeEntry entry in sensor.EnumerateCalibrationsAsync(token)
                .WithCancellation(token).ConfigureAwait(false))
            {
                CellCompanionSupport.CheckCount(++count, Math.Min(context.MaxTargets, 16), "Vision calibrations");
                string prefix = $"Calibration {count}";
                fields.Add($"{prefix} node", Variant.From(entry.NodeId));
                if (await IsTypeAsync(context, entry.TypeDefinitionId,
                    Opc.Ua.Vision.ObjectTypes.IntrinsicCalibrationType, token).ConfigureAwait(false))
                {
                    VisionIntrinsicCalibrationSnapshot calibration = await sensor.ReadIntrinsicCalibrationAsync(
                        entry.NodeId, token).ConfigureAwait(false);
                    AddIdentity(fields, prefix, calibration.CalibrationId, calibration.PerformedAt,
                        calibration.Valid, calibration.ResidualError, calibration.Method);
                    AddIntrinsics(fields, prefix, calibration.Intrinsics);
                }
                else if (await IsTypeAsync(context, entry.TypeDefinitionId,
                    Opc.Ua.Vision.ObjectTypes.ExtrinsicCalibrationType, token).ConfigureAwait(false))
                {
                    VisionExtrinsicCalibrationSnapshot calibration = await sensor.ReadExtrinsicCalibrationAsync(
                        entry.NodeId, token).ConfigureAwait(false);
                    AddIdentity(fields, prefix, calibration.CalibrationId, calibration.PerformedAt,
                        calibration.Valid, calibration.ResidualError, calibration.Method);
                    fields.AddText($"{prefix} mount", calibration.Mount.ToString());
                    fields.Add($"{prefix} source frame", Variant.From(calibration.SourceFrameId));
                    fields.Add($"{prefix} target frame", Variant.From(calibration.TargetFrameId));
                    if (calibration.Transform is { } transform)
                    {
                        ValidatePose(transform);
                        fields.AddText($"{prefix} transform frame ID", transform.FrameId);
                        fields.Add($"{prefix} position metres", Variant.From(transform.Position.Span.ToArray()));
                        fields.Add($"{prefix} quaternion XYZW", Variant.From(transform.Orientation.Span.ToArray()));
                    }
                }
                else
                {
                    fields.AddText($"{prefix} capability",
                        "Unsupported calibration subtype; no write or fallback method was attempted.");
                }
            }
            token.ThrowIfCancellationRequested();
            return new CompanionOperationResult(
                $"Read {count} published draft Vision calibration(s). Invalid calibrations are not usable. " +
                "Calibration creation, solving and writes have no supported typed client contract.", fields.ToArray());
        }

        public static void AddIntrinsics(
            CellCompanionFields fields, string prefix, VisionIntrinsicsDataType? intrinsics)
        {
            if (intrinsics is null)
            {
                fields.AddText($"{prefix} intrinsics", "Not published.");
                return;
            }
            if (!double.IsFinite(intrinsics.Fx) ||
                !double.IsFinite(intrinsics.Fy) ||
                intrinsics.Fx <= 0 ||
                intrinsics.Fy <= 0 ||
                !double.IsFinite(intrinsics.Cx) ||
                !double.IsFinite(intrinsics.Cy) ||
                !double.IsFinite(intrinsics.Skew) ||
                intrinsics.Width == 0 ||
                intrinsics.Height == 0 ||
                !Enum.IsDefined(intrinsics.DistortionModel) ||
                intrinsics.DistortionCoefficients.Count > 16)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Malformed Vision intrinsics.");
            }
            foreach (double coefficient in intrinsics.DistortionCoefficients)
            {
                if (!double.IsFinite(coefficient))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Non-finite distortion coefficient.");
                }
            }
            fields.Add($"{prefix} focal X pixels", Variant.From(intrinsics.Fx));
            fields.Add($"{prefix} focal Y pixels", Variant.From(intrinsics.Fy));
            fields.Add($"{prefix} principal X pixels", Variant.From(intrinsics.Cx));
            fields.Add($"{prefix} principal Y pixels", Variant.From(intrinsics.Cy));
            fields.Add($"{prefix} skew", Variant.From(intrinsics.Skew));
            fields.Add($"{prefix} width", Variant.From(intrinsics.Width));
            fields.Add($"{prefix} height", Variant.From(intrinsics.Height));
            fields.AddText($"{prefix} distortion model", intrinsics.DistortionModel.ToString());
            ArrayOf<double> coefficients = intrinsics.DistortionCoefficients.IsNull
                ? default : new ArrayOf<double>(intrinsics.DistortionCoefficients.Span.ToArray());
            fields.Add($"{prefix} distortion coefficients", Variant.From(coefficients));
        }

        public static void ValidatePose(VisionPose3DDataType pose)
        {
            if (pose.Position.Count != 3 || pose.Orientation.Count != 4 || pose.Covariance.Count is not (0 or 36))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Malformed Vision pose dimensions.");
            }
            double norm = 0;
            foreach (double value in pose.Orientation)
            {
                norm += value * value;
            }
            if (!double.IsFinite(norm) || Math.Abs(norm - 1) > 1e-6)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "A Vision quaternion must be unit length.");
            }
            foreach (double value in pose.Position)
            {
                RequireFinite(value);
            }
            foreach (double value in pose.Covariance)
            {
                RequireFinite(value);
            }
        }

        private static void AddIdentity(
            CellCompanionFields fields, string prefix, string? id, DateTimeUtc performed,
            bool valid, double residual, string? method)
        {
            if (string.IsNullOrWhiteSpace(id) || performed == default || residual < 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "Incomplete Vision calibration identity.");
            }
            RequireFinite(residual);
            fields.AddText($"{prefix} ID", id);
            fields.Add($"{prefix} performed", Variant.From(performed));
            fields.Add($"{prefix} valid", Variant.From(valid));
            fields.Add($"{prefix} residual error", Variant.From(residual));
            fields.AddText($"{prefix} method", method);
        }

        private static ValueTask<bool> IsTypeAsync(
            CompanionContext context, NodeId type, uint identifier, CancellationToken cancellationToken)
        {
            var expected = new NodeId(identifier,
                (ushort)context.Session.NamespaceUris.GetIndex(Opc.Ua.Vision.Namespaces.Vision));
            return context.Session.NodeCache.IsTypeOfAsync(type, expected, cancellationToken);
        }

        private static void RequireFinite(double value)
        {
            if (!double.IsFinite(value))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Non-finite Vision calibration value.");
            }
        }
    }
}
