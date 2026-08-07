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

namespace Opc.Ua.Server
{
    /// <summary>
    /// The ambient view of a running server, handed to components that need to observe it
    /// and act on it without owning any part of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is deliberately small. Every member either answers a question about the server
    /// as a whole or performs a server-wide operation; none of them hands out a subsystem.
    /// A component that needs a particular subsystem asks for that subsystem in its
    /// constructor, which states the dependency instead of hiding it behind an ambient
    /// handle.
    /// </para>
    /// <para>
    /// The ambient tables - namespace URIs, server URIs, the type table, the encodeable
    /// factory and telemetry - are reached through <see cref="DefaultSystemContext"/>,
    /// which already carries all of them. They are not repeated here.
    /// </para>
    /// </remarks>
    public interface IServerContext
    {
        /// <summary>
        /// The current state of the server.
        /// </summary>
        ServerState CurrentState { get; }

        /// <summary>
        /// The Server object node.
        /// </summary>
        ServerObjectState ServerObject { get; }

        /// <summary>
        /// The context to use when serializing and deserializing extension objects.
        /// </summary>
        /// <remarks>
        /// This is the server's own context and carries its configured decoding limits.
        /// It is deliberately not derived from <see cref="DefaultSystemContext"/>:
        /// <c>ISystemContext.AsMessageContext()</c> produces a context with default
        /// limits, which would silently widen what a component accepts.
        /// </remarks>
        IServiceMessageContext MessageContext { get; }

        /// <summary>
        /// The context to use for operations that are not attributable to a session.
        /// Also carries the namespace URIs, server URIs, type table, encodeable factory
        /// and telemetry context.
        /// </summary>
        ServerSystemContext DefaultSystemContext { get; }

        /// <summary>
        /// Creates a context that attributes operations to the given session, carrying its
        /// identity and preferred locales.
        /// </summary>
        /// <param name="session">The session to attribute operations to.</param>
        ServerSystemContext CreateSystemContext(ISession session);

        /// <summary>
        /// Finds a node in the server's predefined address space.
        /// </summary>
        /// <typeparam name="T">The expected node type.</typeparam>
        /// <param name="nodeId">The node to find.</param>
        /// <returns>
        /// The node, or <c>null</c> when it is absent or is not of type
        /// <typeparamref name="T"/>.
        /// </returns>
        T? FindPredefinedNode<T>(NodeId nodeId) where T : NodeState;

        /// <summary>
        /// Finds every registered node manager that provides the requested capability.
        /// </summary>
        /// <remarks>
        /// Node managers advertise optional capabilities by implementing marker interfaces.
        /// This asks for the capability rather than for the registry, so callers do not
        /// have to know how node managers are stored or filter the list themselves.
        /// </remarks>
        /// <typeparam name="T">The capability to look for.</typeparam>
        IEnumerable<T> FindNodeManagers<T>() where T : class;

        /// <summary>
        /// Reports a global event.
        /// </summary>
        /// <param name="e">The event.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask ReportEventAsync(IFilterTarget e, CancellationToken cancellationToken = default);

        /// <summary>
        /// Closes the specified session.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="sessionId">The session identifier.</param>
        /// <param name="deleteSubscriptions">if set to <c>true</c> subscriptions are to be deleted.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask CloseSessionAsync(
            OperationContext context,
            NodeId sessionId,
            bool deleteSubscriptions,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the specified subscription.
        /// </summary>
        /// <param name="subscriptionId">The subscription identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask DeleteSubscriptionAsync(uint subscriptionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Applies an update to the server diagnostics while holding the server's
        /// diagnostics lock.
        /// </summary>
        /// <remarks>
        /// The server owns its lock and never exposes it, so callers cannot participate in
        /// the server's locking order. The diagnostic nodes are marked dirty inside the
        /// critical section.
        /// </remarks>
        /// <param name="update">The mutation to apply to the diagnostics.</param>
        void UpdateServerDiagnostics(Action<ServerDiagnosticsSummaryDataType> update);
    }
}
