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
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Configuration
{
    /// <summary>
    ///  Interface of the application instance.
    /// </summary>
    public interface IApplicationInstance
    {
        /// <summary>
        /// Gets or sets the name of the application.
        /// </summary>
        /// <value>The name of the application.</value>
        string ApplicationName { get; set; }

        /// <summary>
        /// Gets or sets the type of the application.
        /// </summary>
        /// <value>The type of the application.</value>
        ApplicationType ApplicationType { get; set; }

        /// <summary>
        /// Gets or sets the name of the config section containing the path to the application configuration file.
        /// </summary>
        /// <value>The name of the config section.</value>
        string ConfigSectionName { get; set; }

        /// <summary>
        /// Gets or sets the type of configuration file.
        /// </summary>
        /// <value>The type of configuration file.</value>
        Type ConfigurationType { get; set; }

        /// <summary>
        /// Gets the server.
        /// </summary>
        /// <value>The server.</value>
        IServerBase Server { get; }

        /// <summary>
        /// Gets the application configuration used when the Start() method was called.
        /// </summary>
        /// <value>The application configuration.</value>
        ApplicationConfiguration ApplicationConfiguration { get; set; }

        /// <summary>
        /// Get or set the certificate password provider.
        /// </summary>
        ICertificatePasswordProvider CertificatePasswordProvider { get; set; }

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
        bool DisableCertificateAutoCreation { get; set; }

        /// <summary>
        /// Processes the command line.
        /// </summary>
        /// <returns>
        /// True if the arguments were processed; False otherwise.
        /// </returns>
        bool ProcessCommandLine();

        /// <summary>
        /// Starts the UA server as a Windows Service.
        /// </summary>
        /// <param name="server">The server.</param>
        void StartAsService(IServerBase server);

        /// <summary>
        /// Starts the UA server.
        /// </summary>
        /// <param name="server">The server.</param>
        Task Start(IServerBase server);

        /// <summary>
        /// Stops the UA server.
        /// </summary>
        void Stop();

        /// <summary>
        /// Loads the configuration.
        /// </summary>
        Task<ApplicationConfiguration> LoadAppConfig(
            bool silent,
            string filePath,
            ApplicationType applicationType,
            Type configurationType,
            bool applyTraceSettings,
            ICertificatePasswordProvider certificatePasswordProvider = null);

        /// <summary>
        /// Loads the configuration.
        /// </summary>
        Task<ApplicationConfiguration> LoadAppConfig(
            bool silent,
            Stream stream,
            ApplicationType applicationType,
            Type configurationType,
            bool applyTraceSettings,
            ICertificatePasswordProvider certificatePasswordProvider = null);

        /// <summary>
        /// Loads the application configuration.
        /// </summary>
        Task<ApplicationConfiguration> LoadApplicationConfiguration(Stream stream, bool silent);

        /// <summary>
        /// Loads the application configuration.
        /// </summary>
        Task<ApplicationConfiguration> LoadApplicationConfiguration(string filePath, bool silent);

        /// <summary>
        /// Loads the application configuration.
        /// </summary>
        Task<ApplicationConfiguration> LoadApplicationConfiguration(bool silent);

        /// <summary>
        /// Create a builder for a UA application configuration.
        /// </summary>
        IApplicationConfigurationBuilderTypes Build(
            string applicationUri,
            string productUri
        );

        /// <summary>
        /// Checks for a valid application instance certificate.
        /// </summary>
        /// <param name="silent">if set to <c>true</c> no dialogs will be displayed.</param>
        /// <param name="minimumKeySize">Minimum size of the key.</param>
        Task<bool> CheckApplicationInstanceCertificate(
            bool silent,
            ushort minimumKeySize);

        /// <summary>
        /// Checks for a valid application instance certificate.
        /// </summary>
        /// <param name="silent">if set to <c>true</c> no dialogs will be displayed.</param>
        /// <param name="minimumKeySize">Minimum size of the key.</param>
        /// <param name="lifeTimeInMonths">The lifetime in months.</param>
        /// <param name="ct"></param>
        Task<bool> CheckApplicationInstanceCertificate(
            bool silent,
            ushort minimumKeySize,
            ushort lifeTimeInMonths,
            CancellationToken ct = default);

        /// <summary>
        /// Checks for a valid application instance certificate.
        /// </summary>
        /// <param name="silent">if set to <c>true</c> no dialogs will be displayed.</param>
        Task<bool> CheckApplicationInstanceCertificates(
            bool silent);

        /// <summary>
        /// Checks for a valid application instance certificate.
        /// </summary>
        /// <param name="silent">if set to <c>true</c> no dialogs will be displayed.</param>
        /// <param name="lifeTimeInMonths">The lifetime in months.</param>
        /// <param name="ct"></param>
        Task<bool> CheckApplicationInstanceCertificates(
            bool silent,
            ushort lifeTimeInMonths,
            CancellationToken ct = default);

        /// <summary>
        /// Deletes all application certificates.
        /// </summary>
        Task DeleteApplicationInstanceCertificate(string[] profileIds = null, CancellationToken ct = default);

        /// <summary>
        /// Adds a Certificate to the Trusted Store of the Application, needed e.g. for the GDS to trust it´s own CA
        /// </summary>
        /// <param name="certificate">The certificate to add to the store</param>
        /// <param name="ct">The cancellation token</param>
        /// <returns></returns>
        Task AddOwnCertificateToTrustedStoreAsync(X509Certificate2 certificate, CancellationToken ct);
    }
}
