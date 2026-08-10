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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Identity;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The interface that a server exposes to objects that it contains.
    /// </summary>
    /// <remarks>
    /// Components that only need to observe the server and act on it should take
    /// <see cref="IServerContext"/> instead. This interface additionally hands out the
    /// server's subsystems, which a component should ask for by constructor injection
    /// rather than reach for through an ambient handle.
    /// </remarks>
    public interface IServerInternal : IServerContext, IAuditEventServer, IDisposable
    {
        /// <summary>
        /// The endpoint addresses used by the server.
        /// </summary>
        /// <value>The endpoint addresses.</value>
        IEnumerable<Uri> EndpointAddresses { get; }

        /// <summary>
        /// The table of namespace uris known to the server.
        /// </summary>
        /// <value>The namespace URIs.</value>
        NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// The table of remote server uris known to the server.
        /// </summary>
        /// <value>The server URIs.</value>
        StringTable ServerUris { get; }

        /// <summary>
        /// The factory used to create encodeable objects that the server understands.
        /// </summary>
        /// <value>The factory.</value>
        IEncodeableFactory Factory { get; }

        /// <summary>
        /// The datatypes, object types and variable types known to the server.
        /// </summary>
        /// <value>The type tree.</value>
        /// <remarks>
        /// The type tree table is a global object that all components of a server have access to.
        /// Node managers must populate this table with all types that they define.
        /// This object is thread safe.
        /// </remarks>
        TypeTable TypeTree { get; }

        /// <summary>
        /// The factory which helps creating main
        /// node managers used by the server.
        /// </summary>
        IMainNodeManagerFactory MainNodeManagerFactory { get; }

        /// <summary>
        /// The master node manager for the server.
        /// </summary>
        /// <value>The node manager.</value>
        IMasterNodeManager NodeManager { get; }

        /// <summary>
        /// The internal node manager for the servers.
        /// </summary>
        /// <value>The core node manager.</value>
        ICoreNodeManager CoreNodeManager { get; }

        /// <summary>
        /// Returns the node manager that managers the server diagnostics.
        /// </summary>
        /// <value>The diagnostics node manager.</value>
        IDiagnosticsNodeManager DiagnosticsNodeManager { get; }

        /// <summary>
        /// Returns the node manager that managers the server configuration.
        /// </summary>
        /// <value>The configuration node manager.</value>
        IConfigurationNodeManager ConfigurationNodeManager { get; }

        /// <summary>
        /// The manager for events that all components use to queue events that occur.
        /// </summary>
        /// <value>The event manager.</value>
        EventManager EventManager { get; }

        /// <summary>
        /// A manager for localized resources that components can use to localize text.
        /// </summary>
        /// <value>The resource manager.</value>
        ResourceManager ResourceManager { get; }

        /// <summary>
        /// A manager for outstanding requests that allows components to receive notifications if the timeout or are cancelled.
        /// </summary>
        /// <value>The request manager.</value>
        RequestManager RequestManager { get; }

        /// <summary>
        /// A manager for aggregate calculators supported by the server.
        /// </summary>
        /// <value>The aggregate manager.</value>
        AggregateManager AggregateManager { get; }

        /// <summary>
        /// The manager for active sessions.
        /// </summary>
        /// <value>The session manager.</value>
        ISessionManager SessionManager { get; }

        /// <summary>
        /// The manager for role identity / application / endpoint mapping rules
        /// per OPC UA Part 18 §6.4. <c>null</c> only on stripped-down server hosts
        /// that don't expose Server.ServerCapabilities.RoleSet. Integrators override
        /// the default in-memory implementation through
        /// <see cref="StandardServer.CreateRoleManager"/> or by registering one in
        /// the service container.
        /// </summary>
        IRoleManager RoleManager { get; }

        /// <summary>
        /// The registry that validates user identity tokens before falling back
        /// to the legacy <c>SessionManager.ImpersonateUser</c> event. Integrators
        /// add authenticators to the default registry rather than replacing it;
        /// see <c>ServerIdentityRegistryExtensions.RegisterDefaultAuthenticators</c>.
        /// </summary>
        IServerIdentityRegistry IdentityRegistry { get; }

        /// <summary>
        /// The manager for the OPC UA Part 18 §5 user-management model.
        /// <c>null</c> when the server doesn't expose
        /// <c>ServerConfiguration.UserManagement</c>. Integrators supply a concrete
        /// instance through <see cref="StandardServer.CreateUserManagement"/> or by
        /// registering one in the service container.
        /// </summary>
        UserManagement.IUserManagement? UserManagement { get; }

        /// <summary>
        /// The manager for active subscriptions.
        /// </summary>
        ISubscriptionManager SubscriptionManager { get; }

        /// <summary>
        /// The factory for (durable) monitored item queues
        /// </summary>
        IMonitoredItemQueueFactory MonitoredItemQueueFactory { get; }

        /// <summary>
        /// The store to persist and retrieve subscriptions
        /// </summary>
        ISubscriptionStore SubscriptionStore { get; }

        /// <summary>
        /// The server's telemetry context
        /// </summary>
        ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Whether the server is currently running.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance is running; otherwise, <c>false</c>.
        /// </value>
        /// <remarks>
        /// This flag is set to false when the server shuts down. Threads running should check this flag whenever
        /// they return from a blocking operation. If it is false the thread should clean up and terminate.
        /// </remarks>
        bool IsRunning { get; }

        /// <summary>
        /// Called by any component to report a global event.
        /// </summary>
        /// <param name="e">The event.</param>
        void ReportEvent(IFilterTarget e);

        /// <summary>
        /// Called by any component to report a global event.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="e">The event.</param>
        void ReportEvent(ISystemContext context, IFilterTarget e);

        /// <summary>
        /// Asynchronously reports a global event, awaiting an asynchronous report sink so the caller
        /// is never blocked.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="e">The event.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask ReportEventAsync(
            ISystemContext context,
            IFilterTarget e,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Refreshes the conditions for the specified subscription.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="subscriptionId">The subscription identifier.</param>
        void ConditionRefresh(OperationContext context, uint subscriptionId);

        /// <summary>
        /// Refreshes the conditions for the specified subscription and monitored item.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="subscriptionId">The subscription identifier.</param>
        /// <param name="monitoredItemId">The monitored item identifier.</param>
        void ConditionRefresh2(OperationContext context, uint subscriptionId, uint monitoredItemId);

        /// <summary>
        /// Updates the server status safely.
        /// </summary>
        /// <param name="action">Action to perform on the server status object.</param>
        void UpdateServerStatus(Action<ServerStatusValue> action);
    }
}
