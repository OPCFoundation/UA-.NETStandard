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

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for driving Vision inference pipelines.
    /// </summary>
    [McpServerToolType]
    public sealed class VisionInferenceTools
    {
        /// <summary>
        /// Runs a single one-shot inference on a Vision pipeline.
        /// </summary>
        [McpServerTool(Name = "vision_run_inference")]
        [Description("Invokes RunInference on a Vision pipeline for a single acquisition and returns both " +
            "the ResultId the server assigned and the NodeId it published the result at. Use this when you " +
            "want one deterministic pass, then pass resultNodeId straight to vision_read_detection_result, " +
            "vision_read_inspection_result or vision_read_segmentation_result. Use " +
            "vision_start_continuous_inference instead when you want the server to acquire and publish " +
            "repeatedly. Reports the server's refusal honestly if the pipeline is not runnable; never " +
            "retries silently and never adjusts the pipeline configuration. When the server assigns a " +
            "ResultId but publishes no addressable node, resultNodeId is empty and resolved is false.")]
        public static async Task<VisionInferenceRunHandle> RunInferenceAsync(
            VisionClientAccessor accessor,
            [Description("Pipeline NodeId, for example ns=2;s=Vision/Pipelines/BinPickingPipeline.")] string pipelineNodeId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionPipelineClient pipeline = accessor.OpenPipeline(pipelineNodeId, sessionName);
            string resultId = await pipeline.RunInferenceAsync(timestamp: default, ct).ConfigureAwait(false);
            NodeId resultNodeId = await pipeline.ResolveResultNodeIdAsync(resultId, ct).ConfigureAwait(false);
            return new VisionInferenceRunHandle(
                resultId,
                resultNodeId.IsNull ? string.Empty : resultNodeId.ToString(),
                !resultNodeId.IsNull);
        }

        /// <summary>
        /// Starts continuous inference on a Vision pipeline.
        /// </summary>
        [McpServerTool(Name = "vision_start_continuous_inference")]
        [Description("Invokes StartContinuous on a Vision pipeline so the server acquires and publishes " +
            "results repeatedly. Use vision_run_inference for a single one-shot invocation. Use " +
            "vision_stop_inference to halt the loop. Reports the server's refusal honestly if the " +
            "pipeline is not runnable; never retries silently and never asks the server to change mode. " +
            "Returns no value on success.")]
        public static Task StartContinuousInferenceAsync(
            VisionClientAccessor accessor,
            [Description("Pipeline NodeId to start.")] string pipelineNodeId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionPipelineClient pipeline = accessor.OpenPipeline(pipelineNodeId, sessionName);
            return pipeline.StartContinuousAsync(ct);
        }

        /// <summary>
        /// Stops continuous or in-progress inference on a Vision pipeline.
        /// </summary>
        [McpServerTool(Name = "vision_stop_inference")]
        [Description("Invokes Stop on a Vision pipeline to halt continuous or in-progress inference. Use " +
            "vision_start_continuous_inference to start it again. Use vision_run_inference to run a " +
            "single pass instead. Reports the server's refusal honestly if the pipeline cannot be " +
            "stopped in its current state; never retries silently. Returns no value on success.")]
        public static Task StopInferenceAsync(
            VisionClientAccessor accessor,
            [Description("Pipeline NodeId to stop.")] string pipelineNodeId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionPipelineClient pipeline = accessor.OpenPipeline(pipelineNodeId, sessionName);
            return pipeline.StopAsync(ct);
        }
    }

    /// <summary>
    /// What a single inference run produced: the ResultId the Server assigned and the
    /// NodeId it published the result at. Returned by the vision_run_inference tool.
    /// </summary>
    public sealed record VisionInferenceRunHandle
    {
        /// <summary>
        /// Initializes a run handle.
        /// </summary>
        /// <param name="resultId">The ResultId the Server assigned.</param>
        /// <param name="resultNodeId">
        /// The NodeId the result was published at, empty when it could not be resolved.
        /// </param>
        /// <param name="resolved">
        /// True when the result is addressable and can be read.
        /// </param>
        public VisionInferenceRunHandle(string resultId, string resultNodeId, bool resolved)
        {
            ResultId = resultId;
            ResultNodeId = resultNodeId;
            Resolved = resolved;
        }

        /// <summary>
        /// The ResultId the Server assigned to this run.
        /// </summary>
        public string ResultId { get; }

        /// <summary>
        /// The NodeId the Server published the result at, which is what the
        /// vision_read_*_result tools take. Empty when it could not be resolved.
        /// </summary>
        public string ResultNodeId { get; }

        /// <summary>
        /// True when <see cref="ResultNodeId"/> is usable. False means the Server answered
        /// with a ResultId but published nothing addressable under the pipeline's Results
        /// folder, which a reader would otherwise report as an empty result.
        /// </summary>
        public bool Resolved { get; }
    }
}
