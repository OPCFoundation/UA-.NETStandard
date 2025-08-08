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
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Opc.Ua.Utils;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// A class that install, configures and runs a UA application.
    /// </summary>
    public class ApplicationInstance
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationInstance"/> class.
        /// </summary>
        public ApplicationInstance()
        {
            DisableCertificateAutoCreation = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationInstance"/> class.
        /// </summary>
        /// <param name="applicationConfiguration">The application configuration.</param>
        public ApplicationInstance(ApplicationConfiguration applicationConfiguration)
            : this()
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
        /// Gets or sets the name of the config section containing the path to the application configuration file.
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
        public ServerBase Server => m_server;

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
        /// Processes the command line.
        /// </summary>
        /// <returns>
        /// True if the arguments were processed; False otherwise.
        /// </returns>
        public bool ProcessCommandLine()
        {
            // ignore processing of command line
            return false;
        }

        /// <summary>
        /// Starts the UA server as a Windows Service.
        /// </summary>
        /// <param name="server">The server.</param>
        public void StartAsService(ServerBase server)
        {
            throw new NotImplementedException(".NetStandard Opc.Ua libraries do not support to start as a windows service");
        }
        /// <summary>
        /// Starts the UA server.
        /// </summary>
        /// <param name="server">The server.</param>
        [Obsolete("Use StartAsync(ServerBase server) instead.")]
        public Task Start(ServerBase server)
        {
            return StartAsync(server);
        }

        /// <summary>
        /// Starts the UA server.
        /// </summary>
        /// <param name="server">The server.</param>
        public async Task StartAsync(ServerBase server)
        {
            m_server = server;

            if (ApplicationConfiguration == null)
            {
                await LoadApplicationConfiguration(false).ConfigureAwait(false);
            }

            server.Start(ApplicationConfiguration);
        }

        /// <summary>
        /// Stops the UA server.
        /// </summary>
        public void Stop()
        {
            m_server.Stop();
        }

        /// <summary>
        /// Loads the configuration.
        /// </summary>
        public async Task<ApplicationConfiguration> LoadAppConfig(
            bool silent,
            string filePath,
            ApplicationType applicationType,
            Type configurationType,
            bool applyTraceSettings,
            ICertificatePasswordProvider certificatePasswordProvider = null)
        {
            LogInfo("Loading application configuration file. {0}", filePath);

            try
            {
                // load the configuration file.
                return await ApplicationConfiguration.LoadAsync(
                    new FileInfo(filePath),
                    applicationType,
                    configurationType,
                    applyTraceSettings,
                    certificatePasswordProvider)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                LogError(e, "Could not load configuration file. {0}", filePath);

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
        [Obsolete("Use LoadAppConfigAsync instead.")]
        public Task<ApplicationConfiguration> LoadAppConfig(
            bool silent,
            Stream stream,
            ApplicationType applicationType,
            Type configurationType,
            bool applyTraceSettings,
            ICertificatePasswordProvider certificatePasswordProvider = null)
        {
            return LoadAppConfigAsync(
                silent, stream, applicationType, configurationType, applyTraceSettings, certificatePasswordProvider);
        }

        /// <summary>
        /// Loads the configuration.
        /// </summary>
        public async Task<ApplicationConfiguration> LoadAppConfigAsync(
            bool silent,
            Stream stream,
            ApplicationType applicationType,
            Type configurationType,
            bool applyTraceSettings,
            ICertificatePasswordProvider certificatePasswordProvider = null)
        {
            LogInfo("Loading application from stream.");

            try
            {
                // load the configuration file.
                return await ApplicationConfiguration.LoadAsync(
                    stream,
                    applicationType,
                    configurationType,
                    applyTraceSettings,
                    certificatePasswordProvider)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                LogError(e, "Could not load configuration from stream.");

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
        /// Loads the application configuration.
        /// </summary>
        [Obsolete("Use LoadApplicationConfigurationAsync instead.")]
        public Task<ApplicationConfiguration> LoadApplicationConfiguration(Stream stream, bool silent)
        {
            return LoadApplicationConfigurationAsync(stream, silent);
        }

        /// <summary>
        /// Loads the application configuration.
        /// </summary>
        public async Task<ApplicationConfiguration> LoadApplicationConfigurationAsync(Stream stream, bool silent)
        {
            ApplicationConfiguration configuration = null;

            try
            {
                configuration = await LoadAppConfigAsync(
                    silent, stream, ApplicationType, ConfigurationType, true, CertificatePasswordProvider)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (silent)
            {
            }

            if (configuration == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "Could not load configuration.");
            }

            ApplicationConfiguration = FixupAppConfig(configuration);

            return configuration;
        }

        /// <summary>
        /// Loads the application configuration.
        /// </summary>
        public async Task<ApplicationConfiguration> LoadApplicationConfiguration(string filePath, bool silent)
        {
            ApplicationConfiguration configuration = null;

            try
            {
                configuration = await LoadAppConfig(
                    silent, filePath, ApplicationType, ConfigurationType, true, CertificatePasswordProvider)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (silent)
            {
            }

            if (configuration == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "Could not load configuration file.");
            }

            ApplicationConfiguration = FixupAppConfig(configuration);

            return configuration;
        }

        /// <summary>
        /// Loads the application configuration.
        /// </summary>
        public Task<ApplicationConfiguration> LoadApplicationConfiguration(bool silent)
        {
            string filePath = ApplicationConfiguration.GetFilePathFromAppConfig(ConfigSectionName);

            return LoadApplicationConfiguration(filePath, silent);
        }

        /// <summary>
        /// Helper to replace localhost with the hostname
        /// in the application uri and base addresses of the
        /// configuration.
        /// </summary>
        /// <param name="configuration"></param>
        public static ApplicationConfiguration FixupAppConfig(
            ApplicationConfiguration configuration)
        {
            configuration.ApplicationUri = ReplaceLocalhost(configuration.ApplicationUri);
            if (configuration.ServerConfiguration != null)
            {
                for (int i = 0; i < configuration.ServerConfiguration.BaseAddresses.Count; i++)
                {
                    configuration.ServerConfiguration.BaseAddresses[i] =
                        ReplaceLocalhost(configuration.ServerConfiguration.BaseAddresses[i]);
                }
            }
            return configuration;
        }

        /// <summary>
        /// Create a builder for a UA application configuration.
        /// </summary>
        public IApplicationConfigurationBuilderTypes Build(
            string applicationUri,
            string productUri
            )
        {
            // App Uri and cert subject
            ApplicationConfiguration = new ApplicationConfiguration {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType,
                ApplicationUri = applicationUri,
                ProductUri = productUri,
                TraceConfiguration = new TraceConfiguration {
                    TraceMasks = TraceMasks.None
                },
                TransportQuotas = new TransportQuotas()
            };

            // Trace off
            ApplicationConfiguration.TraceConfiguration.ApplySettings();

            return new ApplicationConfigurationBuilder(this);
        }

        /// <summary>
        /// Checks for a valid application instance certificate.
        /// </summary>
        /// <param name="silent">if set to <c>true</c> no dialogs will be displayed.</param>
        public Task<bool> CheckApplicationInstanceCertificates(
            bool silent)
        {
            return CheckApplicationInstanceCertificates(silent, CertificateFactory.DefaultLifeTime);
        }

        /// <summary>
        /// Deletes all application certificates.
        /// </summary>
        public async Task DeleteApplicationInstanceCertificate(string[] profileIds = null, CancellationToken ct = default)
        {
            // TODO: delete only selected profiles
            if (ApplicationConfiguration == null)
            {
                throw new ArgumentException("Missing configuration.");
            }

            foreach (CertificateIdentifier id in ApplicationConfiguration.SecurityConfiguration.ApplicationCertificates)
            {
                await DeleteApplicationInstanceCertificateAsync(ApplicationConfiguration, id, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Checks for a valid application instance certificate.
        /// </summary>
        /// <param name="silent">if set to <c>true</c> no dialogs will be displayed.</param>
        /// <param name="lifeTimeInMonths">The lifetime in months.</param>
        /// <param name="ct"></param>
        public async Task<bool> CheckApplicationInstanceCertificates(
            bool silent,
            ushort lifeTimeInMonths,
            CancellationToken ct = default)
        {
            LogInfo("Checking application instance certificate.");

            if (ApplicationConfiguration == null)
            {
                await LoadApplicationConfiguration(silent).ConfigureAwait(false);
            }

            // find the existing certificates.
            SecurityConfiguration securityConfiguration = ApplicationConfiguration.SecurityConfiguration;

            if (securityConfiguration.ApplicationCertificates.Count == 0)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError, "Need at least one Application Certificate.");
            }

            bool result = true;
            foreach (CertificateIdentifier certId in securityConfiguration.ApplicationCertificates)
            {
                ushort minimumKeySize = certId.GetMinKeySize(securityConfiguration);
                bool nextResult = await CheckCertificateTypeAsync(certId, silent, minimumKeySize, lifeTimeInMonths, ct).ConfigureAwait(false);
                result = result && nextResult;
            }

            return result;
        }

        /// <summary>
        /// Check certificate type
        /// </summary>
        /// <param name="id"></param>
        /// <param name="silent"></param>
        /// <param name="minimumKeySize"></param>
        /// <param name="lifeTimeInMonths"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async Task<bool> CheckCertificateTypeAsync(
            CertificateIdentifier id,
            bool silent,
            ushort minimumKeySize,
            ushort lifeTimeInMonths,
            CancellationToken ct = default
            )
        {
            ApplicationConfiguration configuration = ApplicationConfiguration;

            if (id == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                    "Configuration file does not specify a certificate.");
            }

            // reload the certificate from disk in the cache.
            ICertificatePasswordProvider passwordProvider = configuration.SecurityConfiguration.CertificatePasswordProvider;
            await id.LoadPrivateKeyExAsync(passwordProvider, configuration.ApplicationUri).ConfigureAwait(false);

            // load the certificate
            X509Certificate2 certificate = await id.FindAsync(true, configuration.ApplicationUri).ConfigureAwait(false);

            // check that it is ok.
            if (certificate != null)
            {
                LogCertificate("Check certificate:", certificate);
                bool certificateValid = await CheckApplicationInstanceCertificateAsync(configuration, id, certificate, silent, minimumKeySize, ct).ConfigureAwait(false);

                if (!certificateValid)
                {
                    var message = new StringBuilder();
                    message.AppendLine("The certificate with subject {0} in the configuration is invalid.");
                    message.AppendLine(" Please update or delete the certificate from this location:");
                    message.AppendLine(" {1}");
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                        message.ToString(), id.SubjectName, ReplaceSpecialFolderNames(id.StorePath)
                        );
                }
            }
            else
            {
                // check for missing private key.
                certificate = await id.FindAsync(false, configuration.ApplicationUri).ConfigureAwait(false);

                if (certificate != null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                        "Cannot access certificate private key. Subject={0}", certificate.Subject);
                }

                // check for missing thumbprint.
                if (!string.IsNullOrEmpty(id.Thumbprint))
                {
                    if (!string.IsNullOrEmpty(id.SubjectName))
                    {
                        var id2 = new CertificateIdentifier {
                            StoreType = id.StoreType,
                            StorePath = id.StorePath,
                            SubjectName = id.SubjectName
                        };
                        certificate = await id2.FindAsync(true, configuration.ApplicationUri).ConfigureAwait(false);
                    }

                    if (certificate != null)
                    {
                        var message = new StringBuilder();
                        message.AppendLine("Thumbprint was explicitly specified in the configuration.");
                        message.AppendLine("Another certificate with the same subject name was found.");
                        message.AppendLine("Use it instead?");
                        message.AppendLine("Requested: {0}");
                        message.AppendLine("Found: {1}");
                        if (!await ApproveMessageAsync(Format(message.ToString(), id.SubjectName, certificate.Subject), silent).ConfigureAwait(false))
                        {
                            throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                                message.ToString(), id.SubjectName, certificate.Subject);
                        }
                    }
                    else
                    {
                        var message = new StringBuilder();
                        message.AppendLine("Thumbprint was explicitly specified in the configuration.");
                        message.AppendLine("Cannot generate a new certificate.");
                        throw ServiceResultException.Create(StatusCodes.BadConfigurationError, message.ToString());
                    }
                }
            }

            if (certificate == null)
            {
                if (!DisableCertificateAutoCreation)
                {
                    certificate = await CreateApplicationInstanceCertificateAsync(configuration, id,
                        lifeTimeInMonths, ct).ConfigureAwait(false);
                }
                else
                {
                    LogWarning("Application Instance certificate auto creation is disabled.");
                }

                if (certificate == null)
                {
                    var message = new StringBuilder();
                    message.AppendLine("There is no cert with subject {0} in the configuration.");
                    message.AppendLine(" Please generate a cert for your application,");
                    message.AppendLine(" then copy the new cert to this location:");
                    message.AppendLine(" {1}");
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                        message.ToString(), id.SubjectName, id.StorePath
                        );
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
        /// <returns></returns>
        public async Task AddOwnCertificateToTrustedStoreAsync(X509Certificate2 certificate, CancellationToken ct)
        {
            await AddToTrustedStoreAsync(ApplicationConfiguration, certificate, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Helper to suppress errors which are allowed for the application certificate validation.
        /// </summary>
        private class CertValidationSuppressibleStatusCodes
        {
            public StatusCode[] ApprovedCodes { get; }

            public CertValidationSuppressibleStatusCodes(StatusCode[] approvedCodes)
            {
                ApprovedCodes = approvedCodes;
            }

            public void OnCertificateValidation(object sender, CertificateValidationEventArgs e)
            {
                if (ApprovedCodes.Contains(e.Error.StatusCode))
                {
                    LogWarning("Application Certificate Validation suppressed {0}", e.Error.StatusCode);
                    e.Accept = true;
                }
            }
        }

        /// <summary>
        /// Creates an application instance certificate if one does not already exist.
        /// </summary>
        private static async Task<bool> CheckApplicationInstanceCertificateAsync(
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
            var certValidator = new CertValidationSuppressibleStatusCodes(
                [
                    StatusCodes.BadCertificateUntrusted,
                    StatusCodes.BadCertificateTimeInvalid,
                    StatusCodes.BadCertificateIssuerTimeInvalid,
                    StatusCodes.BadCertificateHostNameInvalid,
                    StatusCodes.BadCertificateRevocationUnknown,
                    StatusCodes.BadCertificateIssuerRevocationUnknown,
                ]);

            LogCertificate("Check application instance certificate.", certificate);

            try
            {
                // validate certificate.
                configuration.CertificateValidator.CertificateValidation += certValidator.OnCertificateValidation;
                await configuration.CertificateValidator.ValidateAsync(
                    certificate.HasPrivateKey ?
                    X509CertificateLoader.LoadCertificate(certificate.RawData) : certificate, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                string message = Format(
                    "Error validating certificate. Exception: {0}. Use certificate anyway?", ex.Message);
                if (!await ApproveMessageAsync(message, silent).ConfigureAwait(false))
                {
                    return false;
                }
            }
            finally
            {
                configuration.CertificateValidator.CertificateValidation -= certValidator.OnCertificateValidation;
            }

            // check key size
            int keySize = X509Utils.GetPublicKeySize(certificate);
            if (minimumKeySize > keySize)
            {
                string message = Format(
                    "The key size ({0}) in the certificate is less than the minimum provided ({1}). Use certificate anyway?",
                    keySize,
                    minimumKeySize);

                if (!await ApproveMessageAsync(message, silent).ConfigureAwait(false))
                {
                    return false;
                }
            }

            // check domains.
            if (configuration.ApplicationType != ApplicationType.Client && !await CheckDomainsInCertificateAsync(configuration, certificate, silent, ct).ConfigureAwait(false))
            {
                return false;
            }

            // check uri.
            string applicationUri = X509Utils.GetApplicationUriFromCertificate(certificate);

            if (string.IsNullOrEmpty(applicationUri))
            {
                const string message = "The Application URI could not be read from the certificate. Use certificate anyway?";
                if (!await ApproveMessageAsync(message, silent).ConfigureAwait(false))
                {
                    return false;
                }
            }
            else if (!configuration.ApplicationUri.Equals(applicationUri, StringComparison.Ordinal))
            {
                LogInfo("Updated the ApplicationUri: {0} --> {1}", configuration.ApplicationUri, applicationUri);
                configuration.ApplicationUri = applicationUri;
            }

            LogInfo("Using the ApplicationUri: {0}", applicationUri);

            // update configuration.
            id.Certificate = certificate;

            return true;
        }

        /// <summary>
        /// Checks that the domains in the server addresses match the domains in the certificates.
        /// </summary>
        private static async Task<bool> CheckDomainsInCertificateAsync(
            ApplicationConfiguration configuration,
            X509Certificate2 certificate,
            bool silent,
            CancellationToken ct)
        {
            LogInfo("Check domains in certificate.");

            bool valid = true;
            IList<string> serverDomainNames = configuration.GetServerDomainNames();
            IList<string> certificateDomainNames = X509Utils.GetDomainsFromCertificate(certificate);

            LogInfo("Server Domain names:");
            foreach (string name in serverDomainNames)
            {
                LogInfo(" {0}", name);
            }

            LogInfo("Certificate Domain names:");
            foreach (string name in certificateDomainNames)
            {
                LogInfo(" {0}", name);
            }

            // get computer name.
            string computerName = GetHostName();

            // get IP addresses.
            IPAddress[] addresses = null;

            for (int ii = 0; ii < serverDomainNames.Count; ii++)
            {
                if (FindStringIgnoreCase(certificateDomainNames, serverDomainNames[ii]))
                {
                    continue;
                }

                if (string.Equals(serverDomainNames[ii], "localhost", StringComparison.OrdinalIgnoreCase))
                {
                    if (FindStringIgnoreCase(certificateDomainNames, computerName))
                    {
                        continue;
                    }

                    // check for aliases.
                    bool found = false;

                    // get IP addresses only if necessary.
                    if (addresses == null)
                    {
                        addresses = await GetHostAddressesAsync(computerName).ConfigureAwait(false);
                    }

                    // check for ip addresses.
                    for (int jj = 0; jj < addresses.Length; jj++)
                    {
                        if (FindStringIgnoreCase(certificateDomainNames, addresses[jj].ToString()))
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

                string message = Format(
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
        /// <param name="ct"></param>
        /// <returns>The new certificate</returns>
        private static async Task<X509Certificate2> CreateApplicationInstanceCertificateAsync(
            ApplicationConfiguration configuration,
            CertificateIdentifier id,
            ushort lifeTimeInMonths,
            CancellationToken ct)
        {
            // delete any existing certificate.
            await DeleteApplicationInstanceCertificateAsync(configuration, id, ct).ConfigureAwait(false);

            LogInfo("Creating application instance certificate.");

            // get the domains from the configuration file.
            IList<string> serverDomainNames = configuration.GetServerDomainNames();

            if (serverDomainNames.Count == 0)
            {
                serverDomainNames.Add(GetHostName());
            }

            // ensure the certificate store directory exists.
            if (id.StoreType == CertificateStoreType.Directory)
            {
                GetAbsoluteDirectoryPath(id.StorePath, true, true, true);
            }

            Security.Certificates.ICertificateBuilder builder = CertificateFactory.CreateCertificate(
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
                id.Certificate = builder
                    .SetRSAKeySize(CertificateFactory.DefaultKeySize)
                    .CreateForRSA();

                LogCertificate("Certificate created for RSA.", id.Certificate);
            }
            else
            {
#if !ECC_SUPPORT
                throw new ServiceResultException(StatusCodes.BadConfigurationError, "The Ecc certificate type is not supported.");
#else
                ECCurve? curve = EccUtils.GetCurveFromCertificateTypeId(id.CertificateType);

                if (curve == null)
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError, "The Ecc certificate type is not supported.");
                }

                id.Certificate = builder
                    .SetECCurve(curve.Value)
                    .CreateForECDsa();

                LogCertificate("Certificate created for {0}.", id.Certificate, curve.Value.Oid.FriendlyName);
#endif
            }

            ICertificatePasswordProvider passwordProvider = configuration.SecurityConfiguration.CertificatePasswordProvider;
            await id.Certificate.AddToStoreAsync(
                id.StoreType,
                id.StorePath,
                passwordProvider?.GetPassword(id),
                ct).ConfigureAwait(false);

            // ensure the certificate is trusted.
            if (configuration.SecurityConfiguration.AddAppCertToTrustedStore)
            {
                await AddToTrustedStoreAsync(configuration, id.Certificate, ct).ConfigureAwait(false);
            }

            // reload the certificate from disk.
            id.Certificate = await id.LoadPrivateKeyExAsync(passwordProvider, configuration.ApplicationUri).ConfigureAwait(false);

            await configuration.CertificateValidator.UpdateAsync(configuration.SecurityConfiguration).ConfigureAwait(false);

            LogCertificate("Certificate created for {0}.", id.Certificate, configuration.ApplicationUri);

            // do not dispose temp cert, or X509Store certs become unusable

            return id.Certificate;
        }

        /// <summary>
        /// Deletes an existing application instance certificate.
        /// </summary>
        /// <param name="configuration">The configuration instance that stores the configurable information for a UA application.</param>
        /// <param name="id">The certificate identifier.</param>
        /// <param name="ct"></param>
        private static async Task DeleteApplicationInstanceCertificateAsync(ApplicationConfiguration configuration, CertificateIdentifier id, CancellationToken ct)
        {
            if (id == null)
            {
                return;
            }

            // delete certificate and private key.
            X509Certificate2 certificate = await id.FindAsync(configuration.ApplicationUri).ConfigureAwait(false);
            if (certificate != null)
            {
                LogCertificate(TraceMasks.Security, "Deleting application instance certificate and private key.", certificate);
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
                    ICertificateStore store = configuration.SecurityConfiguration.TrustedPeerCertificates.OpenStore();
                    if (store != null)
                    {
                        try
                        {
                            bool deleted = await store.DeleteAsync(thumbprint).ConfigureAwait(false);
                            if (deleted)
                            {
                                LogInfo(TraceMasks.Security, "Application Instance Certificate [{0}] deleted from trusted store.", thumbprint);
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
                using (ICertificateStore store = id.OpenStore())
                {
                    bool deleted = await store.DeleteAsync(certificate.Thumbprint).ConfigureAwait(false);
                    if (deleted)
                    {
                        LogCertificate(TraceMasks.Security, "Application certificate and private key deleted.", certificate);
                    }
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
        private static async Task AddToTrustedStoreAsync(ApplicationConfiguration configuration, X509Certificate2 certificate, CancellationToken ct)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            string storePath = null;

            if (configuration != null && configuration.SecurityConfiguration != null && configuration.SecurityConfiguration.TrustedPeerCertificates != null)
            {
                storePath = configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath;
            }

            if (string.IsNullOrEmpty(storePath))
            {
                LogWarning("WARNING: Trusted peer store not specified.");
                return;
            }

            try
            {
                ICertificateStore store = configuration.SecurityConfiguration.TrustedPeerCertificates.OpenStore();

                if (store == null)
                {
                    LogWarning("Could not open trusted peer store.");
                    return;
                }

                try
                {
                    // check if it already exists.
                    X509Certificate2Collection existingCertificates = await store.FindByThumbprintAsync(certificate.Thumbprint).ConfigureAwait(false);

                    if (existingCertificates.Count > 0)
                    {
                        return;
                    }

                    LogCertificate("Adding application certificate to trusted peer store.", certificate);

                    List<string> subjectName = X509Utils.ParseDistinguishedName(certificate.Subject);

                    // check for old certificate.
                    X509Certificate2Collection certificates = await store.EnumerateAsync().ConfigureAwait(false);

                    for (int ii = 0; ii < certificates.Count; ii++)
                    {
                        if (X509Utils.CompareDistinguishedName(certificates[ii], subjectName))
                        {
                            if (certificates[ii].Thumbprint == certificate.Thumbprint)
                            {
                                return;
                            }

                            bool deleteCert = false;
                            if (X509Utils.IsECDsaSignature(certificates[ii]) && X509Utils.IsECDsaSignature(certificate))
                            {
                                if (X509Utils.GetECDsaQualifier(certificates[ii]).Equals(X509Utils.GetECDsaQualifier(certificate), StringComparison.Ordinal))
                                {
                                    deleteCert = true;
                                }
                            }
                            else if (!X509Utils.IsECDsaSignature(certificates[ii]) && !X509Utils.IsECDsaSignature(certificate))
                            {
                                deleteCert = true;
                            }

                            if (deleteCert)
                            {
                                LogCertificate("Delete Certificate from trusted store.", certificate);
                                await store.DeleteAsync(certificates[ii].Thumbprint).ConfigureAwait(false);
                                break;
                            }
                        }
                    }

                    // add new certificate.
                    X509Certificate2 publicKey = X509CertificateLoader.LoadCertificate(certificate.RawData);

                    await store.AddAsync(publicKey).ConfigureAwait(false);

                    LogInfo("Added application certificate to trusted peer store.");
                }
                finally
                {
                    store.Close();
                }
            }
            catch (Exception e)
            {
                LogError("Could not add certificate to trusted peer store: {0}", Redaction.Redact.Create(e));
            }
        }

        /// <summary>
        /// Show a message for approval and return result.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="silent"></param>
        /// <returns>True if approved, false otherwise.</returns>
        private static async Task<bool> ApproveMessageAsync(string message, bool silent)
        {
            if (!silent && MessageDlg != null)
            {
                MessageDlg.Message(message, true);
                return await MessageDlg.ShowAsync().ConfigureAwait(false);
            }
            else
            {
                LogError(message);
                return false;
            }
        }

        private ServerBase m_server;
    }
}
