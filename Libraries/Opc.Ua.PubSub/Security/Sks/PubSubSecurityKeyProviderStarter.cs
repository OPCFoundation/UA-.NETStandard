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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Generic-host adapter that starts the background key-refresh loop of
    /// every registered <see cref="PullSecurityKeyProvider"/> so subscribers
    /// begin pulling keys from their remote Security Key Service when the host
    /// starts. Registered by
    /// <c>AddPubSubSecurityKeyServiceClient</c>. Providers that do not pull
    /// (for example the push target) are left untouched.
    /// </summary>
    internal sealed class PubSubSecurityKeyProviderStarter : IHostedService
    {
        private readonly IEnumerable<IPubSubSecurityKeyProvider> m_providers;
        private readonly ILogger m_logger;

        /// <summary>
        /// Initializes a new <see cref="PubSubSecurityKeyProviderStarter"/>.
        /// </summary>
        /// <param name="providers">All registered key providers.</param>
        /// <param name="telemetry">Telemetry context.</param>
        public PubSubSecurityKeyProviderStarter(
            IEnumerable<IPubSubSecurityKeyProvider> providers,
            ITelemetryContext telemetry)
        {
            m_providers = providers ?? throw new ArgumentNullException(nameof(providers));
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_logger = telemetry.CreateLogger<PubSubSecurityKeyProviderStarter>();
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (IPubSubSecurityKeyProvider provider in m_providers)
            {
                if (provider is not PullSecurityKeyProvider pullProvider)
                {
                    continue;
                }
                try
                {
                    await pullProvider.StartAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(
                        ex,
                        "Initial SKS key pull failed for SecurityGroupId {GroupId}; " +
                        "the provider keeps serving cached keys and retries in the background.",
                        pullProvider.SecurityGroupId);
                }
            }
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
