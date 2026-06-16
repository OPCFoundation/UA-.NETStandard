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
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.Server;

namespace Opc.Ua.PubSub.Server
{
    /// <summary>
    /// <see cref="INodeManagerFactory"/> that produces
    /// <see cref="PubSubNodeManager"/> instances bound to a shared
    /// <see cref="IPubSubApplication"/> and optional
    /// <see cref="IPubSubKeyServiceServer"/>.
    /// </summary>
    /// <remarks>
    /// Mirrors the WoT Connectivity factory pattern. The factory
    /// itself does not own any namespaces beyond the PubSub server
    /// vendor URI; the standard PubSub nodes are loaded by the
    /// hosting server's diagnostics node manager.
    /// </remarks>
    public sealed class PubSubNodeManagerFactory : INodeManagerFactory
    {
        private readonly IPubSubApplication m_application;
        private readonly IPubSubKeyServiceServer? m_keyService;
        private readonly PubSubServerOptions m_options;
        private readonly ITelemetryContext m_telemetry;

        /// <summary>
        /// Creates a new factory with explicit dependencies.
        /// </summary>
        /// <param name="application">Runtime application.</param>
        /// <param name="keyService">Optional SKS server.</param>
        /// <param name="options">Server options.</param>
        /// <param name="telemetry">Telemetry context.</param>
        public PubSubNodeManagerFactory(
            IPubSubApplication application,
            IPubSubKeyServiceServer? keyService,
            PubSubServerOptions options,
            ITelemetryContext telemetry)
        {
            if (application is null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_application = application;
            m_keyService = keyService;
            m_options = options;
            m_telemetry = telemetry;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => new string[] { PubSubNodeManager.NamespaceUri };

        /// <inheritdoc/>
        public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration)
        {
            // The node manager is owned by the MasterNodeManager once registered;
            // returning its SyncNodeManager wrapper transfers ownership to the host.
#pragma warning disable CA2000 // Dispose objects before losing scope
            return new PubSubNodeManager(
                    server,
                    configuration,
                    m_application,
                    m_keyService,
                    m_options,
                    m_telemetry)
                .SyncNodeManager;
#pragma warning restore CA2000 // Dispose objects before losing scope
        }
    }
}
