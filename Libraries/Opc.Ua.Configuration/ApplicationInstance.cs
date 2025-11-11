/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// A class that install, configures and runs a UA application.
    /// </summary>
    public class ApplicationInstance
    {
        /// <summary>
        /// Obsolete constructor
        /// </summary>
        [Obsolete("Use ApplicationInstance(ITelemetryContext) instead.")]
        public ApplicationInstance()
            : this((ITelemetryContext)null)
        {
        }

        /// <summary>
        /// Obsolete constructor
        /// </summary>
        [Obsolete("Use ApplicationInstance(ApplicationConfiguration, ITelemetryContext) instead.")]
        public ApplicationInstance(ApplicationConfiguration applicationConfiguration)
            : this(applicationConfiguration, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationInstance"/> class.
        /// </summary>
        public ApplicationInstance(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<ApplicationInstance>();
            DisableCertificateAutoCreation = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationInstance"/> class.
        /// </summary>
        /// <param name="applicationConfiguration">The application configuration.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public ApplicationInstance(
            ApplicationConfiguration applicationConfiguration,
            ITelemetryContext telemetry)
            : this(telemetry)
        {
            ApplicationConfiguration = applicationConfiguration;
        }

        /// <summary>
        /// Gets or sets the name of the application.
        /// </summary>
        /// <value>The name of the application.</value>
        public string ApplicationName { get; set; }

        /// <summary>
        /// Gets or sets the type of the application.
        /// </summary>
        /// <value>The type of the application.</value>
        public ApplicationType ApplicationType { get; set; }

        /// <summary>
        /// Gets or sets the name of the config section containing the path
        /// to the application configuration file.
        /// </summary>
        /// <value>The name of the config section.</value>
        public string ConfigSectionName { get; set; }

        /// <summary>
        /// Gets or sets the type of configuration file.
        /// </summary>
        /// <value>The type of configuration file.</value>
        public Type ConfigurationType { get; set; }

        /// <summary>
        /// Gets the server.
        /// </summary>
        /// <value>The server.</value>
        public ServerBase Server { get; private set; }

        /// <summary>
        /// Gets the application configuration used when the Start() method was called.
        /// </summary>
        /// <value>The application configuration.</value>
        public ApplicationConfiguration ApplicationConfiguration { get; set; }

        /// <summary>
        /// Get or set the message dialog.
        /// </summary>
        public static IApplicationMessageDlg MessageDlg { get; set; }

        /// <summary>
        /// Get or set the certificate password provider.
        /// </summary>
        public ICertificatePasswordProvider CertificatePasswordProvider { get; set; }

        /// <summary>
        /// Get or set bool which indicates if the auto creation
        /// of a new application certificate during startup is disabled.
        /// Default is enabled./>
        /// </summary>
        /// <remarks>
        /// Prevents auto self signed cert creation in use cases
        /// where an expired certificate should not be automatically
        /// renewed or where it is required to only use certificates
        /// provided by the user.
        /// </remarks>
        public bool DisableCertificateAutoCreation { get; set; }

        /// <summary>
        /// Starts the UA server.
        /// </summary>
        /// <param name="server">The server.</param>
        public async Task StartAsync(ServerBase server)
        {
            Server = server;

            if (ApplicationConfiguration == null)
            {
                await LoadApplicationConfigurationAsync(false).ConfigureAwait(false);
            }

            await server.StartAsync(ApplicationConfiguration).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops the UA server.
        /// </summary>
        public ValueTask StopAsync()
        {
            return Server.StopAsync();
        }

        /// <summary>
        /// Stops the UA server.
        /// </summary>
        [Obsolete("Use StopAsync")]
        public void Stop()
        {
            Server.Stop();
        }

        /// <summary>
        /// Loads the application configuration.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async Task<ApplicationConfiguration> LoadApplicationConfigurationAsync(
            Stream stream,
            bool silent,
            CancellationToken ct = default)
        {
            ApplicationConfiguration configuration = null;

            try
            {
                configuration = await LoadAppConfigAsync(
                        silent,
                        stream,
                        ApplicationType,
                        ConfigurationType,
                        true,
                        CertificatePasswordProvider,
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (silent)
            {
            }

            if (configuration == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Could not load configuration.");
            }

            ApplicationConfiguration = FixupAppConfig(configuration);

            return configuration;
        }

        /// <summary>
        /// Loads the application configuration.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<ApplicationConfiguration> LoadApplicationConfigurationAsync(
            string filePath,
            bool silent,
            CancellationToken ct = default)
        {
            ApplicationConfiguration configuration = null;

            try
            {
                configuration = await LoadAppConfigAsync(
                        silent,
                        filePath,
                        ApplicationType,
                        ConfigurationType,
                        true,
                        CertificatePasswordProvider,
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (silent)
            {
            }

            if (configuration == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Could not load configuration file.");
            }

            ApplicationConfiguration = FixupAppConfig(configuration);

            return configuration;
        }

        /// <summary>
        /// Loads the application configuration.
        /// </summary>
        public ValueTask<ApplicationConfiguration> LoadApplicationConfigurationAsync(
            bool silent,
            CancellationToken ct = default)
        {
            string filePath = ApplicationConfiguration.GetFilePathFromAppConfig(ConfigSectionName, m_logger);
            return LoadApplicationConfigurationAsync(filePath, silent, ct);
        }

        /// <summary>
        /// Helper to replace localhost with the hostname
        /// in the application uri and base addresses of the
        /// configuration.
        /// </summary>
        public static ApplicationConfiguration FixupAppConfig(
            ApplicationConfiguration configuration)
        {
            configuration.ApplicationUri = Utils.ReplaceLocalhost(configuration.ApplicationUri);
            if (configuration.ServerConfiguration != null)
            {
                for (int i = 0; i < configuration.ServerConfiguration.BaseAddresses.Count; i++)
                {
                    configuration.ServerConfiguration.BaseAddresses[i] = Utils.ReplaceLocalhost(
                        configuration.ServerConfiguration.BaseAddresses[i]);
                }
            }
            return configuration;
        }

        /// <summary>
        /// Create a builder for a UA application configuration.
        /// </summary>
        public IApplicationConfigurationBuilderTypes Build(string applicationUri, string productUri)
        {
            // App Uri and cert subject
            ApplicationConfiguration = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType,
                ApplicationUri = applicationUri,
                ProductUri = productUri,
                TraceConfiguration = new TraceConfiguration { TraceMasks = Utils.TraceMasks.None },
                TransportQuotas = new TransportQuotas()
            };

            // Trace off
#pragma warning disable CS0618 // Type or member is obsolete
            ApplicationConfiguration.TraceConfiguration.ApplySettings();
#pragma warning restore CS0618 // Type or member is obsolete

            return new ApplicationConfigurationBuilder(this);
        }

        /// <summary>
        /// Deletes all application certificates.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public async ValueTask DeleteApplicationInstanceCertificateAsync(
            string[] profileIds = null,
            CancellationToken ct = default)
        {
            // TODO: delete only selected profiles
            if (ApplicationConfiguration == null)
            {
                throw new ArgumentException("Missing configuration.");
            }

            foreach (CertificateIdentifier id in ApplicationConfiguration.SecurityConfiguration
                .ApplicationCertificates)
            {
                await DeleteApplicationInstanceCertificateAsync(ApplicationConfiguration, id, ct)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Checks for a valid application instance certificate.
        /// </summary>
        /// <param name="silent">if set to <c>true</c> no dialogs will be displayed.</param>
        /// <param name="lifeTimeInMonths">The lifetime in months.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<bool> CheckApplicationInstanceCertificatesAsync(
            bool silent,
            ushort? lifeTimeInMonths = null,
            CancellationToken ct = default)
        {
            lifeTimeInMonths ??= CertificateFactory.DefaultLifeTime;
            m_logger.LogInformation("Checking application instance certificate.");

            if (ApplicationConfiguration == null)
            {
                await LoadApplicationConfigurationAsync(silent, ct).ConfigureAwait(false);
            }

            // find the existing certificates.
            SecurityConfiguration securityConfiguration = ApplicationConfiguration
                .SecurityConfiguration;

            if (securityConfiguration.ApplicationCertificates.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "Need at least one Application Certificate.");
            }

            // Note: The FindAsync method searches certificates in this order: thumbprint, subjectName, then applicationUri.
            // When SubjectName or Thumbprint is specified, certificates may be loaded even if their ApplicationUri
            // doesn't match ApplicationConfiguration.ApplicationUri, however each certificate is validated individually
            // in CheckApplicationInstanceCertificateAsync (called via CheckOrCreateCertificateAsync) to ensure it contains
            // the configuration's ApplicationUri.
            bool result = true;
            foreach (CertificateIdentifier certId in securityConfiguration.ApplicationCertificates)
            {
                ushort minimumKeySize = certId.GetMinKeySize(securityConfiguration);
                bool nextResult = await CheckOrCreateCertificateAsync(
                        certId,
                        silent,
                        minimumKeySize,
                        lifeTimeInMonths.Value,
                        ct)
                    .ConfigureAwait(false);
                result = result && nextResult;
            }

            return result;
        }

        /// <summary>
        /// Checks, validates, and optionally creates an application certificate.
        /// Loads the certificate, validates it against configured requirements (ApplicationUri, key size, domains),
        /// and creates a new certificate if none exists and auto-creation is enabled.
        /// Note: FindAsync searches certificates in order: thumbprint, subjectName, applicationUri.
        /// The applicationUri parameter is only used if thumbprint and subjectName don't find a match.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async Task<bool> CheckOrCreateCertificateAsync(
            CertificateIdentifier id,
            bool silent,
            ushort minimumKeySize,
            ushort lifeTimeInMonths,
            CancellationToken ct = default)
        {
            ApplicationConfiguration configuration = ApplicationConfiguration;

            if (id == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Configuration file does not specify a certificate.");
            }

            // reload the certificate from disk in the cache.
            ICertificatePasswordProvider passwordProvider = configuration
                .SecurityConfiguration
                .CertificatePasswordProvider;
            await id.LoadPrivateKeyExAsync(passwordProvider, configuration.ApplicationUri, m_telemetry, ct)
                .ConfigureAwait(false);

            // load the certificate
            X509Certificate2 certificate = await id.FindAsync(
                true,
                configuration.ApplicationUri,
                m_telemetry,
                ct)
                .ConfigureAwait(false);

            // check that it is ok.
            if (certificate != null)
            {
                m_logger.LogInformation("Check certificate: {Certificate}", certificate.AsLogSafeString());
                bool certificateValid = await CheckApplicationInstanceCertificateAsync(
                        configuration,
                        id,
                        certificate,
                        silent,
                        minimumKeySize,
                        ct)
                    .ConfigureAwait(false);

                if (!certificateValid)
                {
                    var message = new StringBuilder();
                    message.AppendLine(
                        "The certificate with subject {0} in the configuration is invalid.")
                        .AppendLine(" Please update or delete the certificate from this location:")
                        .AppendLine(" {1}");
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        message.ToString(),
                        id.SubjectName,
                        Utils.ReplaceSpecialFolderNames(id.StorePath));
                }
            }
            else
            {
                // check for missing private key.
                certificate = await id.FindAsync(false, configuration.ApplicationUri, m_telemetry, ct)
                    .ConfigureAwait(false);

                if (certificate != null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Cannot access private key for certificate with thumbprint={0}",
                        certificate.Thumbprint);
                }

                // check for missing thumbprint.
                if (!string.IsNullOrEmpty(id.Thumbprint))
                {
                    if (!string.IsNullOrEmpty(id.SubjectName))
                    {
                        var id2 = new CertificateIdentifier
                        {
                            StoreType = id.StoreType,
                            StorePath = id.StorePath,
                            SubjectName = id.SubjectName
                        };
                        certificate = await id2.FindAsync(true, configuration.ApplicationUri, m_telemetry, ct)
                            .ConfigureAwait(false);
                    }

                    if (certificate != null)
                    {
                        var message = new StringBuilder();
                        message.AppendLine(
                            "Thumbprint was explicitly specified in the configuration.")
                            .AppendLine("Another certificate with the same subject name was found.")
                            .AppendLine("Use it instead?")
                            .AppendLine("Requested: {0}")
                            .AppendLine("Found: {1}");
                        if (!await ApproveMessageAsync(
                            Utils.Format(message.ToString(), id.SubjectName, certificate.Subject), silent)
                                .ConfigureAwait(false))
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadConfigurationError,
                                message.ToString(),
                                id.SubjectName,
                                certificate.Subject);
                        }
                    }
                    else
                    {
                        var message = new StringBuilder();
                        message.AppendLine(
                            "Thumbprint was explicitly specified in the configuration.")
                            .AppendLine("Cannot generate a new certificate.");
                        throw ServiceResultException.Create(
                            StatusCodes.BadConfigurationError,
                            message.ToString());
                    }
                }
            }

            if (certificate == null)
            {
                if (!DisableCertificateAutoCreation)
                {
                    certificate = await CreateApplicationInstanceCertificateAsync(
                            configuration,
                            id,
                            lifeTimeInMonths,
                            ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    m_logger.LogWarning("Application Instance certificate auto creation is disabled.");
                }

                if (certificate == null)
                {
                    var message = new StringBuilder();
                    message.AppendLine("There is no cert with subject {0} in the configuration.")
                        .AppendLine(" Please generate a cert for your application,")
                        .AppendLine(" then copy the new cert to this location:")
                        .AppendLine(" {1}");
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        message.ToString(),
                        id.SubjectName,
                        id.StorePath);
                }
            }
            else if (configuration.SecurityConfiguration.AddAppCertToTrustedStore)
            {
                // ensure it is trusted.
                await AddToTrustedStoreAsync(configuration, certificate, ct).ConfigureAwait(false);
            }

            return true;
        }

        /// <summary>
        /// Adds a Certificate to the Trusted Store of the Application, needed e.g. for the GDS to trust it´s own CA
        /// </summary>
        /// <param name="certificate">The certificate to add to the store</param>
        /// <param name="ct">The cancellation token</param>
        public async Task AddOwnCertificateToTrustedStoreAsync(
            X509Certificate2 certificate,
            CancellationToken ct)
        {
            await AddToTrustedStoreAsync(ApplicationConfiguration, certificate, ct).ConfigureAwait(
                false);
        }

        /// <summary>
        /// Loads the configuration.
        /// </summary>
        internal async ValueTask<ApplicationConfiguration> LoadAppConfigAsync(
            bool silent,
            string filePath,
            ApplicationType applicationType,
            Type configurationType,
            bool applyTraceSettings,
            ICertificatePasswordProvider certificatePasswordProvider = null,
            CancellationToken ct = default)
        {
            m_logger.LogInformation("Loading application configuration file. {FilePath}", filePath);

            try
            {
                // load the configuration file.
                return await ApplicationConfiguration
                    .LoadAsync(
                        new FileInfo(filePath),
                        applicationType,
                        configurationType,
                        applyTraceSettings,
                        m_telemetry,
                        certificatePasswordProvider,
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Could not load configuration file. {FilePath}", filePath);

                // warn user.
                if (!silent)
                {
                    if (MessageDlg != null)
                    {
                        MessageDlg.Message("Load Application Configuration: " + e.Message);
                        await MessageDlg.ShowAsync().ConfigureAwait(false);
                    }

                    throw;
                }

                return null;
            }
        }

        /// <summary>
        /// Loads the configuration.
        /// </summary>
        internal async ValueTask<ApplicationConfiguration> LoadAppConfigAsync(
            bool silent,
            Stream stream,
            ApplicationType applicationType,
            Type configurationType,
            bool applyTraceSettings,
            ICertificatePasswordProvider certificatePasswordProvider = null,
            CancellationToken ct = default)
        {
            m_logger.LogInformation("Loading application from stream.");

            try
            {
                // load the configuration file.
                return await ApplicationConfiguration
                    .LoadAsync(
                        stream,
                        applicationType,
                        configurationType,
                        applyTraceSettings,
                        m_telemetry,
                        certificatePasswordProvider,
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Could not load configuration from stream.");

                // warn user.
                if (!silent)
                {
                    if (MessageDlg != null)
                    {
                        MessageDlg.Message("Load Application Configuration: " + e.Message);
                        await MessageDlg.ShowAsync().ConfigureAwait(false);
                    }

                    throw;
                }

                return null;
            }
        }

        /// <summary>
        /// Creates an application instance certificate if one does not already exist.
        /// </summary>
        private async Task<bool> CheckApplicationInstanceCertificateAsync(
            ApplicationConfiguration configuration,
            CertificateIdentifier id,
            X509Certificate2 certificate,
            bool silent,
            ushort minimumKeySize,
            CancellationToken ct)
        {
            if (certificate == null)
            {
                return false;
            }

            // set suppressible errors
            HashSet<StatusCode> approvedCodes =
            [
                StatusCodes.BadCertificateUntrusted,
                StatusCodes.BadCertificateTimeInvalid,
                StatusCodes.BadCertificateIssuerTimeInvalid,
                StatusCodes.BadCertificateHostNameInvalid,
                StatusCodes.BadCertificateRevocationUnknown,
                StatusCodes.BadCertificateIssuerRevocationUnknown
            ];
            void OnCertificateValidation(object sender, CertificateValidationEventArgs e)
            {
                if (approvedCodes.Contains(e.Error.StatusCode))
                {
                    m_logger.LogWarning(
                        "Application Certificate Validation suppressed {ErrorMessage}",
                        e.Error.StatusCode);
                    e.Accept = true;
                }
            }

            m_logger.LogInformation(
                "Check application instance certificate {Certificate}.",
                certificate.AsLogSafeString());

            try
            {
                // validate certificate.
                configuration.CertificateValidator.CertificateValidation += OnCertificateValidation;
                await configuration
                    .CertificateValidator.ValidateAsync(
                        certificate.HasPrivateKey
                            ? CertificateFactory.Create(certificate.RawData)
                            : certificate,
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                string message = Utils.Format(
                    "Error validating certificate. Exception: {0}. Use certificate anyway?",
                    ex.Message);
                if (!await ApproveMessageAsync(message, silent).ConfigureAwait(false))
                {
                    return false;
                }
            }
            finally
            {
                configuration.CertificateValidator.CertificateValidation -= OnCertificateValidation;
            }

            // check key size
            int keySize = X509Utils.GetPublicKeySize(certificate);
            if (minimumKeySize > keySize)
            {
                string message = Utils.Format(
                    "The key size ({0}) in the certificate is less than the minimum provided ({1}). Use certificate anyway?",
                    keySize,
                    minimumKeySize);

                if (!await ApproveMessageAsync(message, silent).ConfigureAwait(false))
                {
                    return false;
                }
            }

            // check domains.
            if (configuration.ApplicationType != ApplicationType.Client &&
                !await CheckDomainsInCertificateAsync(configuration, certificate, silent, ct)
                    .ConfigureAwait(false))
            {
                return false;
            }

            // Validate that the certificate contains the configuration's ApplicationUri
            if (!X509Utils.CompareApplicationUriWithCertificate(
                certificate,
                configuration.ApplicationUri,
                out IReadOnlyList<string> certificateUris))
            {
                if (certificateUris.Count == 0)
                {
                    const string message =
                        "The Application URI could not be found in the certificate. Use certificate anyway?";
                    if (!await ApproveMessageAsync(message, silent).ConfigureAwait(false))
                    {
                        return false;
                    }
                }
                else
                {
                    string message = Utils.Format(
                        "The certificate with subject '{0}' does not contain the ApplicationUri '{1}' " +
                        "from the configuration. Certificate contains: {2}. Use certificate anyway?",
                        certificate.Subject,
                        configuration.ApplicationUri,
                        string.Join(", ", certificateUris));

                    if (!await ApproveMessageAsync(message, silent).ConfigureAwait(false))
                    {
                        return false;
                    }
                }
            }

            m_logger.LogInformation(
                "Certificate {Certificate} validated for ApplicationUri: {ApplicationUri}",
                certificate.AsLogSafeString(),
                configuration.ApplicationUri);

            // update configuration.
            id.Certificate = certificate;

            return true;
        }

        /// <summary>
        /// Checks that the domains in the server addresses match the domains in the certificates.
        /// </summary>
        private async Task<bool> CheckDomainsInCertificateAsync(
            ApplicationConfiguration configuration,
            X509Certificate2 certificate,
            bool silent,
            CancellationToken ct)
        {
            m_logger.LogInformation("Check domains in certificate.");

            bool valid = true;
            IList<string> serverDomainNames = configuration.GetServerDomainNames();
            IList<string> certificateDomainNames = X509Utils.GetDomainsFromCertificate(certificate);

            m_logger.LogInformation("Server Domain names:");
            foreach (string name in serverDomainNames)
            {
                m_logger.LogInformation(" {ServerDomainName}", name);
            }

            m_logger.LogInformation("Certificate Domain names:");
            foreach (string name in certificateDomainNames)
            {
                m_logger.LogInformation(" {ClientDomainName}", name);
            }

            // get computer name.
            string computerName = Utils.GetHostName();

            // get IP addresses.
            IPAddress[] addresses = null;

            for (int ii = 0; ii < serverDomainNames.Count; ii++)
            {
                if (Utils.FindStringIgnoreCase(certificateDomainNames, serverDomainNames[ii]))
                {
                    continue;
                }

                if (string.Equals(
                    serverDomainNames[ii],
                    "localhost",
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (Utils.FindStringIgnoreCase(certificateDomainNames, computerName))
                    {
                        continue;
                    }

                    // check for aliases.
                    bool found = false;

                    // get IP addresses only if necessary.
                    addresses ??= await Utils.GetHostAddressesAsync(computerName, ct).ConfigureAwait(
                        false);

                    // check for ip addresses.
                    for (int jj = 0; jj < addresses.Length; jj++)
                    {
                        if (Utils.FindStringIgnoreCase(certificateDomainNames, addresses[jj].ToString()))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (found)
                    {
                        continue;
                    }
                }

                string message = Utils.Format(
                    "The server is configured to use domain '{0}' which does not appear in the certificate. Use certificate anyway?",
                    serverDomainNames[ii]);

                valid = false;

                if (await ApproveMessageAsync(message, silent).ConfigureAwait(false))
                {
                    valid = true;
                    continue;
                }

                break;
            }

            return valid;
        }

        /// <summary>
        /// Creates the application instance certificate.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="id">The certificate identifier.</param>
        /// <param name="lifeTimeInMonths">The lifetime in months.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>The new certificate</returns>
        /// <exception cref="ServiceResultException"></exception>
        private async Task<X509Certificate2> CreateApplicationInstanceCertificateAsync(
            ApplicationConfiguration configuration,
            CertificateIdentifier id,
            ushort lifeTimeInMonths,
            CancellationToken ct)
        {
            // delete any existing certificate.
            await DeleteApplicationInstanceCertificateAsync(configuration, id, ct).ConfigureAwait(
                false);

            m_logger.LogInformation("Creating application instance certificate.");

            // get the domains from the configuration file.
            IList<string> serverDomainNames = configuration.GetServerDomainNames();

            if (serverDomainNames.Count == 0)
            {
                serverDomainNames.Add(Utils.GetHostName());
            }

            // ensure the certificate store directory exists.
            if (id.StoreType == CertificateStoreType.Directory)
            {
                Utils.GetAbsoluteDirectoryPath(id.StorePath, true, true, true);
            }

            Security.Certificates.ICertificateBuilder builder = CertificateFactory
                .CreateCertificate(
                    configuration.ApplicationUri,
                    configuration.ApplicationName,
                    id.SubjectName,
                    serverDomainNames)
                .SetLifeTime(lifeTimeInMonths);

            if (id.CertificateType == null ||
                id.CertificateType == ObjectTypeIds.ApplicationCertificateType ||
                id.CertificateType == ObjectTypeIds.RsaMinApplicationCertificateType ||
                id.CertificateType == ObjectTypeIds.RsaSha256ApplicationCertificateType)
            {
                id.Certificate = builder.SetRSAKeySize(CertificateFactory.DefaultKeySize)
                    .CreateForRSA();

                m_logger.LogInformation("Certificate {Certificate} created for RSA.", id.Certificate.AsLogSafeString());
            }
            else
            {
                ECCurve? curve =
                    EccUtils.GetCurveFromCertificateTypeId(id.CertificateType)
                    ?? throw new ServiceResultException(
                        StatusCodes.BadConfigurationError,
                        "The Ecc certificate type is not supported.");

                id.Certificate = builder.SetECCurve(curve.Value).CreateForECDsa();

                m_logger.LogInformation(
                    "Certificate {Certificate} created for {Curve}.",
                    id.Certificate.AsLogSafeString(),
                    curve.Value.Oid.FriendlyName);
            }

            ICertificatePasswordProvider passwordProvider = configuration
                .SecurityConfiguration
                .CertificatePasswordProvider;
            await id
                .Certificate.AddToStoreAsync(
                    id.StoreType,
                    id.StorePath,
                    passwordProvider?.GetPassword(id),
                    m_telemetry,
                    ct)
                .ConfigureAwait(false);

            // ensure the certificate is trusted.
            if (configuration.SecurityConfiguration.AddAppCertToTrustedStore)
            {
                await AddToTrustedStoreAsync(configuration, id.Certificate, ct).ConfigureAwait(
                    false);
            }

            // reload the certificate from disk.
            id.Certificate = await id.LoadPrivateKeyExAsync(
                passwordProvider,
                configuration.ApplicationUri,
                m_telemetry,
                ct)
                .ConfigureAwait(false);

            await configuration
                .CertificateValidator.UpdateAsync(configuration.SecurityConfiguration, applicationUri: null, ct)
                .ConfigureAwait(false);

            m_logger.LogInformation(
                "Certificate {Certificate} created for {ApplicationUri}.",
                id.Certificate.AsLogSafeString(),
                configuration.ApplicationUri);

            // do not dispose temp cert, or X509Store certs become unusable

            return id.Certificate;
        }

        /// <summary>
        /// Deletes an existing application instance certificate.
        /// </summary>
        /// <param name="configuration">The configuration instance that stores the configurable information for a UA application.</param>
        /// <param name="id">The certificate identifier.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        private async Task DeleteApplicationInstanceCertificateAsync(
            ApplicationConfiguration configuration,
            CertificateIdentifier id,
            CancellationToken ct)
        {
            if (id == null)
            {
                return;
            }

            // delete certificate and private key.
            X509Certificate2 certificate = await id.FindAsync(configuration.ApplicationUri, m_telemetry, ct)
                .ConfigureAwait(false);
            if (certificate != null)
            {
                m_logger.LogInformation(
                    Utils.TraceMasks.Security,
                    "Deleting application instance certificate {Certificate} and private key.",
                    certificate.AsLogSafeString());
            }

            // delete trusted peer certificate.
            if (configuration.SecurityConfiguration != null &&
                configuration.SecurityConfiguration.TrustedPeerCertificates != null)
            {
                string thumbprint = id.Thumbprint;

                if (certificate != null)
                {
                    thumbprint = certificate.Thumbprint;
                }

                if (!string.IsNullOrEmpty(thumbprint))
                {
                    ICertificateStore store = configuration.SecurityConfiguration
                        .TrustedPeerCertificates
                        .OpenStore(m_telemetry);
                    if (store != null)
                    {
                        try
                        {
                            bool deleted = await store.DeleteAsync(thumbprint, ct)
                                .ConfigureAwait(false);
                            if (deleted)
                            {
                                m_logger.LogInformation(
                                    Utils.TraceMasks.Security,
                                    "Application Instance Certificate [{Thumbprint}] deleted from trusted store.",
                                    thumbprint);
                            }
                        }
                        finally
                        {
                            store.Close();
                        }
                    }
                }
            }

            // delete certificate and private key from owner store.
            if (certificate != null)
            {
                using ICertificateStore store = id.OpenStore(m_telemetry);
                bool deleted = await store.DeleteAsync(certificate.Thumbprint, ct)
                    .ConfigureAwait(false);
                if (deleted)
                {
                    m_logger.LogInformation(
                        Utils.TraceMasks.Security,
                        "Application certificate {Certificate} and private key deleted.",
                        certificate.AsLogSafeString());
                }
            }

            // erase the memory copy of the deleted certificate
            id.Certificate = null;
        }

        /// <summary>
        /// Adds the certificate to the Trusted Certificate Store
        /// </summary>
        /// <param name="configuration">The application's configuration which specifies the location of the TrustedStore.</param>
        /// <param name="certificate">The certificate to register.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="certificate"/> is <c>null</c>.</exception>
        private async Task AddToTrustedStoreAsync(
            ApplicationConfiguration configuration,
            X509Certificate2 certificate,
            CancellationToken ct)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            string storePath = null;

            if (configuration != null &&
                configuration.SecurityConfiguration != null &&
                configuration.SecurityConfiguration.TrustedPeerCertificates != null)
            {
                storePath = configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath;
            }

            if (string.IsNullOrEmpty(storePath))
            {
                m_logger.LogWarning("WARNING: Trusted peer store not specified.");
                return;
            }

            try
            {
                ICertificateStore store = configuration.SecurityConfiguration
                    .TrustedPeerCertificates
                    .OpenStore(m_telemetry);

                if (store == null)
                {
                    m_logger.LogWarning("Could not open trusted peer store.");
                    return;
                }

                try
                {
                    // check if it already exists.
                    X509Certificate2Collection existingCertificates = await store
                        .FindByThumbprintAsync(certificate.Thumbprint, ct)
                        .ConfigureAwait(false);

                    if (existingCertificates.Count > 0)
                    {
                        return;
                    }

                    m_logger.LogInformation(
                        "Adding application certificate {Certificate} to trusted peer store.",
                        certificate.AsLogSafeString());

                    List<string> subjectName = X509Utils.ParseDistinguishedName(
                        certificate.Subject);

                    // check for old certificate.
                    X509Certificate2Collection certificates = await store.EnumerateAsync(ct)
                        .ConfigureAwait(false);

                    for (int ii = 0; ii < certificates.Count; ii++)
                    {
                        if (X509Utils.CompareDistinguishedName(certificates[ii], subjectName))
                        {
                            if (certificates[ii].Thumbprint == certificate.Thumbprint)
                            {
                                return;
                            }

                            bool deleteCert = false;
                            if (X509Utils.IsECDsaSignature(certificates[ii]) &&
                                X509Utils.IsECDsaSignature(certificate))
                            {
                                if (X509Utils
                                        .GetECDsaQualifier(certificates[ii])
                                        .Equals(
                                            X509Utils.GetECDsaQualifier(certificate),
                                            StringComparison.Ordinal))
                                {
                                    deleteCert = true;
                                }
                            }
                            else if (!X509Utils.IsECDsaSignature(certificates[ii]) &&
                                !X509Utils.IsECDsaSignature(certificate))
                            {
                                deleteCert = true;
                            }

                            if (deleteCert)
                            {
                                m_logger.LogInformation(
                                    "Delete Certificate {Certificate} from trusted store.",
                                    certificate.AsLogSafeString());
                                await store.DeleteAsync(certificates[ii].Thumbprint, ct)
                                    .ConfigureAwait(false);
                                break;
                            }
                        }
                    }

                    // add new certificate.
                    using X509Certificate2 publicKey = CertificateFactory.Create(certificate.RawData);
                    await store.AddAsync(publicKey, ct: ct).ConfigureAwait(false);

                    m_logger.LogInformation("Added application certificate to trusted peer store.");
                }
                finally
                {
                    store.Close();
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(
                    "Could not add certificate to trusted peer store: {ErrorMessage}",
                    Redaction.Redact.Create(e));
            }
        }

        /// <summary>
        /// Show a message for approval and return result.
        /// </summary>
        /// <returns>True if approved, false otherwise.</returns>
        private async Task<bool> ApproveMessageAsync(string message, bool silent)
        {
            if (!silent && MessageDlg != null)
            {
                MessageDlg.Message(message, true);
                return await MessageDlg.ShowAsync().ConfigureAwait(false);
            }
            m_logger.LogError("Approve Message prompt: {Message} -> Rejected", message);
            return false;
        }

        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
    }
}
