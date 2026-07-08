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
 *
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
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server.Hosting
{
    internal sealed class OpcUaServerSessionManagerRegistration
    {
        public OpcUaServerSessionManagerRegistration(
            Func<IServiceProvider, IServerInternal, ApplicationConfiguration, ISessionManager> factory)
        {
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public ISessionManager CreateManager(
            IServiceProvider services,
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return m_factory(services, server, configuration);
        }

        private readonly Func<IServiceProvider, IServerInternal, ApplicationConfiguration, ISessionManager> m_factory;
    }

    internal sealed class OpcUaServerSubscriptionManagerRegistration
    {
        public OpcUaServerSubscriptionManagerRegistration(
            Func<IServiceProvider, IServerInternal, ApplicationConfiguration, ISubscriptionManager> factory)
        {
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public ISubscriptionManager CreateManager(
            IServiceProvider services,
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return m_factory(services, server, configuration);
        }

        private readonly Func<IServiceProvider, IServerInternal, ApplicationConfiguration, ISubscriptionManager> m_factory;
    }

    internal sealed class OpcUaServerHistorianRegistration
    {
        public OpcUaServerHistorianRegistration(IHistorianProvider provider)
        {
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public IHistorianProvider Provider { get; }
    }

    internal sealed class OpcUaServerAliasNameStoreRegistration
    {
        public OpcUaServerAliasNameStoreRegistration(IAliasNameStore store)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public IAliasNameStore Store { get; }
    }
}
