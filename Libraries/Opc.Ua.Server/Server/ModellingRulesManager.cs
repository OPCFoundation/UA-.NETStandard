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
using System.Threading;

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
            if (modellingRuleId.IsNullNodeId)
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
        public void RegisterModellingRule(
            NodeId modellingRuleId,
            string modellingRuleName)
        {
            lock (m_lock)
            {
                m_modellingRules[modellingRuleId] = modellingRuleName;
            }

            m_server?.DiagnosticsNodeManager.AddModellingRule(modellingRuleId, modellingRuleName);
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
