/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Text;
using System.ServiceModel;
using System.Runtime.Serialization;
using System.Security.Principal;
using System.Security.Cryptography.X509Certificates;

#pragma warning disable 0618

namespace Opc.Ua.Server
{
    /// <summary>
    /// The interface that a server exposes to objects that it contains.
    /// </summary>
    public interface IServerInternal
    {
        /// <summary>
        /// The endpoint addresses used by the server.
        /// </summary>
        /// <value>The endpoint addresses.</value>
        IEnumerable<Uri> EndpointAddresses { get; }

        /// <summary>
        /// The context to use when serializing/deserializing extension objects.
        /// </summary>
        /// <value>The message context.</value>
        ServiceMessageContext MessageContext { get; }

        /// <summary>
        /// The default system context for the server.
        /// </summary>
        /// <value>The default system context.</value>
        ServerSystemContext DefaultSystemContext { get; }

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
        EncodeableFactory Factory { get; }

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
        
#if LEGACY_CORENODEMANAGER
        /// <summary>
        /// Returns the source for a types that has shared components defined.
        /// </summary>
        /// <value>The type sources.</value>
        /// <remarks>
        /// Some types define shared components which are used by all instances of the type. This
        /// table contains sources for those shared components. The namespace qualified browse name
        /// is assumed to be a unique identifier for a type.
        /// </remarks>
        TypeSourceTable TypeSources { get; }
#endif

        /// <summary>
        /// The master node manager for the server.
        /// </summary>
        /// <value>The node manager.</value>
        MasterNodeManager NodeManager { get; }

        /// <summary>
        /// The internal node manager for the servers.
        /// </summary>
        /// <value>The core node manager.</value>
        CoreNodeManager CoreNodeManager { get; }

        /// <summary>
        /// Returns the node manager that managers the server diagnostics.
        /// </summary>
        /// <value>The diagnostics node manager.</value>
        DiagnosticsNodeManager DiagnosticsNodeManager { get; }

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
        /// The manager for active subscriptions.
        /// </summary>
        ISubscriptionManager SubscriptionManager { get; }
        
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
        /// Returns the status object for the server.
        /// </summary>
        /// <value>The status.</value>
        [Obsolete("No longer thread safe. Must not use.")]
        ServerStatusValue Status { get; }

        /// <summary>
        /// Gets or sets the current state of the server.
        /// </summary>
        /// <value>The state of the current.</value>
        ServerState CurrentState { get; set; }

        /// <summary>
        /// Returns the Server object node
        /// </summary>
        /// <value>The Server object node.</value>
        ServerObjectState ServerObject { get; }

        /// <summary>
        /// Used to synchronize access to the server diagnostics.
        /// </summary>
        /// <value>The diagnostics lock.</value>
        object DiagnosticsLock { get; }

        /// <summary>
        /// Used to synchronize write access to the server diagnostics.
        /// </summary>
        /// <value>The diagnostics lock.</value>
        object DiagnosticsWriteLock { get; }

        /// <summary>
        /// Returns the diagnostics structure for the server.
        /// </summary>
        /// <value>The server diagnostics.</value>
        ServerDiagnosticsSummaryDataType ServerDiagnostics { get; }
        
        /// <summary>
        /// Whether the server is collecting diagnostics.
        /// </summary>
        /// <value><c>true</c> if diagnostics is enabled; otherwise, <c>false</c>.</value>
        bool DiagnosticsEnabled { get; }

        /// <summary>
        /// Closes the specified session.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="sessionId">The session identifier.</param>
        /// <param name="deleteSubscriptions">if set to <c>true</c> subscriptions are to be deleted.</param>
        void CloseSession(OperationContext context, NodeId sessionId, bool deleteSubscriptions);

        /// <summary>
        /// Deletes the specified subscription.
        /// </summary>
        /// <param name="subscriptionId">The subscription identifier.</param>
        void DeleteSubscription(uint subscriptionId);

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
        /// Refreshes the conditions for the specified subscription.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="subscriptionId">The subscription identifier.</param>
        void ConditionRefresh(OperationContext context, uint subscriptionId);
    }
}
