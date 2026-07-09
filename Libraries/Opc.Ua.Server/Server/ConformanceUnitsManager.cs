/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
    /// An object that manages the conformance units and server profiles the
    /// server advertises on <c>Server/ServerCapabilities/ConformanceUnits</c>
    /// and <c>Server/ServerCapabilities/ServerProfileArray</c> (OPC UA Part 7).
    /// </summary>
    /// <remarks>
    /// The published set is derived from the contributions of every registered
    /// node manager (or other feature) implementing
    /// <see cref="IConformanceContributor"/> rather than a fixed hard-coded list,
    /// so areas that are only enabled at runtime (for example Historical Access
    /// when history archiving is turned on) are advertised only when actually
    /// present. Registrations are aggregated and de-duplicated, then written to
    /// the address space through the <see cref="IDiagnosticsNodeManager"/>,
    /// mirroring how <see cref="ModellingRulesManager"/> delegates to it.
    /// </remarks>
    public class ConformanceUnitsManager : IDisposable
    {
        /// <summary>
        /// Initializes the manager.
        /// </summary>
        public ConformanceUnitsManager(IServerInternal server)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_conformanceUnits = new HashSet<QualifiedName>();
            m_serverProfiles = new HashSet<string>(StringComparer.Ordinal);
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
            // No unmanaged resources held.
        }

        /// <summary>
        /// Checks whether the conformance unit is registered as supported.
        /// </summary>
        /// <param name="conformanceUnit">
        /// The browse name of the conformance unit.
        /// </param>
        /// <returns>
        /// True if the conformance unit is currently advertised.
        /// </returns>
        public bool IsSupported(QualifiedName conformanceUnit)
        {
            if (conformanceUnit.IsNull)
            {
                return false;
            }

            lock (m_lock)
            {
                return m_conformanceUnits.Contains(conformanceUnit);
            }
        }

        /// <summary>
        /// Registers the conformance units and server profiles a contributor
        /// enables.
        /// </summary>
        /// <param name="contributor">
        /// The contributor whose supported units and profiles are added.
        /// </param>
        public void Register(IConformanceContributor contributor)
        {
            if (contributor == null)
            {
                throw new ArgumentNullException(nameof(contributor));
            }

            lock (m_lock)
            {
                foreach (QualifiedName unit in contributor.ConformanceUnits)
                {
                    if (!unit.IsNull)
                    {
                        m_conformanceUnits.Add(unit);
                    }
                }
                foreach (string profile in contributor.ServerProfiles)
                {
                    if (!string.IsNullOrEmpty(profile))
                    {
                        m_serverProfiles.Add(profile);
                    }
                }
            }
        }

        /// <summary>
        /// Publishes the aggregated conformance units and server profiles to the
        /// address space through the diagnostics node manager.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        public async ValueTask PublishAsync(CancellationToken cancellationToken = default)
        {
            ArrayOf<QualifiedName> units;
            ArrayOf<string> profiles;

            lock (m_lock)
            {
                var orderedUnits = new List<QualifiedName>(m_conformanceUnits);
                orderedUnits.Sort(
                    (left, right) => string.CompareOrdinal(left.Name, right.Name));
                units = orderedUnits.ToArrayOf();
                profiles = new List<string>(m_serverProfiles).ToArrayOf();
            }

            await m_server.DiagnosticsNodeManager
                .PublishConformanceUnitsAsync(units, profiles, cancellationToken)
                .ConfigureAwait(false);
        }

        private readonly Lock m_lock = new();
        private readonly IServerInternal m_server;
        private readonly HashSet<QualifiedName> m_conformanceUnits;
        private readonly HashSet<string> m_serverProfiles;
    }
}
