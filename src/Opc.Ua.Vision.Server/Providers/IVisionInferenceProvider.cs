// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Executes the inference for one <c>InferencePipelineType</c> node.
    /// A pipeline binds a sensor to whatever actually computes results —
    /// on the Server, on an edge GPU, in the cloud, or in a simulator.
    /// </summary>
    /// <remarks>
    /// The Server surfaces <c>RunInference</c>, <c>StartContinuous</c>
    /// and <c>Stop</c> as OPC UA methods; providers only implement the
    /// underlying operations.
    /// </remarks>
    public interface IVisionInferenceProvider
    {
        /// <summary>
        /// Runs a single-shot inference. The returned
        /// <see cref="VisionInferenceRunResult.ResultId"/> is the id the
        /// Server publishes on the pipeline's <c>Results</c> folder for
        /// clients to inspect and reference in
        /// <see cref="IVisionFeedbackSink"/> submissions.
        /// </summary>
        ValueTask<VisionInferenceRunResult> RunInferenceAsync(
            VisionInferenceRunRequest request,
            CancellationToken cancellationToken);

        /// <summary>
        /// Starts continuous inference on the pipeline. Implementations
        /// must be idempotent: calling this on an already-running
        /// pipeline must succeed.
        /// </summary>
        ValueTask<ServiceResult> StartContinuousAsync(
            NodeId pipeline,
            CancellationToken cancellationToken);

        /// <summary>
        /// Stops continuous inference on the pipeline. Implementations
        /// must be idempotent.
        /// </summary>
        ValueTask<ServiceResult> StopAsync(
            NodeId pipeline,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Input to <see cref="IVisionInferenceProvider.RunInferenceAsync"/>.
    /// </summary>
    public readonly record struct VisionInferenceRunRequest(
        NodeId Pipeline,
        NodeId Sensor,
        NodeId Deployment,
        DateTimeUtc Timestamp);

    /// <summary>
    /// Result of
    /// <see cref="IVisionInferenceProvider.RunInferenceAsync"/>.
    /// </summary>
    public sealed record VisionInferenceRunResult(
        ServiceResult ServiceResult,
        string ResultId);
}
