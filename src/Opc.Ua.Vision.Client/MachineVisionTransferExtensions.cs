// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.StateMachines;

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// Shares transfer-completion semantics between complete and chunked Vision workflows.
    /// </summary>
    public static class MachineVisionTransferExtensions
    {
        /// <summary>
        /// Waits for Idle, rejects Error or unavailable state, and preserves caller cancellation.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="client"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="IOException"></exception>
        /// <exception cref="TimeoutException"></exception>
        public static async ValueTask<FiniteStateSnapshot> WaitForCompletionAsync(
            this FileTransferStateMachineTypeClient client,
            TimeSpan? timeout = null,
            TimeProvider? timeProvider = null,
            CancellationToken ct = default)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }
            using CancellationTokenSource? deadline = timeout.HasValue
                ? (timeProvider ?? TimeProvider.System).CreateCancellationTokenSource(timeout.Value) : null;
            using CancellationTokenSource? linked = deadline == null
                ? null : CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            CancellationToken effective = linked?.Token ?? ct;
            try
            {
                while (true)
                {
                    effective.ThrowIfCancellationRequested();
                    FiniteStateSnapshot state = await client.GetCurrentFiniteStateAsync(effective)
                        .ConfigureAwait(false);
                    if (StatusCode.IsBad(state.Status))
                    {
                        throw new ServiceResultException(state.Status);
                    }
                    if (state.CurrentStateId.IsNull)
                    {
                        throw new ServiceResultException(StatusCodes.BadNoData,
                            "The transfer completion state is unavailable.");
                    }
                    if (state.CurrentStateId == ObjectIds.FileTransferStateMachineType_Idle)
                    {
                        return state;
                    }
                    if (state.CurrentStateId == ObjectIds.FileTransferStateMachineType_Error)
                    {
                        throw new IOException("The server failed to apply the uploaded vision document.");
                    }
                    await Task.Delay(100, effective).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException error) when (
                deadline?.IsCancellationRequested == true && !ct.IsCancellationRequested)
            {
                throw new TimeoutException("The transfer did not complete within the requested timeout.", error);
            }
        }
    }
}
