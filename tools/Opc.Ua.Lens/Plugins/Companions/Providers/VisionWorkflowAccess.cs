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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.AI.Client;
using Opc.Ua.Vision;

namespace UaLens.Plugins.Companions.Providers
{
    internal static class VisionWorkflowAccess
    {
        public static async Task<VisionWorkflowBinding> ReadBindingAsync(
            CompanionContext context, CompanionTarget target, VisionTaskDefinition definition,
            ArrayOf<CompanionValue> inputs, CancellationToken cancellationToken)
        {
            await IndustrialCompanionAccess.RequireTargetAsync(
                context, target, "vision", sTargets, cancellationToken).ConfigureAwait(false);
            AICompanionTaskAccess.RequireSecureChannel(context);
            NodeId sensor = target.NodeId;
            NodeId deployment = NodeId.Null;
            VisionEndpointStateEnum state = VisionEndpointStateEnum.Ready;
            bool continuous = false;
            AIDeploymentSnapshot? aiDeployment = null;
            var evidence = new List<CompanionValue>();
            string identity;
            if (target.TypeName == "InferencePipeline")
            {
                ArrayOf<CompanionValue> properties = await PropertiesAsync(
                    context, target.NodeId, ["PipelineId", "Sensor", "Deployment", "State", "Continuous"],
                    cancellationToken)
                    .ConfigureAwait(false);
                identity = Text(properties[0].Value);
                sensor = Node(properties[1].Value);
                deployment = Node(properties[2].Value, allowNull: true);
                state = EnumValue<VisionEndpointStateEnum>(properties[3].Value);
                continuous = Boolean(properties[4].Value);
                evidence.AddRange(properties.ToList());
                await IndustrialCompanionAccess.RequireTargetAsync(context,
                    new CompanionTarget("vision", sensor, "Pipeline sensor", "VisionSensor"),
                    "vision", sTargets, cancellationToken).ConfigureAwait(false);
                if (!deployment.IsNull &&
                    context.Session.NamespaceUris.GetIndex(Opc.Ua.AI.Namespaces.AI) >= 0)
                {
                    await AICompanionTaskAccess.RequireInstanceAsync(
                        context, deployment, "Deployment", cancellationToken).ConfigureAwait(false);
                    aiDeployment = await AICompanionTaskAccess.ReadDeploymentAsync(
                        context, deployment, cancellationToken).ConfigureAwait(false);
                    evidence.AddRange(DeploymentEvidence(aiDeployment).ToList());
                }
            }
            else
            {
                identity = target.Identifier;
            }
            ArrayOf<CompanionValue> sensorValues = await PropertiesAsync(
                context, sensor, ["SensorId", "RealityKind", "FrameId"], cancellationToken).ConfigureAwait(false);
            _ = Text(sensorValues[0].Value);
            _ = Text(sensorValues[2].Value);
            if (EnumValue<VisionRealityKindEnum>(sensorValues[1].Value) != VisionRealityKindEnum.Simulated)
            {
                throw new UnauthorizedAccessException("Vision workflow mutations are limited to simulated sensors.");
            }
            evidence.AddRange(sensorValues.ToList());
            if (target.TypeName == "VisionSensor")
            {
                identity = Text(sensorValues[0].Value);
            }
            NodeId component = definition.Component.Length == 0 ? target.NodeId :
                await IndustrialCompanionAccess.ResolveChildAsync(
                    context, target.NodeId, Opc.Ua.Vision.Namespaces.Vision, definition.Component, false,
                    cancellationToken)
                    .ConfigureAwait(false);
            if (definition.Component.Length != 0)
            {
                ExpandedNodeId componentType = definition.Component == "Media"
                    ? Opc.Ua.Vision.ObjectTypeIds.VisionMediaManagementType
                    : Opc.Ua.Vision.ObjectTypeIds.VisionFeedbackType;
                await IndustrialCompanionAccess.RequireTargetAsync(context,
                    new CompanionTarget("vision", component, definition.Component, definition.Component), "vision",
                    [new IndustrialCompanionType(componentType, definition.Component)], cancellationToken)
                    .ConfigureAwait(false);
            }
            evidence.Add(new CompanionValue("Component", Variant.From(component)));
            if (definition.Component == "Feedback")
            {
                evidence.AddRange((await ReadFeedbackEvidenceAsync(context, target.NodeId, sensor,
                    component, definition.Operation.Id, inputs, cancellationToken).ConfigureAwait(false)).ToList());
            }
            if (definition.Component == "Media")
            {
                if (definition.Operation.Id == "select-endpoints")
                {
                    NodeId stream = Node(inputs[0].Value, true);
                    NodeId clip = Node(inputs[1].Value, true);
                    if (stream.IsNull && clip.IsNull)
                    {
                        throw new ArgumentException("Choose at least one explicit media endpoint.");
                    }
                    if (!stream.IsNull)
                    {
                        evidence.AddRange((await ReadEndpointAsync(context, component, stream, false, cancellationToken)
                            .ConfigureAwait(false)).ToList());
                    }
                    if (!clip.IsNull)
                    {
                        evidence.AddRange((await ReadEndpointAsync(context, component, clip, true, cancellationToken)
                            .ConfigureAwait(false)).ToList());
                    }
                }
                else
                {
                    NodeId endpoint = Node(inputs[0].Value);
                    ArrayOf<CompanionValue> endpointValues = await ReadEndpointAsync(context, component, endpoint,
                        definition.Operation.Id == "get-clip", cancellationToken).ConfigureAwait(false);
                    if (definition.Operation.Id == "probe-stream" && !Boolean(endpointValues[2].Value))
                    {
                        throw new UnauthorizedAccessException(
                            "Stream-lease probing requires the selected media endpoint to provide confidentiality.");
                    }
                    evidence.AddRange(endpointValues.ToList());
                }
            }
            await AICompanionTaskAccess.RequireExecutableAsync(context, component, definition.Method,
                Opc.Ua.Vision.Namespaces.Vision, cancellationToken).ConfigureAwait(false);
            if (definition.Operation.Id == "probe-stream")
            {
                await AICompanionTaskAccess.RequireExecutableAsync(context, component, "ReleaseStreamEndpoint",
                    Opc.Ua.Vision.Namespaces.Vision, cancellationToken).ConfigureAwait(false);
            }
            if (definition.Operation.Id == "run-continuous-window")
            {
                await AICompanionTaskAccess.RequireExecutableAsync(context, component, "Stop",
                    Opc.Ua.Vision.Namespaces.Vision, cancellationToken).ConfigureAwait(false);
            }
            return new VisionWorkflowBinding(sensor, component, deployment, identity,
                VisionWorkflowTask.Digest(context, evidence.ToArray()), state, continuous, aiDeployment)
            {
                SensorIdentity = Text(sensorValues[0].Value),
                FrameIdentity = Text(sensorValues[2].Value)
            };
        }

