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
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.Server.Hosting
{
    internal sealed class DelegateServerStartupTask : IServerStartupTask
    {
        public DelegateServerStartupTask(
            IServiceProvider services,
            Func<IServiceProvider, IServerContext, CancellationToken, ValueTask> callback)
        {
            m_services = services;
            m_callback = callback;
        }

        public ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            return m_callback(m_services, server, cancellationToken);
        }

        private readonly IServiceProvider m_services;
        private readonly Func<IServiceProvider, IServerContext, CancellationToken, ValueTask> m_callback;
    }

    internal sealed class OpcUaServerResourceRegistration
    {
        public OpcUaServerResourceRegistration(Action<IServiceProvider, ResourceManager> configure)
        {
            m_configure = configure;
        }

        public void Apply(IServiceProvider services, ResourceManager resources)
        {
            m_configure(services, resources);
        }

        private readonly Action<IServiceProvider, ResourceManager> m_configure;
    }

    internal sealed class OpcUaServerAliasNameStartupTask : IServerPreStartupTask
    {
        public OpcUaServerAliasNameStartupTask(IServiceProvider services)
        {
            m_services = services;
        }

        public ValueTask OnServerStartingAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            if (server is not IAliasNameStoreRegistryProvider provider)
            {
                throw new InvalidOperationException("The server does not provide an alias-name store registry.");
            }

            foreach (IAliasNameStoreRegistry registry in m_services.GetServices<IAliasNameStoreRegistry>())
            {
                foreach (IAliasNameStore store in registry.Stores)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    provider.AliasNameStoreRegistry.Register(store);
                }
            }

            foreach (OpcUaServerAliasNameStoreRegistration registration in
                m_services.GetServices<OpcUaServerAliasNameStoreRegistration>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                provider.AliasNameStoreRegistry.Register(registration.Store);
            }
            return default;
        }

        private readonly IServiceProvider m_services;
    }
}
