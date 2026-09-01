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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Vision;

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
