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
    /// Renewable lease for a brokered real-time channel.
    /// </summary>
    public sealed class RealTimeChannelLease : IAsyncDisposable
    {
        /// <summary>
        /// Creates a channel lease.
        /// </summary>
        public RealTimeChannelLease(IRobotIntentTransport transport, string channelId, TimeSpan requestedLease)
        {
            m_transport = transport ?? throw new ArgumentNullException(nameof(transport));
            ChannelId = channelId;
            m_requestedLease = requestedLease;
        }

        /// <summary>
        /// Gets the channel id.
        /// </summary>
        public string ChannelId { get; }

        /// <summary>
        /// Gets a value indicating whether the lease was granted.
        /// </summary>
        public bool Granted { get; private set; }

        /// <summary>
        /// Gets the endpoint URL.
        /// </summary>
        public string EndpointUrl { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the payload descriptor.
        /// </summary>
        public string PayloadDescriptor { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the lease expiry.
        /// </summary>
        public DateTimeUtc LeaseExpiry { get; private set; }

        /// <summary>
        /// Gets the refusal message.
        /// </summary>
        public LocalizedText Message { get; private set; } = LocalizedText.Null;

        /// <summary>
        /// Opens the channel.
        /// </summary>
        public async ValueTask OpenAsync(CancellationToken cancellationToken = default)
        {
            await RenewAsync(cancellationToken).ConfigureAwait(false);
            if (Granted)
            {
                m_transport.Logger.ChannelLeaseGranted(ChannelId, EndpointUrl, LeaseExpiry);
                m_renewTask = RenewLoopAsync(m_disposeCts.Token);
            }
            else
            {
                m_transport.Logger.ChannelLeaseLapsed(ChannelId, Message.Text ?? string.Empty);
            }
        }

        /// <summary>
        /// Renews the lease by calling OpenRealTimeChannel again from the holding Session.
        /// </summary>
        public async ValueTask RenewAsync(CancellationToken cancellationToken = default)
        {
            RealTimeChannelOpenResult result = await m_transport.OpenRealTimeChannelAsync(
                ChannelId,
                m_requestedLease.TotalMilliseconds,
                cancellationToken).ConfigureAwait(false);
            Granted = result.Granted;
            EndpointUrl = result.EndpointUrl;
            PayloadDescriptor = result.PayloadDescriptor;
            LeaseExpiry = result.LeaseExpiry;
            Message = result.Message;
            if (Granted)
            {
                m_transport.Logger.ChannelLeaseRenewed(ChannelId, LeaseExpiry);
            }
            else
            {
                m_transport.Logger.ChannelLeaseLapsed(ChannelId, Message.Text ?? string.Empty);
            }
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
                if (m_renewTask != null)
                {
                    try
                    {
                        await m_renewTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception exception)
                    {
                        m_transport.Logger.ChannelLeaseRenewalFailed(exception, ChannelId);
                    }
                }
                if (Granted)
                {
                    try
                    {
                        _ = await m_transport.CloseRealTimeChannelAsync(ChannelId, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        m_transport.Logger.ChannelLeaseRenewalFailed(exception, ChannelId);
                    }
                }
            }
            finally
            {
                m_disposeCts.Dispose();
            }
        }

        private async Task RenewLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    TimeSpan delay = ComputeRenewDelay();
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    await RenewAsync(cancellationToken).ConfigureAwait(false);
                    if (!Granted)
                    {
                        return;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    m_transport.Logger.ChannelLeaseRenewalFailed(exception, ChannelId);
                    await Task.Delay(s_renewFailureBackoff, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private TimeSpan ComputeRenewDelay()
        {
            var expiry = (DateTime)LeaseExpiry;
            TimeSpan remaining = expiry - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return s_minimumRenewDelay;
            }
            var half = TimeSpan.FromTicks(remaining.Ticks / 2);
            return half < s_minimumRenewDelay ? s_minimumRenewDelay : half;
        }

        private static readonly TimeSpan s_minimumRenewDelay = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan s_renewFailureBackoff = TimeSpan.FromMilliseconds(100);

        private readonly IRobotIntentTransport m_transport;
        private readonly TimeSpan m_requestedLease;
        private readonly CancellationTokenSource m_disposeCts = new();
        private Task? m_renewTask;
        private int m_disposed;
    }

    internal static partial class RealTimeChannelLeaseLog
    {
        [LoggerMessage(
            EventId = RobotIntentClientEventIds.ChannelLeaseGranted,
            Level = LogLevel.Information,
            Message = "Robot Intent real-time channel lease granted. ChannelId={ChannelId}, " +
                "EndpointUrl={EndpointUrl}, " +
                "LeaseExpiry={LeaseExpiry}.")]
        public static partial void ChannelLeaseGranted(
            this ILogger logger,
            string channelId,
            string endpointUrl,
            DateTimeUtc leaseExpiry);

        [LoggerMessage(
            EventId = RobotIntentClientEventIds.ChannelLeaseRenewed,
            Level = LogLevel.Debug,
            Message = "Robot Intent real-time channel lease renewed. ChannelId={ChannelId}, " +
                "LeaseExpiry={LeaseExpiry}.")]
        public static partial void ChannelLeaseRenewed(
            this ILogger logger,
            string channelId,
            DateTimeUtc leaseExpiry);

        [LoggerMessage(
            EventId = RobotIntentClientEventIds.ChannelLeaseLapsed,
            Level = LogLevel.Warning,
            Message = "Robot Intent real-time channel lease lapsed or was refused. ChannelId={ChannelId}, " +
                "Message={Message}.")]
        public static partial void ChannelLeaseLapsed(this ILogger logger, string channelId, string message);

        [LoggerMessage(
            EventId = RobotIntentClientEventIds.ChannelLeaseRenewalFailed,
            Level = LogLevel.Warning,
            Message = "Robot Intent real-time channel lease renewal failed. ChannelId={ChannelId}.")]
        public static partial void ChannelLeaseRenewalFailed(
            this ILogger logger, Exception exception, string channelId);
    }
}
