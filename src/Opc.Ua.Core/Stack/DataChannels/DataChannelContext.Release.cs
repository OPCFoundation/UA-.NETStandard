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

namespace Opc.Ua.DataChannels
{
    public sealed partial class DataChannelContext
    {
        /// <summary>
        /// Starts one bounded release per flow and retains its native-stream slot until the operation exits.
        /// The task is observable by both the facade and context disposal, including repeated reset/dispose calls.
        /// </summary>
        private Task<DataChannelResult> QueueFlowReleaseLocked(
            FlowState flow,
            Task afterTransmission,
            DataChannelResult result,
            bool interruptNative = true,
            bool finishNativeStream = false)
        {
            if (flow.ReleaseStarted)
            {
                return flow.Released.Task;
            }
            flow.ReleaseStarted = true;
            m_releasingFlows.Add(flow.Id, flow);
            m_pendingFlowReleases++;
            Task writer = flow.ActiveWrite?.Completion.Task ?? Task.CompletedTask;
            _ = Task.Run(() => ReleaseFlowAsync(
                flow, afterTransmission, writer, result, interruptNative, finishNativeStream));
            return flow.Released.Task;
        }

        /// <summary>
        /// Joins ordered END/RESET, actual native release and any already writer-owned record.
        /// No carrier callback runs while context state is synchronized.
        /// </summary>
        private async Task ReleaseFlowAsync(
            FlowState flow,
            Task afterTransmission,
            Task writer,
            DataChannelResult result,
            bool interruptNative,
            bool finishNativeStream)
        {
            DataChannelResult outcome = DataChannelResult.Good;
            try
            {
                bool native = IsNative && flow.StreamReserved && flow.StreamId != m_carrier.ManagementStreamId;
                if (flow.DirectLane != null && interruptNative)
                {
                    await ReleaseDirectLaneAsync(flow.DirectLane).ConfigureAwait(false);
                }
                if (native && finishNativeStream)
                {
                    using var deadline = m_clock.CreateCancellationTokenSource(m_options.DrainTimeout);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        deadline.Token, m_shutdown.Token);
                    try
                    {
                        await afterTransmission.WaitAsync(linked.Token).ConfigureAwait(false);
                        await writer.WaitAsync(linked.Token).ConfigureAwait(false);
                        if (m_carrier is not IDataChannelFlowFinisher finisher)
                        {
                            throw new InvalidOperationException(
                                "The native carrier does not support orderly flow FIN completion.");
                        }
                        if (flow.ResetSent)
                        {
                            await flow.ResetReceivedSignal.Task.WaitAsync(linked.Token).ConfigureAwait(false);
                        }
                        await finisher.FinishFlowAsync(flow.StreamId, linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (
                        deadline.IsCancellationRequested && !m_shutdown.IsCancellationRequested)
                    {
                        await CloseContextAfterDrainTimeoutAsync().ConfigureAwait(false);
                        outcome = DataChannelResult.Timeout;
                    }
                    catch (OperationCanceledException) when (m_shutdown.IsCancellationRequested)
                    {
                        await m_carrier.CloseFlowAsync(flow.StreamId, result, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                else if (native && interruptNative)
                {
                    // Native RESET is out-of-band: the common RESET can itself be blocked by connection byte credit.
                    await m_carrier.CloseFlowAsync(flow.StreamId, result, CancellationToken.None).ConfigureAwait(false);
                }
                try
                {
                    await afterTransmission.WaitAsync(m_shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (m_shutdown.IsCancellationRequested)
                {
                    // Context shutdown supersedes an unsent RESET but must still release the real stream.
                }
                if (native && !interruptNative && !finishNativeStream)
                {
                    await m_carrier.CloseFlowAsync(flow.StreamId, result, CancellationToken.None).ConfigureAwait(false);
                }
                if (flow.DirectLane != null && !interruptNative)
                {
                    await ReleaseDirectLaneAsync(flow.DirectLane).ConfigureAwait(false);
                }
                await writer.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                m_logger.FlowCarrierFailure(exception);
                outcome = DataChannelResult.Closed;
            }
            lock (m_gate)
            {
                if (flow.StreamReserved)
                {
                    flow.StreamReserved = false;
                    m_budget.Release(FlowResource.Stream, 1);
                }
                if (outcome != DataChannelResult.Good)
                {
                    m_flowReleaseFailed = true;
                    flow.State = DataChannelState.Faulted;
                    flow.Result = outcome;
                    NotifyStateLocked(flow);
                }
                m_pendingFlowReleases--;
                m_releasingFlows.Remove(flow.Id);
                flow.Released.TrySetResult(outcome);
                PulseCapacityLocked();
                WakeLocked();
            }
        }
    }
}
