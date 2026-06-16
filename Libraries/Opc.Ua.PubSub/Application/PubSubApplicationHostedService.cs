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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Generic-host adapter that drives an
    /// <see cref="IPubSubApplication"/> through its
    /// <see cref="IPubSubApplication.StartAsync"/> /
    /// <see cref="IPubSubApplication.StopAsync"/> lifecycle. Registered
    /// automatically by <c>AddPubSub</c>.
    /// </summary>
    /// <remarks>
    /// Wires the application into the
    /// <see href="https://learn.microsoft.com/dotnet/core/extensions/generic-host">
    /// .NET Generic Host</see> lifetime, mirroring
    /// <c>Opc.Ua.Server.Hosting.OpcUaServerHostedService</c> in
    /// Opc.Ua.Server.
    /// </remarks>
    public sealed class PubSubApplicationHostedService : IHostedService
    {
        private readonly IPubSubApplication m_application;
        private readonly ILogger<PubSubApplicationHostedService> m_logger;

        /// <summary>
        /// Initializes a new <see cref="PubSubApplicationHostedService"/>.
        /// </summary>
        /// <param name="application">The application to drive.</param>
        /// <param name="logger">Logger.</param>
        public PubSubApplicationHostedService(
            IPubSubApplication application,
            ILogger<PubSubApplicationHostedService> logger)
        {
            if (application is null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            if (logger is null)
            {
                throw new ArgumentNullException(nameof(logger));
            }
            m_application = application;
            m_logger = logger;
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            m_logger.LogInformation(
                "Starting PubSub application {Id}.",
                m_application.ApplicationId);
            await m_application.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            m_logger.LogInformation(
                "Stopping PubSub application {Id}.",
                m_application.ApplicationId);
            await m_application.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
