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
