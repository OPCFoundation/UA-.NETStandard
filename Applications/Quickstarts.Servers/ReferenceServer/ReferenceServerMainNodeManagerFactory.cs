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

using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// Main node manager factory variant that returns the reference-server-specific
    /// <see cref="ReferenceServerConfigurationNodeManager"/> in place of the default
    /// <see cref="ConfigurationNodeManager"/>. This keeps CTT-only address-space
    /// tweaks out of the SDK.
    /// </summary>
    internal sealed class ReferenceServerMainNodeManagerFactory : IMainNodeManagerFactory
    {
        public ReferenceServerMainNodeManagerFactory(
            ApplicationConfiguration applicationConfiguration,
            IServerInternal server)
        {
            m_applicationConfiguration = applicationConfiguration;
            m_server = server;
        }

        /// <inheritdoc/>
        public IConfigurationNodeManager CreateConfigurationNodeManager()
        {
            return new ReferenceServerConfigurationNodeManager(
                m_server, m_applicationConfiguration);
        }

        /// <inheritdoc/>
        public ICoreNodeManager CreateCoreNodeManager(ushort dynamicNamespaceIndex)
        {
            return new CoreNodeManager(
                m_server, m_applicationConfiguration, dynamicNamespaceIndex);
        }

        private readonly ApplicationConfiguration m_applicationConfiguration;
        private readonly IServerInternal m_server;
    }
}
