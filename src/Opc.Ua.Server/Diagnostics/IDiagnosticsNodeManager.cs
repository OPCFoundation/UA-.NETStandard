// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A node manager the diagnostic information exposed by the server.
    /// </summary>
    public interface IDiagnosticsNodeManager : IAsyncNodeManager, INodeIdFactory
    {
        /// <summary>
        /// True if diagnostics are currently enabled.
        /// </summary>
        bool DiagnosticsEnabled { get; }

        /// <summary>
        /// Adds an aggregate function to the server capabilities object.
        /// </summary>
        ValueTask AddAggregateFunctionAsync(
            NodeId aggregateId,
            string aggregateName,
            bool isHistorical,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a modelling rule to the server capabilities object.
        /// </summary>
        ValueTask AddModellingRuleAsync(NodeId modellingRuleId, string modellingRuleName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes the supported conformance units to
        /// <c>Server/ServerCapabilities/ConformanceUnits</c> and merges the
        /// enabled server profiles into <c>Server/ServerCapabilities/ServerProfileArray</c>
        /// (per OPC UA Part 7). Profiles present before the first call (e.g. from
        /// configuration) are preserved; the merged profiles of an earlier call
        /// are replaced, so a profile left out is withdrawn.
        /// </summary>
        ValueTask PublishConformanceUnitsAsync(
            ArrayOf<QualifiedName> conformanceUnits,
            ArrayOf<string> serverProfiles,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates the diagnostics node for the server.
        /// </summary>
        ValueTask CreateServerDiagnosticsAsync(
            ServerSystemContext systemContext,
            ServerDiagnosticsSummaryDataType diagnostics,
            NodeValueSimpleEventHandler updateCallback,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates the diagnostics node for a session.
        /// </summary>
        /// <remarks>
        /// A non-null <see cref="SessionDiagnosticsDataType.SessionId"/> in
        /// <paramref name="diagnostics"/> requests that id for the session (for example the
        /// id of a session restored from another replica of a redundant server set). An
        /// implementation may assign a different id, for example when the requested one is
        /// already in use; the returned id is the one assigned.
        /// </remarks>
        ValueTask<NodeId> CreateSessionDiagnosticsAsync(
            ServerSystemContext systemContext,
            SessionDiagnosticsDataType diagnostics,
            NodeValueSimpleEventHandler updateCallback,
            SessionSecurityDiagnosticsDataType securityDiagnostics,
            NodeValueSimpleEventHandler updateSecurityCallback,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates the diagnostics node for a subscription.
        /// </summary>
        ValueTask<NodeId> CreateSubscriptionDiagnosticsAsync(
            ServerSystemContext systemContext,
            SubscriptionDiagnosticsDataType diagnostics,
            NodeValueSimpleEventHandler updateCallback,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Delete the diagnostics node for a session.
        /// </summary>
        ValueTask DeleteSessionDiagnosticsAsync(ServerSystemContext systemContext, NodeId nodeId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Delete the diagnostics node for a subscription.
        /// </summary>
        ValueTask DeleteSubscriptionDiagnosticsAsync(ServerSystemContext systemContext, NodeId nodeId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds the specified and checks if it is of the expected type.
        /// </summary>
        /// <typeparam name="T">Type of node state</typeparam>
        /// <returns>Returns null if not found or not of the correct type.</returns>
        T FindPredefinedNode<T>(NodeId nodeId) where T : NodeState;

        /// <summary>
        /// Force out of band diagnostics update after a change of diagnostics variables.
        /// </summary>
        void ForceDiagnosticsScan();

        /// <summary>
        /// Gets the default history capabilities object.
        /// </summary>
        ValueTask<HistoryServerCapabilitiesState> GetDefaultHistoryCapabilitiesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the flag controlling whether diagnostics is enabled for the server.
        /// </summary>
        ValueTask SetDiagnosticsEnabledAsync(ServerSystemContext context, bool enabled, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the Server object EventNotifier based on history capabilities.
        /// </summary>
        /// <remarks>
        /// This method can be overridden to customize the Server EventNotifier based on
        /// history capabilities settings.
        /// </remarks>
        ValueTask UpdateServerEventNotifierAsync(CancellationToken cancellationToken = default);
    }
}
