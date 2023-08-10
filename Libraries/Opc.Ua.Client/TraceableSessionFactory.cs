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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Object that creates instances of an Opc.Ua.Client.Session object with Activity Source.
    /// </summary>
    public class TraceableSessionFactory : DefaultSessionFactory
    {
        /// <summary>
        /// The default instance of the factory.
        /// </summary>
        public new static readonly TraceableSessionFactory Instance = new TraceableSessionFactory();

        /// <summary>
        /// Force use of the default instance.
        /// </summary>
        protected TraceableSessionFactory()
        {
        }

        #region ISessionFactory Members
        /// <inheritdoc/>
        public override async Task<ISession> CreateAsync(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales,
            CancellationToken ct = default)
        {
            using (Activity activity = TraceableSession.ActivitySource.StartActivity(nameof(CreateAsync)))
            {
                ISession session = await Session.Create(configuration, endpoint, updateBeforeConnect, false,
                    sessionName, sessionTimeout, identity, preferredLocales, ct).ConfigureAwait(false);

                return new TraceableSession(session);
            }
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
            IList<string> preferredLocales,
            CancellationToken ct = default)
        {
            using (Activity activity = TraceableSession.ActivitySource.StartActivity(nameof(CreateAsync)))
            {
                ISession session = await Session.Create(configuration, (ITransportWaitingConnection)null, endpoint,
                    updateBeforeConnect, checkDomain, sessionName, sessionTimeout,
                    identity, preferredLocales, ct).ConfigureAwait(false);

                return new TraceableSession(session);
            }
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
            IList<string> preferredLocales,
            CancellationToken ct = default)
        {
            using (Activity activity = TraceableSession.ActivitySource.StartActivity(nameof(CreateAsync)))
            {
                ISession session = await Session.Create(configuration, connection, endpoint,
                    updateBeforeConnect, checkDomain, sessionName, sessionTimeout,
                    identity, preferredLocales, ct
                    ).ConfigureAwait(false);

                return new TraceableSession(session);
            }
        }

        /// <inheritdoc/>
        public override ISession Create(
           ApplicationConfiguration configuration,
           ITransportChannel channel,
           ConfiguredEndpoint endpoint,
           X509Certificate2 clientCertificate,
           EndpointDescriptionCollection availableEndpoints = null,
           StringCollection discoveryProfileUris = null)
        {
            using (Activity activity = TraceableSession.ActivitySource.StartActivity(nameof(CreateAsync)))
            {
                return new TraceableSession(base.Create(configuration, channel, endpoint, clientCertificate, availableEndpoints, discoveryProfileUris));
            }
        }

        /// <inheritdoc/>
        public override Task<ITransportChannel> CreateChannelAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            CancellationToken ct = default)
        {
            using (Activity activity = TraceableSession.ActivitySource.StartActivity(nameof(CreateAsync)))
            {
                return base.CreateChannelAsync(configuration, connection, endpoint, updateBeforeConnect, checkDomain, ct);
            }
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
            using (Activity activity = TraceableSession.ActivitySource.StartActivity(nameof(CreateAsync)))
            {
                ISession session = await base.CreateAsync(configuration,
                    reverseConnectManager, endpoint,
                    updateBeforeConnect,
                    checkDomain, sessionName,
                    sessionTimeout, userIdentity,
                    preferredLocales, ct).ConfigureAwait(false);

                return new TraceableSession(session);
            }
        }

        /// <inheritdoc/>
        public override Task<ISession> RecreateAsync(ISession sessionTemplate, CancellationToken ct = default)
        {
            Session session = ValidateISession(sessionTemplate);
            using (Activity activity = TraceableSession.ActivitySource.StartActivity(nameof(RecreateAsync)))
            {
                return Task.FromResult((ISession)new TraceableSession(Session.Recreate(session)));
            }
        }

        /// <inheritdoc/>
        public override Task<ISession> RecreateAsync(ISession sessionTemplate, ITransportWaitingConnection connection, CancellationToken ct = default)
        {
            Session session = ValidateISession(sessionTemplate);
            using (Activity activity = TraceableSession.ActivitySource.StartActivity(nameof(RecreateAsync)))
            {
                return Task.FromResult((ISession)new TraceableSession(Session.Recreate(session, connection)));
            }
        }

        /// <inheritdoc/>
        public override Task<ISession> RecreateAsync(ISession sessionTemplate, ITransportChannel channel, CancellationToken ct = default)
        {
            Session session = ValidateISession(sessionTemplate);
            using (Activity activity = TraceableSession.ActivitySource.StartActivity(nameof(RecreateAsync)))
            {
                return Task.FromResult((ISession)new TraceableSession(Session.Recreate(session, channel)));
            }
        }
        #endregion

        #region Private Methods
        private Session ValidateISession(ISession sessionTemplate)
        {
            if (!(sessionTemplate is Session session))
            {
                if (sessionTemplate is TraceableSession template)
                {
                    session = (Session)template.Session;
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(sessionTemplate), "The ISession provided is not of a supported type.");
                }
            }
            return session;
        }
        #endregion
    }
}
