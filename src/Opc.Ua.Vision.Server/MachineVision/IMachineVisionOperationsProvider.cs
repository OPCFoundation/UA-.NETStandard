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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Vision.MachineVision;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// An application-controlled action of the released vision state model.
    /// </summary>
    public enum MachineVisionControlAction
    {
        /// <summary>
        /// Release hardware resources so the system can be powered down.
        /// </summary>
        Halt,

        /// <summary>
        /// Clear job parameterization and return the system to Preoperational.
        /// </summary>
        Reset,

        /// <summary>
        /// Enter automatic operation.
        /// </summary>
        SelectAutomatic,

        /// <summary>
        /// Gracefully stop the current job.
        /// </summary>
        Stop,

        /// <summary>
        /// Abort the current job.
        /// </summary>
        Abort,

        /// <summary>
        /// Enable simulated operation.
        /// </summary>
        EnableSimulation,

        /// <summary>
        /// Disable simulated operation.
        /// </summary>
        DisableSimulation
    }

    /// <summary>
    /// Preserves the independent service and domain error channels of OPC 40100-1.
    /// Positive domain errors are reserved for future specification allocations.
    /// </summary>
    /// <param name="ServiceResult">The OPC UA service result.</param>
    /// <param name="Error">Zero on success, or an application-specific negative error.</param>
    public sealed record MachineVisionOperationResult(ServiceResult ServiceResult, int Error = 0)
    {
        /// <summary>
        /// Gets the successful outcome.
        /// </summary>
        public static MachineVisionOperationResult Good { get; } = new(ServiceResult.Good);

        /// <summary>
        /// Gets whether both outcome channels indicate success.
        /// </summary>
        public bool Succeeded => ServiceResult != null && ServiceResult.IsGood(ServiceResult) && Error == 0;
    }

    /// <summary>
    /// The immutable execution identity and inputs of one accepted vision job.
    /// </summary>
    /// <param name="JobId">The server-generated job identifier.</param>
    /// <param name="MeasurementId">The client's optional measurement identifier.</param>
    /// <param name="PartId">The client's optional part identifier.</param>
    /// <param name="Recipe">The selected prepared recipe, if managed by the runtime.</param>
    /// <param name="ProductId">The client's optional product identifier.</param>
    /// <param name="Configuration">The active configuration, if managed by the runtime.</param>
    /// <param name="Parameters">Opaque application parameters.</param>
    /// <param name="Continuous">Whether the job may produce multiple measurements.</param>
    /// <param name="Simulated">The simulation setting captured at job creation.</param>
    public sealed record MachineVisionJob(
        JobIdDataType JobId,
        MeasIdDataType MeasurementId,
        PartIdDataType PartId,
        MachineVisionDocument? Recipe,
        ProductIdDataType ProductId,
        MachineVisionDocument? Configuration,
        ArrayOf<Variant> Parameters,
        bool Continuous,
        bool Simulated);

    /// <summary>
    /// An acquisition notification and optional result produced by an execution provider.
    /// The stream ending signals job completion; each result keeps its own partial/final status.
    /// </summary>
    /// <param name="AcquisitionDone">Whether external equipment can move the observed part.</param>
    /// <param name="Result">Optional result metadata and content.</param>
    /// <param name="FileContent">Additional opaque result content available by file transfer.</param>
    public sealed record MachineVisionAcquisition(
        bool AcquisitionDone,
        ResultDataType? Result,
        ByteString FileContent = default);

    /// <summary>
    /// Context for one application-defined synchronization step.
    /// </summary>
    /// <param name="ParentState">The published state number of the owning state.</param>
    /// <param name="StepIndex">Zero-based index within the current step sequence.</param>
    /// <param name="Cause">The client's application-defined cause.</param>
    /// <param name="CauseDescription">The client's description.</param>
    public sealed record MachineVisionStepRequest(
        uint ParentState,
        uint StepIndex,
        int Cause,
        string CauseDescription);

    /// <summary>
    /// Outcome of a synchronized step. MoreSteps is consulted only after success.
    /// </summary>
    /// <param name="Outcome">The service and domain outcome.</param>
    /// <param name="MoreSteps">Whether another synchronization cycle is required.</param>
    public sealed record MachineVisionStepResult(MachineVisionOperationResult Outcome, bool MoreSteps);

    /// <summary>
    /// The application/hardware seam behind OPC 40100-1. The runtime owns protocol state,
    /// metadata, file handles, and events; providers own acquisition and payload interpretation.
    /// </summary>
    /// <remarks>
    /// Calls for one system are serialized except for the execution stream. Control operations
    /// may run while that stream is active and must honor cancellation. Providers must not
    /// call back into a serialized command while handling that command.
    /// </remarks>
    public interface IMachineVisionOperationsProvider
    {
        /// <summary>
        /// Applies a control action before the runtime commits the associated transition.
        /// </summary>
        ValueTask<MachineVisionOperationResult> ControlAsync(
            MachineVisionControlAction action,
            int cause,
            string causeDescription,
            CancellationToken cancellationToken);

        /// <summary>
        /// Prepares or unprepares the supplied recipe. Completion means it can, or can no longer,
        /// be selected for execution; long preparations remain fully asynchronous.
        /// </summary>
        ValueTask<MachineVisionOperationResult> PrepareRecipeAsync(
            MachineVisionDocument recipe,
            bool prepare,
            CancellationToken cancellationToken);

        /// <summary>
        /// Applies a committed configuration before it becomes the active configuration.
        /// </summary>
        ValueTask<MachineVisionOperationResult> ActivateConfigurationAsync(
            MachineVisionDocument configuration,
            CancellationToken cancellationToken);

        /// <summary>
        /// Validates opaque uploaded recipe or configuration data before atomic store commit.
        /// </summary>
        ValueTask<MachineVisionOperationResult> ValidateDocumentAsync(
            MachineVisionDocument document,
            CancellationToken cancellationToken);

        /// <summary>
        /// Validates and accepts a job before the start method returns its identifier.
        /// </summary>
        ValueTask<MachineVisionOperationResult> StartJobAsync(
            MachineVisionJob job,
            CancellationToken cancellationToken);

        /// <summary>
        /// Produces the acquisitions of an accepted single or continuous job.
        /// </summary>
        IAsyncEnumerable<MachineVisionAcquisition> ExecuteAsync(
            MachineVisionJob job,
            CancellationToken cancellationToken);

        /// <summary>
        /// Executes one step after external synchronization.
        /// </summary>
        ValueTask<MachineVisionStepResult> ExecuteStepAsync(
            MachineVisionStepRequest request,
            CancellationToken cancellationToken);
    }
}
