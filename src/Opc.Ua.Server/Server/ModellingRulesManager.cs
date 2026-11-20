/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// An object that manages modelling rules supported by the server.
    /// </summary>
    public class ModellingRulesManager : IDisposable
    {
        /// <summary>
        /// Initializes the manager.
        /// </summary>
        public ModellingRulesManager(IServerInternal server)
        {
            m_server = server;
            m_modellingRules = [];
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // TBD
            }
        }

        /// <summary>
        /// Checks if the modelling rule is supported by the server.
        /// </summary>
        /// <param name="modellingRuleId">The id of the modelling rule.</param>
        /// <returns>True if the modelling rule is supported.</returns>
        public bool IsSupported(NodeId modellingRuleId)
        {
            if (modellingRuleId.IsNull)
            {
                return false;
            }

            lock (m_lock)
            {
                return m_modellingRules.ContainsKey(modellingRuleId);
            }
        }

        /// <summary>
        /// Registers a modelling rule.
        /// </summary>
        /// <param name="modellingRuleId">The id of the modelling rule.</param>
        /// <param name="modellingRuleName">The name of the modelling rule.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        public async ValueTask RegisterModellingRuleAsync(
            NodeId modellingRuleId,
            string modellingRuleName,
            CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                m_modellingRules[modellingRuleId] = modellingRuleName;
            }

            await m_server.DiagnosticsNodeManager.AddModellingRuleAsync(modellingRuleId, modellingRuleName, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Unregisters a modelling rule.
        /// </summary>
        /// <param name="modellingRuleId">The id of the modelling rule.</param>
        public void UnregisterModellingRule(NodeId modellingRuleId)
        {
            lock (m_lock)
            {
                m_modellingRules.Remove(modellingRuleId);
            }
        }

        private readonly Lock m_lock = new();
        private readonly IServerInternal m_server;
        private readonly NodeIdDictionary<string> m_modellingRules;
    }
}
