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
using System.Collections.Generic;
using System.Reflection;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Interface of the diagnostic node manager.
    /// </summary>
    public interface IDiagnosticsNodeManager : INodeManager2, INodeIdFactory, IDisposable
    {
        /// <summary>
        /// Called when a client sets a subscription as durable.
        /// </summary>
        ServiceResult OnSetSubscriptionDurable(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint subscriptionId,
            uint lifetimeInHours,
            ref uint revisedLifetimeInHours);

        /// <summary>
        /// Called when a client gets the monitored items of a subscription.
        /// </summary>
        ServiceResult OnGetMonitoredItems(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments);

        /// <summary>
        /// Called when a client initiates resending of all data monitored items in a Subscription.
        /// </summary>
        ServiceResult OnResendData(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments);

        /// <summary>
        /// Called when a client locks the server.
        /// </summary>
        ServiceResult OnLockServer(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments);

        /// <summary>
        /// Called when a client locks the server.
        /// </summary>
        ServiceResult OnUnlockServer(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments);

        /// <summary>
        /// Loads a node set from a file or resource and adds them to the set of predefined nodes.
        /// </summary>
        void LoadPredefinedNodes(
            ISystemContext context,
            Assembly assembly,
            string resourcePath,
            IDictionary<NodeId, IList<IReference>> externalReferences);

        /// <summary>
        /// Force out of band diagnostics update after a change of diagnostics variables.
        /// </summary>
        void ForceDiagnosticsScan();

        /// <summary>
        /// True if diagnostics are currently enabled.
        /// </summary>
        bool DiagnosticsEnabled { get; }

        /// <summary>
        /// Acquires the lock on the node manager.
        /// </summary>
        object Lock { get; }

        /// <summary>
        /// Gets the server that the node manager belongs to.
        /// </summary>
        IServerInternal Server { get; }

        /// <summary>
        /// The default context to use.
        /// </summary>
        ServerSystemContext SystemContext { get; }

        /// <summary>
        /// Gets the default index for the node manager's namespace.
        /// </summary>
        ushort NamespaceIndex { get; }

        /// <summary>
        /// Gets the namespace indexes owned by the node manager.
        /// </summary>
        /// <value>The namespace indexes.</value>
        IReadOnlyList<ushort> NamespaceIndexes { get; }

        /// <summary>
        /// Gets or sets the maximum size of a monitored item queue.
        /// </summary>
        /// <value>The maximum size of a monitored item queue.</value>
        uint MaxQueueSize { get; set; }

        /// <summary>
        /// The root for the alias assigned to the node manager.
        /// </summary>
        string AliasRoot { get; set; }

        /// <summary>
        /// Sets the flag controlling whether diagnostics is enabled for the server.
        /// </summary>
        void SetDiagnosticsEnabled(ServerSystemContext context, bool enabled);

        /// <summary>
        /// Creates the diagnostics node for the server.
        /// </summary>
        void CreateServerDiagnostics(
            ServerSystemContext systemContext,
            ServerDiagnosticsSummaryDataType diagnostics,
            NodeValueSimpleEventHandler updateCallback);

        /// <summary>
        /// Creates the diagnostics node for a subscription.
        /// </summary>
        NodeId CreateSessionDiagnostics(
            ServerSystemContext systemContext,
            SessionDiagnosticsDataType diagnostics,
            NodeValueSimpleEventHandler updateCallback,
            SessionSecurityDiagnosticsDataType securityDiagnostics,
            NodeValueSimpleEventHandler updateSecurityCallback);

        /// <summary>
        /// Delete the diagnostics node for a session.
        /// </summary>
        void DeleteSessionDiagnostics(
            ServerSystemContext systemContext,
            NodeId nodeId);

        /// <summary>
        /// Creates the diagnostics node for a subscription.
        /// </summary>
        NodeId CreateSubscriptionDiagnostics(
            ServerSystemContext systemContext,
            SubscriptionDiagnosticsDataType diagnostics,
            NodeValueSimpleEventHandler updateCallback);

        /// <summary>
        /// Delete the diagnostics node for a subscription.
        /// </summary>
        void DeleteSubscriptionDiagnostics(
            ServerSystemContext systemContext,
            NodeId nodeId);

        /// <summary>
        /// Gets the default history capabilities object.
        /// </summary>
        HistoryServerCapabilitiesState GetDefaultHistoryCapabilities();

        /// <summary>
        /// Adds an aggregate function to the server capabilities object.
        /// </summary>
        void AddAggregateFunction(NodeId aggregateId, string aggregateName, bool isHistorical);

        /// <summary>
        /// Returns the state object for the specified node if it exists.
        /// </summary>
        NodeState Find(NodeId nodeId);

        /// <summary>
        /// Creates a new instance and assigns unique identifiers to all children.
        /// </summary>
        /// <param name="context">The operation context.</param>
        /// <param name="parentId">An optional parent identifier.</param>
        /// <param name="referenceTypeId">The reference type from the parent.</param>
        /// <param name="browseName">The browse name.</param>
        /// <param name="instance">The instance to create.</param>
        /// <returns>The new node id.</returns>
        NodeId CreateNode(
            ServerSystemContext context,
            NodeId parentId,
            NodeId referenceTypeId,
            QualifiedName browseName,
            BaseInstanceState instance);

        /// <summary>
        /// Deletes a node and all of its children.
        /// </summary>
        bool DeleteNode(
            ServerSystemContext context,
            NodeId nodeId);

        /// <summary>
        /// Searches the node id in all node managers
        /// </summary>
        /// <param name="nodeId"></param>
        /// <returns></returns>
        NodeState FindNodeInAddressSpace(NodeId nodeId);

        /// <summary>
        /// Finds the specified and checks if it is of the expected type.
        /// </summary>
        /// <returns>Returns null if not found or not of the correct type.</returns>
        NodeState FindPredefinedNode(NodeId nodeId, Type expectedType);

        /// <summary>
        /// Validates Role permissions for the specified NodeId
        /// </summary>
        /// <param name="operationContext"></param>
        /// <param name="nodeId"></param>
        /// <param name="requestedPermission"></param>
        /// <returns></returns>
        ServiceResult ValidateRolePermissions(OperationContext operationContext, NodeId nodeId, PermissionType requestedPermission);

        /// <summary>
        /// Validates if the specified event monitored item has enough permissions to receive the specified event
        /// </summary>
        /// <returns></returns>
        ServiceResult ValidateEventRolePermissions(IEventMonitoredItem monitoredItem, IFilterTarget filterTarget);
    }
}
