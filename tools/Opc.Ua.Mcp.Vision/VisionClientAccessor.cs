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
using Opc.Ua.Client;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Creates Vision clients from active MCP OPC UA sessions and hands out
    /// focused sub-clients for a named sensor, pipeline, media manager,
    /// feedback object, result, or the frame graph.
    /// </summary>
    public sealed class VisionClientAccessor
    {
        /// <summary>
        /// Initializes the accessor.
        /// </summary>
        public VisionClientAccessor(OpcUaSessionManager sessionManager)
        {
            m_sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        }

        /// <summary>
        /// Creates the top-level Vision client over the named or sole active session.
        /// </summary>
        public VisionClient CreateClient(string? sessionName = null)
        {
            ISession session = m_sessionManager.GetSessionOrThrow(sessionName);
            return new VisionClient(session, m_sessionManager.Telemetry);
        }

        /// <summary>
        /// Opens a focused sensor client over the named or sole active session.
        /// </summary>
        public VisionSensorClient OpenSensor(string sensorNodeId, string? sessionName = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sensorNodeId);

            return CreateClient(sessionName).Sensor(Serialization.OpcUaJsonHelper.ParseNodeId(sensorNodeId));
        }

        /// <summary>
        /// Opens a focused pipeline client over the named or sole active session.
        /// </summary>
        public VisionPipelineClient OpenPipeline(string pipelineNodeId, string? sessionName = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pipelineNodeId);

            return CreateClient(sessionName).Pipeline(
                Serialization.OpcUaJsonHelper.ParseNodeId(pipelineNodeId));
        }

        /// <summary>
        /// Opens a focused media-management client over the named or sole active session.
        /// </summary>
        public VisionMediaClient OpenMedia(string mediaNodeId, string? sessionName = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(mediaNodeId);

            return CreateClient(sessionName).Media(
                Serialization.OpcUaJsonHelper.ParseNodeId(mediaNodeId));
        }

        /// <summary>
        /// Opens a focused feedback client over the named or sole active session.
        /// </summary>
        public VisionFeedbackClient OpenFeedback(string feedbackNodeId, string? sessionName = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(feedbackNodeId);

            return CreateClient(sessionName).Feedback(
                Serialization.OpcUaJsonHelper.ParseNodeId(feedbackNodeId));
        }

        /// <summary>
        /// Opens the feedback client attached to a selected pipeline over the named or sole active session.
        /// </summary>
        public async Task<VisionFeedbackClient> OpenPipelineFeedbackAsync(
            string pipelineSelector,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            (_, VisionPipelineClient pipeline) = await ResolvePipelineAsync(
                pipelineSelector, sessionName, ct).ConfigureAwait(false);
            VisionFeedbackClient? feedback = await pipeline.OpenFeedbackAsync(ct).ConfigureAwait(false);
            return feedback ?? throw new InvalidOperationException(
                "Pipeline does not expose a Feedback object.");
        }

        /// <summary>
        /// Opens a focused result reader over the named or sole active session.
        /// </summary>
        public VisionResultReader OpenResult(string resultNodeId, string? sessionName = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resultNodeId);

            return CreateClient(sessionName).Result(
                Serialization.OpcUaJsonHelper.ParseNodeId(resultNodeId));
        }

        /// <summary>
        /// Opens the frame graph over the named or sole active session.
        /// </summary>
        public VisionFrameGraph OpenFrames(string? sessionName = null)
        {
            return CreateClient(sessionName).Frames();
        }

        /// <summary>
        /// Resolves a pipeline by exact unique name (BrowseName.Name or
        /// DisplayName.Text) or by NodeId string, returning both the resolved
        /// entry and a ready-to-use pipeline client.
        /// </summary>
        /// <param name="pipelineSelector">
        /// A NodeId string or an exact pipeline name.
        /// </param>
        /// <param name="sessionName">
        /// Session name to use; defaults to the only active session.
        /// </param>
        /// <param name="ct">
        /// Cancels the operation.
        /// </param>
        public async Task<(VisionNodeEntry Entry, VisionPipelineClient Pipeline)>
            ResolvePipelineAsync(
                string pipelineSelector,
                string? sessionName = null,
                CancellationToken ct = default)
        {
            VisionClient client = CreateClient(sessionName);
            VisionNodeEntry entry = await client.ResolvePipelineAsync(
                pipelineSelector, ct).ConfigureAwait(false);
            VisionPipelineClient pipeline = client.Pipeline(entry.NodeId);
            return (entry, pipeline);
        }

        /// <summary>
        /// Creates the one-shot inference service from the named or sole active session.
        /// </summary>
        public VisionInferenceService CreateInferenceService(string? sessionName = null)
        {
            return CreateClient(sessionName).Inference();
        }

        private readonly OpcUaSessionManager m_sessionManager;
    }
}
