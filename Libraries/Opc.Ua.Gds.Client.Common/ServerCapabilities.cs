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

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// The set of known server capability identifiers, populated from the
    /// generated <see cref="WellKnownServerCapabilities"/> catalog.
    /// </summary>
    public class ServerCapabilities : IEnumerable<ServerCapability>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ServerCapabilities"/> class.
        /// </summary>
        public ServerCapabilities()
        {
            ImmutableArray<ServerCapability>.Builder builder
                = ImmutableArray.CreateBuilder<ServerCapability>();
            var lookup = new Dictionary<string, ServerCapability>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, string> entry in WellKnownServerCapabilities.All)
            {
                var capability = new ServerCapability { Id = entry.Key, Description = entry.Value };
                builder.Add(capability);
                lookup[entry.Key] = capability;
            }

            m_capabilities = builder.ToImmutable();
            m_lookup = lookup;
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

        private readonly ImmutableArray<ServerCapability> m_capabilities;
        private readonly Dictionary<string, ServerCapability> m_lookup;
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
    }
}
