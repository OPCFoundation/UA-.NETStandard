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

namespace Opc.Ua.Server
{
    /// <summary>
    /// A node manager the diagnostic information exposed by the server.
    /// </summary>
    public interface IDiagnosticsNodeManager : INodeManager2, INodeIdFactory
    {
        /// <summary>
        /// True if diagnostics are currently enabled.
        /// </summary>
        bool DiagnosticsEnabled { get; }

        /// <summary>
        /// Adds an aggregate function to the server capabilities object.
        /// </summary>
        void AddAggregateFunction(NodeId aggregateId, string aggregateName, bool isHistorical);

        /// <summary>
        /// Adds a modelling rule to the server capabilities object.
        /// </summary>
        void AddModellingRule(NodeId modellingRuleId, string modellingRuleName);

        /// <summary>
        /// Creates the diagnostics node for the server.
        /// </summary>
        void CreateServerDiagnostics(
            ServerSystemContext systemContext,
            ServerDiagnosticsSummaryDataType diagnostics,
            NodeValueSimpleEventHandler updateCallback);

        /// <summary>
        /// Creates the diagnostics node for a session.
        /// </summary>
        NodeId CreateSessionDiagnostics(
            ServerSystemContext systemContext,
            SessionDiagnosticsDataType diagnostics,
            NodeValueSimpleEventHandler updateCallback,
            SessionSecurityDiagnosticsDataType securityDiagnostics,
            NodeValueSimpleEventHandler updateSecurityCallback);

        /// <summary>
        /// Creates the diagnostics node for a subscription.
        /// </summary>
        NodeId CreateSubscriptionDiagnostics(
            ServerSystemContext systemContext,
            SubscriptionDiagnosticsDataType diagnostics,
            NodeValueSimpleEventHandler updateCallback);

        /// <summary>
        /// Delete the diagnostics node for a session.
        /// </summary>
        void DeleteSessionDiagnostics(ServerSystemContext systemContext, NodeId nodeId);

        /// <summary>
        /// Delete the diagnostics node for a subscription.
        /// </summary>
        void DeleteSubscriptionDiagnostics(ServerSystemContext systemContext, NodeId nodeId);

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
        HistoryServerCapabilitiesState GetDefaultHistoryCapabilities();

        /// <summary>
        /// Sets the flag controlling whether diagnostics is enabled for the server.
        /// </summary>
        void SetDiagnosticsEnabled(ServerSystemContext context, bool enabled);

        /// <summary>
        /// Updates the Server object EventNotifier based on history capabilities.
        /// </summary>
        /// <remarks>
        /// This method can be overridden to customize the Server EventNotifier based on
        /// history capabilities settings.
        /// </remarks>
        void UpdateServerEventNotifier();
    }
}
