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
using System.Security.Cryptography;
using Opc.Ua;
using Opc.Ua.Vision;

namespace UaLens.Plugins.Companions.Providers
{
    internal static class VisionWorkflowValues
    {
        public static void Validate(
            CompanionContext context, CompanionOperation operation, ArrayOf<CompanionValue> inputs)
        {
            CompanionInputContract.ValidateShape(operation, inputs);
            using (var buffer = new IndustrialDocumentBuffer(MaximumBytes))
            using (var encoder = new BinaryEncoder(buffer, context.Session.MessageContext, leaveOpen: true))
            {
                foreach (CompanionValue input in inputs)
                {
                    encoder.WriteVariant(null, input.Value);
                }
            }
            switch (operation.Id)
            {
                case "get-clip":
                    _ = ResultId(inputs[1].Value, false);
                    _ = VisionWorkflowAccess.Timestamp(inputs[2].Value);
                    _ = VisionWorkflowAccess.EnumValue<VisionClipFormatEnum>(inputs[3].Value);
                    break;
                case "probe-stream":
                    _ = VisionWorkflowAccess.Text(inputs[1].Value, false);
                    _ = VisionWorkflowAccess.EnumValue<VisionStreamProtocolEnum>(inputs[2].Value);
                    break;
                case "configure-stream":
                    _ = VisionWorkflowAccess.EnumValue<VisionVideoCodecEnum>(inputs[1].Value);
                    uint width = VisionWorkflowAccess.Unsigned(inputs[2].Value, 8192);
                    uint height = VisionWorkflowAccess.Unsigned(inputs[3].Value, 8192);
                    if ((ulong)width * height > MaximumPixels)
                    {
                        throw new ArgumentException("A Vision stream cannot exceed 16 megapixels.");
                    }
                    _ = Rate(inputs[4].Value);
                    _ = VisionWorkflowAccess.Unsigned(inputs[5].Value, 100_000_000);
                    break;
                case "run-inference":
                    _ = VisionWorkflowAccess.Timestamp(inputs[0].Value);
                    break;
                case "run-continuous-window":
                    _ = VisionWorkflowAccess.Unsigned(inputs[0].Value, 15);
                    break;
                case "submit-detections":
                    _ = VisionWorkflowAccess.EnumValue<VisionFeedbackPurposeEnum>(inputs[0].Value);
                    ArrayOf<VisionDetectionDataType> detections =
                        Structures<VisionDetectionDataType>(context, inputs[1].Value);
                    ValidateDetections(detections);
                    if (detections.IsEmpty != VisionWorkflowAccess.Boolean(inputs[4].Value))
                    {
                        throw new ArgumentException("An empty scene must have zero detections, and vice versa.");
                    }
                    VisionImageReferenceDataType? image = Image(context, inputs[2].Value, optional: true);
                    ByteString inline = Bytes(inputs[3].Value);
                    if (image is not null)
                    {
                        ValidateImage(image, inline, requireLocal: true);
                    }
                    else if (!inline.IsEmpty)
                    {
                        throw new ArgumentException("Inline feedback bytes require matching image metadata.");
                    }
                    break;
                case "submit-inspection":
                    _ = ResultId(inputs[0].Value);
                    _ = VisionWorkflowAccess.EnumValue<VisionResultEvaluationEnum>(inputs[1].Value);
                    ArrayOf<VisionCharacteristicDataType> characteristics =
                        Structures<VisionCharacteristicDataType>(context, inputs[2].Value);
                    ValidateCharacteristics(characteristics);
                    if (characteristics.IsEmpty)
                    {
                        throw new ArgumentException("An inspection requires measured characteristics.");
                    }
                    break;
                case "submit-correction":
                    _ = ResultId(inputs[0].Value);
                    _ = VisionWorkflowAccess.EnumValue<VisionFeedbackPurposeEnum>(inputs[1].Value);
                    ArrayOf<VisionDetectionDataType> corrected =
                        Structures<VisionDetectionDataType>(context, inputs[2].Value);
                    ArrayOf<VisionCharacteristicDataType> measurements =
                        Structures<VisionCharacteristicDataType>(context, inputs[3].Value);
                    ValidateDetections(corrected);
                    ValidateCharacteristics(measurements);
                    bool retract = VisionWorkflowAccess.Boolean(inputs[6].Value);
                    if (retract
                        ? !corrected.IsEmpty || !measurements.IsEmpty
                        : corrected.IsEmpty == measurements.IsEmpty)
                    {
                        throw new ArgumentException("Supply one correction kind or explicitly retract all.");
                    }
                    if (!inputs[4].Value.TryGetValue(out LocalizedText reason) ||
                        string.IsNullOrWhiteSpace(reason.Text) ||
                        reason.Text.Length > 4096)
                    {
                        throw new ArgumentException("A bounded correction reason is required.");
                    }
                    _ = Bytes(inputs[5].Value);
                    break;
                case "submit-image-reference":
                    _ = VisionWorkflowAccess.EnumValue<VisionFeedbackPurposeEnum>(inputs[0].Value);
                    ValidateImage(Image(context, inputs[1].Value)!, default, requireLocal: true);
                    _ = ResultId(inputs[2].Value, false);
                    break;
            }
        }

