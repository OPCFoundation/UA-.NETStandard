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
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Loads the configuration section for an application.
    /// </summary>
    public class ApplicationConfigurationSection : IConfigurationSectionHandler
    {
        /// <summary>
        /// Creates the configuration object from the configuration section.
        /// </summary>
        /// <param name="parent">The parent object.</param>
        /// <param name="configContext">The configuration context object.</param>
        /// <param name="section">The section as XML node.</param>
        /// <returns>The created section handler object.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="section"/> is <c>null</c>.</exception>
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

            using var reader = XmlReader.Create(
                new StringReader(element.OuterXml),
                Utils.DefaultXmlReaderSettings());
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

                string domainName = url.IdnHost;

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
        /// <param name="clonedFactory">This parameter is obsolete and ignored. A new factory instance is always created.</param>
        /// <returns>A new instance of a ServiceMessageContext object.</returns>
        [Obsolete("Use CreateMessageContext() without parameters or CreateMessageContext(IEncodeableFactory) instead.")]
        public ServiceMessageContext CreateMessageContext(bool clonedFactory)
        {
            return CreateMessageContext((IEncodeableFactory)null);
        }

        /// <summary>
        /// Creates the message context from the configuration.
        /// </summary>
        /// <returns>A new instance of a ServiceMessageContext object with a new encodeable factory.</returns>
        public ServiceMessageContext CreateMessageContext()
        {
            return CreateMessageContext((IEncodeableFactory)null);
        }

        /// <summary>
        /// Creates the message context from the configuration with a private encodeable factory.
        /// </summary>
        /// <param name="factory">The private encodeable factory to use. If null, a new factory will be created.</param>
        /// <returns>A new instance of a ServiceMessageContext object.</returns>
        public ServiceMessageContext CreateMessageContext(IEncodeableFactory factory)
        {
            var messageContext = new ServiceMessageContext(m_telemetry, factory);

            if (TransportQuotas != null)
            {
                messageContext.MaxArrayLength = TransportQuotas.MaxArrayLength;
                messageContext.MaxByteStringLength = TransportQuotas.MaxByteStringLength;
                messageContext.MaxStringLength = TransportQuotas.MaxStringLength;
                messageContext.MaxMessageSize = TransportQuotas.MaxMessageSize;
                messageContext.MaxEncodingNestingLevels = TransportQuotas.MaxEncodingNestingLevels;
                messageContext.MaxDecoderRecoveries = TransportQuotas.MaxDecoderRecoveries;
            }

            return messageContext;
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="sectionName">Name of configuration section for the current application's
        /// default configuration containing <see cref="ConfigurationLocation"/>.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <returns>Application configuration</returns>
        [Obsolete("Use LoadAsync instead.")]
        public static Task<ApplicationConfiguration> Load(
            string sectionName,
            ApplicationType applicationType)
        {
            return LoadAsync(sectionName, applicationType, LoggerUtils.Null.Logger, null);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="sectionName">Name of configuration section for the current application's
        /// default configuration containing <see cref="ConfigurationLocation"/>.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <param name="logger">A contextual logger to log to</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="ct"></param>
        /// <returns>Application configuration</returns>
        public static Task<ApplicationConfiguration> LoadAsync(
            string sectionName,
            ApplicationType applicationType,
            ILogger logger,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            return LoadAsync(
                sectionName,
                applicationType,
                typeof(ApplicationConfiguration),
                logger,
                telemetry,
                ct);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="sectionName">Name of configuration section for the current application's
        /// default configuration containing <see cref="ConfigurationLocation"/>.</param>
        /// <param name="applicationType">A description for the ApplicationType DataType.</param>
        /// <param name="systemType">A user type of the configuration instance.</param>
        /// <returns>Application configuration</returns>
        [Obsolete("Use LoadAsync instead.")]
        public static Task<ApplicationConfiguration> Load(
            string sectionName,
            ApplicationType applicationType,
            Type systemType)
        {
            return LoadAsync(sectionName, applicationType, systemType, LoggerUtils.Null.Logger, null);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="sectionName">Name of configuration section for the current application's
        /// default configuration containing <see cref="ConfigurationLocation"/>.</param>
        /// <param name="applicationType">A description for the ApplicationType DataType.</param>
        /// <param name="systemType">A user type of the configuration instance.</param>
        /// <param name="logger">A contextual logger to log to</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="ct"></param>
        /// <returns>Application configuration</returns>
        /// <exception cref="ServiceResultException"></exception>
        public static Task<ApplicationConfiguration> LoadAsync(
            string sectionName,
            ApplicationType applicationType,
            Type systemType,
            ILogger logger,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            string filePath = GetFilePathFromAppConfig(sectionName, logger);

            var file = new FileInfo(filePath);

            if (!file.Exists)
            {
                var message = new StringBuilder();
                message.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "Configuration file does not exist: {0}",
                    filePath)
                    .AppendLine()
                    .AppendFormat(
                    CultureInfo.InvariantCulture,
                    "Current directory is: {0}",
                    Directory.GetCurrentDirectory());
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    message.ToString());
            }

            return LoadAsync(file, applicationType, systemType, telemetry, ct);
        }

        /// <summary>
        /// Loads but does not validate the application configuration from a configuration section.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="systemType">Type of the system.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <returns>Application configuration</returns>
        /// <remarks>Use this method to ensure the configuration is not changed during loading.</remarks>
        /// <exception cref="ServiceResultException"></exception>
        public static ApplicationConfiguration LoadWithNoValidation(
            FileInfo file,
            Type systemType,
            ITelemetryContext telemetry)
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read);
            try
            {
                var serializer = new DataContractSerializer(systemType);

                using IDisposable scope = AmbientMessageContext.SetScopedContext(telemetry);
                var configuration = serializer.ReadObject(stream) as ApplicationConfiguration;
                configuration.Initialize(telemetry);

                if (configuration != null)
                {
                    configuration.SourceFilePath = file.FullName;
                }

                return configuration;
            }
            catch (Exception e)
            {
                var message = new StringBuilder();
                message.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "Configuration file could not be loaded: {0}",
                    file.FullName)
                    .AppendLine()
                    .AppendFormat(CultureInfo.InvariantCulture, "Error is: {0}", e.Message);
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    e,
                    message.ToString());
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
        public static Task<ApplicationConfiguration> Load(
            FileInfo file,
            ApplicationType applicationType,
            Type systemType)
        {
            return LoadAsync(file, applicationType, systemType, null);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <param name="systemType">Type of the system.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="ct"></param>
        /// <returns>Application configuration</returns>
        public static Task<ApplicationConfiguration> LoadAsync(
            FileInfo file,
            ApplicationType applicationType,
            Type systemType,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            return LoadAsync(file, applicationType, systemType, true, telemetry, ct: ct);
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
            return LoadAsync(
                file,
                applicationType,
                systemType,
                applyTraceSettings,
                null,
                certificatePasswordProvider);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <param name="systemType">Type of the system.</param>
        /// <param name="applyTraceSettings">if set to <c>true</c> apply trace settings after validation.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="certificatePasswordProvider">The certificate password provider.</param>
        /// <param name="ct">Cancellation token to cancel action</param>
        /// <returns>Application configuration</returns>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<ApplicationConfiguration> LoadAsync(
            FileInfo file,
            ApplicationType applicationType,
            Type systemType,
            bool applyTraceSettings,
            ITelemetryContext telemetry,
            ICertificatePasswordProvider certificatePasswordProvider = null,
            CancellationToken ct = default)
        {
            ApplicationConfiguration configuration = null;

            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read);
                configuration = await LoadAsync(
                    stream,
                    applicationType,
                    systemType,
                    applyTraceSettings,
                    telemetry,
                    certificatePasswordProvider,
                    ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                var message = new StringBuilder();
                message.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "Configuration file could not be loaded: {0}",
                    file.FullName)
                    .AppendLine()
                    .Append(e.Message);
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    e,
                    message.ToString());
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
            return LoadAsync(
                stream,
                applicationType,
                systemType,
                applyTraceSettings,
                null,
                certificatePasswordProvider);
        }

        /// <summary>
        /// Loads and validates the application configuration from a configuration section.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="applicationType">Type of the application.</param>
        /// <param name="systemType">Type of the system.</param>
        /// <param name="applyTraceSettings">if set to <c>true</c> apply trace settings after validation.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="certificatePasswordProvider">The certificate password provider.</param>
        /// <param name="ct">Cancellation token to cancel action</param>
        /// <returns>Application configuration</returns>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<ApplicationConfiguration> LoadAsync(
            Stream stream,
            ApplicationType applicationType,
            Type systemType,
            bool applyTraceSettings,
            ITelemetryContext telemetry,
            ICertificatePasswordProvider certificatePasswordProvider = null,
            CancellationToken ct = default)
        {
            systemType ??= typeof(ApplicationConfiguration);

            ApplicationConfiguration configuration;
            try
            {
                var serializer = new DataContractSerializer(systemType);
                using IDisposable scope = AmbientMessageContext.SetScopedContext(telemetry);
                configuration = (ApplicationConfiguration)serializer.ReadObject(stream);
                configuration.Initialize(telemetry);
            }
            catch (Exception e)
            {
                var message = new StringBuilder();
                message.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "Configuration could not be loaded.")
                    .AppendLine()
                    .AppendFormat(CultureInfo.InvariantCulture, "Error is: {0}", e.Message);
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    e,
                    message.ToString());
            }

            if (configuration != null)
            {
                // should not be here but need to preserve old behavior.
                if (applyTraceSettings && configuration.TraceConfiguration != null)
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    configuration.TraceConfiguration.ApplySettings();
#pragma warning restore CS0618 // Type or member is obsolete
                }

                configuration.SecurityConfiguration.CertificatePasswordProvider
                    = certificatePasswordProvider;

                await configuration.ValidateAsync(applicationType, ct).ConfigureAwait(false);
            }

            return configuration;
        }

        /// <summary>
        /// Reads the file path from the application configuration file.
        /// </summary>
        /// <param name="sectionName">Name of configuration section for the current application's default configuration containing <see cref="ConfigurationLocation"/>.
        /// </param>
        /// <param name="logger">A contextual logger to log to</param>
        /// <returns>File path from the application configuration file.</returns>
        public static string GetFilePathFromAppConfig(string sectionName, ILogger logger)
        {
            // convert to absolute file path (expands environment strings).
            try
            {
                string absolutePath = Utils.GetAbsoluteFilePath(
                    sectionName + ".Config.xml",
                    checkCurrentDirectory: true,
                    createAlways: false);
                return absolutePath ?? sectionName + ".Config.xml";
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not get file path from app config - returning: {SectionName}.Config.xml", sectionName);
                return sectionName + ".Config.xml";
            }
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
            using IDisposable scope = AmbientMessageContext.SetScopedContext(m_telemetry);
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
        /// <param name="ct">Cancellation token to cancel action</param>
        /// <exception cref="ServiceResultException"></exception>
        public virtual async Task ValidateAsync(
            ApplicationType applicationType,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(ApplicationName))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "ApplicationName must be specified.");
            }

            if (SecurityConfiguration == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "SecurityConfiguration must be specified.");
            }

            SecurityConfiguration.Validate(m_telemetry);

            // load private keys
            foreach (CertificateIdentifier applicationCertificate in SecurityConfiguration
                .ApplicationCertificates)
            {
                await applicationCertificate
                    .LoadPrivateKeyExAsync(
                        SecurityConfiguration.CertificatePasswordProvider,
                        ApplicationUri,
                        m_telemetry,
                        ct)
                    .ConfigureAwait(false);
            }

            string GenerateDefaultUri()
            {
                var sb = new StringBuilder();
                sb.Append("urn:")
                    .Append(Utils.GetHostName())
                    .Append(':')
                    .Append(ApplicationName);
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
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "ClientConfiguration must be specified.");
                }

                ClientConfiguration.Validate();
            }

            if (applicationType is ApplicationType.Server or ApplicationType.ClientAndServer)
            {
                if (ServerConfiguration == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "ServerConfiguration must be specified.");
                }

                ServerConfiguration.Validate();
            }

            if (applicationType == ApplicationType.DiscoveryServer)
            {
                if (DiscoveryServerConfiguration == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "DiscoveryServerConfiguration must be specified.");
                }

                DiscoveryServerConfiguration.Validate();
            }

            // toggle the state of the hi-res clock.
            HiResClock.Disabled = DisableHiResClock;

            if (HiResClock.Disabled &&
                ServerConfiguration != null &&
                ServerConfiguration.PublishingResolution < 50)
            {
                ServerConfiguration.PublishingResolution = 50;
            }

            await CertificateValidator.UpdateAsync(
                SecurityConfiguration,
                applicationUri: null,
                ct)
                .ConfigureAwait(false);
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
        /// <exception cref="InvalidOperationException"></exception>
        public ConfiguredEndpointCollection LoadCachedEndpoints(
            bool createAlways,
            bool overrideConfiguration)
        {
            if (ClientConfiguration == null)
            {
                throw new InvalidOperationException("Only valid for client configurations.");
            }

            string filePath;
            try
            {
                filePath = Utils.GetAbsoluteFilePath(
                    ClientConfiguration.EndpointCacheFilePath,
                    checkCurrentDirectory: true,
                    createAlways: false,
                    writable: false);
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Could not get file path {FilePath}",
                    ClientConfiguration.EndpointCacheFilePath);
                filePath = null;
            }

            if (filePath == null)
            {
                filePath = ClientConfiguration.EndpointCacheFilePath;

                if (!Utils.IsPathRooted(filePath))
                {
                    var sourceFile = new FileInfo(SourceFilePath);
                    filePath = Utils.Format(
                        "{0}{1}{2}",
                        sourceFile.DirectoryName,
                        Path.DirectorySeparatorChar,
                        filePath);
                }
            }

            if (!createAlways)
            {
                return ConfiguredEndpointCollection.Load(
                    this,
                    filePath,
                    overrideConfiguration,
                    m_telemetry);
            }

            var endpoints = new ConfiguredEndpointCollection(this);
            try
            {
                endpoints = ConfiguredEndpointCollection.Load(
                    this,
                    filePath,
                    overrideConfiguration,
                    m_telemetry);
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Could not load configuration from file: {FilePath}", filePath);
            }
            finally
            {
                try
                {
                    string localFilePath = Utils.GetAbsoluteFilePath(
                        ClientConfiguration.EndpointCacheFilePath,
                        checkCurrentDirectory: true,
                        createAlways: true,
                        writable: true);
                    if (localFilePath != filePath)
                    {
                        endpoints.Save(localFilePath);
                    }
                }
                catch (Exception e2)
                {
                    m_logger.LogError(e2, "Could not save configuration to file: {FilePath}",
                        ClientConfiguration.EndpointCacheFilePath);
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
            return Utils.ParseExtension<T>(m_extensions, elementName, m_telemetry);
        }

        /// <summary>
        /// Updates the extension.
        /// </summary>
        /// <typeparam name="T">The type of extension.</typeparam>
        /// <param name="elementName">Name of the element (null means use type name).</param>
        /// <param name="value">The value.</param>
        public void UpdateExtension<T>(XmlQualifiedName elementName, object value)
        {
            Utils.UpdateExtension<T>(ref m_extensions, elementName, value, m_telemetry);
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
