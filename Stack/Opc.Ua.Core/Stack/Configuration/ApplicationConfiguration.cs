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
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
	/// Loads the configuration section for an application.
	/// </summary>
	public class ApplicationConfigurationSection
    {
        /// <summary>
        /// Creates the configuration object from the configuration section.
        /// </summary>
        /// <param name="parent">The parent object.</param>
        /// <param name="configContext">The configuration context object.</param>
        /// <param name="section">The section as XML node.</param>
        /// <returns>The created section handler object.</returns>
        public object Create(object parent, object configContext, XmlNode section)
        {
            if (section == null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            XmlNode element = section.FirstChild;

            while (element != null && typeof(XmlElement) != element.GetType())
            {
                element = element.NextSibling;
            }

            using var reader = XmlReader.Create(new StringReader(element.OuterXml), Utils.DefaultXmlReaderSettings());
            var serializer = new DataContractSerializer(typeof(ConfigurationLocation));
            return serializer.ReadObject(reader) as ConfigurationLocation;
        }
    }

    /// <summary>
    /// Represents the location of a configuration file.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class ConfigurationLocation
    {
        /// <summary>
        /// Gets or sets the relative or absolute path to the configuration file.
        /// </summary>
        /// <value>The file path.</value>
        [DataMember(IsRequired = true, Order = 0)]
        public string FilePath { get; set; }
    }

    /// <summary>
    /// Stores the configurable configuration information for a UA application.
    /// </summary>
    public partial class ApplicationConfiguration
    {
        /// <summary>
        /// Gets the file that was used to load the configuration.
        /// </summary>
        /// <value>The source file path.</value>
        public string SourceFilePath { get; private set; }

        /// <summary>
        /// Gets or sets the certificate validator which is configured to use.
        /// </summary>
        public CertificateValidator CertificateValidator { get; set; }

        /// <summary>
        /// Returns the domain names which the server is configured to use.
        /// </summary>
        /// <returns>A list of domain names.</returns>
        public IList<string> GetServerDomainNames()
        {
            var baseAddresses = new StringCollection();

            if (ServerConfiguration != null)
            {
                if (ServerConfiguration.BaseAddresses != null)
                {
                    baseAddresses.AddRange(ServerConfiguration.BaseAddresses);
                }

                if (ServerConfiguration.AlternateBaseAddresses != null)
                {
                    baseAddresses.AddRange(ServerConfiguration.AlternateBaseAddresses);
                }
            }

            if (DiscoveryServerConfiguration != null)
            {
                if (DiscoveryServerConfiguration.BaseAddresses != null)
                {
                    baseAddresses.AddRange(DiscoveryServerConfiguration.BaseAddresses);
                }

                if (DiscoveryServerConfiguration.AlternateBaseAddresses != null)
                {
                    baseAddresses.AddRange(DiscoveryServerConfiguration.AlternateBaseAddresses);
                }
            }

            var domainNames = new List<string>();
            for (int ii = 0; ii < baseAddresses.Count; ii++)
            {
                Uri url = Utils.ParseUri(baseAddresses[ii]);

                if (url == null)
                {
                    continue;
                }

                string domainName = url.DnsSafeHost;

                if (url.HostNameType == UriHostNameType.Dns)
                {
                    domainName = Utils.ReplaceLocalhost(domainName);
                }
                else // IPv4/IPv6 address
                {
                    domainName = Utils.NormalizedIPAddress(domainName);
                }

                if (!Utils.FindStringIgnoreCase(domainNames, domainName))
                {
                    domainNames.Add(domainName);
                }
            }

            return domainNames;
        }

        /// <summary>
        /// Creates the message context from the configuration.
        /// </summary>
        /// <returns>A new instance of a ServiceMessageContext object.</returns>
        public ServiceMessageContext CreateMessageContext(bool clonedFactory = false)
        {
            var messageContext = new ServiceMessageContext();

            if (TransportQuotas != null)
            {
                messageContext.MaxArrayLength = TransportQuotas.MaxArrayLength;
                messageContext.MaxByteStringLength = TransportQuotas.MaxByteStringLength;
                messageContext.MaxStringLength = TransportQuotas.MaxStringLength;
                messageContext.MaxMessageSize = TransportQuotas.MaxMessageSize;
                messageContext.MaxEncodingNestingLevels = TransportQuotas.MaxEncodingNestingLevels;
                messageContext.MaxDecoderRecoveries = TransportQuotas.MaxDecoderRecoveries;
            }

            messageContext.NamespaceUris = new NamespaceTable();
            messageContext.ServerUris = new StringTable();
            if (clonedFactory)
            {
                messageContext.Factory = new EncodeableFactory(EncodeableFactory.GlobalFactory);
            }
            return messageContext;
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="sectionName">Name of configuration section for the current application's default configuration containing <see cref="ConfigurationLocation"/>.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <returns>Application configuration</returns>
        [Obsolete("Use LoadAsync instead.")]
        public static Task<ApplicationConfiguration> Load(string sectionName, ApplicationType applicationType)
        {
            return LoadAsync(sectionName, applicationType);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="sectionName">Name of configuration section for the current application's default configuration containing <see cref="ConfigurationLocation"/>.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <returns>Application configuration</returns>
        public static Task<ApplicationConfiguration> LoadAsync(string sectionName, ApplicationType applicationType)
        {
            return LoadAsync(sectionName, applicationType, typeof(ApplicationConfiguration));
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="sectionName">Name of configuration section for the current application's default configuration containing <see cref="ConfigurationLocation"/>.</param>
        /// <param name="applicationType">A description for the ApplicationType DataType.</param>
        /// <param name="systemType">A user type of the configuration instance.</param>
        /// <returns>Application configuration</returns>
        [Obsolete("Use LoadAsync instead.")]
        public static Task<ApplicationConfiguration> Load(string sectionName, ApplicationType applicationType, Type systemType)
        {
            return LoadAsync(sectionName, applicationType, systemType);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="sectionName">Name of configuration section for the current application's default configuration containing <see cref="ConfigurationLocation"/>.</param>
        /// <param name="applicationType">A description for the ApplicationType DataType.</param>
        /// <param name="systemType">A user type of the configuration instance.</param>
        /// <returns>Application configuration</returns>
        public static Task<ApplicationConfiguration> LoadAsync(string sectionName, ApplicationType applicationType, Type systemType)
        {
            string filePath = GetFilePathFromAppConfig(sectionName);

            var file = new FileInfo(filePath);

            if (!file.Exists)
            {
                var message = new StringBuilder();
                message.AppendFormat(CultureInfo.InvariantCulture, "Configuration file does not exist: {0}", filePath);
                message.AppendLine();
                message.AppendFormat(CultureInfo.InvariantCulture, "Current directory is: {0}", Directory.GetCurrentDirectory());
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, message.ToString());
            }

            return LoadAsync(file, applicationType, systemType);
        }

        /// <summary>
        /// Loads but does not validate the application configuration from a configuration section.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="systemType">Type of the system.</param>
        /// <returns>Application configuration</returns>
        /// <remarks>Use this method to ensure the configuration is not changed during loading.</remarks>
        public static ApplicationConfiguration LoadWithNoValidation(FileInfo file, Type systemType)
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read);
            try
            {
                var serializer = new DataContractSerializer(systemType);

                var configuration = serializer.ReadObject(stream) as ApplicationConfiguration;

                if (configuration != null)
                {
                    configuration.SourceFilePath = file.FullName;
                }

                return configuration;
            }
            catch (Exception e)
            {
                var message = new StringBuilder();
                message.AppendFormat(CultureInfo.InvariantCulture, "Configuration file could not be loaded: {0}", file.FullName);
                message.AppendLine();
                message.AppendFormat(CultureInfo.InvariantCulture, "Error is: {0}", e.Message);
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, e, message.ToString());
            }
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <param name="systemType">Type of the system.</param>
        /// <returns>Application configuration</returns>
        [Obsolete("Use LoadAsync instead.")]
        public static Task<ApplicationConfiguration> Load(FileInfo file, ApplicationType applicationType, Type systemType)
        {
            return LoadAsync(file, applicationType, systemType);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <param name="systemType">Type of the system.</param>
        /// <returns>Application configuration</returns>
        public static Task<ApplicationConfiguration> LoadAsync(FileInfo file, ApplicationType applicationType, Type systemType)
        {
            return LoadAsync(file, applicationType, systemType, true);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <param name="systemType">Type of the system.</param>
        /// <param name="applyTraceSettings">if set to <c>true</c> apply trace settings after validation.</param>
        /// <param name="certificatePasswordProvider">The certificate password provider.</param>
        /// <returns>Application configuration</returns>
        [Obsolete("Use LoadAsync instead.")]
        public static Task<ApplicationConfiguration> Load(
            FileInfo file,
            ApplicationType applicationType,
            Type systemType,
            bool applyTraceSettings,
            ICertificatePasswordProvider certificatePasswordProvider = null)
        {
            return LoadAsync(file, applicationType, systemType, applyTraceSettings, certificatePasswordProvider);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <param name="systemType">Type of the system.</param>
        /// <param name="applyTraceSettings">if set to <c>true</c> apply trace settings after validation.</param>
        /// <param name="certificatePasswordProvider">The certificate password provider.</param>
        /// <returns>Application configuration</returns>
        public static async Task<ApplicationConfiguration> LoadAsync(
            FileInfo file,
            ApplicationType applicationType,
            Type systemType,
            bool applyTraceSettings,
            ICertificatePasswordProvider certificatePasswordProvider = null)
        {
            ApplicationConfiguration configuration = null;

            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read);
                configuration = await LoadAsync(stream, applicationType, systemType, applyTraceSettings, certificatePasswordProvider).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                var message = new StringBuilder();
                message.AppendFormat(CultureInfo.InvariantCulture, "Configuration file could not be loaded: {0}", file.FullName);
                message.AppendLine();
                message.Append(e.Message);
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, e, message.ToString());
            }

            if (configuration != null)
            {
                configuration.SourceFilePath = file.FullName;
            }

            return configuration;
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <param name="systemType">Type of the system.</param>
        /// <param name="applyTraceSettings">if set to <c>true</c> apply trace settings after validation.</param>
        /// <param name="certificatePasswordProvider">The certificate password provider.</param>
        /// <returns>Application configuration</returns>
        [Obsolete("Use LoadAsync instead.")]
        public static Task<ApplicationConfiguration> Load(
            Stream stream,
            ApplicationType applicationType,
            Type systemType,
            bool applyTraceSettings,
            ICertificatePasswordProvider certificatePasswordProvider = null)
        {
            return LoadAsync(stream, applicationType, systemType, applyTraceSettings, certificatePasswordProvider);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <param name="systemType">Type of the system.</param>
        /// <param name="applyTraceSettings">if set to <c>true</c> apply trace settings after validation.</param>
        /// <param name="certificatePasswordProvider">The certificate password provider.</param>
        /// <returns>Application configuration</returns>
        public static async Task<ApplicationConfiguration> LoadAsync(
            Stream stream,
            ApplicationType applicationType,
            Type systemType,
            bool applyTraceSettings,
            ICertificatePasswordProvider certificatePasswordProvider = null)
        {
            systemType ??= typeof(ApplicationConfiguration);

            ApplicationConfiguration configuration;
            try
            {
                var serializer = new DataContractSerializer(systemType);
                configuration = (ApplicationConfiguration)serializer.ReadObject(stream);
            }
            catch (Exception e)
            {
                var message = new StringBuilder();
                message.AppendFormat(CultureInfo.InvariantCulture, "Configuration could not be loaded.");
                message.AppendLine();
                message.AppendFormat(CultureInfo.InvariantCulture, "Error is: {0}", e.Message);
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, e, message.ToString());
            }

            if (configuration != null)
            {
                // should not be here but need to preserve old behavior.
                if (applyTraceSettings && configuration.TraceConfiguration != null)
                {
                    configuration.TraceConfiguration.ApplySettings();
                }

                configuration.SecurityConfiguration.CertificatePasswordProvider = certificatePasswordProvider;

                await configuration.ValidateAsync(applicationType).ConfigureAwait(false);
            }

            return configuration;
        }

        /// <summary>
        /// Reads the file path from the application configuration file.
        /// </summary>
	    /// <param name="sectionName">Name of configuration section for the current application's default configuration containing <see cref="ConfigurationLocation"/>.
	    /// </param>
        /// <returns>File path from the application configuration file.</returns>
        public static string GetFilePathFromAppConfig(string sectionName)
        {
            // convert to absolute file path (expands environment strings).
            string absolutePath = Utils.GetAbsoluteFilePath(sectionName + ".Config.xml", true, false, false);
            return absolutePath ?? sectionName + ".Config.xml";
        }

        /// <summary>
        /// Saves the configuration file.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        /// <remarks>Calls GetType() on the current instance and passes that to the DataContractSerializer.</remarks>
        public void SaveToFile(string filePath)
        {
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
            settings.CloseOutput = true;

            using Stream ostrm = File.Open(filePath, FileMode.Create, FileAccess.ReadWrite);
            using var writer = XmlWriter.Create(ostrm, settings);
            var serializer = new DataContractSerializer(GetType());
            serializer.WriteObject(writer, this);
        }

        /// <summary>
        /// Ensures that the application configuration is valid.
        /// </summary>
        /// <param name="applicationType">Type of the application.</param>
        [Obsolete("Use ValidateAsync instead.")]
        public virtual Task Validate(ApplicationType applicationType)
        {
            return ValidateAsync(applicationType);
        }

        /// <summary>
        /// Ensures that the application configuration is valid.
        /// </summary>
        /// <param name="applicationType">Type of the application.</param>
        public virtual async Task ValidateAsync(ApplicationType applicationType)
        {
            if (string.IsNullOrEmpty(ApplicationName))
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "ApplicationName must be specified.");
            }

            if (SecurityConfiguration == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "SecurityConfiguration must be specified.");
            }

            SecurityConfiguration.Validate();

            // load private keys
            foreach (CertificateIdentifier applicationCertificate in SecurityConfiguration.ApplicationCertificates)
            {
                await applicationCertificate.LoadPrivateKeyExAsync(SecurityConfiguration.CertificatePasswordProvider, ApplicationUri).ConfigureAwait(false);
            }

            string GenerateDefaultUri()
            {
                var sb = new StringBuilder();
                sb.Append("urn:");
                sb.Append(Utils.GetHostName());
                sb.Append(':');
                sb.Append(ApplicationName);
                return sb.ToString();
            }

            if (string.IsNullOrEmpty(ApplicationUri))
            {
                ApplicationUri = GenerateDefaultUri();
            }

            if (applicationType is ApplicationType.Client or ApplicationType.ClientAndServer)
            {
                if (ClientConfiguration == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "ClientConfiguration must be specified.");
                }

                ClientConfiguration.Validate();
            }

            if (applicationType is ApplicationType.Server or ApplicationType.ClientAndServer)
            {
                if (ServerConfiguration == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "ServerConfiguration must be specified.");
                }

                ServerConfiguration.Validate();
            }

            if (applicationType == ApplicationType.DiscoveryServer)
            {
                if (DiscoveryServerConfiguration == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "DiscoveryServerConfiguration must be specified.");
                }

                DiscoveryServerConfiguration.Validate();
            }

            // toggle the state of the hi-res clock.
            HiResClock.Disabled = DisableHiResClock;

            if (HiResClock.Disabled && ServerConfiguration != null && ServerConfiguration.PublishingResolution < 50)
            {
                ServerConfiguration.PublishingResolution = 50;
            }

            await CertificateValidator.UpdateAsync(SecurityConfiguration).ConfigureAwait(false);
        }

        /// <summary>
        /// Loads the endpoints cached on disk.
        /// </summary>
        /// <param name="createAlways">if set to <c>true</c> ConfiguredEndpointCollection is always returned,
        ///	even if loading from disk fails</param>
        /// <returns>Collection of configured endpoints from the disk.</returns>
        public ConfiguredEndpointCollection LoadCachedEndpoints(bool createAlways)
        {
            return LoadCachedEndpoints(createAlways, false);
        }

        /// <summary>
        /// Loads the endpoints cached on disk.
        /// </summary>
        /// <param name="createAlways">if set to <c>true</c> ConfiguredEndpointCollection is always returned,
        /// even if loading from disk fails</param>
        /// <param name="overrideConfiguration">if set to <c>true</c> overrides the configuration.</param>
        /// <returns>
        /// Collection of configured endpoints from the disk.
        /// </returns>
        public ConfiguredEndpointCollection LoadCachedEndpoints(bool createAlways, bool overrideConfiguration)
        {
            if (ClientConfiguration == null)
            {
                throw new InvalidOperationException("Only valid for client configurations.");
            }

            string filePath = Utils.GetAbsoluteFilePath(ClientConfiguration.EndpointCacheFilePath, true, false, false, false);

            if (filePath == null)
            {
                filePath = ClientConfiguration.EndpointCacheFilePath;

                if (!Utils.IsPathRooted(filePath))
                {
                    var sourceFile = new FileInfo(SourceFilePath);
                    filePath = Utils.Format("{0}{1}{2}", sourceFile.DirectoryName, Path.DirectorySeparatorChar, filePath);
                }
            }

            if (!createAlways)
            {
                return ConfiguredEndpointCollection.Load(this, filePath, overrideConfiguration);
            }

            var endpoints = new ConfiguredEndpointCollection(this);
            try
            {
                endpoints = ConfiguredEndpointCollection.Load(this, filePath, overrideConfiguration);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Could not load configuration from file: {0}", filePath);
            }
            finally
            {
                string localFilePath = Utils.GetAbsoluteFilePath(ClientConfiguration.EndpointCacheFilePath, true, false, true, true);
                if (localFilePath != filePath)
                {
                    endpoints.Save(localFilePath);
                }
            }

            return endpoints;
        }

        /// <summary>
        /// Looks for an extension with the specified type and uses the DataContractSerializer to parse it.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>
        /// The deserialized extension. Null if an error occurs.
        /// </returns>
        /// <remarks>
        /// The containing element must use the name and namespace uri specified by the DataContractAttribute for the type.
        /// </remarks>
        public T ParseExtension<T>()
        {
            return ParseExtension<T>(null);
        }

        /// <summary>
        /// Looks for an extension with the specified type and uses the DataContractSerializer to parse it.
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
    }

    /// <summary>
    /// Specifies parameters used for tracing.
    /// </summary>
    public partial class TraceConfiguration
    {
        /// <summary>
        /// Applies the trace settings to the current process.
        /// </summary>
        public void ApplySettings()
        {
            Utils.SetTraceLog(OutputFilePath, DeleteOnLoad);
            Utils.SetTraceMask(TraceMasks);

            if (TraceMasks == 0)
            {
                Utils.SetTraceOutput(Utils.TraceOutput.Off);
            }
            else
            {
                Utils.SetTraceOutput(Utils.TraceOutput.DebugAndFile);
            }
        }
    }

    /// <summary>
    /// Specifies the configuration for a server application.
    /// </summary>
    public partial class ServerBaseConfiguration
    {
        /// <summary>
        /// Validates the configuration.
        /// </summary>
        public virtual void Validate()
        {
            if (m_securityPolicies.Count == 0)
            {
                m_securityPolicies.Add(new ServerSecurityPolicy());
            }
        }
    }

    /// <summary>
    /// Specifies the configuration for a server application.
    /// </summary>
    public partial class ServerConfiguration : ServerBaseConfiguration
    {
        /// <summary>
        /// Validates the configuration.
        /// </summary>
        public override void Validate()
        {
            base.Validate();

            if (m_userTokenPolicies.Count == 0)
            {
                m_userTokenPolicies.Add(new UserTokenPolicy());
            }
        }
    }

    /// <summary>
    /// The configuration for a client application.
    /// </summary>
    public partial class ClientConfiguration
    {
        /// <summary>
        /// Validates the configuration.
        /// </summary>
        public void Validate()
        {
            if (WellKnownDiscoveryUrls.Count == 0)
            {
                WellKnownDiscoveryUrls.AddRange(Utils.DiscoveryUrls);
            }
        }
    }
}
