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
using System.Diagnostics.CodeAnalysis;
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
    public class ApplicationConfigurationSection
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

            XmlNode? element = section.FirstChild;

            while (element != null && typeof(XmlElement) != element.GetType())
            {
                element = element.NextSibling;
            }

            using var parser = new XmlParser(
                typeof(ConfigurationLocation),
                element!.OuterXml,
                ServiceMessageContext.CreateEmpty(null!));
            return new ConfigurationLocation { FilePath = parser.ReadString("FilePath") };
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
        public string? FilePath { get; set; }
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
        public string? SourceFilePath { get; private set; }

        /// <summary>
        /// Gets or sets the certificate validator which is configured to use.
        /// </summary>
        public CertificateValidator? CertificateValidator { get; set; }

        /// <summary>
        /// Returns the domain names which the server is configured to use.
        /// </summary>
        /// <returns>A list of domain names.</returns>
        public ArrayOf<string> GetServerDomainNames()
        {
            var baseAddresses = new List<string>();

            if (ServerConfiguration != null)
            {
                if (!ServerConfiguration.BaseAddresses.IsEmpty)
                {
                    baseAddresses.AddRange(ServerConfiguration.BaseAddresses);
                }

                if (!ServerConfiguration.AlternateBaseAddresses.IsEmpty)
                {
                    baseAddresses.AddRange(ServerConfiguration.AlternateBaseAddresses);
                }
            }

            if (DiscoveryServerConfiguration != null)
            {
                if (!DiscoveryServerConfiguration.BaseAddresses.IsEmpty)
                {
                    baseAddresses.AddRange(DiscoveryServerConfiguration.BaseAddresses);
                }

                if (!DiscoveryServerConfiguration.AlternateBaseAddresses.IsEmpty)
                {
                    baseAddresses.AddRange(DiscoveryServerConfiguration.AlternateBaseAddresses);
                }
            }

            var domainNames = new List<string>();
            for (int ii = 0; ii < baseAddresses.Count; ii++)
            {
                Uri? url = Utils.ParseUri(baseAddresses[ii]);

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
            return CreateMessageContext(null);
        }

        /// <summary>
        /// Creates the message context from the configuration.
        /// </summary>
        /// <returns>A new instance of a ServiceMessageContext object with a new encodeable factory.</returns>
        public ServiceMessageContext CreateMessageContext()
        {
            return CreateMessageContext(null);
        }

        /// <summary>
        /// Creates the message context from the configuration with a private encodeable factory.
        /// </summary>
        /// <param name="factory">The private encodeable factory to use. If null, a new factory will be created.</param>
        /// <returns>A new instance of a ServiceMessageContext object.</returns>
        public ServiceMessageContext CreateMessageContext(IEncodeableFactory? factory)
        {
            var messageContext = new ServiceMessageContext(
                m_telemetry,
                factory ?? EncodeableFactory.Create());

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
            ITelemetryContext? telemetry,
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
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type systemType)
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
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type systemType,
            ILogger logger,
            ITelemetryContext? telemetry,
            CancellationToken ct = default)
        {
            string filePath = GetFilePathFromAppConfig(sectionName, logger);

            var file = new FileInfo(filePath);

            if (!file.Exists)
            {
                throw ServiceResultException.ConfigurationError(
                    "Configuration file does not exist: {0}\nCurrent directory is: {1}",
                    filePath,
                    Directory.GetCurrentDirectory());
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
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? systemType,
            ITelemetryContext? telemetry)
        {
            systemType ??= typeof(ApplicationConfiguration);

            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read);
            try
            {
                using IDisposable scope = AmbientMessageContext.SetScopedContext(telemetry!);
                IServiceMessageContext context = AmbientMessageContext.CurrentContext ??
                    ServiceMessageContext.CreateEmpty(telemetry!);
                using var parser = new XmlParser(typeof(ApplicationConfiguration), stream, context);
                ApplicationConfiguration configuration;
                if (systemType == typeof(ApplicationConfiguration))
                {
                    configuration = new ApplicationConfiguration(telemetry!);
                }
                else
                {
                    configuration = (ApplicationConfiguration)Activator.CreateInstance(systemType, [telemetry])!;
                }

                configuration.Decode(parser);
                configuration.ServerConfiguration?.ValidateSecurityPolicies();
                configuration.DiscoveryServerConfiguration?.ValidateSecurityPolicies();
                configuration.SourceFilePath = file.FullName;
                return configuration;
            }
            catch (Exception e)
            {
                throw ServiceResultException.ConfigurationError(
                    e,
                    "Configuration file could not be loaded: {0}\nError: {1}",
                    file.FullName,
                    e.Message);
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
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? systemType)
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
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? systemType,
            ITelemetryContext? telemetry,
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
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? systemType,
            bool applyTraceSettings,
            ICertificatePasswordProvider? certificatePasswordProvider = null)
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
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? systemType,
            bool applyTraceSettings,
            ITelemetryContext? telemetry,
            ICertificatePasswordProvider? certificatePasswordProvider = null,
            CancellationToken ct = default)
        {
            ApplicationConfiguration? configuration = null;

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
                throw ServiceResultException.ConfigurationError(
                    e,
                    "Configuration file could not be loaded: {0}\nError is: {1}",
                    file.FullName,
                    e.Message);
            }

            configuration!.SourceFilePath = file.FullName;

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
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? systemType,
            bool applyTraceSettings,
            ICertificatePasswordProvider? certificatePasswordProvider = null)
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
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? systemType,
            bool applyTraceSettings,
            ITelemetryContext? telemetry,
            ICertificatePasswordProvider? certificatePasswordProvider = null,
            CancellationToken ct = default)
        {
            systemType ??= typeof(ApplicationConfiguration);

            ApplicationConfiguration configuration;
            try
            {
                using IDisposable scope = AmbientMessageContext.SetScopedContext(telemetry!);
                IServiceMessageContext ctx = AmbientMessageContext.CurrentContext ??
                    ServiceMessageContext.CreateEmpty(telemetry!);
                using var parser = new XmlParser(typeof(ApplicationConfiguration), stream, ctx);
                if (systemType == typeof(ApplicationConfiguration))
                {
                    configuration = new ApplicationConfiguration(telemetry!);
                }
                else
                {
                    configuration = (ApplicationConfiguration)Activator.CreateInstance(systemType, [telemetry])!;
                }
                configuration.Decode(parser);
                configuration.ServerConfiguration?.ValidateSecurityPolicies();
                configuration.DiscoveryServerConfiguration?.ValidateSecurityPolicies();
            }
            catch (Exception e)
            {
                throw ServiceResultException.ConfigurationError(
                    e,
                    "Configuration could not be loaded.\nError is: {0}",
                    e.Message);
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
                    = certificatePasswordProvider!;

                await configuration.ValidateAsync(applicationType, ct).ConfigureAwait(false);
            }

            return configuration!;
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
                string? absolutePath = Utils.GetAbsoluteFilePath(
                    sectionName + ".Config.xml",
                    checkCurrentDirectory: true,
                    createAlways: false);
                return absolutePath ?? $"{sectionName}.Config.xml";
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not get file path from app config - returning: {SectionName}.Config.xml", sectionName);
                return $"{sectionName}.Config.xml";
            }
        }

        /// <summary>
        /// Saves the configuration file.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        public void SaveToFile(string filePath)
        {
            using Stream ostrm = File.Open(filePath, FileMode.Create, FileAccess.ReadWrite);
            using IDisposable scope = AmbientMessageContext.SetScopedContext(m_telemetry);
            IServiceMessageContext context = AmbientMessageContext.CurrentContext
                ?? ServiceMessageContext.CreateEmpty(m_telemetry);
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
            settings.CloseOutput = true;
            using var writer = XmlWriter.Create(ostrm, settings);
            using var encoder = new XmlEncoder(typeof(ApplicationConfiguration), writer, context);
            this.Encode(encoder);
            encoder.Close();
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
                throw ServiceResultException.ConfigurationError(
                    "ApplicationName must be specified.");
            }

            if (SecurityConfiguration == null)
            {
                throw ServiceResultException.ConfigurationError(
                    "SecurityConfiguration must be specified.");
            }

            SecurityConfiguration.Validate(m_telemetry);

            // load private keys
            ArrayOf<CertificateIdentifier> appCerts = SecurityConfiguration.ApplicationCertificates;
            for (int i = 0; i < appCerts.Count; i++)
            {
                CertificateIdentifier applicationCertificate = appCerts[i];
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
                    throw ServiceResultException.ConfigurationError(
                        "ClientConfiguration must be specified.");
                }

                ClientConfiguration.Validate();
            }

            if (applicationType is ApplicationType.Server or ApplicationType.ClientAndServer)
            {
                if (ServerConfiguration == null)
                {
                    throw ServiceResultException.ConfigurationError(
                        "ServerConfiguration must be specified.");
                }

                ServerConfiguration.Validate();
            }

            if (applicationType == ApplicationType.DiscoveryServer)
            {
                if (DiscoveryServerConfiguration == null)
                {
                    throw ServiceResultException.ConfigurationError(
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

            await CertificateValidator!.UpdateAsync(
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

            string? filePath;
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
                    var sourceFile = new FileInfo(SourceFilePath!);
                    filePath = Utils.Format(
                        "{0}{1}{2}",
                        sourceFile.DirectoryName!,
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
        /// Looks for an extension with the specified type and uses the supplied decoder function to parse it.
        /// </summary>
        /// <typeparam name="T">The type of extension.</typeparam>
        /// <param name="elementName">Name of the element (required).</param>
        /// <param name="decoderFunc">A function that reads the value from an <see cref="IDecoder"/>.</param>
        /// <returns>The extension if found. Default otherwise.</returns>
        public T? ParseExtension<T>(XmlQualifiedName elementName, Func<IDecoder, T> decoderFunc)
        {
            return Utils.ParseExtension(m_extensions, elementName, m_telemetry, decoderFunc);
        }

        /// <summary>
        /// Updates the extension using the supplied encoder function.
        /// </summary>
        /// <typeparam name="T">The type of extension.</typeparam>
        /// <param name="elementName">Name of the element (required).</param>
        /// <param name="value">The value.</param>
        /// <param name="encoderFunc">A function that writes the value to an <see cref="IEncoder"/>.</param>
        public void UpdateExtension<T>(XmlQualifiedName elementName, T value, Action<IEncoder, T> encoderFunc)
        {
            Utils.UpdateExtension(ref m_extensions, elementName, value, m_telemetry, encoderFunc);
        }

        /// <summary>
        /// Looks for an extension with the specified IEncodeable type and decodes it.
        /// </summary>
        /// <typeparam name="T">The type of extension (must implement IEncodeable).</typeparam>
        /// <param name="elementName">Name of the element (null to derive from type).</param>
        /// <returns>The extension if found. Default otherwise.</returns>
        public T? ParseExtension<T>(XmlQualifiedName? elementName = null)
            where T : IEncodeable, new()
        {
            return Utils.ParseExtension<T>(m_extensions, elementName, m_telemetry);
        }

        /// <summary>
        /// Updates or adds an extension using the IEncodeable implementation.
        /// </summary>
        /// <typeparam name="T">The type of extension (must implement IEncodeable).</typeparam>
        /// <param name="elementName">Name of the element (null to derive from type).</param>
        /// <param name="value">The value to encode.</param>
        public void UpdateExtension<T>(XmlQualifiedName? elementName, T value)
            where T : IEncodeable
        {
            Utils.UpdateExtension(ref m_extensions, elementName, value, m_telemetry);
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
                m_securityPolicies = m_securityPolicies.AddItem(new ServerSecurityPolicy());
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

            if (m_userTokenPolicies.IsEmpty)
            {
                m_userTokenPolicies = [new UserTokenPolicy()];
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
            if (WellKnownDiscoveryUrls.IsEmpty)
            {
                WellKnownDiscoveryUrls = Utils.DiscoveryUrls;
            }
        }
    }
}
