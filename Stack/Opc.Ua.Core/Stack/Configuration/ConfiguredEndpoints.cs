/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
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
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Stores a list of cached endpoints.
    /// </summary>
    public partial class ConfiguredEndpointCollection : ICloneable
    {
        /// <summary>
        /// Initializes the object with its default endpoint configuration.
        /// </summary>
        public ConfiguredEndpointCollection(EndpointConfiguration configuration)
        {
            Initialize();

            DefaultConfiguration = (EndpointConfiguration)configuration.Clone();
        }

        /// <summary>
        /// Initializes the object from an application configuration.
        /// </summary>
        public ConfiguredEndpointCollection(ApplicationConfiguration configuration)
        {
            Initialize();

            DefaultConfiguration = EndpointConfiguration.Create(configuration);

            if (configuration.ClientConfiguration != null)
            {
                m_discoveryUrls = [.. configuration.ClientConfiguration.WellKnownDiscoveryUrls];
            }
        }

        /// <summary>
        /// Loads a collection of endpoints from a file and overrides the endpoint configuration.
        /// </summary>
        public static ConfiguredEndpointCollection Load(
            ApplicationConfiguration configuration,
            string filePath)
        {
            return Load(configuration, filePath, false);
        }

        /// <summary>
        /// Loads a collection of endpoints from a file and overrides the endpoint configuration.
        /// </summary>
        public static ConfiguredEndpointCollection Load(
            ApplicationConfiguration configuration,
            string filePath,
            bool overrideConfiguration)
        {
            ConfiguredEndpointCollection endpoints = Load(filePath);

            endpoints.DefaultConfiguration = EndpointConfiguration.Create(configuration);

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
            var endpointsToRemove = new List<ConfiguredEndpoint>();
            var servers = new Dictionary<string, ApplicationDescription>();

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
                    endpoint.Description.Server = new ApplicationDescription
                    {
                        ApplicationType = ApplicationType.Server
                    };
                }

                // set a default for application uri.
                if (string.IsNullOrEmpty(endpoint.Description.Server.ApplicationUri))
                {
                    endpoint.Description.Server.ApplicationUri = endpoint.Description.EndpointUrl;
                }

                if (endpoint.Description.Server.DiscoveryUrls == null)
                {
                    endpoint.Description.Server.DiscoveryUrls = [];
                }

                if (endpoint.Description.Server.DiscoveryUrls.Count == 0)
                {
                    string discoveryUrl = endpoint.Description.EndpointUrl;

                    if (Utils.IsUriHttpRelatedScheme(discoveryUrl))
                    {
                        discoveryUrl += ConfiguredEndpoint.DiscoverySuffix;
                    }

                    endpoint.Description.Server.DiscoveryUrls.Add(discoveryUrl);
                }

                // normalize transport profile uri.
                if (endpoint.Description.TransportProfileUri != null)
                {
                    endpoint.Description.TransportProfileUri = Profiles.NormalizeUri(
                        endpoint.Description.TransportProfileUri);
                }

                if (!servers.TryGetValue(
                    endpoint.Description.Server.ApplicationUri,
                    out ApplicationDescription server))
                {
                    // use the first description in the file as the correct master.
                    server = endpoint.Description.Server;

                    servers[server.ApplicationUri] = server;

                    // check if the server uri needs to be made globally unique.
                    server.ApplicationUri = Utils.UpdateInstanceUri(server.ApplicationUri);
                    servers[server.ApplicationUri] = server;
                    continue;
                }

                endpoint.Description.Server = (ApplicationDescription)server.Clone();
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
                var serializer = new DataContractSerializer(typeof(ConfiguredEndpointCollection));
                var endpoints = serializer.ReadObject(istrm) as ConfiguredEndpointCollection;

                if (endpoints != null)
                {
                    foreach (ConfiguredEndpoint endpoint in endpoints)
                    {
                        if (endpoint.Description != null)
                        {
                            endpoint.Description.TransportProfileUri = Profiles.NormalizeUri(
                                endpoint.Description.TransportProfileUri);
                        }
                    }
                }

                return endpoints;
            }
            catch (Exception e)
            {
                Utils.LogError(
                    "Unexpected error loading ConfiguredEndpoints: {0}",
                    Redaction.Redact.Create(e));
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
            var serializer = new DataContractSerializer(typeof(ConfiguredEndpointCollection));
            serializer.WriteObject(ostrm, this);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Returns a deep copy of the collection.
        /// </summary>
        public new object MemberwiseClone()
        {
            var clone = new ConfiguredEndpointCollection
            {
                m_filepath = m_filepath,
                m_knownHosts = [.. m_knownHosts],
                DefaultConfiguration = (EndpointConfiguration)DefaultConfiguration.MemberwiseClone()
            };

            foreach (ConfiguredEndpoint endpoint in m_endpoints)
            {
                var clonedEndpoint = (ConfiguredEndpoint)endpoint.MemberwiseClone();
                clonedEndpoint.Collection = clone;
                clone.m_endpoints.Add(clonedEndpoint);
            }

            return clone;
        }

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
                if (ReferenceEquals(item, m_endpoints[ii]))
                {
                    return ii;
                }
            }

            return -1;
        }

        /// <summary>
        /// Inserts an item to the <see cref="System.Collections.IList"/> at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index at which <paramref name="item"/> should be inserted.</param>
        /// <param name="item">The object to insert into the <see cref="System.Collections.IList"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// 	<paramref name="index"/> is not a valid index in the <see cref="System.Collections.IList"/>.</exception>
        /// <exception cref="NotSupportedException">The <see cref="System.Collections.IList"/> is read-only.</exception>
        public void Insert(int index, ConfiguredEndpoint item)
        {
            Insert(item, index);
        }

        /// <summary>
        /// Removes the <see cref="System.Collections.IList"/> item at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the item to remove.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// 	<paramref name="index"/> is not a valid index in the <see cref="System.Collections.IList"/>.</exception>
        /// <exception cref="NotSupportedException">The <see cref="System.Collections.IList"/> is read-only.</exception>
        public void RemoveAt(int index)
        {
            if (index < 0 || index >= m_endpoints.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            Remove(m_endpoints[index]);
        }

        /// <summary>
        /// Gets or sets the <see cref="ConfiguredEndpoint"/> at the specified index.
        /// </summary>
        /// <value>The <see cref="ConfiguredEndpoint"/> at the index</value>
        /// <exception cref="NotImplementedException"></exception>
        public ConfiguredEndpoint this[int index]
        {
            get => m_endpoints[index];
            set => throw new NotImplementedException();
        }

        /// <summary>
        /// Removes all items from the <see cref="T:System.Collections.Generic.ICollection`1"/>.
        /// </summary>
        /// <exception cref="T:System.NotSupportedException">The <see cref="T:System.Collections.Generic.ICollection`1"/> is read-only. </exception>
        public void Clear()
        {
            m_endpoints.Clear();
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
                if (ReferenceEquals(item, m_endpoints[ii]))
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
        /// <returns>true if the <see cref="T:System.Collections.Generic.ICollection`1"/> is read-only; otherwise, false.</returns>
        public bool IsReadOnly => false;

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
        /// <exception cref="ArgumentException"></exception>
        public ConfiguredEndpoint Add(
            EndpointDescription endpoint,
            EndpointConfiguration configuration)
        {
            ValidateEndpoint(endpoint);

            foreach (ConfiguredEndpoint item in m_endpoints)
            {
                if (ReferenceEquals(item.Description, endpoint))
                {
                    throw new ArgumentException("Endpoint already exists in the collection.");
                }
            }

            var configuredEndpoint = new ConfiguredEndpoint(this, endpoint, configuration);
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
        /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        private void Insert(ConfiguredEndpoint endpoint, int index)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            ValidateEndpoint(endpoint.Description);

            // update collection.
            endpoint.Collection?.Remove(endpoint);

            endpoint.Collection = this;

            if (!ReferenceEquals(endpoint.Collection, this))
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
        /// <exception cref="ArgumentNullException"><paramref name="item"/> is <c>null</c>.</exception>
        public bool Remove(ConfiguredEndpoint item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            return m_endpoints.Remove(item);
        }

        /// <summary>
        /// Removes all endpoints for the specified server.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="serverUri"/> is <c>null</c>.</exception>
        public void RemoveServer(string serverUri)
        {
            if (serverUri == null)
            {
                throw new ArgumentNullException(nameof(serverUri));
            }

            foreach (ConfiguredEndpoint endpointToRemove in GetEndpoints(serverUri))
            {
                Remove(endpointToRemove);
            }
        }

        /// <summary>
        /// Updates the server descrption for the endpoints.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="server"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public void SetApplicationDescription(string serverUri, ApplicationDescription server)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            if (string.IsNullOrEmpty(server.ApplicationUri))
            {
                throw new ArgumentException("A ServerUri must provided.", nameof(server));
            }

            if (server.DiscoveryUrls.Count == 0)
            {
                throw new ArgumentException(
                    "At least one DiscoveryUrl must be provided.",
                    nameof(server));
            }

            List<ConfiguredEndpoint> endpoints = GetEndpoints(server.ApplicationUri);

            // create a placeholder endpoint for the server description.
            if (endpoints.Count == 0)
            {
                string endpointUrl = null;

                for (int ii = 0; ii < server.DiscoveryUrls.Count; ii++)
                {
                    if (!string.IsNullOrEmpty(server.DiscoveryUrls[ii]))
                    {
                        endpointUrl = server.DiscoveryUrls[ii];
                        break;
                    }
                }

                if (endpointUrl != null &&
                    Utils.IsUriHttpRelatedScheme(endpointUrl) &&
                    endpointUrl.EndsWith(
                        ConfiguredEndpoint.DiscoverySuffix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    endpointUrl = endpointUrl[..^ConfiguredEndpoint.DiscoverySuffix.Length];
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
                    endpointToUpdate.Description.Server = (ApplicationDescription)server
                        .MemberwiseClone();
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
                parameters = url[(index + 3)..];
                url = url[..index].Trim();
            }

            MessageSecurityMode securityMode = MessageSecurityMode.SignAndEncrypt;
            string securityPolicyUri = SecurityPolicies.Basic256Sha256;
            bool useBinaryEncoding = true;

            if (!string.IsNullOrEmpty(parameters))
            {
                string[] fields = parameters.Split(
                    s_separator,
                    StringSplitOptions.RemoveEmptyEntries);

                try
                {
                    if (fields.Length > 0)
                    {
#if NET8_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
                        securityMode = Enum.Parse<MessageSecurityMode>(fields[0], false);
#else
                        securityMode = (MessageSecurityMode)Enum.Parse(
                            typeof(MessageSecurityMode),
                            fields[0],
                            false);
#endif
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

            var uri = new Uri(url);

            var description = new EndpointDescription
            {
                EndpointUrl = uri.ToString(),
                SecurityMode = securityMode,
                SecurityPolicyUri = securityPolicyUri
            };
            description.Server.ApplicationUri = Utils.UpdateInstanceUri(uri.ToString());
            description.Server.ApplicationName = uri.AbsolutePath;

            if (description.EndpointUrl.StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
            {
                description.TransportProfileUri = Profiles.UaTcpTransport;
                description.Server.DiscoveryUrls.Add(description.EndpointUrl);
            }
            else if (Utils.IsUriHttpsScheme(description.EndpointUrl))
            {
                description.TransportProfileUri = Profiles.HttpsBinaryTransport;
                description.Server.DiscoveryUrls.Add(description.EndpointUrl);
            }
            else if (description.EndpointUrl
                .StartsWith(Utils.UriSchemeOpcWss, StringComparison.Ordinal))
            {
                description.TransportProfileUri = Profiles.UaTcpTransport;
                description.Server.DiscoveryUrls.Add(description.EndpointUrl);
            }

            var endpoint = new ConfiguredEndpoint(this, description, null);
            endpoint.Configuration.UseBinaryEncoding = useBinaryEncoding;
            endpoint.UpdateBeforeConnect = true;
            return endpoint;
        }

        /// <summary>
        /// Returns the configured endpoints for the server uri.
        /// </summary>
        public List<ConfiguredEndpoint> GetEndpoints(string serverUri)
        {
            var endpoints = new List<ConfiguredEndpoint>();

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
            var servers = new Dictionary<string, ApplicationDescription>();

            foreach (ConfiguredEndpoint endpoint in m_endpoints)
            {
                ApplicationDescription server = endpoint.Description.Server;

                if (!string.IsNullOrEmpty(server.ApplicationUri))
                {
#if NET6_0_OR_GREATER
                    servers.TryAdd(server.ApplicationUri, server);
#else
                    servers.TryAdd(server.ApplicationUri, server);
#endif
                }
            }

            return [.. servers.Values];
        }

        /// <summary>
        /// A list of well known urls that can be used for discovery.
        /// </summary>
        public StringCollection DiscoveryUrls
        {
            get => m_discoveryUrls;
            set => m_discoveryUrls = value ?? [.. Utils.DiscoveryUrls];
        }

        /// <summary>
        /// The default configuration for new ConfiguredEndpoints.
        /// </summary>
        public EndpointConfiguration DefaultConfiguration { get; private set; }

        private static readonly char[] s_separator = ['-', '[', ':', ']'];

        /// <summary>
        /// Throws exceptions if the endpoint is not valid.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="endpoint"/></exception>
        private static void ValidateEndpoint(EndpointDescription endpoint)
        {
            if (endpoint == null)
            {
                throw new ArgumentException("Endpoint must not be null.");
            }

            if (string.IsNullOrEmpty(endpoint.EndpointUrl))
            {
                throw new ArgumentException("Endpoint must have a valid URL.");
            }

            endpoint.Server ??= new ApplicationDescription
            {
                ApplicationType = ApplicationType.Server
            };

            if (string.IsNullOrEmpty(endpoint.Server.ApplicationUri))
            {
                endpoint.Server.ApplicationUri = endpoint.EndpointUrl;
            }
        }
    }

    /// <summary>
    /// Stores the configuration information for an endpoint.
    /// </summary>
    public partial class ConfiguredEndpoint : IFormattable, ICloneable
    {
        /// <summary>
        /// A discovery suffix that may be appended to the discovery url of https endpoints.
        /// </summary>
        public static readonly string DiscoverySuffix = "/discovery";

        /// <summary>
        /// Creates a configured endpoint from the server description.
        /// </summary>
        public ConfiguredEndpoint(
            ApplicationDescription server,
            EndpointConfiguration configuration)
        {
            m_description = new EndpointDescription();
            UpdateBeforeConnect = true;

            m_description.Server = server ?? throw new ArgumentNullException(nameof(server));

            foreach (string discoveryUrl in server.DiscoveryUrls)
            {
                string baseUrl = discoveryUrl;

                if (baseUrl != null &&
                    Utils.IsUriHttpRelatedScheme(baseUrl) &&
                    baseUrl.EndsWith(DiscoverySuffix, StringComparison.Ordinal))
                {
                    baseUrl = baseUrl[..^DiscoverySuffix.Length];
                }

                Uri url = Utils.ParseUri(baseUrl);

                if (url != null)
                {
                    m_description.EndpointUrl = url.ToString();
                    m_description.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                    m_description.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
                    m_description.UserIdentityTokens
                        .Add(new UserTokenPolicy(UserTokenType.Anonymous));

                    if (url.Scheme == Utils.UriSchemeOpcTcp)
                    {
                        m_description.TransportProfileUri = Profiles.UaTcpTransport;
                    }
                    else if (Utils.IsUriHttpsScheme(url.Scheme))
                    {
                        m_description.TransportProfileUri = Profiles.HttpsBinaryTransport;
                    }
                    else if (url.Scheme == Utils.UriSchemeOpcWss)
                    {
                        m_description.TransportProfileUri = Profiles.UaWssTransport;
                    }

                    break;
                }
            }

            // ensure a default configuration.
            configuration ??= EndpointConfiguration.Create();

            Update(configuration);
        }

        /// <summary>
        /// The default constructor.
        /// </summary>
        public ConfiguredEndpoint(
            ConfiguredEndpointCollection collection,
            EndpointDescription description)
            : this(collection, description, null)
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
            m_collection = collection;
            m_description = description ?? throw new ArgumentNullException(nameof(description));
            UpdateBeforeConnect = true;

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

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Returns a deep copy of the endpoint.
        /// </summary>
        public new object MemberwiseClone()
        {
            var clone = new ConfiguredEndpoint { Collection = Collection };
            clone.Update(this);
            return clone;
        }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        public override string ToString()
        {
            return ToString(null, null);
        }

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
                    m_configuration != null && m_configuration.UseBinaryEncoding
                        ? "Binary"
                        : "XML");
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Determine if an update of the endpoint from the server is needed.
        /// </summary>
        public bool NeedUpdateFromServer()
        {
            bool hasCertificate = Description.ServerCertificate != null &&
                Description.ServerCertificate.Length > 0;
            bool usingUserTokenSecurity =
                (SelectedUserTokenPolicy.TokenType != UserTokenType.Anonymous) &&
                (SelectedUserTokenPolicy.SecurityPolicyUri ??
                    SecurityPolicies.None) != SecurityPolicies.None;
            bool usingTransportSecurity = Description.SecurityPolicyUri != SecurityPolicies.None;
            return (usingUserTokenSecurity || usingTransportSecurity) && !hasCertificate;
        }

        /// <summary>
        /// Updates the endpoint description.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <c>null</c>.</exception>
        public void Update(ConfiguredEndpoint endpoint)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            m_description = (EndpointDescription)endpoint.Description.MemberwiseClone();
            m_configuration = (EndpointConfiguration)endpoint.Configuration.MemberwiseClone();

            // normalize transport profile uri.
            if (m_description.TransportProfileUri != null)
            {
                m_description.TransportProfileUri = Profiles.NormalizeUri(
                    m_description.TransportProfileUri);
            }

            UpdateBeforeConnect = endpoint.UpdateBeforeConnect;
            SelectedUserTokenPolicyIndex = endpoint.SelectedUserTokenPolicyIndex;
            BinaryEncodingSupport = endpoint.BinaryEncodingSupport;

            if (endpoint.UserIdentity != null)
            {
                UserIdentity = (UserIdentityToken)endpoint.UserIdentity.MemberwiseClone();
            }
        }

        /// <summary>
        /// Updates the endpoint description.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="description"/> is <c>null</c>.</exception>
        public void Update(EndpointDescription description)
        {
            if (description == null)
            {
                throw new ArgumentNullException(nameof(description));
            }

            m_description = (EndpointDescription)description.MemberwiseClone();

            // normalize transport profile uri.
            if (m_description.TransportProfileUri != null)
            {
                m_description.TransportProfileUri = Profiles.NormalizeUri(
                    m_description.TransportProfileUri);
            }

            // set the proxy url.
            if (m_collection != null &&
                m_description.EndpointUrl != null &&
                m_description.EndpointUrl
                    .StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
            {
                m_description.ProxyUrl = m_collection.TcpProxyUrl;
            }
        }

        /// <summary>
        /// Updates the endpoint configuration.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        public void Update(EndpointConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            m_configuration = (EndpointConfiguration)configuration.MemberwiseClone();

            BinaryEncodingSupport binaryEncodingSupport = m_description.EncodingSupport;

            // check if the configuration restricts the encoding if the endpoint supports both.
            if (binaryEncodingSupport == BinaryEncodingSupport.Optional)
            {
                binaryEncodingSupport = BinaryEncodingSupport;
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
        [Obsolete("Use UpdateFromServerAsync() instead.")]
        public void UpdateFromServer()
        {
            UpdateFromServerAsync()
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Updates an endpoint with information from the server's discovery endpoint.
        /// </summary>
        [Obsolete("Use UpdateFromServerAsync() instead.")]
        public void UpdateFromServer(
            Uri endpointUrl,
            MessageSecurityMode securityMode,
            string securityPolicyUri)
        {
            UpdateFromServerAsync(endpointUrl, securityMode, securityPolicyUri)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Updates an endpoint with information from the server's discovery endpoint.
        /// </summary>
        [Obsolete("Use UpdateFromServerAsync() instead.")]
        public void UpdateFromServer(
            Uri endpointUrl,
            ITransportWaitingConnection connection,
            MessageSecurityMode securityMode,
            string securityPolicyUri)
        {
            UpdateFromServerAsync(endpointUrl, connection, securityMode, securityPolicyUri)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Updates an endpoint with information from the server's discovery endpoint.
        /// </summary>
        public Task UpdateFromServerAsync(CancellationToken ct = default)
        {
            return UpdateFromServerAsync(
                EndpointUrl,
                m_description.SecurityMode,
                m_description.SecurityPolicyUri,
                ct);
        }

        /// <summary>
        /// Updates an endpoint with information from the server's discovery endpoint.
        /// </summary>
        public Task UpdateFromServerAsync(
            Uri endpointUrl,
            MessageSecurityMode securityMode,
            string securityPolicyUri,
            CancellationToken ct = default)
        {
            return UpdateFromServerAsync(endpointUrl, null, securityMode, securityPolicyUri, ct);
        }

        /// <summary>
        /// Updates an endpoint with information from the server's discovery endpoint.
        /// </summary>
        public async Task UpdateFromServerAsync(
            Uri endpointUrl,
            ITransportWaitingConnection connection,
            MessageSecurityMode securityMode,
            string securityPolicyUri,
            CancellationToken ct = default)
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
                EndpointDescriptionCollection collection = await client
                    .GetEndpointsAsync(null, ct)
                    .ConfigureAwait(false);

                // find list of matching endpoints.
                EndpointDescriptionCollection matches = MatchEndpoints(
                    collection,
                    endpointUrl,
                    securityMode,
                    securityPolicyUri);

                // select best match
                EndpointDescription match = SelectBestMatch(matches, discoveryUrl);

                // update the endpoint.
                Update(match);
            }
            finally
            {
                await client.CloseAsync(ct).ConfigureAwait(false);
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
                if (Utils.IsUriHttpRelatedScheme(endpointUrl.Scheme))
                {
                    return new Uri(Utils.Format("{0}{1}", endpointUrl, DiscoverySuffix));
                }

                return endpointUrl;
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

        /// <summary>
        /// The collection that the endpoint belongs to.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public ConfiguredEndpointCollection Collection
        {
            get => m_collection;
            internal set => m_collection = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// The URL used to create a sessions.
        /// </summary>
        public Uri EndpointUrl
        {
            get
            {
                if (string.IsNullOrEmpty(m_description.EndpointUrl))
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

                m_description.EndpointUrl = Utils.Format("{0}", value);
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

                    if (SelectedUserTokenPolicyIndex >= 0 &&
                        policies.Count > SelectedUserTokenPolicyIndex)
                    {
                        return policies[SelectedUserTokenPolicyIndex];
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
                        if (ReferenceEquals(policies[ii], value))
                        {
                            SelectedUserTokenPolicyIndex = ii;
                            break;
                        }
                    }
                }

                SelectedUserTokenPolicyIndex = -1;
            }
        }

        private static EndpointDescriptionCollection MatchEndpoints(
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
            var matches = new EndpointDescriptionCollection();

            // first pass - match on the requested security parameters.
            foreach (EndpointDescription description in collection)
            {
                // check for match on security policy.
                if (!string.IsNullOrEmpty(securityPolicyUri) &&
                    securityPolicyUri != description.SecurityPolicyUri)
                {
                    continue;
                }

                // check for match on security mode.
                if (securityMode != MessageSecurityMode.Invalid &&
                    securityMode != description.SecurityMode)
                {
                    continue;
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
            // first narrows down on scheme, then again on ports
            bool checkWithPorts = false;
            while (matches.Count > 1)
            {
                collection = matches;
                matches = [];

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

                    // check for matching port.
                    if (checkWithPorts && sessionUrl.Port != endpointUrl.Port)
                    {
                        continue;
                    }

                    matches.Add(description);
                }

                if (checkWithPorts)
                {
                    break;
                }

                checkWithPorts = true;
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
        private static EndpointDescription SelectBestMatch(
            EndpointDescriptionCollection matches,
            Uri discoveryUrl)
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
                if (matchUrl == null ||
                    !string.Equals(
                        discoveryUrl.DnsSafeHost,
                        matchUrl.DnsSafeHost,
                        StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new UriBuilder(matchUrl)
                    {
                        Host = discoveryUrl.DnsSafeHost,
                        Port = discoveryUrl.Port
                    };
                    match.EndpointUrl = uri.ToString();

                    // need to update the discovery urls.
                    match.Server.DiscoveryUrls.Clear();
                    match.Server.DiscoveryUrls.Add(discoveryUrl.ToString());
                }
            }

            return match;
        }
    }
}
