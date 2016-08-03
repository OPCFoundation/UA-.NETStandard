using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Opc.Ua.Gds
{
    /// <summary>
    /// The set known capability identifiers.
    /// </summary>
    public class ServerCapabilities : IEnumerable<ServerCapability>
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ServerCapabilities"/> class.
        /// </summary>
        public ServerCapabilities()
        {
            Load();
        }
        #endregion

        #region IEnumerable Members
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
            return this.GetEnumerator();
        }
        #endregion

        #region Public Members
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
                foreach (var resourceName in typeof(Opc.Ua.ObjectIds).Assembly.GetManifestResourceNames())
                {
                    if (resourceName.EndsWith("ServerCapabilities.csv", StringComparison.OrdinalIgnoreCase))
                    {
                        istrm = typeof(Opc.Ua.ObjectIds).Assembly.GetManifestResourceStream(resourceName);
                        break;
                    }
                }
            }

            if (istrm != null)
            {
                using (StreamReader reader = new StreamReader(istrm))
                {
                    string line = reader.ReadLine();

                    while (line != null)
                    {
                        int index = line.IndexOf(',');

                        if (index >= 0)
                        {
                            string id = line.Substring(0, index).Trim();
                            string description = line.Substring(index + 1).Trim();
                            capabilities.Add(new ServerCapability() { Id = id, Description = description });
                        }

                        line = reader.ReadLine();
                    }
                }
            }

            m_capabilities = capabilities;
        }

        /// <summary>
        /// Finds the sever capability with the specified identifier.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <returns>The sever capability, if found. NULL if it does not exist.</returns>
        public ServerCapability Find(string id)
        {
            if (id != null)
            {
                if (m_capabilities != null)
                {
                    foreach (var capability in m_capabilities)
                    {
                        if (capability.Id == id)
                        {
                            return capability;
                        }
                    }
                }
            }

            return null;
        }
        #endregion

        #region Private Fields
        private List<ServerCapability> m_capabilities;
        #endregion
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
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="formatProvider">The format provider.</param>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            return "[" + Id + "] " + Description;
        }

        #region Well Known Identifiers
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
        /// The server supports the data integration (DI) information model.
        /// </summary>
        public const string DI = "DI";
        #endregion
    }
}