        public static async Task RequireRunIdentityAsync(
            CompanionContext context, VisionWorkflowTask task, CancellationToken cancellationToken)
        {
            VisionWorkflowBinding binding = task.Binding;
            await IndustrialCompanionAccess.RequireTargetAsync(
                context, task.Target, "vision", sTargets, cancellationToken).ConfigureAwait(false);
            ArrayOf<CompanionValue> pipeline = await PropertiesAsync(context, task.Target.NodeId,
                ["PipelineId", "Sensor", "Deployment"], cancellationToken).ConfigureAwait(false);
            ArrayOf<CompanionValue> sensor = await PropertiesAsync(context, binding.Sensor,
                ["SensorId", "RealityKind", "FrameId"], cancellationToken).ConfigureAwait(false);
            if (Text(pipeline[0].Value) != binding.Identity ||
                Node(pipeline[1].Value) != binding.Sensor ||
                Node(pipeline[2].Value, true) != binding.Deployment ||
                Text(sensor[0].Value) != binding.SensorIdentity ||
                EnumValue<VisionRealityKindEnum>(sensor[1].Value) != VisionRealityKindEnum.Simulated ||
                Text(sensor[2].Value) != binding.FrameIdentity)
            {
                throw new InvalidOperationException(
                    "The Vision binding changed; the replacement or reclassified pipeline was not stopped.");
            }
            await AICompanionTaskAccess.RequireExecutableAsync(context, task.Target.NodeId, "Stop",
                Opc.Ua.Vision.Namespaces.Vision, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<ArrayOf<CompanionValue>> ReadEndpointAsync(
            CompanionContext context, NodeId media, NodeId endpoint, bool clip, CancellationToken cancellationToken)
        {
            await RequireEndpointAsync(context, media, endpoint, clip, cancellationToken).ConfigureAwait(false);
            ArrayOf<CompanionValue> values = await PropertiesAsync(
                context, endpoint, ["EndpointId", "State", "SecureTransport", "EndpointUri", "Authentication"],
                cancellationToken).ConfigureAwait(false);
            _ = Text(values[0].Value);
            _ = Boolean(values[2].Value);
            _ = Text(values[3].Value);
            _ = EnumValue<VisionEndpointAuthenticationEnum>(values[4].Value);
            if (EnumValue<VisionEndpointStateEnum>(values[1].Value) is
                VisionEndpointStateEnum.Inactive or VisionEndpointStateEnum.Faulted)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The media endpoint is not ready.");
            }
            return [.. values, new("Endpoint", Variant.From(endpoint))];
        }

        public static async Task RequireEndpointAsync(
            CompanionContext context, NodeId media, NodeId endpoint, bool clip, CancellationToken cancellationToken)
        {
            NodeId folder = await IndustrialCompanionAccess.ResolveChildAsync(
                context, media, Opc.Ua.Vision.Namespaces.Vision,
                clip ? "ClipEndpoints" : "StreamEndpoints", false, cancellationToken).ConfigureAwait(false);
            bool found = false;
            int count = 0;
            await foreach (ReferenceDescription item in IndustrialCompanionAccess.BrowseAsync(
                context, folder, BrowseDirection.Forward, Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                NodeClass.Object, new IndustrialBrowseBudget(context), cancellationToken).ConfigureAwait(false))
            {
                CellCompanionSupport.CheckCount(++count, Math.Min(context.MaxTargets, 32), "Vision media endpoints");
                if (IndustrialCompanionAccess.LocalId(context, item.NodeId) == endpoint)
                {
                    if (found)
                    {
                        throw new ServiceResultException(StatusCodes.BadTooManyMatches, "Ambiguous media endpoint.");
                    }
                    found = true;
                }
            }
            if (!found)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid,
                    "The selected endpoint does not belong to the sensor's media manager.");
            }
            await IndustrialCompanionAccess.RequireTargetAsync(context,
                new CompanionTarget("vision", endpoint, "Endpoint", "Endpoint"), "vision",
                [new IndustrialCompanionType(clip ? Opc.Ua.Vision.ObjectTypeIds.ClipEndpointType :
                    Opc.Ua.Vision.ObjectTypeIds.StreamEndpointType, "Endpoint")], cancellationToken)
                .ConfigureAwait(false);
        }

