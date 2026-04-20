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

#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Processes rejected certificate chains asynchronously via a
    /// bounded channel, writing them to the rejected certificate store.
    /// </summary>
    internal sealed class RejectedCertificateProcessor : IAsyncDisposable
    {
        private readonly Channel<CertificateCollection> _channel;
        private readonly Task _processingTask;
        private readonly ICertificateTrustListManager _trustListManager;
        private readonly int _maxRejectedCertificates;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="RejectedCertificateProcessor"/> class.
        /// </summary>
        public RejectedCertificateProcessor(
            ICertificateTrustListManager trustListManager,
            int maxRejectedCertificates,
            ITelemetryContext telemetry)
        {
            _trustListManager = trustListManager;
            _maxRejectedCertificates = maxRejectedCertificates;
            _logger = telemetry.CreateLogger<RejectedCertificateProcessor>();
            _channel = Channel.CreateBounded<CertificateCollection>(
                new BoundedChannelOptions(100)
                {
                    FullMode = BoundedChannelFullMode.DropOldest
                });
            _processingTask = ProcessAsync();
        }

        /// <summary>
        /// Enqueues a rejected certificate chain for background processing.
        /// </summary>
        public ValueTask EnqueueAsync(
            CertificateCollection chain,
            CancellationToken ct = default)
            => _channel.Writer.TryWrite(chain)
                ? default
                : _channel.Writer.WriteAsync(chain, ct);

        /// <summary>
        /// Completes the channel and waits for all queued items to be
        /// processed.
        /// </summary>
        public async Task DrainAsync(CancellationToken ct = default)
        {
            _channel.Writer.Complete();
            await _processingTask.WaitAsync(ct).ConfigureAwait(false);
        }

        private async Task ProcessAsync()
        {
            await foreach (var chain in _channel.Reader.ReadAllAsync()
                .ConfigureAwait(false))
            {
                try
                {
                    if (!_trustListManager.TrustLists
                        .Contains(TrustListIdentifier.Rejected))
                    {
                        continue;
                    }

                    using var store = _trustListManager
                        .OpenTrustedStore(TrustListIdentifier.Rejected);
                    await store.AddRejectedAsync(
                        chain, _maxRejectedCertificates)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(
                        ex,
                        "Could not write rejected certificate to store.");
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            _channel.Writer.TryComplete();
            await _processingTask.ConfigureAwait(false);
        }
    }
}
