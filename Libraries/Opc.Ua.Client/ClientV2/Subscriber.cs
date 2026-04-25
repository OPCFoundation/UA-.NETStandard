#if OPCUA_CLIENT_V2
/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Client
{
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;

    /// <summary>
    /// Subscription api that supports not just virtual subscriptions (decoupled
    /// subscribers from subscription on the server overcoming limitations of the
    /// server) but also data sampling.
    /// </summary>
    internal sealed class Subscriber : ISubscribe, IDisposable
    {
        /// <summary>
        /// Create subscriber
        /// </summary>
        /// <param name="telemetry"></param>
        public Subscriber(ITelemetryContext telemetry)
        {
            _observability = telemetry;
            _logger = telemetry.LoggerFactory.CreateLogger<Subscriber>();
        }

        /// <inheritdoc/>
        public async ValueTask<IReader<Notification>> SubscribeAsync(
            IOptionsMonitor<SubscriptionClientOptions> subscription, CancellationToken ct)
        {
            await _subscriptionLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var registration = new Reader(this, subscription);
                _registrations.Add(registration);
                return registration;
            }
            finally
            {
                _subscriptionLock.Release();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _disposed = true;
        }

        /// <summary>
        /// Subscription registration which tracks the registered options
        /// and provides the channel to communicate back with the api client.
        /// </summary>
        internal sealed record Reader : IReader<Notification>, INotificationQueue
        {
            /// <summary>
            /// Monitored items on the subscriber
            /// </summary>
            public IOptionsMonitor<SubscriptionClientOptions> Options { get; }

            /// <summary>
            /// Mark the registration as dirty
            /// </summary>
            internal bool Dirty { get; set; }

            /// <summary>
            /// Create subscription
            /// </summary>
            /// <param name="outer"></param>
            /// <param name="options"></param>
            public Reader(Subscriber outer,
                IOptionsMonitor<SubscriptionClientOptions> options)
            {
                Options = options;
                options.OnChange((_, __) => Dirty = true);
                _outer = outer;
                _channel = Channel.CreateUnbounded<Notification>();
            }

            /// <inheritdoc/>
            public IAsyncEnumerable<Notification> ReadAsync(CancellationToken ct)
            {
                return _channel.Reader.ReadAllAsync(ct);
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                if (_outer._disposed)
                {
                    //
                    // Possibly the client has shut down before the owners of
                    // the registration have disposed it. This is not an error.
                    // It might however be better to order the clients to get
                    // disposed before clients.
                    //
                    return;
                }

                // Remove registration
                await _outer._subscriptionLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    _outer._registrations.Remove(this);
                }
                finally
                {
                    _outer._subscriptionLock.Release();
                }
            }

            /// <summary>
            /// Queue notification
            /// </summary>
            /// <param name="notification"></param>
            /// <param name="ct"></param>
            /// <returns></returns>
            public ValueTask QueueAsync(Notification notification, CancellationToken ct)
            {
                return _channel.Writer.WriteAsync(notification, ct);
            }

            private readonly Subscriber _outer;
            private readonly Channel<Notification> _channel;
        }

        private readonly SemaphoreSlim _subscriptionLock = new(1, 1);
        private readonly HashSet<Reader> _registrations = [];
        private readonly ITelemetryContext _observability;
        private readonly ILogger _logger;
        private bool _disposed;
    }
}
#endif
