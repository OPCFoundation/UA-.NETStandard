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
using System.Collections.Immutable;
using System.IO;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// The set of known capability identifiers, loaded from the embedded
    /// ServerCapabilities.csv resource by default.
    /// </summary>
    public class ServerCapabilities : IEnumerable<ServerCapability>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ServerCapabilities"/> class.
        /// </summary>
        public ServerCapabilities()
        {
            Load();
        }

        /// <inheritdoc/>
        public IEnumerator<ServerCapability> GetEnumerator()
        {
            ImmutableArray<ServerCapability> capabilities = m_capabilities;
            for (int i = 0; i < capabilities.Length; i++)
            {
                yield return capabilities[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Loads the default set of server capability identifiers from the
        /// embedded ServerCapabilities.csv resource.
        /// </summary>
        public void Load()
        {
            Load(null);
        }

        /// <summary>
        /// Loads the set of server capability identifiers from the stream.
        /// </summary>
        /// <param name="istrm">The input stream. If null the embedded resource is used.</param>
        public void Load(Stream istrm)
        {
            ImmutableArray<ServerCapability>.Builder builder
                = ImmutableArray.CreateBuilder<ServerCapability>();
            var lookup = new Dictionary<string, ServerCapability>(StringComparer.Ordinal);

            Stream resourceStream = null;
            if (istrm == null)
            {
                foreach (string resourceName in typeof(Ua.ObjectIds).Assembly
                    .GetManifestResourceNames())
                {
                    if (resourceName.EndsWith(
                        "ServerCapabilities.csv",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        resourceStream = typeof(Ua.ObjectIds).Assembly
                            .GetManifestResourceStream(resourceName);
                        istrm = resourceStream;
                        break;
                    }
                }
            }

            try
            {
                if (istrm != null)
                {
                    using var reader = new StreamReader(istrm);
                    string line = reader.ReadLine();

                    while (line != null)
                    {
                        int index = line.IndexOf(',', StringComparison.Ordinal);

                        if (index >= 0)
                        {
                            string id = line[..index].Trim();
                            string description = line[(index + 1)..].Trim();
                            var capability = new ServerCapability { Id = id, Description = description };
                            builder.Add(capability);
                            lookup[id] = capability;
                        }

                        line = reader.ReadLine();
                    }
                }
            }
            finally
            {
                resourceStream?.Dispose();
            }

            m_capabilities = builder.ToImmutable();
            m_lookup = lookup;
        }

        /// <summary>
        /// Finds the server capability with the specified identifier.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <returns>The server capability, if found. NULL if it does not exist.</returns>
        public ServerCapability Find(string id)
        {
            if (id == null || m_lookup == null)
            {
                return null;
            }

            return m_lookup.TryGetValue(id, out ServerCapability capability) ? capability : null;
        }

        private ImmutableArray<ServerCapability> m_capabilities = ImmutableArray<ServerCapability>.Empty;
        private Dictionary<string, ServerCapability> m_lookup;
    }

    /// <summary>
    /// A server capability.
    /// </summary>
    public class ServerCapability : IFormattable
    {
        /// <summary>
        /// Gets or sets the identifier.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the description.
        /// </summary>
        public string Description { get; set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <param name="format">The format. Must be null.</param>
        /// <param name="formatProvider">The format provider.</param>
        /// <exception cref="FormatException"></exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format != null)
            {
                throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
            }

            return string.Format(formatProvider, "[{0}] {1}", Id, Description);
        }

        /// <summary>No information is available.</summary>
        [Obsolete("Use WellKnownServerCapabilities.NoInformation instead.")]
        public const string NoInformation = WellKnownServerCapabilities.NoInformation;

        /// <summary>The server supports live data.</summary>
        [Obsolete("Use WellKnownServerCapabilities.LiveData instead.")]
        public const string LiveData = WellKnownServerCapabilities.LiveData;

        /// <summary>The server supports alarms and conditions.</summary>
        [Obsolete("Use WellKnownServerCapabilities.AlarmsAndConditions instead.")]
        public const string AlarmsAndConditions = WellKnownServerCapabilities.AlarmsAndConditions;

        /// <summary>The server supports historical data.</summary>
        [Obsolete("Use WellKnownServerCapabilities.HistoricalData instead.")]
        public const string HistoricalData = WellKnownServerCapabilities.HistoricalData;

        /// <summary>The server supports historical events.</summary>
        [Obsolete("Use WellKnownServerCapabilities.HistoricalEvents instead.")]
        public const string HistoricalEvents = WellKnownServerCapabilities.HistoricalEvents;

        /// <summary>The server is a global discovery server.</summary>
        [Obsolete("Use WellKnownServerCapabilities.GlobalDiscoveryServer instead.")]
        public const string GlobalDiscoveryServer = WellKnownServerCapabilities.GlobalDiscoveryServer;

        /// <summary>The server is a local discovery server.</summary>
        [Obsolete("Use WellKnownServerCapabilities.LocalDiscoveryServer instead.")]
        public const string LocalDiscoveryServer = WellKnownServerCapabilities.LocalDiscoveryServer;

        /// <summary>The server supports the device integration (DI) information model.</summary>
        [Obsolete("Use WellKnownServerCapabilities.DI instead.")]
        public const string DI = WellKnownServerCapabilities.DI;
    }
}
