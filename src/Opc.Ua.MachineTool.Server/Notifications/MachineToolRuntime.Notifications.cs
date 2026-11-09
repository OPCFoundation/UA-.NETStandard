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
