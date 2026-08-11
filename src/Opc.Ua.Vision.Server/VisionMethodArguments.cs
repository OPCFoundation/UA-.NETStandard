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

using Opc.Ua.Vision.Server.Builders;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Declares the <c>InputArguments</c> and <c>OutputArguments</c>
    /// Properties of the Vision Methods.
    /// </summary>
    /// <remarks>
    /// The NodeSet now browse-names its Argument Properties correctly, so the
    /// generated <c>CreateInstanceOf…MethodType</c> factories carry the
    /// signatures. The builder does not use those factories, though: the
    /// Methods here are Optional children materialised by
    /// <c>CreateOrReplace…</c>, which constructs the state object directly and
    /// leaves <c>InputArguments</c> unset. A Method reached that way is
    /// uncallable — <c>MethodState.Call</c> compares the supplied arguments
    /// against an <c>InputArguments</c> Property it cannot find and concludes
    /// that none were expected, so every call carrying arguments is refused
    /// with <c>Bad_TooManyArguments</c> — and a client cannot discover the
    /// signature either. Declaring them here closes that gap. Removing this
    /// class makes five of the eight <c>VisionMethodSurfaceTests</c> fail, which
    /// is the check to repeat if the generator ever starts populating them.
    /// The declarations match the specification's Method definitions and the
    /// generated Method state classes argument for argument.
    /// </remarks>
    internal static class VisionMethodArguments
    {
        internal static void Declare(ISystemContext context, RunInferenceMethodState method)
        {
            SetInput(context, method, Argument("Timestamp", global::Opc.Ua.DataTypeIds.DateTime));
            SetOutput(context, method, Argument("ResultId", global::Opc.Ua.DataTypeIds.String));
        }

        internal static void DeclareStartContinuous(ISystemContext context, MethodState method)
        {
            SetInput(context, method);
            SetOutput(context, method);
        }

        internal static void DeclareStop(ISystemContext context, MethodState method)
        {
            SetInput(context, method);
            SetOutput(context, method);
        }

        internal static void Declare(ISystemContext context, SubmitDetectionsMethodState method)
        {
            SetInput(
                context,
                method,
                Argument("Purpose", VisionDataType(context, DataTypeIds.VisionFeedbackPurposeEnum)),
                Argument(
                    "Detections",
                    VisionDataType(context, DataTypeIds.VisionDetectionDataType),
                    ValueRanks.OneDimension),
                Argument("FrameReference", VisionDataType(context, DataTypeIds.VisionImageReferenceDataType)),
                Argument("InlineImage", global::Opc.Ua.DataTypeIds.ByteString),
                Argument("SceneIsEmpty", global::Opc.Ua.DataTypeIds.Boolean));
            SetOutput(context, method);
        }

        internal static void Declare(ISystemContext context, SubmitInspectionResultMethodState method)
        {
            SetInput(
                context,
                method,
                Argument("ResultId", global::Opc.Ua.DataTypeIds.String),
                Argument("Evaluation", VisionDataType(context, DataTypeIds.VisionResultEvaluationEnum)),
                Argument(
                    "Characteristics",
                    VisionDataType(context, DataTypeIds.VisionCharacteristicDataType),
                    ValueRanks.OneDimension));
            SetOutput(context, method);
        }

        internal static void Declare(ISystemContext context, SubmitCorrectionMethodState method)
        {
            SetInput(
                context,
                method,
                Argument("ResultId", global::Opc.Ua.DataTypeIds.String),
                Argument("Purpose", VisionDataType(context, DataTypeIds.VisionFeedbackPurposeEnum)),
                Argument(
                    "CorrectedDetections",
                    VisionDataType(context, DataTypeIds.VisionDetectionDataType),
                    ValueRanks.OneDimension),
                Argument(
                    "CorrectedCharacteristics",
                    VisionDataType(context, DataTypeIds.VisionCharacteristicDataType),
                    ValueRanks.OneDimension),
                Argument("Reason", global::Opc.Ua.DataTypeIds.LocalizedText),
                Argument("InlineImage", global::Opc.Ua.DataTypeIds.ByteString),
                Argument("RetractAll", global::Opc.Ua.DataTypeIds.Boolean));
            SetOutput(context, method);
        }

        internal static void Declare(ISystemContext context, SubmitImageReferenceMethodState method)
        {
            SetInput(
                context,
                method,
                Argument("Purpose", VisionDataType(context, DataTypeIds.VisionFeedbackPurposeEnum)),
                Argument("Image", VisionDataType(context, DataTypeIds.VisionImageReferenceDataType)),
                Argument("ResultId", global::Opc.Ua.DataTypeIds.String));
            SetOutput(context, method);
        }

        internal static void Declare(ISystemContext context, GetStreamEndpointMethodState method)
        {
            SetInput(
                context,
                method,
                Argument("Endpoint", global::Opc.Ua.DataTypeIds.NodeId),
                Argument("ProfileName", global::Opc.Ua.DataTypeIds.String),
                Argument("PreferredProtocol", VisionDataType(context, DataTypeIds.VisionStreamProtocolEnum)));
            SetOutput(
                context,
                method,
                Argument("Session", VisionDataType(context, DataTypeIds.VisionStreamSessionDataType)),
                Argument("Endpoint", global::Opc.Ua.DataTypeIds.NodeId));
        }

        internal static void Declare(ISystemContext context, ReleaseStreamEndpointMethodState method)
        {
            SetInput(context, method, Argument("SessionToken", global::Opc.Ua.DataTypeIds.ByteString));
            SetOutput(context, method);
        }

        internal static void Declare(ISystemContext context, ConfigureStreamEndpointMethodState method)
        {
            SetInput(
                context,
                method,
                Argument("Endpoint", global::Opc.Ua.DataTypeIds.NodeId),
                Argument("Codec", VisionDataType(context, DataTypeIds.VisionVideoCodecEnum)),
                Argument("Width", global::Opc.Ua.DataTypeIds.UInt32),
                Argument("Height", global::Opc.Ua.DataTypeIds.UInt32),
                Argument("FrameRate", global::Opc.Ua.DataTypeIds.Double),
                Argument("Bitrate", global::Opc.Ua.DataTypeIds.UInt32));
            SetOutput(context, method);
        }

        internal static void Declare(ISystemContext context, SelectEndpointMethodState method)
        {
            SetInput(
                context,
                method,
                Argument("StreamEndpoint", global::Opc.Ua.DataTypeIds.NodeId),
                Argument("ClipEndpoint", global::Opc.Ua.DataTypeIds.NodeId));
            SetOutput(context, method);
        }

        internal static void Declare(ISystemContext context, GetClipMethodState method)
        {
            SetInput(
                context,
                method,
                Argument("Endpoint", global::Opc.Ua.DataTypeIds.NodeId),
                Argument("ResultId", global::Opc.Ua.DataTypeIds.String),
                Argument("Timestamp", global::Opc.Ua.DataTypeIds.DateTime),
                Argument("Format", VisionDataType(context, DataTypeIds.VisionClipFormatEnum)),
                Argument("RequestInline", global::Opc.Ua.DataTypeIds.Boolean));
            SetOutput(
                context,
                method,
                Argument("Image", VisionDataType(context, DataTypeIds.VisionImageReferenceDataType)),
                Argument("Endpoint", global::Opc.Ua.DataTypeIds.NodeId),
                Argument("InlineImage", global::Opc.Ua.DataTypeIds.ByteString));
        }

        private static void SetInput(
            ISystemContext context,
            MethodState method,
            params Argument[] arguments)
        {
            method.CreateOrReplaceInputArguments(context, null).Value = arguments.ToArrayOf();
        }

        private static void SetOutput(
            ISystemContext context,
            MethodState method,
            params Argument[] arguments)
        {
            method.CreateOrReplaceOutputArguments(context, null).Value = arguments.ToArrayOf();
        }

        private static Argument Argument(
            string name,
            NodeId dataType,
            int valueRank = ValueRanks.Scalar)
        {
            return new Argument
            {
                Name = name,
                DataType = dataType,
                ValueRank = valueRank
            };
        }

        private static NodeId VisionDataType(ISystemContext context, ExpandedNodeId dataType)
        {
            return ExpandedNodeId.ToNodeId(dataType, context.NamespaceUris);
        }
    }
}
