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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;

namespace Opc.Ua.Stress.Tests.Channels.Helpers
{
    /// <summary>
    /// Observes real TCP transport lifetimes without changing their security or retry behavior.
    /// </summary>
    internal sealed class TrackingTcpChannelBindings : ITransportChannelBindings
    {
        internal ArrayOf<TrackingTcpTransportChannel> Channels => [.. m_channels];

        /// <inheritdoc/>
        public ITransportChannel? Create(string uriScheme, ITelemetryContext telemetry)
        {
            if (!string.Equals(uriScheme, Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
            {
                return null;
            }

            var channel = new TrackingTcpTransportChannel(
                Interlocked.Increment(ref m_nextId),
                telemetry,
                m_events);
            m_channels.Enqueue(channel);
            return channel;
        }

        internal string Describe()
        {
            return string.Join(
                Environment.NewLine,
                m_channels.Select(channel => string.Create(
                    CultureInfo.InvariantCulture,
                    $"Transport {channel.Id}: certificate={channel.CertificateThumbprint}, " +
                    $"open={channel.OpenCount}/{channel.OpenSucceededCount}, " +
                    $"reconnect={channel.ReconnectCount}, close={channel.CloseCount}, " +
                    $"dispose={channel.DisposeCount}/{channel.DisposeCompletedCount}"))
                    .Concat(m_events));
        }

        /// <summary>
        /// Reimplements lifecycle calls while retaining the TCP channel's inherited retry-hint support.
        /// </summary>
        internal sealed class TrackingTcpTransportChannel :
            TcpTransportChannel,
            ITransportChannel,
            ISecureChannel
        {
            internal TrackingTcpTransportChannel(
                int id,
                ITelemetryContext telemetry,
                ConcurrentQueue<string> events)
                : base(telemetry)
            {
                Id = id;
                m_events = events;
                Record("created");
            }

            internal int Id { get; }

            internal string CertificateThumbprint => m_certificateThumbprint;

            internal int OpenCount => Volatile.Read(ref m_openCount);

            internal int OpenSucceededCount => Volatile.Read(ref m_openSucceededCount);

            internal int ReconnectCount => Volatile.Read(ref m_reconnectCount);

            internal int CloseCount => Volatile.Read(ref m_closeCount);

            internal int DisposeCount => Volatile.Read(ref m_disposeCount);

            internal int DisposeCompletedCount => Volatile.Read(ref m_disposeCompletedCount);

            async ValueTask ITransportChannel.ReconnectAsync(
                ITransportWaitingConnection? connection,
                CancellationToken ct)
            {
                Interlocked.Increment(ref m_reconnectCount);
                Record("reconnect-start");
                await ReconnectAsync(connection, ct).ConfigureAwait(false);
                Record("reconnect-completed");
            }

            async ValueTask ITransportChannel.CloseAsync(CancellationToken ct)
            {
                Interlocked.Increment(ref m_closeCount);
                Record("close-start");
                await CloseAsync(ct).ConfigureAwait(false);
                Record("close-completed");
            }

            ValueTask ISecureChannel.OpenAsync(
                Uri url,
                TransportChannelSettings settings,
                CancellationToken ct)
            {
                return RecordOpenAsync(() => OpenAsync(url, settings, ct), settings);
            }

            ValueTask ISecureChannel.OpenAsync(
                ITransportWaitingConnection connection,
                TransportChannelSettings settings,
                CancellationToken ct)
            {
                return RecordOpenAsync(() => OpenAsync(connection, settings, ct), settings);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Interlocked.Increment(ref m_disposeCount);
                    Record("dispose-start");
                }

                base.Dispose(disposing);

                if (disposing)
                {
                    Interlocked.Increment(ref m_disposeCompletedCount);
                    Record("dispose-completed");
                }
            }

            private async ValueTask RecordOpenAsync(
                Func<ValueTask> open,
                TransportChannelSettings settings)
            {
                Interlocked.Increment(ref m_openCount);
                m_certificateThumbprint = settings.ClientCertificate?.Thumbprint ?? string.Empty;
                Record("open-start");
                bool opened = false;
                try
                {
                    await open().ConfigureAwait(false);
                    Interlocked.Increment(ref m_openSucceededCount);
                    opened = true;
                }
                finally
                {
                    Record(opened ? "open-completed" : "open-failed");
                }
            }

            private void Record(string operation)
            {
                m_events.Enqueue(FormattableString.Invariant(
                    $"{DateTimeOffset.UtcNow:O} transport={Id} {operation}"));
            }

            private readonly ConcurrentQueue<string> m_events;
            private string m_certificateThumbprint = string.Empty;
            private int m_openCount;
            private int m_openSucceededCount;
            private int m_reconnectCount;
            private int m_closeCount;
            private int m_disposeCount;
            private int m_disposeCompletedCount;
        }

        private readonly ConcurrentQueue<TrackingTcpTransportChannel> m_channels = new();
        private readonly ConcurrentQueue<string> m_events = new();
        private int m_nextId;
    }
}
