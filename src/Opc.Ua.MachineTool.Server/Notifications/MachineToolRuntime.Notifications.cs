// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class MachineToolRuntime<TState>
    {
        /// <summary>
        /// Gets the selected per-instance notification binding, or null for mandatory-only machines.
        /// </summary>
        public MachineToolNotificationController? Notifications { get; private set; }

        /// <summary>
        /// Refreshes operator-visible notifications without commanding equipment or acknowledging any condition.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask RefreshNotificationsAsync(CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource operation = BeginOperation(cancellationToken);
            try
            {
                MachineToolNotificationController notifications = Notifications ??
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "This machine has no selected notification provider.");
                await notifications.RefreshAsync(operation.Token).ConfigureAwait(false);
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// Materializes the validated optional binding before publication, retaining runtime-owned cleanup on failure.
        /// </summary>
        internal async ValueTask InitializeNotificationsAsync(
            MachineToolNotificationOptions options, MachineToolNotificationSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            Notifications = new MachineToolNotificationController(
                m_context, State, options, m_context.GetService<TimeProvider>() ?? TimeProvider.System);
            await Notifications.InitializeAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
    }
}