        public static async Task<ArrayOf<CompanionValue>> PropertiesAsync(
            CompanionContext context, NodeId node, ArrayOf<string> names, CancellationToken cancellationToken)
        {
            ArrayOf<CompanionValue> values = await AICompanionTaskAccess.PropertiesAsync(
                context, node, names, Opc.Ua.Vision.Namespaces.Vision, cancellationToken).ConfigureAwait(false);
            foreach (CompanionValue value in values)
            {
                if (value.Value.TryGetValue(out StatusCode missing))
                {
                    throw new ServiceResultException(missing,
                        $"Required Vision workflow member '{value.Name}' is unavailable.");
                }
            }
            return values;
        }

        public static string Text(Variant value, bool required = true)
        {
            if (!value.TryGetValue(out string? text) ||
                (required && string.IsNullOrWhiteSpace(text)) ||
                text is { Length: > 4096 })
            {
                throw new ArgumentException("A bounded, correctly typed Vision string is required.");
            }
            return text ?? string.Empty;
        }

        public static NodeId Node(Variant value, bool allowNull = false)
        {
            if (!value.TryGetValue(out NodeId node) || (!allowNull && node.IsNull))
            {
                throw new ArgumentException("An exact Vision NodeId is required.");
            }
            return node;
        }

        public static bool Boolean(Variant value)
        {
            return value.TryGetValue(out bool result)
                ? result : throw new ArgumentException("A Vision Boolean is required.");
        }