        public static double Rate(Variant value)
        {
            return value.TryGetValue(out double rate) && double.IsFinite(rate) && rate is > 0 and <= 240
                ? rate : throw new ArgumentException("Frame rate must be finite and in (0, 240].");
        }

        public static string ResultId(Variant value, bool required = true)
        {
            string id = VisionWorkflowAccess.Text(value, required);
            return id.Length <= 256 ? id :
                throw new ArgumentException("A Vision result identifier cannot exceed 256 characters.");
        }

        public static ByteString Bytes(Variant value)
        {
            return value.TryGetValue(out ByteString bytes) && bytes.Length <= MaximumBytes
                ? bytes.Copy() : throw new ArgumentException("Vision inline media is limited to 1 MiB.");
        }

        public static ArrayOf<T> Structures<T>(CompanionContext context, Variant value) where T : class, IEncodeable
        {
            if (!value.TryGetValue(out ArrayOf<T> items, context.Session.MessageContext) ||
                items.IsNull ||
                items.Count > MaximumItems)
            {
                throw new ArgumentException("A typed Vision array of at most 32 entries is required.");
            }
            foreach (T item in items)
            {
                ArgumentNullException.ThrowIfNull(item);
            }
            return items;
        }

        public static VisionImageReferenceDataType? Image(
            CompanionContext context, Variant value, bool optional = false)
        {
            if (optional && (value.IsNull || (value.TryGetValue(out ExtensionObject empty) && empty.IsNull)))
            {
                return null;
            }
            return value.TryGetValue<VisionImageReferenceDataType>(
                out VisionImageReferenceDataType? image, context.Session.MessageContext) &&
                image is not null
                ? image : throw new ArgumentException("A typed Vision image reference is required.");
        }

