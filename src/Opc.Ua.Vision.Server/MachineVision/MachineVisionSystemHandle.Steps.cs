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
using Opc.Ua.Vision.MachineVision;
using Control = Opc.Ua.Vision.MachineVision.VisionStateMachineTypeIds;
using Step = Opc.Ua.Vision.MachineVision.VisionStepModelStateMachineTypeIds;

namespace Opc.Ua.Vision.Server
{
    public sealed partial class MachineVisionSystemHandle
    {
        /// <summary>
        /// Selects whether a parent state requires an externally synchronized sequence.
        /// Without a sequence the optional step model follows Entry directly to Exit.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask SetStepSequenceAsync(
            uint parentStateNumber,
            bool required,
            CancellationToken cancellationToken = default)
        {
            await EnterRuntimeAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (!m_steps.TryGetValue(parentStateNumber, out StepSlot? slot))
                {
                    throw ServiceResultException.Create(StatusCodes.BadNotSupported, "This step model is not enabled.");
                }
                if (slot.Required == required)
                {
                    return;
                }
                if (m_activeStep == slot &&
                    (!slot.Completion.Task.IsCompleted || parentStateNumber is 7 or 8))
                {
                    throw InvalidState("An in-progress sequence or running job cannot be reconfigured.");
                }
                slot.Required = required;
                if (m_activeStep == slot)
                {
                    await ActivateStepAsync(slot).ConfigureAwait(false);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Signals synchronization from an application-owned interface rather than the OPC UA Sync method.
        /// The step uses the model's WaitToStepAuto transition and the same provider as Sync.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<MachineVisionOperationResult> SynchronizeStepAsync(
            uint parentStateNumber,
            int cause = 0,
            string causeDescription = "",
            CancellationToken cancellationToken = default)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_shutdown.Token);
            await EnterRuntimeAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (!m_steps.TryGetValue(parentStateNumber, out StepSlot? slot))
                {
                    throw ServiceResultException.Create(StatusCodes.BadNotSupported, "This step model is not enabled.");
                }
                return await ExecuteStepAsync(slot, cause, causeDescription, true, lifetime.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        private void BindSteps()
        {
            if (!Has(MachineVisionFeatures.Steps))
            {
                return;
            }
            AddStep(ControlState, "PreoperationalStepModel", 1);
            AddStep(ControlState, "HaltedStepModel", 2);
            AddStep(ControlState, "ErrorStepModel", 3);
            AddStep(AutomaticState, "InitializedStepModel", 5);
            AddStep(AutomaticState, "ReadyStepModel", 6);
            AddStep(AutomaticState, "SingleExecutionStepModel", 7);
            AddStep(AutomaticState, "ContinuousExecutionStepModel", 8);
        }

        private void AddStep(NodeState parent, string name, uint number)
        {
            VisionStepModelStateMachineState state = MachineVisionNodes.Require<VisionStepModelStateMachineState>(
                m_context.Manager, parent, name);
            InitializeFiniteState(state);
            state.SuppressTransitionEvents = true;
            state.SetState(m_context.Context, 0);
            var slot = new StepSlot(state, number);
            m_steps.Add(number, slot);
            Bind(state, "Sync", async (context, args, output, ct) =>
            {
                MachineVisionOperationResult result = await ExecuteStepAsync(
                    slot, Int32(args, 0), Text(args, 1), false, ct)
                    .ConfigureAwait(false);
                Outputs(output, Variant.From(result.Error));
                return result;
            });
        }

        private async ValueTask<MachineVisionOperationResult> ExecuteStepAsync(
            StepSlot slot, int cause, string description, bool automatic, CancellationToken ct)
        {
            VisionStepModelStateMachineState state = slot.State;
            if (m_activeStep != slot || state.GetStateId(state.CurrentState!.Id!.Value) != Step.StateIds.Wait)
            {
                throw InvalidState("The synchronization step model is not waiting.");
            }
            await TransitionAsync(state, automatic ? Step.TransitionIds.WaitToStepAuto : Step.TransitionIds.WaitToStep)
                .ConfigureAwait(false);
            MachineVisionStepResult result;
            try
            {
                result = await m_provider.ExecuteStepAsync(
                    new MachineVisionStepRequest(slot.ParentStateNumber, slot.Index, cause, description), ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                await EnterErrorAsync("Synchronization step did not complete.", CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
            if (!result.Outcome.Succeeded)
            {
                await EnterErrorAsync("Synchronization step failed.", ct).ConfigureAwait(false);
                return result.Outcome;
            }
            slot.Index++;
            await TransitionAsync(state, result.MoreSteps
                ? Step.TransitionIds.StepToWaitAuto : Step.TransitionIds.StepToExitAuto).ConfigureAwait(false);
            if (result.MoreSteps)
            {
                await EmitAsync(new NextStepEventState(null), "Vision system is waiting for the next synchronization.")
                    .ConfigureAwait(false);
            }
            else
            {
                await EmitAsync(new LeaveStepSequenceEventState(null), "Vision synchronization sequence completed.")
                    .ConfigureAwait(false);
                slot.Completion.TrySetResult(true);
                if (slot.ParentStateNumber == 5)
                {
                    await UpdateReadyStateAsync(ct).ConfigureAwait(false);
                }
            }
            return result.Outcome;
        }

        private async ValueTask RefreshStepsAsync()
        {
            uint number = ControlState.CurrentState?.Number?.Value ?? 0;
            if (ControlState.GetStateId(ControlState.CurrentState!.Id!.Value) == Control.StateIds.Operational)
            {
                number = AutomaticState.CurrentState?.Number?.Value ?? 0;
            }
            m_steps.TryGetValue(number, out StepSlot? current);
            if (ReferenceEquals(current, m_activeStep))
            {
                return;
            }
            if (m_activeStep != null)
            {
                m_activeStep.State.SetState(m_context.Context, 0);
                m_activeStep.Completion.TrySetCanceled();
            }
            m_activeStep = current;
            if (current != null)
            {
                await ActivateStepAsync(current).ConfigureAwait(false);
            }
        }

        private async ValueTask ActivateStepAsync(StepSlot slot)
        {
            slot.Completion.TrySetCanceled();
            slot.Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            slot.Index = 0;
            slot.State.SetState(m_context.Context, Step.StateIds.Entry);
            await TransitionAsync(slot.State, slot.Required
                ? Step.TransitionIds.EntryToWaitAuto : Step.TransitionIds.EntryToExitAuto).ConfigureAwait(false);
            if (slot.Required)
            {
                await EmitAsync(new EnterStepSequenceEventState(null), "Vision synchronization sequence entered.")
                    .ConfigureAwait(false);
            }
            else
            {
                slot.Completion.TrySetResult(true);
            }
        }

        private Task CurrentStepCompletion()
        {
            return m_activeStep?.Completion.Task ?? Task.CompletedTask;
        }

        private void BindSafety()
        {
            if (!Has(MachineVisionFeatures.Safety))
            {
                return;
            }
            SafetyStateManagementState safety = MachineVisionNodes.Require<SafetyStateManagementState>(
                m_context.Manager, State, "SafetyStateManagement");
            Bind(safety, "ReportSafetyState", async (context, args, output, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                bool triggered = Boolean(args, 0);
                string information = Text(args, 1);
                MachineVisionNodes.Set(context, safety.VisionSafetyTriggered!, Variant.From(triggered), Now);
                MachineVisionNodes.Set(context, safety.VisionSafetyInformation!, Variant.From(information), Now);
                await safety.ClearChangeMasksAsync(context, true, CancellationToken.None).ConfigureAwait(false);
                await EmitAsync(new VisionSafetyEventState(null), "Vision safety information reported.", notification =>
                {
                    notification.VisionSafetyTriggered!.Value = triggered;
                    notification.VisionSafetyInformation!.Value = information;
                }).ConfigureAwait(false);
                Outputs(output, Variant.From(0));
                return MachineVisionOperationResult.Good;
            });
        }

        private readonly Dictionary<uint, StepSlot> m_steps = [];
        private StepSlot? m_activeStep;

        private sealed class StepSlot
        {
            public StepSlot(VisionStepModelStateMachineState state, uint parentStateNumber)
            {
                State = state;
                ParentStateNumber = parentStateNumber;
            }

            public VisionStepModelStateMachineState State { get; }

            public uint ParentStateNumber { get; }

            public uint Index { get; set; }

            public bool Required { get; set; }

            public TaskCompletionSource<bool> Completion { get; set; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
