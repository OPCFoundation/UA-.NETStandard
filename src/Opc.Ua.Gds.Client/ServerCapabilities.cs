/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// The set of known server capability identifiers, populated from the
    /// generated <see cref="ServerCapability"/> catalog.
    /// </summary>
    public class ServerCapabilities : IEnumerable<ServerCapabilityInfo>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ServerCapabilities"/> class.
        /// </summary>
        public ServerCapabilities()
        {
            ImmutableArray<ServerCapabilityInfo>.Builder builder
                = ImmutableArray.CreateBuilder<ServerCapabilityInfo>();
            var lookup = new Dictionary<string, ServerCapabilityInfo>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, string> entry in ServerCapability.All)
            {
                var capability = new ServerCapabilityInfo { Id = entry.Key, Description = entry.Value };
                builder.Add(capability);
                lookup[entry.Key] = capability;
            }

            m_capabilities = builder.ToImmutable();
            m_lookup = lookup;
        }

        /// <inheritdoc/>
        public IEnumerator<ServerCapabilityInfo> GetEnumerator()
        {
            ImmutableArray<ServerCapabilityInfo> capabilities = m_capabilities;
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
        /// Finds the server capability with the specified identifier.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <returns>The server capability, if found. NULL if it does not exist.</returns>
        public ServerCapabilityInfo? Find(string? id)
        {
            if (id == null || m_lookup == null)
            {
                return null;
            }

            return m_lookup.TryGetValue(id, out ServerCapabilityInfo? capability) ? capability : null;
        }

        private readonly ImmutableArray<ServerCapabilityInfo> m_capabilities;
        private readonly Dictionary<string, ServerCapabilityInfo> m_lookup;
    }

    /// <summary>
    /// A server capability entry (identifier and human-readable description).
    /// </summary>
    public class ServerCapabilityInfo : IFormattable
    {
        /// <summary>
        /// Gets or sets the identifier.
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Gets or sets the description.
        /// </summary>
        public string? Description { get; set; }

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
        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            if (format != null)
            {
                throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
            }

            return string.Format(formatProvider, "[{0}] {1}", Id, Description);
        }
    }
}
