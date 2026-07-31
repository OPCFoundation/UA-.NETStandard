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
    /// Aggregates and publishes conformance units and server profiles supplied
    /// by the features installed in a server.
    /// </summary>
    public class ConformanceUnitsManager : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the manager.
        /// </summary>
        public ConformanceUnitsManager(IServerInternal server)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_conformanceUnits = [];
            m_serverProfiles = new HashSet<string>(StringComparer.Ordinal);
            m_serverProfilesOrdered = [];
        }

        /// <summary>
        /// Returns whether a conformance unit has been registered.
        /// </summary>
        public bool IsSupported(QualifiedName conformanceUnit)
        {
            if (QualifiedName.IsNull(conformanceUnit))
            {
                return false;
            }

            lock (m_lock)
            {
                return m_conformanceUnits.Contains(conformanceUnit);
            }
        }

        /// <summary>
        /// Registers the units and profiles supplied by a contributor.
        /// </summary>
        public void Register(IConformanceContributor contributor)
        {
            if (contributor == null)
            {
                throw new ArgumentNullException(nameof(contributor));
            }

            lock (m_lock)
            {
                IReadOnlyList<QualifiedName> conformanceUnits = contributor.ConformanceUnits;
                if (conformanceUnits != null)
                {
                    foreach (QualifiedName conformanceUnit in conformanceUnits)
                    {
                        if (!QualifiedName.IsNull(conformanceUnit))
                        {
                            m_conformanceUnits.Add(conformanceUnit);
                        }
                    }
                }

                IReadOnlyList<string> serverProfiles = contributor.ServerProfiles;
                if (serverProfiles != null)
                {
                    foreach (string serverProfile in serverProfiles)
                    {
                        if (!string.IsNullOrEmpty(serverProfile))
                        {
                            if (m_serverProfiles.Add(serverProfile))
                            {
                                m_serverProfilesOrdered.Add(serverProfile);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Publishes the registered values in the ServerCapabilities object.
        /// </summary>
        public ValueTask PublishAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            QualifiedName[] conformanceUnits;
            string[] serverProfiles;

            lock (m_lock)
            {
                var orderedUnits = new List<QualifiedName>(m_conformanceUnits);
                orderedUnits.Sort(
                    (left, right) => string.CompareOrdinal(left.Name, right.Name));
                conformanceUnits = [.. orderedUnits];
                serverProfiles = [.. m_serverProfilesOrdered];
            }

            lock (m_server.DiagnosticsLock)
            {
                ServerCapabilitiesState capabilities = m_server.DiagnosticsNodeManager
                    .FindPredefinedNode<ServerCapabilitiesState>(
                        ObjectIds.Server_ServerCapabilities);

                if (capabilities == null)
                {
                    return default;
                }

                if (capabilities.ConformanceUnits != null)
                {
                    capabilities.ConformanceUnits.Value = conformanceUnits;
                    capabilities.ConformanceUnits.ClearChangeMasks(
                        m_server.DefaultSystemContext,
                        false);
                }

                if (capabilities.ServerProfileArray != null && serverProfiles.Length > 0)
                {
                    var mergedProfiles = new List<string>();
                    var knownProfiles = new HashSet<string>(StringComparer.Ordinal);

                    string[] configuredProfiles = capabilities.ServerProfileArray.Value;
                    if (configuredProfiles != null)
                    {
                        foreach (string profile in configuredProfiles)
                        {
                            if (!string.IsNullOrEmpty(profile) && knownProfiles.Add(profile))
                            {
                                mergedProfiles.Add(profile);
                            }
                        }
                    }

                    foreach (string profile in serverProfiles)
                    {
                        if (knownProfiles.Add(profile))
                        {
                            mergedProfiles.Add(profile);
                        }
                    }

                    capabilities.ServerProfileArray.Value = [.. mergedProfiles];
                    capabilities.ServerProfileArray.ClearChangeMasks(
                        m_server.DefaultSystemContext,
                        false);
                }
            }

            return default;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases resources held by the manager.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            // The manager does not own unmanaged resources.
        }

        private readonly object m_lock = new object();
        private readonly IServerInternal m_server;
        private readonly HashSet<QualifiedName> m_conformanceUnits;
        private readonly HashSet<string> m_serverProfiles;
        private readonly List<string> m_serverProfilesOrdered;
    }
}
