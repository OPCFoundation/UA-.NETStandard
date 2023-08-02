/* ========================================================================
 * Copyright (c) 2005-2023 The OPC Foundation, Inc. All rights reserved.
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

#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// 
    /// </summary>
    public class DefaultActivitySessionFactory : DefaultSessionFactory
    {
        #region Public Methods
        /// <inheritdoc/>
        public override async Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales)
        {
            ISession session = await Session.Create(configuration, endpoint, updateBeforeConnect, false,
                sessionName, sessionTimeout, identity, preferredLocales).ConfigureAwait(false);

            return new SessionActivitySource(session);            
        }

        /// <inheritdoc/>
        public override async Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales)
        {
            ISession session = await Session.Create(configuration, null, endpoint,
                updateBeforeConnect, checkDomain, sessionName, sessionTimeout,
                identity, preferredLocales).ConfigureAwait(false);

            return new SessionActivitySource(session);
        }

        /// <inheritdoc/>
        public override async Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales)
        {
            ISession session = await Session.Create(configuration, connection, endpoint,
                updateBeforeConnect, checkDomain, sessionName, sessionTimeout,
                identity, preferredLocales
                ).ConfigureAwait(false);

            return new SessionActivitySource(session);
        }

        /// <inheritdoc/>
        public override async Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ReverseConnectManager reverseConnectManager,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity userIdentity,
            IList<string> preferredLocales,
            CancellationToken ct = default
            )
        {
            ISession session = await base.CreateAsync(configuration,
                reverseConnectManager, endpoint,
                updateBeforeConnect,
                checkDomain, sessionName,
                sessionTimeout, userIdentity,
                preferredLocales, ct).ConfigureAwait(false);

            return new SessionActivitySource(session);
        }

        /// <inheritdoc/>
        public override Task<ISession> RecreateAsync(ISession sessionTemplate)
        {
            if (!(sessionTemplate is SessionActivitySource template))
            {
                throw new ArgumentOutOfRangeException(nameof(sessionTemplate), "The ISession provided is not of a supported type.");
            }

            return base.RecreateAsync(template.Session);
        }

        /// <inheritdoc/>
        public override Task<ISession> RecreateAsync(ISession sessionTemplate, ITransportWaitingConnection connection)
        {
            if (!(sessionTemplate is SessionActivitySource template))
            {
                throw new ArgumentOutOfRangeException(nameof(sessionTemplate), "The ISession provided is not of a supported type");
            }

            return base.RecreateAsync(template.Session, connection);
        }
        #endregion
    }
}
#endif
