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

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// Focused client over a single <c>InferencePipelineType</c> instance. Wraps
    /// <c>RunInference</c>, <c>StartContinuous</c> and <c>Stop</c>, and reads the
    /// pipeline's identity, current state, sensor binding and deployment reference
    /// (§8.3).
    /// </summary>
    public sealed class VisionPipelineClient
    {
        private readonly VisionClientOperations m_operations;
        private readonly InferencePipelineTypeClient m_proxy;

        internal VisionPipelineClient(
            VisionClientOperations operations, NodeId pipelineNodeId)
        {
            m_operations = operations
                ?? throw new ArgumentNullException(nameof(operations));
            if (pipelineNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Pipeline NodeId must not be null.", nameof(pipelineNodeId));
            }
            PipelineNodeId = pipelineNodeId;
            m_proxy = new InferencePipelineTypeClient(
                m_operations.Session, pipelineNodeId, m_operations.Telemetry);
        }

        /// <summary>
        /// Gets the pipeline object NodeId.
        /// </summary>
        public NodeId PipelineNodeId { get; }

        /// <summary>
        /// Reads the pipeline's identity and current state.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<VisionPipelineSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            string[] members =
            [
                BrowseNames.PipelineId,
                BrowseNames.Sensor,
                BrowseNames.Deployment,
                BrowseNames.State,
                BrowseNames.Continuous,
                BrowseNames.LearningJob
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                PipelineNodeId, members, cancellationToken).ConfigureAwait(false);
            var toRead = new List<NodeId>();
            for (int ii = 0; ii < nodes.Count; ii++)
            {
                if (!nodes[ii].IsNull)
                {
                    toRead.Add(nodes[ii]);
                }
            }
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(
                toRead, cancellationToken).ConfigureAwait(false);
            int cursor = 0;
            string? pipelineId = null;
            if (!nodes[0].IsNull)
            {
                pipelineId = VisionClientOperations.ReadString(values[cursor++]);
            }
            NodeId sensorId = NodeId.Null;
            if (!nodes[1].IsNull)
            {
                VisionClientOperations.TryReadNodeId(values[cursor++], out sensorId);
            }
            NodeId deploymentId = NodeId.Null;
            if (!nodes[2].IsNull)
            {
                VisionClientOperations.TryReadNodeId(values[cursor++], out deploymentId);
            }
            VisionEndpointStateEnum state = default;
            if (!nodes[3].IsNull)
            {
                VisionClientOperations.TryReadEnum(values[cursor++], out state);
            }
            bool continuous = false;
            if (!nodes[4].IsNull)
            {
                DataValue value = values[cursor++];
                if (value.WrappedValue.TryGetValue(out bool b))
                {
                    continuous = b;
                }
            }
            NodeId learningJobId = NodeId.Null;
            if (!nodes[5].IsNull)
            {
                VisionClientOperations.TryReadNodeId(values[cursor++], out learningJobId);
            }
            return new VisionPipelineSnapshot
            {
                NodeId = PipelineNodeId,
                PipelineId = pipelineId,
                SensorId = sensorId,
                DeploymentId = deploymentId,
                State = state,
                Continuous = continuous,
                LearningJobId = learningJobId
            };
        }

        /// <summary>
        /// Reads only the current <c>State</c> of the pipeline. Suitable for a
        /// short-polling wait loop that avoids reading the full snapshot each time.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<VisionEndpointStateEnum> ReadStateAsync(
            CancellationToken cancellationToken = default)
        {
            NodeId node = await m_operations.ResolveChildAsync(
                PipelineNodeId, BrowseNames.State, cancellationToken).ConfigureAwait(false);
            if (node.IsNull)
            {
                return default;
            }
            DataValue value = await m_operations.ReadValueAsync(
                node, cancellationToken).ConfigureAwait(false);
            return VisionClientOperations.TryReadEnum(
                value, out VisionEndpointStateEnum result)
                ? result
                : default;
        }

        /// <summary>
        /// Runs a single inference. Returns the <c>ResultId</c> of the newly
        /// published result. §8.4 permits the Server to reject the call with a
        /// <see cref="ServiceResultException"/>; the exception is surfaced.
        /// </summary>
        /// <param name="timestamp">
        /// The requested acquisition timestamp; a caller can pass <c>default</c> to
        /// let the Server acquire "now".
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<string> RunInferenceAsync(
            DateTimeUtc timestamp = default,
            CancellationToken cancellationToken = default)
        {
            return await m_proxy.RunInferenceAsync(
                timestamp, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves the NodeId of a published result from the ResultId that
        /// <see cref="RunInferenceAsync"/> returned.
        /// </summary>
        /// <remarks>
        /// The Part 4 method answers with the ResultId the Server assigned, which identifies
        /// the result but is not addressable: every tool that reads a result needs its NodeId.
        /// A Server publishes each result under the pipeline's <c>Results</c> folder with the
        /// ResultId as its BrowseName, so the two are one enumeration apart - without this an
        /// agent has to guess the Server's NodeId convention, and a wrong guess reads some
        /// other node and reports an empty result rather than failing.
        /// </remarks>
        /// <param name="resultId">The ResultId the Server assigned.</param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        /// <returns>
        /// The result NodeId, or a null NodeId when the Server publishes no such result.
        /// </returns>
        public async Task<NodeId> ResolveResultNodeIdAsync(
            string resultId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(resultId))
            {
                return NodeId.Null;
            }
            await foreach (VisionNodeEntry entry in EnumerateResultsAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (string.Equals(entry.BrowseName.Name, resultId, StringComparison.Ordinal))
                {
                    return entry.NodeId;
                }
                // A Server may prefix the ResultId to keep BrowseNames unique within the
                // folder. Match that, but only on the ResultId itself: falling back to the
                // most recently published result would answer confidently with the wrong
                // one, which is worse than saying it was not found.
                if (entry.BrowseName.Name is { } name
                    && name.EndsWith(resultId, StringComparison.Ordinal))
                {
                    return entry.NodeId;
                }
            }
            return NodeId.Null;
        }

        /// <summary>
        /// Starts continuous inference. Throws where the Server refuses.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public Task StartContinuousAsync(CancellationToken cancellationToken = default)
        {
            return m_proxy.StartContinuousAsync(cancellationToken).AsTask();
        }

        /// <summary>
        /// Stops continuous or in-progress inference. Throws where the Server refuses.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            return m_proxy.StopAsync(cancellationToken).AsTask();
        }

        /// <summary>
        /// Enumerates the results the pipeline has published, browsing its
        /// <c>Results</c> folder.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async IAsyncEnumerable<VisionNodeEntry> EnumerateResultsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            FolderTypeClient? folder = await m_proxy.GetResultsAsync(
                m_operations.Telemetry, cancellationToken).ConfigureAwait(false);
            NodeId folderId = folder is null ? NodeId.Null : folder.ObjectId;
            if (folderId.IsNull)
            {
                yield break;
            }
            NodeId resultType = m_operations.VisionNamespaceType(
                ObjectTypes.VisionResultType);
            ArrayOf<ReferenceDescription> refs = await m_operations
                .BrowseHierarchicalObjectsAsync(folderId, cancellationToken)
                .ConfigureAwait(false);
            for (int ii = 0; ii < refs.Count; ii++)
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(
                    refs[ii].NodeId, m_operations.Session.NamespaceUris);
                NodeId typeDef = ExpandedNodeId.ToNodeId(
                    refs[ii].TypeDefinition, m_operations.Session.NamespaceUris);
                if (nodeId.IsNull || typeDef.IsNull)
                {
                    continue;
                }
                if (!resultType.IsNull && !await m_operations.Session.NodeCache
                        .IsTypeOfAsync(typeDef, resultType, cancellationToken)
                        .ConfigureAwait(false))
                {
                    continue;
                }
                yield return new VisionNodeEntry(
                    nodeId, refs[ii].BrowseName, refs[ii].DisplayName, typeDef);
            }
        }

        /// <summary>
        /// Opens the feedback client rooted at this pipeline's <c>Feedback</c>
        /// object, or returns <c>null</c> when the pipeline does not expose one.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<VisionFeedbackClient?> OpenFeedbackAsync(
            CancellationToken cancellationToken = default)
        {
            VisionFeedbackTypeClient? feedback = await m_proxy.GetFeedbackAsync(
                m_operations.Telemetry, cancellationToken).ConfigureAwait(false);
            NodeId feedbackId = feedback is null ? NodeId.Null : feedback.ObjectId;
            return feedbackId.IsNull ? null : new VisionFeedbackClient(m_operations, feedbackId);
        }
    }
}
