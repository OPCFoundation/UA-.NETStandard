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
    /// <para>
    /// The published set is derived from the contributions of every registered
    /// node manager (or other feature) implementing
    /// <see cref="IConformanceContributor"/> rather than a fixed hard-coded list,
    /// so areas that are only enabled at runtime (for example Historical Access
    /// when history archiving is turned on) are advertised only when actually
    /// present. Registrations are aggregated and de-duplicated, then written to
    /// the address space through the <see cref="IDiagnosticsNodeManager"/>,
    /// mirroring how <see cref="ModellingRulesManager"/> delegates to it.
    /// </para>
    /// <para>
    /// The manager keeps the registered contributors and reads them again on
    /// every <see cref="PublishAsync"/>, because the capabilities describe the
    /// current configuration of the server (OPC 10000-5 §6.3.2): a unit or
    /// profile a contributor no longer reports disappears with the next
    /// publish. The profiles the server declares in its configuration stay.
    /// </para>
    /// </remarks>
    public class ConformanceUnitsManager : IDisposable
    {
        /// <summary>
        /// Initializes the manager.
        /// </summary>
        public ConformanceUnitsManager(IServerInternal server)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_conformanceUnits = [];
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
            if (disposing && !m_disposed)
            {
                m_disposed = true;
                m_publishLock.Dispose();
            }
        }

        /// <summary>
        /// Checks whether the conformance unit is registered as supported.
        /// </summary>
        /// <param name="conformanceUnit">
        /// The browse name of the conformance unit.
        /// </param>
        /// <returns>
        /// True if the conformance unit is supported as of the last
        /// registration or publish.
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
        /// Registers a contributor whose conformance units and server profiles
        /// the server advertises.
        /// </summary>
        /// <remarks>
        /// Registering a contributor again has no effect and does not read it
        /// again; every contributor is read again on each
        /// <see cref="PublishAsync"/>.
        /// </remarks>
        /// <param name="contributor">
        /// The contributor whose supported units and profiles are added.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="contributor"/> is <c>null</c>.</exception>
        public void Register(IConformanceContributor contributor)
        {
            if (contributor == null)
            {
                throw new ArgumentNullException(nameof(contributor));
            }

            lock (m_lock)
            {
                if (m_contributors.Contains(contributor))
                {
                    return;
                }
                m_contributors.Add(contributor);
                m_registrationVersion++;
            }
            Aggregate();
        }

        /// <summary>
        /// Removes a contributor; its units and profiles disappear with the
        /// next <see cref="PublishAsync"/> unless another contributor reports
        /// them.
        /// </summary>
        /// <param name="contributor">The contributor to remove.</param>
        /// <returns><see langword="true"/> when the contributor was registered.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="contributor"/> is <c>null</c>.</exception>
        public bool Unregister(IConformanceContributor contributor)
        {
            if (contributor == null)
            {
                throw new ArgumentNullException(nameof(contributor));
            }

            bool removed;
            lock (m_lock)
            {
                removed = m_contributors.Remove(contributor);
                if (removed)
                {
                    m_registrationVersion++;
                }
            }
            if (removed)
            {
                Aggregate();
            }
            return removed;
        }

        /// <summary>
        /// Reads every registered contributor again and publishes the
        /// aggregated conformance units and server profiles to the address
        /// space through the diagnostics node manager.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        public async ValueTask PublishAsync(CancellationToken cancellationToken = default)
        {
            // Publishing is serialized, so the address space ends with the
            // set read last.
            await m_publishLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                (ArrayOf<QualifiedName> units, ArrayOf<string> profiles) = Aggregate();
                await m_server.DiagnosticsNodeManager
                    .PublishConformanceUnitsAsync(units, profiles, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_publishLock.Release();
            }
        }

        /// <summary>
        /// Reads the contributors and replaces the aggregated sets.
        /// </summary>
        /// <remarks>
        /// The contributors are read outside the lock: computing what they
        /// support may take locks of their own. A registration that changes
        /// while they are read makes the read stale, so it is repeated with
        /// the current contributors rather than replacing the set that
        /// registration already committed.
        /// </remarks>
        private (ArrayOf<QualifiedName> Units, ArrayOf<string> Profiles) Aggregate()
        {
            while (true)
            {
                IConformanceContributor[] contributors;
                long registrationVersion;
                lock (m_lock)
                {
                    contributors = [.. m_contributors];
                    registrationVersion = m_registrationVersion;
                }

                var units = new HashSet<QualifiedName>();
                var profiles = new HashSet<string>(StringComparer.Ordinal);
                var orderedProfiles = new List<string>();
                foreach (IConformanceContributor contributor in contributors)
                {
                    foreach (QualifiedName unit in contributor.ConformanceUnits)
                    {
                        if (!unit.IsNull)
                        {
                            units.Add(unit);
                        }
                    }
                    foreach (string profile in contributor.ServerProfiles)
                    {
                        if (!string.IsNullOrEmpty(profile) && profiles.Add(profile))
                        {
                            orderedProfiles.Add(profile);
                        }
                    }
                }

                lock (m_lock)
                {
                    if (registrationVersion != m_registrationVersion)
                    {
                        continue;
                    }
                    m_conformanceUnits = units;
                }

                var orderedUnits = new List<QualifiedName>(units);
                orderedUnits.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
                return (orderedUnits.ToArrayOf(), orderedProfiles.ToArrayOf());
            }
        }

        private readonly Lock m_lock = new();
        private readonly SemaphoreSlim m_publishLock = new(1, 1);
        private readonly IServerInternal m_server;
        private readonly List<IConformanceContributor> m_contributors = [];
        private HashSet<QualifiedName> m_conformanceUnits;
        private long m_registrationVersion;
        private bool m_disposed;
    }
}
