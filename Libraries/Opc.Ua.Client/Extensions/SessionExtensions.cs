using System.Collections.Generic;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Extending Session object to have pre-existing methods of the old API but using the new API with ISession in the background
    /// </summary>
    public static class SessionExtensions
    {
        /// <summary>
        /// Creates a new communication session with a server by invoking the CreateSession service
        /// </summary>
        /// <param name="session">Not used</param>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint is used to update the endpoint description before connecting.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain in the certificate must match the endpoint used.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="identity">The user identity to associate with the session.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <returns>The new session object.</returns>
        public static async Task<Session> Create(this Session session,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales)
        {
            var factory = new DefaultSessionFactory();
            return (Session)await factory.Create(configuration, endpoint, updateBeforeConnect, checkDomain, sessionName, sessionTimeout, identity, preferredLocales);
        }

        /// <summary>
        /// Creates a new communication session with a server using a reverse connection.
        /// </summary>
        /// <param name="session">Not used</param>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="connection">The client endpoint for the reverse connect.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint is used to update the endpoint description before connecting.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain in the certificate must match the endpoint used.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="identity">The user identity to associate with the session.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <returns>The new session object.</returns>
        public static async Task<Session> Create(this Session session,
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
            var factory = new DefaultSessionFactory();
            return (Session)await factory.Create(configuration, connection, endpoint, updateBeforeConnect, checkDomain, sessionName, sessionTimeout, identity, preferredLocales);
        }

        /// <summary>
        /// Gets the concrete type of ISession if it is Session, else null
        /// </summary>
        /// <param name="reconnectHandler">The object containing the ISession object</param>
        /// <returns>Session</returns>
        public static Session Session(this SessionReconnectHandler reconnectHandler)
        {
            if (reconnectHandler.Session is Session session)
                return session;

            return null;
        }

        /// <summary>
        /// Gets the concrete type of ISession if it is Session, else null
        /// </summary>
        /// <param name="browser">The object containing the ISession object</param>
        /// <returns></returns>
        public static Session Session(this Browser browser)
        {
            if (browser.Session is Session session)
                return session;

            return null;
        }

        /// <summary>
        /// Gets the concrete type of ISession if it is Session, else null
        /// </summary>
        /// <param name="subscription">The object containing the ISession object</param>
        /// <returns></returns>
        public static Session Session(this Subscription subscription)
        {
            if (subscription.Session is Session session)
                return session;

            return null;
        }
    }
}
