/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Stores a list of cached enpoints.
    /// </summary>
    public partial class ConfiguredEndpointCollection
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with its default endpoint configuration.
        /// </summary>
        public ConfiguredEndpointCollection(EndpointConfiguration configuration)
        {
            Initialize();

            m_defaultConfiguration = (EndpointConfiguration)configuration.MemberwiseClone();
        }

        /// <summary>
        /// Initializes the object from an application configuration.
        /// </summary>
        public ConfiguredEndpointCollection(ApplicationConfiguration configuration)
        {
            Initialize();

            m_defaultConfiguration = EndpointConfiguration.Create(configuration);

            if (configuration.ClientConfiguration != null)
            {
                m_discoveryUrls = new StringCollection(configuration.ClientConfiguration.WellKnownDiscoveryUrls);
            }
        }
        #endregion

        #region Static Methods
        /// <summary>
        /// Loads a collection of endpoints from a file and overrides the endpoint configuration.
        /// </summary>
        public static ConfiguredEndpointCollection Load(ApplicationConfiguration configuration, string filePath)
        {
            return Load(configuration, filePath, false);
        }

        /// <summary>
        /// Loads a collection of endpoints from a file and overrides the endpoint configuration.
        /// </summary>
        public static ConfiguredEndpointCollection Load(ApplicationConfiguration configuration, string filePath, bool overrideConfiguration)
        {
            ConfiguredEndpointCollection endpoints = Load(filePath);

            endpoints.m_defaultConfiguration = EndpointConfiguration.Create(configuration);

            // override the settings in the configuration.
            foreach (ConfiguredEndpoint endpoint in endpoints.Endpoints)
            {
                if (endpoint.Configuration == null || overrideConfiguration)
                {
                    endpoint.Update(endpoints.DefaultConfiguration);
                }
            }

            return endpoints;
        }

        /// <summary>
        /// Loads a collection of endpoints from a file.
        /// </summary>
        public static ConfiguredEndpointCollection Load(string filePath)
        {
            // load from file.
            ConfiguredEndpointCollection endpoints;
            using (Stream stream = File.OpenRead(filePath))
            {
                endpoints = Load(stream);
            }
            endpoints.m_filepath = filePath;

            // remove invalid endpoints and ensure server descriptions are consistent.
            List<ConfiguredEndpoint> endpointsToRemove = new List<ConfiguredEndpoint>();
            Dictionary<string, ApplicationDescription> servers = new Dictionary<string, ApplicationDescription>();

            foreach (ConfiguredEndpoint endpoint in endpoints.m_endpoints)
            {
                if (endpoint.Description == null)
                {
                    endpointsToRemove.Add(endpoint);
                    continue;
                }

                // set a default value for the server.
                if (endpoint.Description.Server == null)
                {
                    endpoint.Description.Server = new ApplicationDescription();
                    endpoint.Description.Server.ApplicationType = ApplicationType.Server;
                }

                // set a default for application uri.
                if (String.IsNullOrEmpty(endpoint.Description.Server.ApplicationUri))
                {
                    endpoint.Description.Server.ApplicationUri = endpoint.Description.EndpointUrl;
                }

                if (endpoint.Description.Server.DiscoveryUrls == null)
                {
                    endpoint.Description.Server.DiscoveryUrls = new StringCollection();
                }

                if (endpoint.Description.Server.DiscoveryUrls.Count == 0)
                {
                    string discoveryUrl = endpoint.Description.EndpointUrl;

                    if (!discoveryUrl.StartsWith(Utils.UriSchemeOpcTcp))
                    {
                        discoveryUrl += "/discovery";
                    }

                    endpoint.Description.Server.DiscoveryUrls.Add(discoveryUrl);
                }

                // normalize transport profile uri.
                if (endpoint.Description.TransportProfileUri != null)
                {
                    endpoint.Description.TransportProfileUri = Profiles.NormalizeUri(endpoint.Description.TransportProfileUri);
                }

                ApplicationDescription server = null;

                if (!servers.TryGetValue(endpoint.Description.Server.ApplicationUri, out server))
                {
                    // use the first description in the file as the correct master.
                    server = endpoint.Description.Server;

                    servers[server.ApplicationUri] = server;

                    // check if the server uri needs to be made globally unique.
                    server.ApplicationUri = Utils.UpdateInstanceUri(server.ApplicationUri);
                    servers[server.ApplicationUri] = server;
                    continue;
                }

                endpoint.Description.Server = (ApplicationDescription)server.MemberwiseClone();


            }

            // remove invalid endpoints.
            foreach (ConfiguredEndpoint endpoint in endpointsToRemove)
            {
                endpoints.Remove(endpoint);
            }

            // return processed collection.
            return endpoints;
        }

        /// <summary>
        /// Loads a collection of endpoints from a stream.
        /// </summary>
        public static ConfiguredEndpointCollection Load(Stream istrm)
        {
            try
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(ConfiguredEndpointCollection));
                ConfiguredEndpointCollection endpoints = serializer.ReadObject(istrm) as ConfiguredEndpointCollection;

                if (endpoints != null)
                {
                    foreach (ConfiguredEndpoint endpoint in endpoints)
                    {
                        if (endpoint.Description != null)
                        {
                            endpoint.Description.TransportProfileUri = Profiles.NormalizeUri(endpoint.Description.TransportProfileUri);
                        }
                    }
                }

                return endpoints;
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error loading ConfiguredEnpoints.");
                throw;
            }
        }

        /// <summary>
        /// Saves a collection of endpoints the file that it was loaded from.
        /// </summary>
        public void Save()
        {
            Save(m_filepath);
        }

        /// <summary>
        /// Saves a collection of endpoints to a file.
        /// </summary>
        public void Save(string filePath)
        {
            using (Stream stream = File.Open(filePath, FileMode.Create))
            {
                Save(stream);
            }
            m_filepath = filePath;
        }

        /// <summary>
        /// Saves a collection of endpoints to a stream.
        /// </summary>
        public void Save(Stream ostrm)
        {
            DataContractSerializer serializer = new DataContractSerializer(typeof(ConfiguredEndpointCollection));
            serializer.WriteObject(ostrm, this);
        }

        /// <summary>
        /// Returns a deep copy of the collection.
        /// </summary>
        public new object MemberwiseClone()
        {
            ConfiguredEndpointCollection clone = new ConfiguredEndpointCollection();

            clone.m_filepath = m_filepath;
            clone.m_knownHosts = new StringCollection(m_knownHosts);
            clone.m_defaultConfiguration = (EndpointConfiguration)m_defaultConfiguration.MemberwiseClone();

            foreach (ConfiguredEndpoint endpoint in m_endpoints)
            {
                ConfiguredEndpoint clonedEndpoint = (ConfiguredEndpoint)endpoint.MemberwiseClone();
                clonedEndpoint.Collection = clone;
                clone.m_endpoints.Add(clonedEndpoint);
            }

            return clone;
        }

        #region IList<ConfiguredEndpoint> Members
        /// <summary>
        /// Determines the index of a specific item in the <see cref="T:System.Collections.Generic.IList`1"/>.
        /// </summary>
        /// <param name="item">The object to locate in the <see cref="T:System.Collections.Generic.IList`1"/>.</param>
        /// <returns>
        /// The index of <paramref name="item"/> if found in the list; otherwise, -1.
        /// </returns>
        public int IndexOf(ConfiguredEndpoint item)
        {
            for (int ii = 0; ii < m_endpoints.Count; ii++)
            {
                if (object.ReferenceEquals(item, m_endpoints[ii]))
                {
                    return ii;
                }
            }

            return -1;
        }

        /// <summary>
        /// Inserts an item to the <see cref="T:System.Collections.Generic.IList`1"/> at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index at which <paramref name="item"/> should be inserted.</param>
        /// <param name="item">The object to insert into the <see cref="T:System.Collections.Generic.IList`1"/>.</param>
        /// <exception cref="T:System.ArgumentOutOfRangeException">
        /// 	<paramref name="index"/> is not a valid index in the <see cref="T:System.Collections.Generic.IList`1"/>.</exception>
        /// <exception cref="T:System.NotSupportedException">The <see cref="T:System.Collections.Generic.IList`1"/> is read-only.</exception>
        public void Insert(int index, ConfiguredEndpoint item)
        {
            Insert(item, index);
        }

        /// <summary>
        /// Removes the <see cref="T:System.Collections.Generic.IList`1"/> item at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the item to remove.</param>
        /// <exception cref="T:System.ArgumentOutOfRangeException">
        /// 	<paramref name="index"/> is not a valid index in the <see cref="T:System.Collections.Generic.IList`1"/>.</exception>
        /// <exception cref="T:System.NotSupportedException">The <see cref="T:System.Collections.Generic.IList`1"/> is read-only.</exception>
        public void RemoveAt(int index)
        {
            if (index < 0 || index >= m_endpoints.Count) throw new ArgumentOutOfRangeException(nameof(index));
            Remove(m_endpoints[index]);
        }

        /// <summary>
        /// Gets or sets the <see cref="Opc.Ua.ConfiguredEndpoint"/> at the specified index.
        /// </summary>
        /// <value>The <see cref="Opc.Ua.ConfiguredEndpoint"/> at the index</value>
        public ConfiguredEndpoint this[int index]
        {
            get
            {
                return m_endpoints[index];
            }

            set
            {
                throw new NotImplementedException();
            }
        }
        #endregion

        #region ICollection<ConfiguredEndpoint> Members
        /// <summary>
        /// Removes all items from the <see cref="T:System.Collections.Generic.ICollection`1"/>.
        /// </summary>
        /// <exception cref="T:System.NotSupportedException">The <see cref="T:System.Collections.Generic.ICollection`1"/> is read-only. </exception>
        public void Clear()
        {
            this.m_endpoints.Clear();
        }

        /// <summary>
        /// Determines whether the <see cref="T:System.Collections.Generic.ICollection`1"/> contains a specific value.
        /// </summary>
        /// <param name="item">The object to locate in the <see cref="T:System.Collections.Generic.ICollection`1"/>.</param>
        /// <returns>
        /// true if <paramref name="item"/> is found in the <see cref="T:System.Collections.Generic.ICollection`1"/>; otherwise, false.
        /// </returns>
        public bool Contains(ConfiguredEndpoint item)
        {
            for (int ii = 0; ii < m_endpoints.Count; ii++)
            {
                if (object.ReferenceEquals(item, m_endpoints[ii]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Copies the elements of the <see cref="T:System.Collections.Generic.ICollection`1"/> to an <see cref="T:System.Array"/>, starting at a particular <see cref="T:System.Array"/> index.
        /// </summary>
        /// <param name="array">The one-dimensional <see cref="T:System.Array"/> that is the destination of the elements copied from <see cref="T:System.Collections.Generic.ICollection`1"/>. The <see cref="T:System.Array"/> must have zero-based indexing.</param>
        /// <param name="arrayIndex">The zero-based index in <paramref name="array"/> at which copying begins.</param>
        /// <exception cref="T:System.ArgumentNullException">
        /// 	<paramref name="array"/> is null.</exception>
        /// <exception cref="T:System.ArgumentOutOfRangeException">
        /// 	<paramref name="arrayIndex"/> is less than 0.</exception>
        /// <exception cref="T:System.ArgumentException">
        /// 	<paramref name="array"/> is multidimensional.-or-<paramref name="arrayIndex"/> is equal to or greater than the length of <paramref name="array"/>.-or-The number of elements in the source <see cref="T:System.Collections.Generic.ICollection`1"/> is greater than the available space from <paramref name="arrayIndex"/> to the end of the destination <paramref name="array"/>./>.</exception>
        public void CopyTo(ConfiguredEndpoint[] array, int arrayIndex)
        {
            m_endpoints.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Gets the number of elements contained in the <see cref="T:System.Collections.Generic.ICollection`1"/>.
        /// </summary>
        /// <returns>The number of elements contained in the <see cref="T:System.Collections.Generic.ICollection`1"/>.</returns>
        public int Count => m_endpoints.Count;

        /// <summary>
        /// Gets a value indicating whether the <see cref="T:System.Collections.Generic.ICollection`1"/> is read-only.
        /// </summary>
        /// <value></value>
        /// <returns>true if the <see cref="T:System.Collections.Generic.ICollection`1"/> is read-only; otherwise, false.</returns>
        public bool IsReadOnly => false;
        #endregion

        #region IEnumerable<ConfiguredEndpoint> Members
        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IEnumerator`1"/> that can be used to iterate through the collection.
        /// </returns>
        public IEnumerator<ConfiguredEndpoint> GetEnumerator()
        {
            return m_endpoints.GetEnumerator();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Add the endpoint description to the cache.
        /// </summary>
        public ConfiguredEndpoint Add(EndpointDescription endpoint)
        {
            return Add(endpoint, null);
        }

        /// <summary>
        /// Add the endpoint description and configuration to the cache.
        /// </summary>
        public ConfiguredEndpoint Add(EndpointDescription endpoint, EndpointConfiguration configuration)
        {
            ValidateEndpoint(endpoint);

            foreach (ConfiguredEndpoint item in m_endpoints)
            {
                if (Object.ReferenceEquals(item.Description, endpoint))
                {
                    throw new ArgumentException("Endpoint already exists in the collection.");
                }
            }

            ConfiguredEndpoint configuredEndpoint = new ConfiguredEndpoint(this, endpoint, configuration);
            m_endpoints.Add(configuredEndpoint);
            return configuredEndpoint;
        }

        /// <summary>
        /// Adds a previous created endpoint to the collection.
        /// </summary>
        public void Add(ConfiguredEndpoint item)
        {
            Insert(item, -1);
        }

        /// <summary>
        /// Adds a previous created endpoint to the collection.
        /// </summary>
        private void Insert(ConfiguredEndpoint endpoint, int index)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            ValidateEndpoint(endpoint.Description);

            // update collection.
            if (endpoint.Collection != null)
            {
                endpoint.Collection.Remove(endpoint);
            }

            endpoint.Collection = this;

            if (!Object.ReferenceEquals(endpoint.Collection, this))
            {
                throw new ArgumentException("Cannot add an endpoint from another collection.");
            }

            if (m_endpoints.Contains(endpoint))
            {
                throw new ArgumentException("Endpoint already belongs to the collection.");
            }

            if (index < 0)
            {
                m_endpoints.Add(endpoint);
            }
            else
            {
                m_endpoints.Insert(index, endpoint);
            }
        }

        /// <summary>
        /// Removes the configured endpoint.
        /// </summary>
        public bool Remove(ConfiguredEndpoint item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            return m_endpoints.Remove(item);
        }

        /// <summary>
        /// Removes all endpoints for the specified server.
        /// </summary>
        public void RemoveServer(string serverUri)
        {
            if (serverUri == null) throw new ArgumentNullException(nameof(serverUri));

            foreach (ConfiguredEndpoint endpointToRemove in GetEndpoints(serverUri))
            {
                Remove(endpointToRemove);
            }
        }

        /// <summary>
        /// Updates the server descrption for the endpoints.
        /// </summary>
        public void SetApplicationDescription(string serverUri, ApplicationDescription server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            if (String.IsNullOrEmpty(server.ApplicationUri))
            {
                throw new ArgumentException("A ServerUri must provided.", nameof(server));
            }

            if (server.DiscoveryUrls.Count == 0)
            {
                throw new ArgumentException("At least one DiscoveryUrl must be provided.", nameof(server));
            }

            List<ConfiguredEndpoint> endpoints = GetEndpoints(server.ApplicationUri);

            // create a placeholder endpoint for the server description.
            if (endpoints.Count == 0)
            {
                string endpointUrl = null;

                for (int ii = 0; ii < server.DiscoveryUrls.Count; ii++)
                {
                    if (!String.IsNullOrEmpty(server.DiscoveryUrls[ii]))
                    {
                        endpointUrl = server.DiscoveryUrls[ii];
                        break;
                    }
                }

                if (endpointUrl != null && endpointUrl.EndsWith("/discovery", StringComparison.Ordinal))
                {
                    endpointUrl = endpointUrl.Substring(0, endpointUrl.Length - "/discovery".Length);
                }

                if (endpointUrl != null)
                {
                    ConfiguredEndpoint endpoint = Create(endpointUrl);
                    endpoint.Description.Server = (ApplicationDescription)server.MemberwiseClone();
                    Add(endpoint);
                }
            }

            // update all endpoints with the same server uri.
            else
            {
                foreach (ConfiguredEndpoint endpointToUpdate in GetEndpoints(serverUri))
                {
                    endpointToUpdate.Description.Server = (ApplicationDescription)server.MemberwiseClone();
                }
            }
        }

        /// <summary>
        /// Creates a new endpoint from a url that is not part of the collection.
        /// </summary>
        /// <remarks>
        /// Call the Add() method to add it to the collection.
        /// </remarks>
        public ConfiguredEndpoint Create(string url)
        {
            // check for security parameters appended to the URL
            string parameters = null;

            int index = url.IndexOf("- [", StringComparison.Ordinal);

            if (index != -1)
            {
                parameters = url.Substring(index + 3);
                url = url.Substring(0, index).Trim();
            }

            MessageSecurityMode securityMode = MessageSecurityMode.SignAndEncrypt;
            string securityPolicyUri = SecurityPolicies.Basic256Sha256;
            bool useBinaryEncoding = true;

            if (!String.IsNullOrEmpty(parameters))
            {
                string[] fields = parameters.Split(new char[] { '-', '[', ':', ']' }, StringSplitOptions.RemoveEmptyEntries);

                try
                {
                    if (fields.Length > 0)
                    {
                        securityMode = (MessageSecurityMode)Enum.Parse(typeof(MessageSecurityMode), fields[0], false);
                    }
                    else
                    {
                        securityMode = MessageSecurityMode.None;
                    }
                }
                catch
                {
                    securityMode = MessageSecurityMode.None;
                }

                try
                {
                    if (fields.Length > 1)
                    {
                        securityPolicyUri = SecurityPolicies.GetUri(fields[1]);
                    }
                    else
                    {
                        securityPolicyUri = SecurityPolicies.None;
                    }
                }
                catch
                {
                    securityPolicyUri = SecurityPolicies.None;
                }

                try
                {
                    if (fields.Length > 2)
                    {
                        useBinaryEncoding = fields[2] == "Binary";
                    }
                    else
                    {
                        useBinaryEncoding = false;
                    }
                }
                catch
                {
                    useBinaryEncoding = false;
                }
            }

            Uri uri = new Uri(url);

            EndpointDescription description = new EndpointDescription();

            description.EndpointUrl = uri.ToString();
            description.SecurityMode = securityMode;
            description.SecurityPolicyUri = securityPolicyUri;
            description.Server.ApplicationUri = Utils.UpdateInstanceUri(uri.ToString());
            description.Server.ApplicationName = uri.AbsolutePath;

            if (description.EndpointUrl.StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
            {
                description.TransportProfileUri = Profiles.UaTcpTransport;
                description.Server.DiscoveryUrls.Add(description.EndpointUrl);
            }
            else if (description.EndpointUrl.StartsWith(Utils.UriSchemeHttps, StringComparison.Ordinal))
            {
                description.TransportProfileUri = Profiles.HttpsBinaryTransport;
                description.Server.DiscoveryUrls.Add(description.EndpointUrl);
            }

            ConfiguredEndpoint endpoint = new ConfiguredEndpoint(this, description, null);
            endpoint.Configuration.UseBinaryEncoding = useBinaryEncoding;
            endpoint.UpdateBeforeConnect = true;
            return endpoint;
        }

        /// <summary>
        /// Returns the configured endpoints for the server uri.
        /// </summary>
        public List<ConfiguredEndpoint> GetEndpoints(string serverUri)
        {
            List<ConfiguredEndpoint> endpoints = new List<ConfiguredEndpoint>();

            foreach (ConfiguredEndpoint endpoint in m_endpoints)
            {
                if (endpoint.Description.Server.ApplicationUri == serverUri)
                {
                    endpoints.Add(endpoint);
                }
            }

            return endpoints;
        }

        /// <summary>
        /// Returns the servers that are part of the collection.
        /// </summary>
        public ApplicationDescriptionCollection GetServers()
        {
            Dictionary<string, ApplicationDescription> servers = new Dictionary<string, ApplicationDescription>();

            foreach (ConfiguredEndpoint endpoint in m_endpoints)
            {
                ApplicationDescription server = endpoint.Description.Server;

                if (!String.IsNullOrEmpty(server.ApplicationUri))
                {
                    if (!servers.ContainsKey(server.ApplicationUri))
                    {
                        servers.Add(server.ApplicationUri, server);
                    }
                }
            }

            return new ApplicationDescriptionCollection(servers.Values);
        }

        /// <summary>
        /// Copies the endpoints.
        /// </summary>
        /// <param name="serverUri">The server URI.</param>
        /// <returns></returns>
        [Obsolete("Non-functional - replaced with GetEndpoints()")]
        public List<ConfiguredEndpoint> CopyEndpoints(string serverUri)
        {
            return null;
        }

        /// <summary>
        /// Updates an endpoint with information from the server's discovery endpoint.
        /// </summary>
        [Obsolete("Non-functional - method not used - updates should be done with ConfiguredEndpoint.UpdateFromServer()")]
        public void UpdateEndpointsForServer(string serverUri)
        {
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// A list of well known urls that can be used for discovery.
        /// </summary>
        public StringCollection DiscoveryUrls
        {
            get
            {
                return m_discoveryUrls;
            }

            set
            {
                if (value == null)
                {
                    m_discoveryUrls = new StringCollection(Utils.DiscoveryUrls);
                }
                else
                {
                    m_discoveryUrls = value;
                }
            }
        }

        /// <summary>
        /// The default configuration for new ConfiguredEndpoints.
        /// </summary>
        public EndpointConfiguration DefaultConfiguration => m_defaultConfiguration;
        #endregion

        #region Private Methods
        /// <summary>
        /// Throws exceptions if the endpoint is not valid.
        /// </summary>
        private static void ValidateEndpoint(EndpointDescription endpoint)
        {
            if (endpoint == null)
            {
                throw new ArgumentException("Endpoint must not be null.");
            }

            if (String.IsNullOrEmpty(endpoint.EndpointUrl))
            {
                throw new ArgumentException("Endpoint must have a valid URL.");
            }

            if (endpoint.Server == null)
            {
                endpoint.Server = new ApplicationDescription();
                endpoint.Server.ApplicationType = ApplicationType.Server;
            }

            if (String.IsNullOrEmpty(endpoint.Server.ApplicationUri))
            {
                endpoint.Server.ApplicationUri = endpoint.EndpointUrl;
            }
        }
        #endregion
    }

    #region ConfiguredEndpoint Class
    /// <summary>
    /// Stores the configuration information for an endpoint.
    /// </summary>
    public partial class ConfiguredEndpoint : IFormattable
    {
        #region Constructors
        /// <summary>
        /// Creates a configured endpoint from the server description.
        /// </summary>
        public ConfiguredEndpoint(
            ApplicationDescription server,
            EndpointConfiguration configuration)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            m_description = new EndpointDescription();
            m_updateBeforeConnect = true;

            m_description.Server = server;

            foreach (string discoveryUrl in server.DiscoveryUrls)
            {
                string baseUrl = discoveryUrl;

                if (baseUrl != null)
                {
                    if (baseUrl.EndsWith("/discovery", StringComparison.Ordinal))
                    {
                        baseUrl = baseUrl.Substring(0, baseUrl.Length - "/discovery".Length);
                    }
                }

                Uri url = Utils.ParseUri(baseUrl);

                if (url != null)
                {
                    m_description.EndpointUrl = url.ToString();
                    m_description.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                    m_description.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
                    m_description.UserIdentityTokens.Add(new UserTokenPolicy(UserTokenType.Anonymous));

                    if (url.Scheme == Utils.UriSchemeHttps)
                    {
                        m_description.TransportProfileUri = Profiles.HttpsBinaryTransport;
                    }

                    if (url.Scheme == Utils.UriSchemeOpcTcp)
                    {
                        m_description.TransportProfileUri = Profiles.UaTcpTransport;
                    }

                    break;
                }
            }

            // ensure a default configuration.
            if (configuration == null)
            {
                configuration = EndpointConfiguration.Create();
            }

            Update(configuration);
        }

        /// <summary>
        /// The default constructor.
        /// </summary>
        public ConfiguredEndpoint(
            ConfiguredEndpointCollection collection,
            EndpointDescription description)
        :
            this(collection, description, null)
        {
        }

        /// <summary>
        /// The default constructor.
        /// </summary>
        public ConfiguredEndpoint(
            ConfiguredEndpointCollection collection,
            EndpointDescription description,
            EndpointConfiguration configuration)
        {
            if (description == null) throw new ArgumentNullException(nameof(description));

            m_collection = collection;
            m_description = description;
            m_updateBeforeConnect = true;

            // ensure a default configuration.
            if (configuration == null)
            {
                if (collection != null)
                {
                    configuration = collection.DefaultConfiguration;
                }
                else
                {
                    configuration = EndpointConfiguration.Create();
                }
            }

            Update(configuration);
        }
        #endregion

        /// <summary>
        /// Returns a deep copy of the endpoint.
        /// </summary>
        public new object MemberwiseClone()
        {
            ConfiguredEndpoint clone = new ConfiguredEndpoint();
            clone.Collection = this.Collection;
            clone.Update(this);
            return clone;
        }

        #region Overridden Methods
        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        public override string ToString()
        {
            return ToString(null, null);
        }
        #endregion

        #region IFormattable Members
        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <param name="format">(Unused). Always pass NULL/NOTHING</param>
        /// <param name="formatProvider">(Unused). Always pass NULL/NOTHING</param>
        /// <exception cref="FormatException">Thrown if non-null parameters are used</exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return Utils.Format(
                    "{0} - [{1}:{2}:{3}]",
                    m_description.EndpointUrl,
                    m_description.SecurityMode,
                    SecurityPolicies.GetDisplayName(m_description.SecurityPolicyUri),
                    (m_configuration != null && m_configuration.UseBinaryEncoding) ? "Binary" : "XML");
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Determine if an update of the endpoint from the server is needed.
        /// </summary>
        public bool NeedUpdateFromServer()
        {
            bool hasCertificate = (Description.ServerCertificate != null && Description.ServerCertificate.Length > 0);
            bool usingUserTokenSecurity =
                (SelectedUserTokenPolicy.TokenType != UserTokenType.Anonymous) &&
                (SelectedUserTokenPolicy.SecurityPolicyUri ?? SecurityPolicies.None) != SecurityPolicies.None;
            bool usingTransportSecurity = Description.SecurityPolicyUri != SecurityPolicies.None;
            return (usingUserTokenSecurity || usingTransportSecurity) && !hasCertificate;
        }

        /// <summary>
        /// Updates the endpoint description.
        /// </summary>
        public void Update(ConfiguredEndpoint endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            m_description = (EndpointDescription)endpoint.Description.MemberwiseClone();
            m_configuration = (EndpointConfiguration)endpoint.Configuration.MemberwiseClone();

            // normalize transport profile uri.
            if (m_description.TransportProfileUri != null)
            {
                m_description.TransportProfileUri = Profiles.NormalizeUri(m_description.TransportProfileUri);
            }

            m_updateBeforeConnect = endpoint.m_updateBeforeConnect;
            m_selectedUserTokenPolicyIndex = endpoint.m_selectedUserTokenPolicyIndex;
            m_binaryEncodingSupport = endpoint.m_binaryEncodingSupport;

            if (endpoint.m_userIdentity != null)
            {
                m_userIdentity = (UserIdentityToken)endpoint.m_userIdentity.MemberwiseClone();
            }
        }

        /// <summary>
        /// Updates the endpoint description.
        /// </summary>
        public void Update(EndpointDescription description)
        {
            if (description == null) throw new ArgumentNullException(nameof(description));

            m_description = (EndpointDescription)description.MemberwiseClone();

            // normalize transport profile uri.
            if (m_description.TransportProfileUri != null)
            {
                m_description.TransportProfileUri = Profiles.NormalizeUri(m_description.TransportProfileUri);
            }

            // set the proxy url.
            if (m_collection != null && m_description.EndpointUrl != null)
            {
                if (m_description.EndpointUrl.StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
                {
                    m_description.ProxyUrl = m_collection.TcpProxyUrl;
                }
            }
        }

        /// <summary>
        /// Updates the endpoint configuration.
        /// </summary>
        public void Update(EndpointConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            m_configuration = (EndpointConfiguration)configuration.MemberwiseClone();

            BinaryEncodingSupport binaryEncodingSupport = m_description.EncodingSupport;

            // check if the configuration restricts the encoding if the endpoint supports both.
            if (binaryEncodingSupport == BinaryEncodingSupport.Optional)
            {
                binaryEncodingSupport = m_binaryEncodingSupport;
            }

            if (binaryEncodingSupport == BinaryEncodingSupport.None)
            {
                m_configuration.UseBinaryEncoding = false;
            }

            if (binaryEncodingSupport == BinaryEncodingSupport.Required)
            {
                m_configuration.UseBinaryEncoding = true;
            }
        }

        /// <summary>
        /// Updates an endpoint with information from the server's discovery endpoint.
        /// </summary>
        public void UpdateFromServer()
        {
            UpdateFromServer(EndpointUrl, m_description.SecurityMode, m_description.SecurityPolicyUri);
        }

        /// <summary>
        /// Updates an endpoint with information from the server's discovery endpoint.
        /// </summary>
        public void UpdateFromServer(
            Uri endpointUrl,
            MessageSecurityMode securityMode,
            string securityPolicyUri)
        {
            UpdateFromServer(endpointUrl, null, securityMode, securityPolicyUri);
        }

        /// <summary>
        /// Updates an endpoint with information from the server's discovery endpoint.
        /// </summary>
        public void UpdateFromServer(
            Uri endpointUrl,
            ITransportWaitingConnection connection,
            MessageSecurityMode securityMode,
            string securityPolicyUri)
        {
            // get the a discovery url.
            Uri discoveryUrl = GetDiscoveryUrl(endpointUrl);

            // create the discovery client.
            DiscoveryClient client;
            if (connection != null)
            {
                client = DiscoveryClient.Create(connection, m_configuration);
            }
            else
            {
                client = DiscoveryClient.Create(discoveryUrl, m_configuration);
            }

            try
            {
                // get the endpoints.
                EndpointDescriptionCollection collection = client.GetEndpoints(null);

                // find list of matching endpoints.
                var matches = MatchEndpoints(
                    collection,
                    endpointUrl,
                    securityMode,
                    securityPolicyUri
                    );

                // select best match
                var match = SelectBestMatch(matches, discoveryUrl);

                // update the endpoint.                        
                Update(match);
            }
            finally
            {
                client.Close();
            }
        }

        /// <summary>
        /// Updates an endpoint with information from the server's discovery endpoint.
        /// </summary>
        public Task UpdateFromServerAsync()
        {
            return UpdateFromServerAsync(EndpointUrl, m_description.SecurityMode, m_description.SecurityPolicyUri);
        }

        /// <summary>
        /// Updates an endpoint with information from the server's discovery endpoint.
        /// </summary>
        public Task UpdateFromServerAsync(
            Uri endpointUrl,
            MessageSecurityMode securityMode,
            string securityPolicyUri)
        {
            return UpdateFromServerAsync(endpointUrl, null, securityMode, securityPolicyUri);
        }

        /// <summary>
        /// Updates an endpoint with information from the server's discovery endpoint.
        /// </summary>
        public async Task UpdateFromServerAsync(
            Uri endpointUrl,
            ITransportWaitingConnection connection,
            MessageSecurityMode securityMode,
            string securityPolicyUri)
        {
            // get the a discovery url.
            Uri discoveryUrl = GetDiscoveryUrl(endpointUrl);

            // create the discovery client.
            DiscoveryClient client;
            if (connection != null)
            {
                client = DiscoveryClient.Create(connection, m_configuration);
            }
            else
            {
                client = DiscoveryClient.Create(discoveryUrl, m_configuration);
            }

            try
            {
                // get the endpoints.
                EndpointDescriptionCollection collection = await client.GetEndpointsAsync(null);

                // find list of matching endpoints.
                var matches = MatchEndpoints(
                    collection,
                    endpointUrl,
                    securityMode,
                    securityPolicyUri
                    );

                // select best match
                var match = SelectBestMatch(matches, discoveryUrl);

                // update the endpoint.                        
                Update(match);
            }
            finally
            {
                client.Close();
            }
        }

        /// <summary>
        /// Returns a discovery url that can be used to update the endpoint description.
        /// </summary>
        public Uri GetDiscoveryUrl(Uri endpointUrl)
        {
            // update the endpoint description.
            if (endpointUrl != null)
            {
                m_description.EndpointUrl = endpointUrl.ToString();
            }
            else
            {
                endpointUrl = Utils.ParseUri(m_description.EndpointUrl);
            }

            // get the know discovery URLs.
            StringCollection discoveryUrls = null;

            if (m_description.Server != null)
            {
                discoveryUrls = m_description.Server.DiscoveryUrls;
            }

            // attempt to construct a discovery url by appending 'discovery' to the endpoint.
            if (discoveryUrls == null || discoveryUrls.Count == 0)
            {
                if (endpointUrl.Scheme != Utils.UriSchemeOpcTcp)
                {
                    return new Uri(String.Format(CultureInfo.InvariantCulture, "{0}/discovery", endpointUrl));
                }
                else
                {
                    return endpointUrl;
                }
            }

            // choose the URL that uses the same protocol if one exists.
            for (int ii = 1; ii < discoveryUrls.Count; ii++)
            {
                if (discoveryUrls[ii].StartsWith(endpointUrl.Scheme, StringComparison.Ordinal))
                {
                    return Utils.ParseUri(discoveryUrls[ii]);
                }
            }

            // return the first in the list.
            return Utils.ParseUri(discoveryUrls[0]);
        }

        /// <summary>
        /// Parses the extension.
        /// </summary>
        /// <typeparam name="T">The type of extension.</typeparam>
        /// <param name="elementName">Name of the element (null means use type name).</param>
        /// <returns>The extension if found. Null otherwise.</returns>
        public T ParseExtension<T>(XmlQualifiedName elementName)
        {
            return Utils.ParseExtension<T>(m_extensions, elementName);
        }

        /// <summary>
        /// Updates the extension.
        /// </summary>
        /// <typeparam name="T">The type of extension.</typeparam>
        /// <param name="elementName">Name of the element (null means use type name).</param>
        /// <param name="value">The value.</param>
        public void UpdateExtension<T>(XmlQualifiedName elementName, object value)
        {
            Utils.UpdateExtension<T>(ref m_extensions, elementName, value);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The collection that the endpoint belongs to. 
        /// </summary>
        public ConfiguredEndpointCollection Collection
        {
            get
            {
                return m_collection;
            }

            internal set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                m_collection = value;
            }
        }

        /// <summary>
        /// The URL used to create a sessions.
        /// </summary>
        public Uri EndpointUrl
        {
            get
            {
                if (String.IsNullOrEmpty(m_description.EndpointUrl))
                {
                    return null;
                }

                return Utils.ParseUri(m_description.EndpointUrl);
            }

            set
            {
                if (value == null)
                {
                    m_description.EndpointUrl = null;
                }

                m_description.EndpointUrl = String.Format(CultureInfo.InvariantCulture, "{0}", value);
            }
        }

        /// <summary>
        /// The user identity to use when connecting to the endpoint.
        /// </summary>
        public UserTokenPolicy SelectedUserTokenPolicy
        {
            get
            {
                if (m_description != null && m_description.UserIdentityTokens != null)
                {
                    UserTokenPolicyCollection policies = m_description.UserIdentityTokens;

                    if (m_selectedUserTokenPolicyIndex >= 0 && policies.Count > m_selectedUserTokenPolicyIndex)
                    {
                        return policies[m_selectedUserTokenPolicyIndex];
                    }
                }

                return null;
            }

            set
            {
                if (m_description != null && m_description.UserIdentityTokens != null)
                {
                    UserTokenPolicyCollection policies = m_description.UserIdentityTokens;

                    for (int ii = 0; ii < policies.Count; ii++)
                    {
                        if (Object.ReferenceEquals(policies[ii], value))
                        {
                            m_selectedUserTokenPolicyIndex = ii;
                            break;
                        }
                    }
                }

                m_selectedUserTokenPolicyIndex = -1;
            }
        }
        #endregion

        #region Private Methods
        private EndpointDescriptionCollection MatchEndpoints(
            EndpointDescriptionCollection collection,
            Uri endpointUrl,
            MessageSecurityMode securityMode,
            string securityPolicyUri)
        {
            if (collection == null || collection.Count == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnknownResponse,
                    "Server does not have any endpoints defined.");
            }

            // find list of matching endpoints.
            EndpointDescriptionCollection matches = new EndpointDescriptionCollection();

            // first pass - match on the requested security parameters.
            foreach (EndpointDescription description in collection)
            {
                // check for match on security policy.
                if (!String.IsNullOrEmpty(securityPolicyUri))
                {
                    if (securityPolicyUri != description.SecurityPolicyUri)
                    {
                        continue;
                    }
                }

                // check for match on security mode.
                if (securityMode != MessageSecurityMode.Invalid)
                {
                    if (securityMode != description.SecurityMode)
                    {
                        continue;
                    }
                }

                // add to list of matches.
                matches.Add(description);
            }

            // no matches (security parameters may have changed).
            if (matches.Count == 0)
            {
                matches = collection;
            }

            // check if list has to be narrowed down further.
            if (matches.Count > 1)
            {
                collection = matches;
                matches = new EndpointDescriptionCollection();

                // second pass - match on the url scheme.
                foreach (EndpointDescription description in collection)
                {
                    // parse the endpoint url.
                    Uri sessionUrl = Utils.ParseUri(description.EndpointUrl);

                    if (sessionUrl == null)
                    {
                        continue;
                    }

                    // check for matching protocol.
                    if (sessionUrl.Scheme != endpointUrl.Scheme)
                    {
                        continue;
                    }

                    matches.Add(description);
                }
            }

            // no matches (protocol may not be supported).
            if (matches.Count == 0)
            {
                matches = collection;
            }

            return matches;
        }

        /// <summary>
        /// Select the best match from a security description.
        /// </summary>
        private EndpointDescription SelectBestMatch(
            EndpointDescriptionCollection matches,
            Uri discoveryUrl
            )
        {
            // choose first in list by default.
            EndpointDescription match = matches[0];

            // check if list has to be narrowed down further.
            if (matches.Count > 1)
            {
                // third pass - match based on security level.
                foreach (EndpointDescription description in matches)
                {
                    if (description.SecurityLevel > match.SecurityLevel)
                    {
                        match = description;
                    }
                }
            }

            // check if the endpoint url matches the endpoint used in the request.
            if (discoveryUrl != null)
            {
                Uri matchUrl = Utils.ParseUri(match.EndpointUrl);
                if (matchUrl == null || String.Compare(discoveryUrl.DnsSafeHost, matchUrl.DnsSafeHost, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    UriBuilder uri = new UriBuilder(matchUrl);
                    uri.Host = discoveryUrl.DnsSafeHost;
                    uri.Port = discoveryUrl.Port;
                    match.EndpointUrl = uri.ToString();

                    // need to update the discovery urls.
                    match.Server.DiscoveryUrls.Clear();
                    match.Server.DiscoveryUrls.Add(discoveryUrl.ToString());
                }
            }

            return match;
        }
        #endregion
    }
    #endregion
}
#endregion
