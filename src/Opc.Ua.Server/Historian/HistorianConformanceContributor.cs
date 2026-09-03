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

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Bridges the Historian feature area's actual, capability-verified
    /// Part 11 profile support into the server's OPC UA Part 7
    /// conformance advertising (<see cref="ConformanceUnitsManager"/>),
    /// by claiming only what <see cref="HistorianProfileCatalog"/> — the
    /// interface- and capability-gated check, not a bare static flag —
    /// confirms every registered <see cref="IHistorianProvider"/>
    /// actually supports.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ConformanceUnits"/> and <see cref="ServerProfiles"/> are
    /// a snapshot computed by the last <see cref="RefreshAsync"/> call
    /// (empty until the first call). <see cref="ConformanceUnitsManager.Register"/>
    /// reads a contributor's properties once, so callers must
    /// <see cref="RefreshAsync"/> before registering, and again — followed
    /// by re-registering — whenever a provider is added, removed, or its
    /// capabilities change.
    /// </para>
    /// <para>
    /// Only Server-side profiles are considered; this contributor
    /// evaluates each provider's provider-wide capability rollup
    /// (<see cref="IHistorianProvider.GetCapabilitiesAsync"/> with
    /// <see cref="NodeId.Null"/>) against every catalogued Server
    /// profile. Because that rollup does not (and cannot generically)
    /// carry any one notifier's <see cref="HistorianNodeCapabilities.EventTypes"/>
    /// or <see cref="HistorianNodeCapabilities.MandatoryEventFields"/>,
    /// Events-family profiles are never claimed through this contributor
    /// — claiming those requires per-notifier evidence that only a
    /// notifier-aware caller can supply directly to
    /// <see cref="HistorianProfileCatalog.IsSupportedByProvider"/>.
    /// </para>
    /// </remarks>
    public sealed class HistorianConformanceContributor : IConformanceContributor
    {
        /// <summary>
        /// Creates a contributor over the server's Historian provider
        /// registry.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="registry"/> is <c>null</c>.</exception>
        public HistorianConformanceContributor(IHistorianProviderRegistry registry)
        {
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ConformanceUnits
        {
            get
            {
                lock (m_lock)
                {
                    return m_conformanceUnits;
                }
            }
        }

        /// <inheritdoc/>
        public ArrayOf<string> ServerProfiles
        {
            get
            {
                lock (m_lock)
                {
                    return m_serverProfiles;
                }
            }
        }

        /// <summary>
        /// Re-evaluates every registered provider against the Server
        /// profile catalog and refreshes <see cref="ConformanceUnits"/>
        /// and <see cref="ServerProfiles"/>. Providers whose
        /// <see cref="IHistorianProvider.GetCapabilitiesAsync"/> throws
        /// <see cref="NotSupportedException"/> or
        /// <see cref="InvalidOperationException"/> for
        /// <see cref="NodeId.Null"/> are skipped, mirroring the
        /// server-wide <c>HistoryServerCapabilities</c> rollup.
        /// </summary>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            var units = new HashSet<string>(StringComparer.Ordinal);
            var profileUris = new HashSet<string>(StringComparer.Ordinal);

            ArrayOf<IHistorianProvider> providers = m_registry.Providers;
            for (int i = 0; i < providers.Count; i++)
            {
                IHistorianProvider provider = providers[i];
                HistorianNodeCapabilities capabilities;
                try
                {
                    capabilities = await provider
                        .GetCapabilitiesAsync(NodeId.Null, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (NotSupportedException)
                {
                    continue;
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                foreach (HistoricalAccessProfileDescriptor profile in
                    HistoricalAccessProfileCatalog.GetProfiles(HistoricalAccessProfileSide.Server))
                {
                    if (!HistorianProfileCatalog.IsSupportedByProvider(profile, provider, capabilities))
                    {
                        continue;
                    }
                    profileUris.Add(profile.ProfileUri);
                    foreach (string unit in profile.MandatoryConformanceUnits)
                    {
                        units.Add(unit);
                    }
                }
            }

            var conformanceUnits = new List<QualifiedName>();
            foreach (string unit in units)
            {
                conformanceUnits.Add(new QualifiedName(unit));
            }
            var newUnits = conformanceUnits.ToArrayOf();
            var newProfiles = new List<string>(profileUris).ToArrayOf();

            lock (m_lock)
            {
                m_conformanceUnits = newUnits;
                m_serverProfiles = newProfiles;
            }
        }

        private readonly Lock m_lock = new();
        private readonly IHistorianProviderRegistry m_registry;
        private ArrayOf<QualifiedName> m_conformanceUnits = [];
        private ArrayOf<string> m_serverProfiles = [];
    }
}
