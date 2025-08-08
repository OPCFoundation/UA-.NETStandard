/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// The set known capability identifiers.
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

        public IEnumerator<ServerCapability> GetEnumerator()
        {
            if (m_capabilities == null)
            {
                return new List<ServerCapability>().GetEnumerator();
            }

            return m_capabilities.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Loads the default set of server capability identifiers.
        /// </summary>
        public void Load()
        {
            Load(null);
        }

        /// <summary>
        /// Loads the set of server capability identifiers from the stream.
        /// </summary>
        /// <param name="istrm">The input stream.</param>
        public void Load(Stream istrm)
        {
            var capabilities = new List<ServerCapability>();

            if (istrm == null)
            {
                foreach (string resourceName in typeof(Ua.ObjectIds).Assembly.GetManifestResourceNames())
                {
                    if (resourceName.EndsWith("ServerCapabilities.csv", StringComparison.OrdinalIgnoreCase))
                    {
                        istrm = typeof(Ua.ObjectIds).Assembly.GetManifestResourceStream(resourceName);
                        break;
                    }
                }
            }

            if (istrm != null)
            {
                using (var reader = new StreamReader(istrm))
                {
                    string line = reader.ReadLine();

                    while (line != null)
                    {
                        int index = line.IndexOf(',', StringComparison.Ordinal);

                        if (index >= 0)
                        {
                            string id = line[..index].Trim();
                            string description = line[(index + 1)..].Trim();
                            capabilities.Add(new ServerCapability() { Id = id, Description = description });
                        }

                        line = reader.ReadLine();
                    }
                }
            }

            m_capabilities = capabilities;
        }

        /// <summary>
        /// Finds the server capability with the specified identifier.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <returns>The server capability, if found. NULL if it does not exist.</returns>
        public ServerCapability Find(string id)
        {
            if (id != null && m_capabilities != null)
            {
                foreach (ServerCapability capability in m_capabilities)
                {
                    if (capability.Id == id)
                    {
                        return capability;
                    }
                }
            }

            return null;
        }

        private List<ServerCapability> m_capabilities;
    }

    /// <summary>
    /// A server capability.
    /// </summary>
    public class ServerCapability : IFormattable
    {
        /// <summary>
        /// Gets or sets the identifier.
        /// </summary>
        /// <value>
        /// The identifier.
        /// </value>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the description.
        /// </summary>
        /// <value>
        /// The description.
        /// </value>
        public string Description { get; set; }

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <param name="format">The format. Must be null.</param>
        /// <param name="formatProvider">The format provider.</param>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format != null)
            {
                throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
            }

            return string.Format(formatProvider, "[{0}] {1}", Id, Description);
        }

        /// <summary>
        /// No information is available.
        /// </summary>
        public const string NoInformation = "NA";

        /// <summary>
        /// The server supports live data.
        /// </summary>
        public const string LiveData = "DA";

        /// <summary>
        /// The server supports alarms and conditions
        /// </summary>
        public const string AlarmsAndConditions = "AC";

        /// <summary>
        /// The server supports historical data.
        /// </summary>
        public const string HistoricalData = "HD";

        /// <summary>
        /// The server supports historical events.
        /// </summary>
        public const string HistoricalEvents = "HE";

        /// <summary>
        /// The server is a global discovery server.
        /// </summary>
        public const string GlobalDiscoveryServer = "GDS";

        /// <summary>
        /// The server is a local discovery server.
        /// </summary>
        public const string LocalDiscoveryServer = "LDS";

        /// <summary>
        /// The server supports the device integration (DI) information model.
        /// </summary>
        public const string DI = "DI";
    }
}
