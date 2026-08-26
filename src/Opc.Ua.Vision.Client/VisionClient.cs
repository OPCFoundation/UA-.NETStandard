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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// High-level client for the OPC UA Vision (draft) companion model. It resolves
    /// the well-known <c>Vision</c> object under the Server (§4.2), enumerates
    /// sensors, inference pipelines and coordinate frames, and hands out focused
    /// sub-clients for sensors, frames, media management, pipelines and feedback.
    /// </summary>
    /// <remarks>
    /// Every enumeration operation is subtype aware: a Server that specialises the
    /// abstract Vision types (<c>ImageSensorType</c> vs a vendor-derived
    /// <c>AcmeCameraType</c>, or one of the concrete result subtypes) is discovered
    /// as an instance of the closest declared Vision base type.
    /// </remarks>
    public sealed class VisionClient
    {
        /// <summary>
        /// Creates a Vision client over a connected session.
        /// </summary>
        /// <param name="session">
        /// The connected session.
        /// </param>
        /// <param name="telemetry">
        /// The telemetry context used by generated proxies.
        /// </param>
        public VisionClient(ISession session, ITelemetryContext telemetry)
        {
            Operations = new VisionClientOperations(session, telemetry);
        }

        /// <summary>
        /// Gets the connected session.
        /// </summary>
        public ISession Session => Operations.Session;

        /// <summary>
        /// Gets the telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry => Operations.Telemetry;

        /// <summary>
        /// Gets whether the Server exposes the Vision namespace at all. Where
        /// <c>false</c>, every enumeration on this client returns an empty result.
        /// </summary>
        public bool IsVisionNamespaceAvailable
            => Operations.TryGetVisionNamespaceIndex(out _);

        /// <summary>
        /// Resolves the NodeId of the well-known <c>Vision</c> object (§4.2). Returns
        /// a null NodeId when the Server does not expose the Vision namespace.
        /// </summary>
        public NodeId VisionRootId
        {
            get
            {
                if (!Operations.TryGetVisionNamespaceIndex(out ushort _))
                {
                    return NodeId.Null;
                }
                return NodeId.Create(
                    Objects.Vision, Namespaces.Vision, Session.NamespaceUris);
            }
        }

        /// <summary>
        /// Resolves the NodeId of the mandatory <c>Vision/Sensors</c> folder. Returns
        /// a null NodeId when the Server does not expose the Vision namespace.
        /// </summary>
        public NodeId SensorsFolderId
        {
            get
            {
                if (!Operations.TryGetVisionNamespaceIndex(out ushort _))
                {
                    return NodeId.Null;
                }
                return NodeId.Create(
                    Objects.Vision_Sensors, Namespaces.Vision, Session.NamespaceUris);
            }
        }

        /// <summary>
        /// Resolves the NodeId of the mandatory <c>Vision/Sensors</c> folder by browse-path
        /// from the Vision root, falling back to the well-known identifier.
        /// </summary>
        /// <remarks>
        /// The well-known identifier only holds for a Server that materialises the Vision
        /// tree in the Vision namespace itself. A Server that builds it as instances in its
        /// own namespace - which is what the fluent builder produces - has a Sensors folder
        /// whose NodeId is its own, so the well-known one resolves to nothing and every
        /// sensor is invisible. Pipelines and Frames already resolve by browse path; this
        /// makes Sensors behave the same way.
        /// </remarks>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async ValueTask<NodeId> GetSensorsFolderIdAsync(
            CancellationToken cancellationToken = default)
        {
            NodeId resolved = await ResolveOptionalRootChildAsync(
                BrowseNames.Sensors, cancellationToken).ConfigureAwait(false);
            return resolved.IsNull ? SensorsFolderId : resolved;
        }

        /// <summary>
        /// Resolves the NodeId of the optional <c>Vision/Pipelines</c> folder by
        /// browse-path from the Vision root. Returns a null NodeId when the folder
        /// is not present.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public ValueTask<NodeId> GetPipelinesFolderIdAsync(
            CancellationToken cancellationToken = default)
        {
            return ResolveOptionalRootChildAsync(
                BrowseNames.Pipelines, cancellationToken);
        }

        /// <summary>
        /// Resolves the NodeId of the optional <c>Vision/Frames</c> folder by
        /// browse-path from the Vision root. Returns a null NodeId when the folder
        /// is not present.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public ValueTask<NodeId> GetFramesFolderIdAsync(
            CancellationToken cancellationToken = default)
        {
            return ResolveOptionalRootChildAsync(
                BrowseNames.Frames, cancellationToken);
        }

        /// <summary>
        /// Discovers every sensor exposed under the Vision root, including instances
        /// of vendor subtypes derived from <c>VisionSensorType</c>.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async ValueTask<ArrayOf<NodeId>> DiscoverSensorsAsync(
            CancellationToken cancellationToken = default)
        {
            NodeId sensors = await GetSensorsFolderIdAsync(cancellationToken).ConfigureAwait(false);
            if (sensors.IsNull)
            {
                return ArrayOf<NodeId>.Empty;
            }
            return await Operations.DiscoverInstancesAsync(
                sensors,
                Operations.VisionNamespaceType(ObjectTypes.VisionSensorType),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Discovers every inference pipeline exposed under the Vision root.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async ValueTask<ArrayOf<NodeId>> DiscoverPipelinesAsync(
            CancellationToken cancellationToken = default)
        {
            NodeId pipelines = await GetPipelinesFolderIdAsync(cancellationToken)
                .ConfigureAwait(false);
            if (pipelines.IsNull)
            {
                return ArrayOf<NodeId>.Empty;
            }
            return await Operations.DiscoverInstancesAsync(
                pipelines,
                Operations.VisionNamespaceType(ObjectTypes.InferencePipelineType),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Discovers every coordinate frame exposed under the Vision root.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async ValueTask<ArrayOf<NodeId>> DiscoverFramesAsync(
            CancellationToken cancellationToken = default)
        {
            NodeId frames = await GetFramesFolderIdAsync(cancellationToken)
                .ConfigureAwait(false);
            if (frames.IsNull)
            {
                return ArrayOf<NodeId>.Empty;
            }
            return await Operations.DiscoverInstancesAsync(
                frames,
                Operations.VisionNamespaceType(ObjectTypes.CoordinateFrameType),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerates the sensors under the Vision root along with their BrowseName,
        /// DisplayName and TypeDefinition, so a client can render a picker without a
        /// second round-trip.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async IAsyncEnumerable<VisionNodeEntry> EnumerateSensorsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId parent = await GetSensorsFolderIdAsync(cancellationToken)
                .ConfigureAwait(false);
            await foreach (VisionNodeEntry entry in EnumerateInstancesAsync(
                parent, ObjectTypes.VisionSensorType, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return entry;
            }
        }

        /// <summary>
        /// Enumerates the inference pipelines under the Vision root along with their
        /// BrowseName, DisplayName and TypeDefinition.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async IAsyncEnumerable<VisionNodeEntry> EnumeratePipelinesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId parent = await GetPipelinesFolderIdAsync(cancellationToken)
                .ConfigureAwait(false);
            await foreach (VisionNodeEntry entry in EnumerateInstancesAsync(
                parent, ObjectTypes.InferencePipelineType, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return entry;
            }
        }

        /// <summary>
        /// Enumerates the coordinate frames under the Vision root along with their
        /// BrowseName, DisplayName and TypeDefinition.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async IAsyncEnumerable<VisionNodeEntry> EnumerateFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId parent = await GetFramesFolderIdAsync(cancellationToken)
                .ConfigureAwait(false);
            await foreach (VisionNodeEntry entry in EnumerateInstancesAsync(
                parent, ObjectTypes.CoordinateFrameType, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return entry;
            }
        }

        /// <summary>
        /// Opens a focused sensor client over <paramref name="sensorNodeId"/>.
        /// </summary>
        /// <param name="sensorNodeId">
        /// The sensor object NodeId; typically obtained from
        /// <see cref="DiscoverSensorsAsync"/>.
        /// </param>
        public VisionSensorClient Sensor(NodeId sensorNodeId)
        {
            return new VisionSensorClient(Operations, sensorNodeId);
        }

        /// <summary>
        /// Opens a focused inference pipeline client over
        /// <paramref name="pipelineNodeId"/>.
        /// </summary>
        /// <param name="pipelineNodeId">
        /// The pipeline object NodeId; typically obtained from
        /// <see cref="DiscoverPipelinesAsync"/>.
        /// </param>
        public VisionPipelineClient Pipeline(NodeId pipelineNodeId)
        {
            return new VisionPipelineClient(Operations, pipelineNodeId);
        }

        /// <summary>
        /// Opens a focused feedback client over the feedback object of a pipeline.
        /// </summary>
        /// <param name="feedbackNodeId">
        /// The <c>VisionFeedbackType</c> object NodeId — the value of
        /// <c>InferencePipelineType.Feedback</c>.
        /// </param>
        public VisionFeedbackClient Feedback(NodeId feedbackNodeId)
        {
            return new VisionFeedbackClient(Operations, feedbackNodeId);
        }

        /// <summary>
        /// Opens a focused media-management client over the media object of a sensor.
        /// </summary>
        /// <param name="mediaNodeId">
        /// The <c>VisionMediaManagementType</c> object NodeId — the value of
        /// <c>VisionSensorType.Media</c>.
        /// </param>
        public VisionMediaClient Media(NodeId mediaNodeId)
        {
            return new VisionMediaClient(Operations, mediaNodeId);
        }

        /// <summary>
        /// Opens a focused result reader over an <c>InspectionResultType</c>,
        /// <c>DetectionResultType</c> or <c>SegmentationResultType</c> instance.
        /// </summary>
        /// <param name="resultNodeId">
        /// The result object NodeId.
        /// </param>
        public VisionResultReader Result(NodeId resultNodeId)
        {
            return new VisionResultReader(Operations, resultNodeId);
        }

        /// <summary>
        /// Opens a focused frame graph over the Server's coordinate-frame tree. The
        /// graph resolves <see cref="VisionFrameSnapshot"/> instances and composes
        /// transforms between two named frames per the §5.12 conventions
        /// (right-handed frames, quaternion order (x, y, z, w), metres).
        /// </summary>
        public VisionFrameGraph Frames()
        {
            return new VisionFrameGraph(Operations);
        }

        /// <summary>
        /// Creates the one-shot inference service for running inference,
        /// determining the result kind, and reading a bounded concise summary.
        /// </summary>
        public VisionInferenceService Inference()
        {
            return new VisionInferenceService(Operations);
        }

        /// <summary>
        /// Resolves a pipeline by exact unique name (BrowseName.Name or
        /// DisplayName.Text, trimmed) or by NodeId string. Returns the
        /// matching <see cref="VisionNodeEntry"/>.
        /// </summary>
        /// <param name="pipelineSelector">
        /// A NodeId string (e.g. <c>ns=2;s=Vision/Pipelines/Abc</c>) or an
        /// exact published name.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        /// <exception cref="ArgumentException">
        /// <paramref name="pipelineSelector"/> is null or empty.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// No match found, or multiple pipelines matched the name.
        /// </exception>
        public async Task<VisionNodeEntry> ResolvePipelineAsync(
            string pipelineSelector,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pipelineSelector))
            {
                throw new ArgumentException(
                    "Pipeline selector must not be null, empty, or whitespace.",
                    nameof(pipelineSelector));
            }

            string trimmed = pipelineSelector.Trim();

            bool isNodeId = NodeId.TryParse(trimmed, out NodeId parsed) &&
                !parsed.IsNull;

            var candidates = new List<VisionNodeEntry>();
            var all = new List<string>();

            await foreach (VisionNodeEntry entry in EnumeratePipelinesAsync(
                cancellationToken).ConfigureAwait(false))
            {
                all.Add(FormatPipelineEntry(entry));

                if (isNodeId && entry.NodeId == parsed)
                {
                    return entry;
                }

                string? browseName = entry.BrowseName.Name?.Trim();
                string? displayName = entry.DisplayName.Text?.Trim();

                if (string.Equals(trimmed, browseName, StringComparison.Ordinal) ||
                    string.Equals(trimmed, displayName, StringComparison.Ordinal))
                {
                    candidates.Add(entry);
                }
            }

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            if (candidates.Count > 1)
            {
                var names = new List<string>(candidates.Count);
                for (int i = 0; i < candidates.Count; i++)
                {
                    names.Add(FormatPipelineEntry(candidates[i]));
                }

                throw new InvalidOperationException(
                    $"Ambiguous pipeline name '{trimmed}': {string.Join(", ", names)}.");
            }

            string available = all.Count > 0
                ? string.Join(", ", all)
                : "(none)";
            throw new InvalidOperationException(
                $"Pipeline '{trimmed}' not found. Available: {available}.");
        }

        internal VisionClientOperations Operations { get; }

        private static string FormatPipelineEntry(VisionNodeEntry entry)
        {
            return $"BrowseName='{entry.BrowseName.Name}', " +
                $"DisplayName='{entry.DisplayName.Text}', NodeId='{entry.NodeId}'";
        }

        private async ValueTask<NodeId> ResolveOptionalRootChildAsync(
            string browseName,
            CancellationToken cancellationToken)
        {
            NodeId root = VisionRootId;
            if (root.IsNull)
            {
                return NodeId.Null;
            }
            return await Operations.ResolveChildAsync(
                root, browseName, cancellationToken).ConfigureAwait(false);
        }

        private async IAsyncEnumerable<VisionNodeEntry> EnumerateInstancesAsync(
            NodeId root,
            uint typeIdentifier,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (root.IsNull)
            {
                yield break;
            }
            NodeId typeDefinition = Operations.VisionNamespaceType(typeIdentifier);
            if (typeDefinition.IsNull)
            {
                yield break;
            }
            ArrayOf<ReferenceDescription> references = await Operations
                .BrowseHierarchicalObjectsAsync(root, cancellationToken).ConfigureAwait(false);
            var matches = new List<VisionNodeEntry>();
            for (int ii = 0; ii < references.Count; ii++)
            {
                ReferenceDescription reference = references[ii];
                NodeId typeDef = ExpandedNodeId.ToNodeId(
                    reference.TypeDefinition, Session.NamespaceUris);
                NodeId nodeId = ExpandedNodeId.ToNodeId(
                    reference.NodeId, Session.NamespaceUris);
                if (typeDef.IsNull || nodeId.IsNull)
                {
                    continue;
                }
                if (await Session.NodeCache.IsTypeOfAsync(
                        typeDef, typeDefinition, cancellationToken).ConfigureAwait(false))
                {
                    matches.Add(new VisionNodeEntry(
                        nodeId, reference.BrowseName, reference.DisplayName, typeDef));
                }
            }
            for (int ii = 0; ii < matches.Count; ii++)
            {
                yield return matches[ii];
            }
        }
    }
}
