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
using Opc.Ua.ISA95.Server.Providers;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class MachineToolJobSource
    {
        /// <inheritdoc/>
        public async ValueTask<Isa95JobOrderReceiptV2> ReceiveJobOrderAsync(
            Isa95JobOrderOperationV2 operation,
            V2.ISA95JobOrderDataType jobOrder,
            ArrayOf<LocalizedText> comment = default,
            CancellationToken cancellationToken = default)
        {
            if (jobOrder == null)
            {
                throw new ArgumentNullException(nameof(jobOrder));
            }
            CheckReentrancy();
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                jobOrder = CopyPayload(jobOrder, comment);
            }
            catch (ServiceResultException exception)
            {
                return Refused(Isa95JobReturnStatus.InvalidRequest, exception.Message);
            }
            DateTimeOffset now = m_timeProvider.GetUtcNow();
            await EnterAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                string id = jobOrder.JobOrderID ?? string.Empty;
                bool writesContent = operation is Isa95JobOrderOperationV2.Store
                    or Isa95JobOrderOperationV2.StoreAndStart or Isa95JobOrderOperationV2.Update;
                if (writesContent && (!m_descriptors.TryGetValue(id, out MachineToolJobSourceDescriptor? descriptor) ||
                    jobOrder.WorkMasterID.Count != 1 ||
                    jobOrder.WorkMasterID[0].ID != descriptor.WorkMasterId))
                {
                    return Refused(Isa95JobReturnStatus.InvalidRequest,
                        "The job and its sole work master must match an explicit program descriptor.");
                }
                ChangeKind changes = operation switch
                {
                    Isa95JobOrderOperationV2.Cancel or Isa95JobOrderOperationV2.Clear => ChangeKind.Catalog,
                    Isa95JobOrderOperationV2.Update => ChangeKind.Status | ChangeKind.Catalog,
                    _ => ChangeKind.Status
                };
                int revisions = operation == Isa95JobOrderOperationV2.Cancel ? 2 : 1;
                if (!HasCapacity(changes, revisions))
                {
                    return Refused(Isa95JobReturnStatus.UnableToAccept,
                        "A revision or subscriber backlog is full; drain it before accepting more changes.");
                }
                MachineToolProductionJob? next = PrepareOperation(operation, id, Sample(now));
                cancellationToken.ThrowIfCancellationRequested();
                m_clock.UtcNow = now;
                Isa95JobOrderReceiptV2 receipt = await m_engine.ReceiveJobOrderAsync(
                    operation, jobOrder, comment, cancellationToken).ConfigureAwait(false);
                if (receipt.ReturnStatus != Isa95JobReturnStatus.Success)
                {
                    return receipt;
                }
                AccountChanges(changes);
                if (next != null)
                {
                    m_jobs[id] = next;
                }
                if (operation == Isa95JobOrderOperationV2.Cancel)
                {
                    // A cancelled initializing job must be finally observed as Aborted before its legacy removal.
                    await CommitAsync().ConfigureAwait(false);
                }
                if (operation is Isa95JobOrderOperationV2.Cancel or Isa95JobOrderOperationV2.Clear)
                {
                    m_jobs.Remove(id);
                    if (m_activeJob == id)
                    {
                        m_activeJob = string.Empty;
                    }
                }
                await CommitAsync().ConfigureAwait(false);
                return receipt;
            }
            finally
            {
                Exit();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<Isa95JobOrderReceiptV2> TransitionAsync(
            string jobOrderId,
            Isa95JobExecutionTransition transition,
            CancellationToken cancellationToken = default)
        {
            if (jobOrderId == null)
            {
                throw new ArgumentNullException(nameof(jobOrderId));
            }
            CheckReentrancy();
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset now = m_timeProvider.GetUtcNow();
            await EnterAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (!HasCapacity(ChangeKind.Status, 1))
                {
                    return Refused(Isa95JobReturnStatus.UnableToAccept, "A delivery backlog is full.");
                }
                m_jobs.TryGetValue(jobOrderId, out MachineToolProductionJob? job);
                if (job != null && transition == Isa95JobExecutionTransition.BeginExecution)
                {
                    foreach (MachineToolProductionJob active in m_jobs.Values)
                    {
                        if (active.Identifier != jobOrderId && active.Phase is (
                            MachineToolProductionPhase.Running or MachineToolProductionPhase.Interrupted))
                        {
                            return Refused(Isa95JobReturnStatus.UnableToAccept,
                                "This single-machine simulator already has an executing job.");
                        }
                    }
                }
                MachineToolProductionJob? next = job;
                if (job != null)
                {
                    bool normalEnd = transition == Isa95JobExecutionTransition.Complete &&
                        job.Phase == MachineToolProductionPhase.Running && job.RunsCompleted + 1 == job.RunsPlanned;
                    if (normalEnd && job.PartSets.Count != 0 &&
                        job.PartSets[0].PartsCompletedPerRun != job.PartSets[0].PartsPlannedPerRun)
                    {
                        return Refused(Isa95JobReturnStatus.UnableToAccept,
                            "Normal completion needs explicit observations for all declared current-run parts.");
                    }
                    MachineToolProductionPhase phase = transition switch
                    {
                        Isa95JobExecutionTransition.BeginExecution => MachineToolProductionPhase.Running,
                        Isa95JobExecutionTransition.Hold => MachineToolProductionPhase.Interrupted,
                        Isa95JobExecutionTransition.Complete => normalEnd
                            ? MachineToolProductionPhase.Ended : MachineToolProductionPhase.Aborted,
                        _ => job.Phase
                    };
                    next = Advance(job, phase, Sample(now), normalEnd);
                }
                cancellationToken.ThrowIfCancellationRequested();
                m_clock.UtcNow = now;
                Isa95JobOrderReceiptV2 receipt = await m_engine.TransitionAsync(
                    jobOrderId, transition, cancellationToken).ConfigureAwait(false);
                if (receipt.ReturnStatus != Isa95JobReturnStatus.Success)
                {
                    return receipt;
                }
                if (next != null)
                {
                    m_jobs[jobOrderId] = next;
                }
                if (transition == Isa95JobExecutionTransition.BeginExecution)
                {
                    m_activeJob = jobOrderId;
                }
                AccountChanges(ChangeKind.Status);
                await CommitAsync().ConfigureAwait(false);
                return receipt;
            }
            finally
            {
                Exit();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<Isa95JobResponseReceiptV2> ReceiveJobResponseAsync(
            V2.ISA95JobResponseDataType response, CancellationToken cancellationToken = default)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }
            CheckReentrancy();
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                response = CopyPayload(response);
            }
            catch (ServiceResultException exception)
            {
                return RefusedResponse(Isa95JobReturnStatus.InvalidRequest, exception.Message);
            }
            DateTimeOffset now = m_timeProvider.GetUtcNow();
            await EnterAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                V2.ISA95JobOrderAndStateDataType? order = null;
                foreach (V2.ISA95JobOrderAndStateDataType candidate in m_capture.JobOrders)
                {
                    if (candidate.JobOrder.JobOrderID == response.JobOrderID)
                    {
                        order = candidate;
                        break;
                    }
                }
                if (order == null)
                {
                    return RefusedResponse(Isa95JobReturnStatus.UnknownJobOrderId,
                        "Responses require an accepted, still stored job order.");
                }
                if (!SameState(order.State, response.JobState))
                {
                    return RefusedResponse(Isa95JobReturnStatus.InvalidStatus,
                        "A response must describe the engine's current accepted state, not command a transition.");
                }
                if (!HasCapacity(ChangeKind.Response, 1))
                {
                    return RefusedResponse(Isa95JobReturnStatus.UnableToAccept, "A delivery backlog is full.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                m_clock.UtcNow = now;
                Isa95JobResponseReceiptV2 receipt = await m_engine.ReceiveJobResponseAsync(response, cancellationToken)
                    .ConfigureAwait(false);
                if (receipt.ReturnStatus == Isa95JobReturnStatus.Success)
                {
                    AccountChanges(ChangeKind.Response);
                    await CommitAsync().ConfigureAwait(false);
                }
                return receipt;
            }
            finally
            {
                Exit();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<V1.ISA95JobOrderDataType>> GetJobOrdersV1Async(
            CancellationToken cancellationToken = default)
        {
            ArrayOf<V1.ISA95JobOrderDataType> orders = await QueryAsync(
                () => m_engine.GetJobOrdersV1Async(cancellationToken), cancellationToken).ConfigureAwait(false);
            return CoreUtils.Clone(orders);
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<V2.ISA95JobOrderAndStateDataType>> GetJobOrdersV2Async(
            CancellationToken cancellationToken = default)
        {
            ArrayOf<V2.ISA95JobOrderAndStateDataType> orders = await QueryAsync(
                () => m_engine.GetJobOrdersV2Async(cancellationToken), cancellationToken).ConfigureAwait(false);
            return CoreUtils.Clone(orders);
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<V2.ISA95JobResponseDataType>> GetJobResponsesV2Async(
            CancellationToken cancellationToken = default)
        {
            ArrayOf<V2.ISA95JobResponseDataType> responses = await QueryAsync(
                () => m_engine.GetJobResponsesV2Async(cancellationToken), cancellationToken).ConfigureAwait(false);
            return CoreUtils.Clone(responses);
        }

        /// <inheritdoc/>
        public async ValueTask<Isa95JobResponseByIdResultV2> RequestJobResponseByJobOrderIdAsync(
            string jobOrderId, CancellationToken cancellationToken = default)
        {
            Isa95JobResponseByIdResultV2 result = await QueryAsync(
                () => m_engine.RequestJobResponseByJobOrderIdAsync(jobOrderId, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            return result with { Response = CoreUtils.Clone(result.Response) };
        }

        /// <inheritdoc/>
        public async ValueTask<Isa95JobResponsesByStateResultV2> RequestJobResponsesByStateAsync(
            ArrayOf<V2.ISA95StateDataType> state, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                state = CopyPayload(new V2.ISA95JobResponseDataType { JobState = state }).JobState;
            }
            catch (ServiceResultException exception)
            {
                return new Isa95JobResponsesByStateResultV2
                {
                    Result = new ServiceResult(StatusCodes.Uncertain, new LocalizedText(exception.Message)),
                    ReturnStatus = Isa95JobReturnStatus.InvalidRequest,
                    Responses = []
                };
            }
            Isa95JobResponsesByStateResultV2 result = await QueryAsync(
                () => m_engine.RequestJobResponsesByStateAsync(state, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            return result with { Responses = CoreUtils.Clone(result.Responses) };
        }

        /// <summary>
        /// Serializes actual engine queries with the projection commit, without implementing a second protocol engine.
        /// </summary>
        /// <typeparam name="T">The existing engine query result.</typeparam>
        private async ValueTask<T> QueryAsync<T>(Func<ValueTask<T>> query, CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                return await query().ConfigureAwait(false);
            }
            finally
            {
                Exit();
            }
        }

        /// <summary>
        /// Stages a legacy projection without deciding whether the protocol operation is valid.
        /// Only the existing ISA-95 engine's successful receipt authorizes committing it.
        /// </summary>
        private MachineToolProductionJob? PrepareOperation(
            Isa95JobOrderOperationV2 operation, string id, MachineToolSampleInfo sample)
        {
            m_jobs.TryGetValue(id, out MachineToolProductionJob? job);
            if (job == null && operation is (
                Isa95JobOrderOperationV2.Store or Isa95JobOrderOperationV2.StoreAndStart))
            {
                MachineToolJobSourceDescriptor descriptor = m_descriptors[id];
                return new MachineToolProductionJob
                {
                    Key = id,
                    Identifier = id,
                    CustomerOrderIdentifier = descriptor.CustomerOrderIdentifier,
                    OrderIdentifier = descriptor.OrderIdentifier,
                    RunsPlanned = descriptor.RunsPlanned,
                    RunsPlannedIsValid = true,
                    Sample = sample,
                    Programs =
                    [
                        new MachineToolProgram
                        {
                            Key = descriptor.ProgramName,
                            Name = descriptor.ProgramName,
                            JobIdentifier = id,
                            Sample = sample
                        }
                    ],
                    PartSets = descriptor.PartSetKey.Length == 0 ? [] :
                    [
                        new MachineToolProductionPartSet
                        {
                            Key = descriptor.PartSetKey,
                            Name = descriptor.PartSetName,
                            PartsPlannedPerRun = descriptor.PartsPlannedPerRun
                        }
                    ]
                };
            }
            if (job == null)
            {
                return null;
            }
            MachineToolProductionPhase phase = operation switch
            {
                Isa95JobOrderOperationV2.Pause => MachineToolProductionPhase.Interrupted,
                Isa95JobOrderOperationV2.Resume => MachineToolProductionPhase.Running,
                Isa95JobOrderOperationV2.Stop or Isa95JobOrderOperationV2.Abort or
                    Isa95JobOrderOperationV2.Cancel => MachineToolProductionPhase.Aborted,
                _ => job.Phase
            };
            return Advance(job, phase, sample);
        }

        /// <summary>
        /// Checks a complete canonical state path without accepting response ingestion as an execution command.
        /// </summary>
        private static bool SameState(
            ArrayOf<V2.ISA95StateDataType> expected, ArrayOf<V2.ISA95StateDataType> actual)
        {
            if (expected.Count != actual.Count)
            {
                return false;
            }
            for (int i = 0; i < expected.Count; i++)
            {
                if (actual[i] == null || actual[i].StateNumber != expected[i].StateNumber ||
                    !actual[i].BrowsePath.IsEqual(expected[i].BrowsePath))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Preserves the standard return-status bitmap semantics for adapter policy refusals.
        /// </summary>
        private static Isa95JobOrderReceiptV2 Refused(ulong returnStatus, string message) => new()
        {
            ReturnStatus = returnStatus,
            Result = new ServiceResult(StatusCodes.Uncertain, new LocalizedText(message))
        };

        /// <summary>
        /// Reports a refused explicit response without mutating either representation.
        /// </summary>
        private static Isa95JobResponseReceiptV2 RefusedResponse(ulong returnStatus, string message) => new()
        {
            ReturnStatus = returnStatus,
            Result = new ServiceResult(StatusCodes.Uncertain, new LocalizedText(message))
        };
    }
}
