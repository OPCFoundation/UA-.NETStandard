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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.AI.Client;
using Opc.Ua.Client;
using Opc.Ua.Vision;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// Additional host authorization when a simulated pipeline's execution
    /// destination is not described by a verifiable local AI deployment.
    /// This never permits physical sensors or replaces deployment confirmation.
    /// </summary>
    internal interface IVisionExecutionPolicy
    {
        ValueTask AuthorizeAsync(
            CompanionContext context, VisionWorkflowTask request, CancellationToken cancellationToken);
    }

    internal sealed record VisionWorkflowBinding(
        NodeId Sensor, NodeId Component, NodeId Deployment, string Identity,
        ByteString Metadata, VisionEndpointStateEnum State, bool Continuous, AIDeploymentSnapshot? AiDeployment)
    {
        public string SensorIdentity { get; init; } = string.Empty;

        public string FrameIdentity { get; init; } = string.Empty;
    }

    internal sealed class VisionWorkflowTask : CompanionTaskInput
    {
        public VisionWorkflowTask(
            CompanionContext context, CompanionTarget target, CompanionOperation operation,
            VisionWorkflowBinding binding, ArrayOf<CompanionValue> inputs, TimeProvider timeProvider)
        {
            Target = target;
            Operation = operation;
            m_binding = binding with { Metadata = binding.Metadata.Copy() };
            m_context = context.Session.MessageContext;
            m_inputs = CompanionInputContract.Snapshot(inputs, m_context);
            m_session = context.Session;
            m_certificateDigest = ByteString.From(SHA256.HashData(context.Session.Endpoint.ServerCertificate.Span));
            m_sessionEvidence = new CompanionOperationDraft(
                target, operation, null, context.Session, timeProvider.GetUtcNow().AddMinutes(5));
            RequestId = Guid.NewGuid().ToString("N");
            Review = $"{operation.DisplayName}\nSensor: {binding.Sensor}; component: {binding.Component}; " +
                $"deployment: {binding.Deployment}.\nObserved identity: {binding.Identity}; " +
                $"state: {binding.State}; continuous: {binding.Continuous}.\n" +
                $"Metadata SHA-256: {Convert.ToHexString(binding.Metadata.Span)}; request: {RequestId}.\n" +
                (binding.AiDeployment is null
                    ? "No AI deployment attests the pipeline destination; a host execution policy is required.\n"
                    : "AI destination: " +
                        (AICompanionProvider.IsOnServerWithoutEndpoint(binding.AiDeployment)
                            ? "the selected OPC UA server (OnServer)" : binding.AiDeployment.EndpointUri) +
                        "; " +
                        $"location: {binding.AiDeployment.InferenceLocation}; " +
                        $"egress permitted: {binding.AiDeployment.EgressPermitted}.\n") +
                "Simulation only. No media URI is fetched and no external backend is implicitly authorized. " +
                "Accepted or ambiguous requests are not replayed. Returned leases are released and acknowledged " +
                "continuous starts are stopped before completion; an unacknowledged start needs manual inspection.";
        }

        public CompanionTarget Target { get; }
        public CompanionOperation Operation { get; }
        public VisionWorkflowBinding Binding => m_binding with { Metadata = m_binding.Metadata.Copy() };
        public string RequestId { get; }
        public ArrayOf<CompanionValue> Inputs => CompanionInputContract.Snapshot(m_inputs, m_context);
        public override string Review { get; }

        public void RequireCurrent(CompanionContext context, TimeProvider timeProvider)
        {
            RequireOwnedSession(context);
            if (!m_sessionEvidence.Matches(context.Session, timeProvider.GetUtcNow()))
            {
                throw new InvalidOperationException("The Vision session or identity changed. Prepare again.");
            }
        }

        public void RequireOwnedSession(CompanionContext context)
        {
            if (!ReferenceEquals(m_session, context.Session) ||
                !m_sessionEvidence.MatchesSession(context.Session) ||
                !CryptographicOperations.FixedTimeEquals(m_certificateDigest.Span,
                    SHA256.HashData(context.Session.Endpoint.ServerCertificate.Span)))
            {
                throw new InvalidOperationException("The Vision session, identity or peer certificate changed.");
            }
        }

        public void BeginExecution()
        {
            if (Interlocked.CompareExchange(ref m_executionStarted, 1, 0) != 0)
            {
                throw new InvalidOperationException(
                    "This Vision request was already dispatched. Inspect its outcome before preparing another.");
            }
        }

        internal static ByteString Digest(CompanionContext context, ArrayOf<CompanionValue> values)
        {
            using var buffer = new IndustrialDocumentBuffer(128 * 1024);
            using var encoder = new BinaryEncoder(buffer, context.Session.MessageContext, leaveOpen: true);
            encoder.SaveStringTable(context.Session.NamespaceUris);
            foreach (CompanionValue value in values)
            {
                encoder.WriteString(null, value.Name);
                encoder.WriteVariant(null, value.Value);
            }
            return ByteString.From(SHA256.HashData(encoder.CloseAndReturnBuffer()!));
        }

        private readonly ISession m_session;
        private readonly VisionWorkflowBinding m_binding;
        private readonly ByteString m_certificateDigest;
        private readonly IServiceMessageContext m_context;
        private readonly CompanionOperationDraft m_sessionEvidence;
        private readonly ArrayOf<CompanionValue> m_inputs;
        private int m_executionStarted;
    }

    internal sealed record VisionTaskDefinition(CompanionOperation Operation, string Method, string Component)
    {
        public static ArrayOf<VisionTaskDefinition> ForTarget(string typeName)
        {
            return typeName switch
            {
                "VisionSensor" => sSensor,
                "InferencePipeline" => sPipeline,
                _ => []
            };
        }

        public static VisionTaskDefinition Find(CompanionTarget target, string id)
        {
            foreach (VisionTaskDefinition definition in ForTarget(target.TypeName))
            {
                if (definition.Operation.Id == id)
                {
                    return definition;
                }
            }
            throw new ServiceResultException(StatusCodes.BadNotSupported, "This Vision task is not offered here.");
        }

        private static VisionTaskDefinition Task(
            string id, string title, string method, string component, ArrayOf<CompanionInputDefinition> inputs)
        {
            return new VisionTaskDefinition(new CompanionOperation(
                id, title, CompanionOperationSafety.DeploymentMutation)
            { Inputs = inputs }, method, component);
        }

        private static CompanionInputDefinition Structure(
            string name, string title, ExpandedNodeId type, bool array = false)
        {
            return new CompanionInputDefinition(name, title, BuiltInType.ExtensionObject, "Review the typed value.")
            {
                DataTypeId = type,
                ValueRank = array ? ValueRanks.OneDimension : ValueRanks.Scalar
            };
        }

        private static CompanionInputDefinition Enumeration(string name, string title, ExpandedNodeId type)
        {
            return new CompanionInputDefinition(name, title, BuiltInType.Enumeration, "Choose the declared enum value.")
            {
                DataTypeId = type
            };
        }

        private static readonly CompanionInputDefinition sEndpoint =
            new("endpoint", "Media endpoint NodeId", BuiltInType.NodeId, "An exact endpoint belonging to this sensor.");

        private static readonly CompanionInputDefinition sTime =
            new("timestamp", "Acquisition time", BuiltInType.DateTime, "UTC time; no deadline or delivery guarantee.");

        private static readonly CompanionInputDefinition sResult =
            new("resultId", "Result ID", BuiltInType.String, "Exact correlation identifier.", Required: false);

        private static readonly CompanionInputDefinition sInline =
            new("inlineImage", "Inline image bytes", BuiltInType.ByteString, "Base64, at most 1 MiB.", Required: false);

        private static readonly CompanionInputDefinition sPurpose =
            Enumeration("purpose", "Feedback purpose", Opc.Ua.Vision.DataTypeIds.VisionFeedbackPurposeEnum);

        private static readonly CompanionInputDefinition sDetections =
            Structure("detections", "Detections", Opc.Ua.Vision.DataTypeIds.VisionDetectionDataType, true);

        private static readonly CompanionInputDefinition sCharacteristics =
            Structure("characteristics", "Characteristics",
                Opc.Ua.Vision.DataTypeIds.VisionCharacteristicDataType, true);

        private static readonly CompanionInputDefinition sImage =
            Structure("image", "Frame reference", Opc.Ua.Vision.DataTypeIds.VisionImageReferenceDataType);

        private static readonly ArrayOf<VisionTaskDefinition> sSensor =
        [
            Task("get-clip", "Capture one simulated frame", "GetClip", "Media",
                [sEndpoint, sResult, sTime,
                    Enumeration("format", "Clip format", Opc.Ua.Vision.DataTypeIds.VisionClipFormatEnum),
                    new("requestInline", "Request inline bytes", BuiltInType.Boolean, "No URI download fallback.")]),
            Task("probe-stream", "Acquire and release a simulated stream lease", "GetStreamEndpoint", "Media",
                [sEndpoint, new("profile", "Profile", BuiltInType.String,
                    "Exact configured stream profile, or empty for its default.", Required: false),
                    Enumeration("protocol", "Stream protocol", Opc.Ua.Vision.DataTypeIds.VisionStreamProtocolEnum)]),
            Task("configure-stream", "Configure the selected simulated stream", "ConfigureStreamEndpoint", "Media",
                [sEndpoint, Enumeration("codec", "Video codec", Opc.Ua.Vision.DataTypeIds.VisionVideoCodecEnum),
                    new("width", "Width", BuiltInType.UInt32, "1-8192 pixels"),
                    new("height", "Height", BuiltInType.UInt32, "1-8192 pixels; at most 16 megapixels"),
                    new("frameRate", "Frame rate", BuiltInType.Double, ">0 and at most 240 frames/second"),
                    new("bitrate", "Bit rate", BuiltInType.UInt32, "1-100000000 bits/second")]),
            Task("select-endpoints", "Select simulated media endpoints", "SelectEndpoint", "Media",
                [new("stream", "Stream endpoint", BuiltInType.NodeId,
                    "Exact stream endpoint or null to leave unchanged."),
                    new("clip", "Clip endpoint", BuiltInType.NodeId,
                        "Exact clip endpoint or null to leave unchanged.")])
        ];

        private static readonly ArrayOf<VisionTaskDefinition> sPipeline =
        [
            Task("run-inference", "Run one simulated inference", "RunInference", string.Empty, [sTime]),
            Task("run-continuous-window", "Run and stop a bounded simulated pipeline", "StartContinuous", string.Empty,
                [new("seconds", "Run window", BuiltInType.UInt32, "1-15 seconds, followed by an owned Stop.")]),
            Task("submit-detections", "Submit reviewed detections", "SubmitDetections", "Feedback",
                [sPurpose, sDetections, sImage with { Required = false }, sInline,
                    new("sceneIsEmpty", "Observed empty scene", BuiltInType.Boolean,
                        "True only when zero detections is a deliberate observation.")]),
            Task("submit-inspection", "Submit a reviewed inspection", "SubmitInspectionResult", "Feedback",
                [sResult with { Required = true },
                    Enumeration("evaluation", "Evaluation", Opc.Ua.Vision.DataTypeIds.VisionResultEvaluationEnum),
                    sCharacteristics]),
            Task("submit-correction", "Submit a reviewed result correction", "SubmitCorrection", "Feedback",
                [sResult with { Required = true }, sPurpose, sDetections, sCharacteristics,
                    new("reason", "Correction reason", BuiltInType.LocalizedText, "Explicit reason."),
                    sInline, new("retractAll", "Retract all results", BuiltInType.Boolean,
                        "Requires both corrected arrays to be empty.")]),
            Task("submit-image-reference", "Submit reviewed frame evidence", "SubmitImageReference", "Feedback",
                [sPurpose, sImage, sResult])
        ];
    }
}