        public static void ValidateImage(
            VisionImageReferenceDataType image, ByteString inline, bool requireLocal = false)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Width is 0 or > 8192 ||
                image.Height is 0 or > 8192 ||
                (ulong)image.Width * image.Height > MaximumPixels ||
                image.SizeBytes is 0 or > MaximumBytes ||
                image.Timestamp == default ||
                image.Digest.Length != 32 ||
                !string.Equals(image.DigestAlgorithm, "SHA-256", StringComparison.OrdinalIgnoreCase) ||
                !Enum.IsDefined(image.Format) ||
                !Uri.TryCreate(image.Uri, UriKind.Absolute, out Uri? uri) ||
                image.Uri.Length > 4096 ||
                (requireLocal && !IsLocalImageReference(uri)))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Invalid bounded Vision frame metadata.");
            }
            if (!inline.IsEmpty &&
                ((uint)inline.Length != image.SizeBytes ||
                    !CryptographicOperations.FixedTimeEquals(SHA256.HashData(inline.Span), image.Digest.Span)))
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed,
                    "Inline Vision bytes do not match their size and SHA-256 evidence.");
            }
        }

        public static void ValidateDetections(ArrayOf<VisionDetectionDataType> items)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (VisionDetectionDataType item in items)
            {
                ArgumentNullException.ThrowIfNull(item);
                if (string.IsNullOrWhiteSpace(item.DetectionId) ||
                    item.DetectionId.Length > 256 ||
                    !ids.Add(item.DetectionId) ||
                    string.IsNullOrWhiteSpace(item.ClassLabel) ||
                    item.ClassLabel.Length > 256 ||
                    !double.IsFinite(item.Confidence) ||
                    item.Confidence is < 0 or > 1)
                {
                    throw new ArgumentException("Vision detections require unique IDs, labels and finite confidence.");
                }
                if (item.HasBoundingBox2D)
                {
                    VisionBoundingBox2DDataType box = item.BoundingBox2D ??
                        throw new ArgumentException("The 2D box flag requires its geometry.");
                    if (!double.IsFinite(box.CenterX) ||
                        !double.IsFinite(box.CenterY) ||
                        !double.IsFinite(box.Width) ||
                        !double.IsFinite(box.Height) ||
                        !double.IsFinite(box.Rotation) ||
                        box.Width <= 0 ||
                        box.Height <= 0)
                    {
                        throw new ArgumentException(
                            "A Vision 2D box must have finite geometry and positive dimensions.");
                    }
                }
                if (item.HasPose)
                {
                    ValidatePose(item.Pose ?? throw new ArgumentException("The pose flag requires a pose."));
                }
                if (item.HasBoundingBox3D)
                {
                    VisionBoundingBox3DDataType box = item.BoundingBox3D ??
                        throw new ArgumentException("The 3D box flag requires its geometry.");
                    ValidatePose(box.Center ??
                        throw new ArgumentException("A Vision 3D box requires its centre pose."));
                    if (box.Size.Count != 3)
                    {
                        throw new ArgumentException("A Vision 3D box requires three positive finite dimensions.");
                    }
                    foreach (double size in box.Size)
                    {
                        if (!double.IsFinite(size) || size <= 0)
                        {
                            throw new ArgumentException("A Vision 3D box requires three positive finite dimensions.");
                        }
                    }
                }
            }
        }

        public static void ValidateCharacteristics(ArrayOf<VisionCharacteristicDataType> items)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (VisionCharacteristicDataType item in items)
            {
                ArgumentNullException.ThrowIfNull(item);
                double deviation = item.Actual - item.Nominal;
                if (string.IsNullOrWhiteSpace(item.CharacteristicId) ||
                    item.CharacteristicId.Length > 256 ||
                    !ids.Add(item.CharacteristicId) ||
                    string.IsNullOrWhiteSpace(item.Name) ||
                    item.Name.Length > 256 ||
                    !double.IsFinite(item.Nominal) ||
                    !double.IsFinite(item.Actual) ||
                    !double.IsFinite(item.Deviation) ||
                    !double.IsFinite(deviation) ||
                    !double.IsFinite(item.Uncertainty) ||
                    !double.IsFinite(item.LowerTolerance) ||
                    !double.IsFinite(item.UpperTolerance) ||
                    item.Uncertainty < 0 ||
                    item.LowerTolerance > item.UpperTolerance ||
                    !Enum.IsDefined(item.Status) ||
                    Math.Abs(item.Deviation - deviation) >
                        1e-9 * Math.Max(1, Math.Abs(item.Deviation)))
                {
                    throw new ArgumentException("Vision characteristics contain inconsistent measurement evidence.");
                }
            }
        }

        private static bool IsLocalImageReference(Uri uri)
        {
            return uri.UserInfo.Length == 0 &&
                uri.Query.Length == 0 &&
                uri.Fragment.Length == 0 &&
                ((uri.Scheme is "http" or "https" && uri.IsLoopback) ||
                    (uri.Scheme == "opcua-inline" &&
                        uri.Host.Length > 0 &&
                        uri.IsDefaultPort &&
                        !uri.AbsolutePath.Contains('\\', StringComparison.Ordinal) &&
                        !uri.AbsolutePath.Contains("%2f", StringComparison.OrdinalIgnoreCase) &&
                        !uri.AbsolutePath.Contains("%5c", StringComparison.OrdinalIgnoreCase)));
        }

        private static void ValidatePose(VisionPose3DDataType pose)
        {
            if (string.IsNullOrWhiteSpace(pose.FrameId) || pose.FrameId.Length > 256)
            {
                throw new ArgumentException("Vision poses require a bounded coordinate frame identifier.");
            }
            VisionInspectionTasks.ValidatePose(pose);
        }

        public const int MaximumBytes = 1024 * 1024;
        public const int MaximumItems = 32;
        public const ulong MaximumPixels = 16 * 1024 * 1024;
    }
}
