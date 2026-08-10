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
        [Description("Invokes RunInference on a Vision pipeline for a single acquisition and returns the " +
            "ResultId the server assigned. Use this when you want one deterministic pass, then read the " +
            "published result with vision_read_detection_result, vision_read_inspection_result or " +
            "vision_read_segmentation_result. Use vision_start_continuous_inference instead when you want " +
            "the server to acquire and publish repeatedly. Reports the server's refusal honestly if the " +
            "pipeline is not runnable; never retries silently and never adjusts the pipeline configuration.")]
        public static Task<string> RunInferenceAsync(
            VisionClientAccessor accessor,
            [Description("Pipeline NodeId, for example ns=2;s=Vision/Pipelines/BinPickingPipeline.")] string pipelineNodeId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionPipelineClient pipeline = accessor.OpenPipeline(pipelineNodeId, sessionName);
            return pipeline.RunInferenceAsync(timestamp: default, ct);
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
}
