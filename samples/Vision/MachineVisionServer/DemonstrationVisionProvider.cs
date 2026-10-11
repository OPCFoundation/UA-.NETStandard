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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Vision;
using Opc.Ua.Vision.MachineVision;
using Opc.Ua.Vision.Server;
using ControlIds = Opc.Ua.Vision.MachineVision.VisionAutomaticModeStateMachineTypeIds;

namespace MachineVisionSample
{
    /// <summary>
    /// One deterministic acquisition engine behind both the released control model and the draft pipeline.
    /// Draft commands enter the same control runtime; no command silently selects or resets its mode.
    /// </summary>
    internal sealed class DemonstrationVisionProvider : IMachineVisionOperationsProvider, IVisionInferenceProvider
    {
        /// <summary>
        /// Creates the engine with an injectable clock.
        /// </summary>
        public DemonstrationVisionProvider(TimeProvider clock)
        {
            m_clock = clock;
        }

        /// <summary>
        /// Connects the two address-space views to this single engine.
        /// </summary>
        public void Attach(
            IVisionBuildContext context, MachineVisionSystemHandle system, InferencePipelineState pipeline)
        {
            m_context = context;
            m_system = system;
            m_pipeline = pipeline;
        }

        /// <inheritdoc/>
        public ValueTask<MachineVisionOperationResult> ControlAsync(
            MachineVisionControlAction action, int cause, string causeDescription, CancellationToken cancellationToken)
        {
            return Accept(cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<MachineVisionOperationResult> PrepareRecipeAsync(
            MachineVisionDocument recipe, bool prepare, CancellationToken cancellationToken)
        {
            return Accept(cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<MachineVisionOperationResult> ActivateConfigurationAsync(
            MachineVisionDocument configuration, CancellationToken cancellationToken)
        {
            return Accept(cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<MachineVisionOperationResult> ValidateDocumentAsync(
            MachineVisionDocument document, CancellationToken cancellationToken)
        {
            return Accept(cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<MachineVisionOperationResult> StartJobAsync(
            MachineVisionJob job, CancellationToken cancellationToken)
        {
            return Accept(cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<MachineVisionStepResult> ExecuteStepAsync(
            MachineVisionStepRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<MachineVisionStepResult>(
                new MachineVisionStepResult(MachineVisionOperationResult.Good, false));
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<MachineVisionAcquisition> ExecuteAsync(
            MachineVisionJob job,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    long sequence = Interlocked.Increment(ref m_sequence);
                    string id = "synthetic-" + sequence.ToString(CultureInfo.InvariantCulture);
                    DateTimeUtc timestamp = m_clock.GetUtcNow().UtcDateTime;
                    yield return new MachineVisionAcquisition(true, new ResultDataType
                    {
                        ResultId = new ResultIdDataType { Id = id },
                        ResultState = 1,
                        CreationTime = timestamp,
                        ResultContent = [Variant.From(sequence)],
                        EncodingMask = (uint)ResultDataTypeFields.ResultContent
                    });
                    await PublishDraftAsync(id, timestamp, cancellationToken).ConfigureAwait(false);
                    if (job.MeasurementId.Id is string measurement &&
                        m_pending.TryGetValue(measurement, out TaskCompletionSource<string>? completion))
                    {
                        completion.TrySetResult(id);
                    }
                    if (job.Continuous)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    }
                }
                while (job.Continuous);
            }
            finally
            {
                if (job.MeasurementId.Id is string measurement &&
                    m_pending.TryGetValue(measurement, out TaskCompletionSource<string>? completion) &&
                    !completion.Task.IsCompleted)
                {
                    completion.TrySetException(ServiceResultException.Create(
                        cancellationToken.IsCancellationRequested
                            ? StatusCodes.BadRequestCancelledByClient : StatusCodes.BadInternalError,
                        "The shared inspection ended before both result views were published."));
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask<VisionInferenceRunResult> RunInferenceAsync(
            VisionInferenceRunRequest request, CancellationToken cancellationToken)
        {
            MachineVisionSystemHandle system = RequireSystem();
            string correlation = Guid.NewGuid().ToString("N");
            var completed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_pending[correlation] = completed;
            try
            {
                ServiceResult outcome = await CallAsync(system.AutomaticState, system.AutomaticState.StartSingleJob!,
                    [
                        Variant.FromStructure(new MeasIdDataType { Id = correlation }),
                        Variant.FromStructure(new PartIdDataType()),
                        Variant.FromStructure(new RecipeIdExternalDataType()),
                        Variant.FromStructure(new ProductIdDataType()),
                        Variant.From(ArrayOf<Variant>.Empty)
                    ], cancellationToken).ConfigureAwait(false);
                if (ServiceResult.IsBad(outcome))
                {
                    return new VisionInferenceRunResult(outcome, string.Empty);
                }
                string result = await completed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new VisionInferenceRunResult(ServiceResult.Good, result);
            }
            finally
            {
                m_pending.TryRemove(correlation, out _);
            }
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> StartContinuousAsync(NodeId pipeline, CancellationToken cancellationToken)
        {
            MachineVisionSystemHandle system = RequireSystem();
            if (system.AutomaticState.GetStateId(system.AutomaticState.CurrentState!.Id!.Value) ==
                ControlIds.StateIds.ContinuousExecution)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }
            return CallAsync(system.AutomaticState, system.AutomaticState.StartContinuous!,
                [
                    Variant.FromStructure(new MeasIdDataType()),
                    Variant.FromStructure(new PartIdDataType()),
                    Variant.FromStructure(new RecipeIdExternalDataType()),
                    Variant.FromStructure(new ProductIdDataType()),
                    Variant.From(ArrayOf<Variant>.Empty)
                ], cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> StopAsync(NodeId pipeline, CancellationToken cancellationToken)
        {
            MachineVisionSystemHandle system = RequireSystem();
            return CallAsync(system.AutomaticState, system.AutomaticState.Stop!,
                [Variant.From(0), Variant.From("Draft Vision stop request")], cancellationToken);
        }

        private async ValueTask PublishDraftAsync(string id, DateTimeUtc timestamp, CancellationToken ct)
        {
            IVisionBuildContext context = m_context ??
                throw new InvalidOperationException("The sample provider is not attached.");
            InferencePipelineState pipeline = m_pipeline!;
            FolderState results = pipeline.Results!;
            InspectionResultState result = context.Context.CreateInstanceOfInspectionResultType(
                results, new QualifiedName(id, context.InstanceNamespaceIndex));
            result.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.Organizes;
            result.ResultId!.Value = id;
            result.CreationTime!.Value = timestamp;
            result.CreateOrReplacePipeline(context.Context, null).Value = pipeline.NodeId;
            result.CreateOrReplaceSensor(context.Context, null).Value = pipeline.Sensor!.Value;
            result.CreateOrReplaceEvaluation(context.Context, null).Value = VisionResultEvaluationEnum.Ok;
            result.CreateOrReplaceCharacteristics(context.Context, null).Value = [];
            results.AddChild(result);
            await context.Manager.AddPredefinedNodeAsync(result, ct).ConfigureAwait(false);
            m_draftResults.Enqueue(result);
            while (m_draftResults.Count > 32 && m_draftResults.TryDequeue(out InspectionResultState? oldest))
            {
                results.RemoveChild(oldest);
                await context.Manager.DeleteNodeAsync(context.Manager.SystemContext, oldest.NodeId, ct)
                    .ConfigureAwait(false);
            }
        }

        private async ValueTask<ServiceResult> CallAsync(
            NodeState owner, MethodState method, ArrayOf<Variant> arguments, CancellationToken ct)
        {
            IVisionBuildContext context = m_context ??
                throw new InvalidOperationException("The sample provider is not attached.");
            var errors = new List<ServiceResult>();
            var output = new List<Variant>();
            ServiceResult status = await method.CallAsync(context.Context, owner.NodeId, arguments, errors, output, ct)
                .ConfigureAwait(false);
            foreach (ServiceResult error in errors)
            {
                if (ServiceResult.IsBad(error))
                {
                    return error;
                }
            }
            if (ServiceResult.IsGood(status) && output.Count != 0 &&
                output[output.Count - 1].TryGetValue(out int domainError) && domainError != 0)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState, "Domain error {0}.", domainError);
            }
            return status;
        }

        private MachineVisionSystemHandle RequireSystem()
        {
            return m_system ?? throw new InvalidOperationException("The sample provider is not attached.");
        }

        private static ValueTask<MachineVisionOperationResult> Accept(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return new ValueTask<MachineVisionOperationResult>(MachineVisionOperationResult.Good);
        }

        private readonly TimeProvider m_clock;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> m_pending = new();
        private readonly ConcurrentQueue<InspectionResultState> m_draftResults = new();
        private IVisionBuildContext? m_context;
        private MachineVisionSystemHandle? m_system;
        private InferencePipelineState? m_pipeline;
        private long m_sequence;
    }
}
