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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Robotics.Client.Intent
{
    /// <summary>
    /// Command authority lease over RequestControl and ReleaseControl.
    /// </summary>
    public sealed class CommandAuthorityLease : IAsyncDisposable
    {
        /// <summary>
        /// Creates a command authority lease.
        /// </summary>
        public CommandAuthorityLease(IRobotIntentTransport transport, bool granted, NodeId currentOwner)
        {
            m_transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Granted = granted;
            m_releaseOnDispose = granted;
            CurrentOwner = currentOwner;
        }

        /// <summary>
        /// Raised when ControlOwner changes.
        /// </summary>
        public event CommandAuthorityChangedHandler? OwnerChanged;

        /// <summary>
        /// Gets a value indicating whether this Session was granted authority.
        /// </summary>
        public bool Granted { get; private set; }

        /// <summary>
        /// Gets the current ControlOwner.
        /// </summary>
        public NodeId CurrentOwner { get; private set; }

        /// <summary>
        /// Starts observing ControlOwner.
        /// </summary>
        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            NodeId ownerNode = await m_transport.ResolveChildAsync(
                m_transport.ControllerId,
                "ControlOwner",
                cancellationToken).ConfigureAwait(false);
            m_pumpTask = PumpAsync(ownerNode, m_disposeCts.Token);
            CurrentOwner = await m_transport.ReadControlOwnerAsync(cancellationToken).ConfigureAwait(false);
            if (m_releaseOnDispose)
            {
                m_transport.Logger.AuthorityGranted(CurrentOwner);
            }
            OwnerChanged?.Invoke(CurrentOwner);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await m_disposeCts.CancelAsync().ConfigureAwait(false);
                if (m_pumpTask != null)
                {
                    try
                    {
                        await m_pumpTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception exception)
                    {
                        m_transport.Logger.AuthorityReleaseFailed(exception, CurrentOwner);
                    }
                }
                if (m_releaseOnDispose)
                {
                    try
                    {
                        await m_transport.ReleaseControlAsync(CancellationToken.None).ConfigureAwait(false);
                        m_transport.Logger.AuthorityReleased(CurrentOwner);
                    }
                    catch (Exception exception)
                    {
                        m_transport.Logger.AuthorityReleaseFailed(exception, CurrentOwner);
                    }
                }
            }
            finally
            {
                m_disposeCts.Dispose();
            }
        }

        private async Task PumpAsync(NodeId ownerNode, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (RobotIntentDataChange change in m_transport
                    .SubscribeDataChangesAsync([ownerNode], cancellationToken).ConfigureAwait(false))
                {
                    if (change.Value.TryGetValue(out NodeId owner))
                    {
                        CurrentOwner = owner;
                        Granted = false;
                        m_transport.Logger.AuthorityLost(owner);
                        OwnerChanged?.Invoke(owner);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private readonly IRobotIntentTransport m_transport;
        private readonly bool m_releaseOnDispose;
        private readonly CancellationTokenSource m_disposeCts = new();
        private Task? m_pumpTask;
        private int m_disposed;
    }

    internal static partial class CommandAuthorityLeaseLog
    {
        [LoggerMessage(
            EventId = RobotIntentClientEventIds.AuthorityGranted,
            Level = LogLevel.Information,
            Message = "Robot Intent command authority granted. CurrentOwner={CurrentOwner}.")]
        public static partial void AuthorityGranted(this ILogger logger, NodeId currentOwner);

        [LoggerMessage(
            EventId = RobotIntentClientEventIds.AuthorityLost,
            Level = LogLevel.Warning,
            Message = "Robot Intent command authority lost. CurrentOwner={CurrentOwner}.")]
        public static partial void AuthorityLost(this ILogger logger, NodeId currentOwner);

        [LoggerMessage(
            EventId = RobotIntentClientEventIds.AuthorityReleased,
            Level = LogLevel.Information,
            Message = "Robot Intent command authority released. LastOwner={CurrentOwner}.")]
        public static partial void AuthorityReleased(this ILogger logger, NodeId currentOwner);

        [LoggerMessage(
            EventId = RobotIntentClientEventIds.AuthorityReleaseFailed,
            Level = LogLevel.Warning,
            Message = "Robot Intent command authority release failed during shutdown. LastOwner={CurrentOwner}.")]
        public static partial void AuthorityReleaseFailed(this ILogger logger, Exception exception, NodeId currentOwner);
    }
}
