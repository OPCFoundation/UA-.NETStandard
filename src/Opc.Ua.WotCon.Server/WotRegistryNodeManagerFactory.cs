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
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// <see cref="INodeManagerFactory"/> that produces the stable
    /// <see cref="WotRegistryNodeManager"/> configured with the shared registry
    /// service and materialization coordinator.
    /// </summary>
    public sealed class WotRegistryNodeManagerFactory : INodeManagerFactory
    {
        /// <summary>Creates a new factory.</summary>
        public WotRegistryNodeManagerFactory(
            WotRegistryServerOptions options,
            IWotRegistryService registry,
            WotMaterializationCoordinator coordinator)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => new string[]
        {
            Namespaces.WotCon,
            XRegistry.Namespaces.XRegistry
        };

        /// <inheritdoc/>
        public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration)
        {
#pragma warning disable CA2000 // Ownership transfers to the MasterNodeManager.
            return new WotRegistryNodeManager(
                server, configuration, m_options, m_registry, m_coordinator).SyncNodeManager;
#pragma warning restore CA2000
        }

        private readonly WotRegistryServerOptions m_options;
        private readonly IWotRegistryService m_registry;
        private readonly WotMaterializationCoordinator m_coordinator;
    }
}