        public static uint Unsigned(Variant value, uint maximum)
        {
            return value.TryGetValue(out uint result) && result is > 0 && result <= maximum
                ? result : throw new ArgumentException("The Vision unsigned input is outside its bounds.");
        }

        public static T EnumValue<T>(Variant value) where T : struct, Enum
        {
            if (!value.TryGetValue(out int number) || !Enum.IsDefined(typeof(T), number))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "An unknown Vision enum was supplied.");
            }
            return (T)Enum.ToObject(typeof(T), number);
        }

        public static DateTimeUtc Timestamp(Variant value)
        {
            return value.TryGetValue(out DateTimeUtc time)
                ? time : throw new ArgumentException("A Vision UTC timestamp is required.");
        }

        private static async Task<ArrayOf<CompanionValue>> ReadFeedbackEvidenceAsync(
            CompanionContext context, NodeId pipeline, NodeId sensor, NodeId feedback, string operation,
            ArrayOf<CompanionValue> inputs, CancellationToken cancellationToken)
        {
            ArrayOf<CompanionValue> limits = await AICompanionTaskAccess.PropertiesAsync(
                context, feedback, ["MaxInlineFeedbackImageSize"], Opc.Ua.Vision.Namespaces.Vision, cancellationToken)
                .ConfigureAwait(false);
            ByteString inline = operation switch
            {
                "submit-detections" => VisionWorkflowValues.Bytes(inputs[3].Value),
                "submit-correction" => VisionWorkflowValues.Bytes(inputs[5].Value),
                _ => default
            };
            Variant limit = limits[0].Value;
            if (!inline.IsEmpty &&
                (limit.TypeInfo.BuiltInType != BuiltInType.UInt32 ||
                    !limit.TryGetValue(out uint maximum) ||
                    inline.Length > maximum))
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "Inline feedback exceeds the advertised limit, or that limit is unavailable.");
            }
            ArrayOf<CompanionValue> learning = await AICompanionTaskAccess.PropertiesAsync(
                context, pipeline, ["LearningJob"], Opc.Ua.Vision.Namespaces.Vision, cancellationToken)
                .ConfigureAwait(false);
            var evidence = new List<CompanionValue>(limits.ToList());
            evidence.AddRange(learning.ToList());
            VisionImageReferenceDataType? image = operation switch
            {
                "submit-detections" => VisionWorkflowValues.Image(context, inputs[2].Value, true),
                "submit-image-reference" => VisionWorkflowValues.Image(context, inputs[1].Value),
                _ => null
            };
            if (image is not null)
            {
                VisionWorkflowValues.ValidateImage(image, inline, requireLocal: true);
                if (Uri.TryCreate(image.Uri, UriKind.Absolute, out Uri? uri) && uri.Scheme == "opcua-inline")
                {
                    evidence.AddRange((await ReadInlineOriginAsync(context, sensor, uri, cancellationToken)
                        .ConfigureAwait(false)).ToList());
                }
            }
            if (operation == "submit-correction")
            {
                string id = VisionWorkflowValues.ResultId(inputs[0].Value);
                NodeId result = await VisionWorkflowResults.FindAsync(
                    context, pipeline, id, cancellationToken).ConfigureAwait(false);
                CompanionOperationResult observed = await VisionWorkflowResults.ReadAsync(
                    context, result, id, sensor, pipeline, cancellationToken).ConfigureAwait(false);
                evidence.AddRange(observed.Values.ToList());
                var client = new Opc.Ua.Vision.Client.VisionClient(context.Session, context.Telemetry);
                Opc.Ua.Vision.Client.VisionResultKind kind = await client.Inference()
                    .DetermineResultKindAsync(result, cancellationToken).ConfigureAwait(false);
                if (kind is not (Opc.Ua.Vision.Client.VisionResultKind.Detection or
                        Opc.Ua.Vision.Client.VisionResultKind.Inspection) ||
                    (!VisionWorkflowValues.Structures<VisionDetectionDataType>(context, inputs[2].Value).IsEmpty &&
                        kind != Opc.Ua.Vision.Client.VisionResultKind.Detection) ||
                    (!VisionWorkflowValues.Structures<VisionCharacteristicDataType>(context, inputs[3].Value).IsEmpty &&
                        kind != Opc.Ua.Vision.Client.VisionResultKind.Inspection))
                {
                    throw new ArgumentException("The corrected geometry must match the existing result kind.");
                }
                if (!inline.IsEmpty)
                {
                    ArrayOf<CompanionValue> frame = await PropertiesAsync(
                        context, result, ["Frame"], cancellationToken).ConfigureAwait(false);
                    VisionWorkflowValues.ValidateImage(VisionWorkflowValues.Image(context, frame[0].Value)!, inline);
                    evidence.AddRange(frame.ToList());
                }
            }
            return [.. evidence];
        }

        private static async Task<ArrayOf<CompanionValue>> ReadInlineOriginAsync(
            CompanionContext context, NodeId sensor, Uri uri,
            CancellationToken cancellationToken)
        {
            NodeId media = await IndustrialCompanionAccess.ResolveChildAsync(
                context, sensor, Opc.Ua.Vision.Namespaces.Vision, "Media", false, cancellationToken)
                .ConfigureAwait(false);
            NodeId folder = await IndustrialCompanionAccess.ResolveChildAsync(
                context, media, Opc.Ua.Vision.Namespaces.Vision, "ClipEndpoints", false, cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<CompanionValue> found = default;
            int count = 0;
            await foreach (ReferenceDescription reference in IndustrialCompanionAccess.BrowseAsync(
                context, folder, BrowseDirection.Forward, Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                NodeClass.Object, new IndustrialBrowseBudget(context), cancellationToken).ConfigureAwait(false))
            {
                CellCompanionSupport.CheckCount(++count, Math.Min(context.MaxTargets, 32), "Inline Vision origins");
                NodeId endpoint = IndustrialCompanionAccess.LocalId(context, reference.NodeId);
                ArrayOf<CompanionValue> values = await ReadEndpointAsync(
                    context, media, endpoint, true, cancellationToken).ConfigureAwait(false);
                string advertised = Text(values[3].Value);
                if (!Uri.TryCreate(advertised, UriKind.Absolute, out Uri? origin) ||
                    origin.Scheme != uri.Scheme ||
                    !string.Equals(origin.IdnHost, uri.IdnHost, StringComparison.OrdinalIgnoreCase) ||
                    origin.Port != uri.Port ||
                    origin.UserInfo.Length != 0 ||
                    origin.Query.Length != 0 ||
                    origin.Fragment.Length != 0)
                {
                    continue;
                }
                string root = origin.AbsolutePath.TrimEnd('/');
                string path = uri.AbsolutePath;
                if (path != root && !path.StartsWith(root + "/", StringComparison.Ordinal))
                {
                    continue;
                }
                if (!found.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyMatches,
                        "The inline frame matches more than one advertised clip origin.");
                }
                found = [new("Inline media manager", Variant.From(media)), .. values];
            }
            if (found.IsNull)
            {
                throw new UnauthorizedAccessException(
                    "The inline frame must belong to an advertised clip origin of the selected simulated sensor.");
            }
            return found;
        }

        private static ArrayOf<CompanionValue> DeploymentEvidence(AIDeploymentSnapshot deployment)
        {
            return
            [
                new("AI ID", Variant.From(deployment.DeploymentId ??
                    throw new InvalidOperationException("The AI deployment identity is absent."))),
                new("AI destination", deployment.EndpointUri is { } endpoint ? Variant.From(endpoint) : Variant.Null),
                new("AI location", Variant.From((int)deployment.InferenceLocation)),
                new("AI state", Variant.From((int)deployment.State)),
                new("AI jurisdiction", deployment.DataJurisdiction is { } jurisdiction
                    ? Variant.From(jurisdiction) : Variant.Null),
                new("AI egress", Variant.From(deployment.EgressPermitted)),
                new("AI inline limit", Variant.From(deployment.MaxInlinePayloadSize)),
                new("AI model", Variant.From(deployment.ModelId)),
                new("AI fallback", Variant.From(deployment.FallbackDeploymentId))
            ];
        }

        private static readonly ArrayOf<IndustrialCompanionType> sTargets =
        [
            new(Opc.Ua.Vision.ObjectTypeIds.VisionSensorType, "VisionSensor"),
            new(Opc.Ua.Vision.ObjectTypeIds.InferencePipelineType, "InferencePipeline")
        ];
    }
}
