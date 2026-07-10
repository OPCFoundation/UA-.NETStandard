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

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Server startup task that materializes the live
    /// <c>Server.ServerRedundancy</c> node as the generated subtype for the
    /// configured <see cref="ServerRedundancyOptions"/> via the
    /// <see cref="ServerRedundancyController"/>.
    /// </summary>
    public sealed class ServerRedundancyStartupTask : IServerStartupTask
    {
        /// <summary>
        /// Creates the task.
        /// </summary>
        /// <param name="controller">The redundancy controller.</param>
        /// <param name="warnIfServiceLevelProviderMissing">
        /// Whether to warn when non-transparent redundancy has no registered service-level provider.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="controller"/> is <c>null</c>.</exception>
        public ServerRedundancyStartupTask(
            ServerRedundancyController controller,
            bool warnIfServiceLevelProviderMissing = false)
        {
            m_controller = controller ?? throw new ArgumentNullException(nameof(controller));
            m_warnIfServiceLevelProviderMissing = warnIfServiceLevelProviderMissing;
        }

        /// <inheritdoc/>
        public async ValueTask OnServerStartedAsync(
            IServerInternal server,
            CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            WarnIfServiceLevelProviderMissing(server);

            await m_controller.AttachAsync(server, cancellationToken).ConfigureAwait(false);
        }

        private void WarnIfServiceLevelProviderMissing(IServerInternal server)
        {
            if (!m_warnIfServiceLevelProviderMissing || !m_controller.Options.IsNonTransparentMode)
            {
                return;
            }

            ILogger logger = server.Telemetry.CreateLogger<ServerRedundancyStartupTask>();
            logger.LogWarning(
                "Non-transparent server redundancy mode {RedundancyMode} is configured without a registered " +
                "IServiceLevelProvider. Register AddServerServiceLevel(...) or an IServiceLevelProvider so " +
                "Server.ServiceLevel reflects failover health.",
                m_controller.Mode);
        }

        private readonly ServerRedundancyController m_controller;
        private readonly bool m_warnIfServiceLevelProviderMissing;
    }
}
