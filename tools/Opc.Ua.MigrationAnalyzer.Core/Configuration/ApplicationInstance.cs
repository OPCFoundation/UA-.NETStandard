/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// A class that install, configures and runs a UA application.
    /// </summary>
    public static class ApplicationInstanceObsolete
    {
        /// <summary>
        /// Creates and updates the application configuration.
        /// </summary>
        [Obsolete("Use CreateAsync instead")]
        public static Task<ApplicationConfiguration> Create(this IApplicationConfigurationBuilderCreate builder)
        {
            return builder.CreateAsync();
        }

        /// <summary>
        /// Processes the command line.
        /// </summary>
        /// <returns>
        /// True if the arguments were processed; False otherwise.
        /// </returns>
        [Obsolete("This call has been a no-op for several releases and will be removed")]
#pragma warning disable RCS1175 // Unused 'this' parameter
        public static bool ProcessCommandLine(this ApplicationInstance application)
#pragma warning restore RCS1175 // Unused 'this' parameter
        {
            // ignore processing of command line
            return false;
        }

        /// <summary>
        /// Starts the UA server as a Windows Service.
        /// </summary>
        /// <param name="application"></param>
        /// <param name="server">The server.</param>
        /// <exception cref="NotImplementedException"></exception>
        [Obsolete(".NetStandard Opc.Ua libraries do not support to start as a windows service")]
#pragma warning disable RCS1175 // Unused 'this' parameter
        public static void StartAsService(this ApplicationInstance application, ServerBase server)
#pragma warning restore RCS1175 // Unused 'this' parameter
        {
        }

        /// <summary>
        /// Starts the UA server.
        /// </summary>
        /// <param name="application"></param>
        /// <param name="server">The server.</param>
        [Obsolete("Use StartAsync(ServerBase server) instead.")]
        public static Task Start(this ApplicationInstance application, ServerBase server)
        {
            return application.StartAsync(server);
        }

        /// <summary>
        /// Load application configuration
        /// </summary>
        [Obsolete("Use LoadAppConfigAsync instead.")]
        public static Task<ApplicationConfiguration?> LoadAppConfig(
            this ApplicationInstance application,
            bool silent,
            string filePath,
            ApplicationType applicationType,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type configurationType,
            bool applyTraceSettings,
            ICertificatePasswordProvider? certificatePasswordProvider = null)
        {
            return application.LoadAppConfigAsync(
                    silent,
                    filePath,
                    applicationType,
                    configurationType,
                    applyTraceSettings,
                    certificatePasswordProvider)
                .AsTask();
        }

        /// <summary>
        /// Loads the configuration.
        /// </summary>
        [Obsolete("Use LoadAppConfigAsync instead.")]
        public static Task<ApplicationConfiguration?> LoadAppConfig(
            this ApplicationInstance application,
            bool silent,
            Stream stream,
            ApplicationType applicationType,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type configurationType,
            bool applyTraceSettings,
            ICertificatePasswordProvider? certificatePasswordProvider = null)
        {
            return application.LoadAppConfigAsync(
                    silent,
                    stream,
                    applicationType,
                    configurationType,
                    applyTraceSettings,
                    certificatePasswordProvider)
                .AsTask();
        }

        /// <summary>
        /// Load configuration from stream
        /// </summary>
        [Obsolete("Use LoadApplicationConfigurationAsync instead.")]
        public static Task<ApplicationConfiguration> LoadApplicationConfiguration(
            this ApplicationInstance application,
            Stream stream,
            bool silent)
        {
            return application.LoadApplicationConfigurationAsync(stream, silent);
        }

        /// <summary>
        /// Load configuration from stream
        /// </summary>
        [Obsolete("Use LoadApplicationConfigurationAsync instead.")]
        public static Task<ApplicationConfiguration> LoadApplicationConfiguration(
            this ApplicationInstance application,
            string filePath,
            bool silent)
        {
            return application.LoadApplicationConfigurationAsync(filePath, silent).AsTask();
        }

        /// <summary>
        /// Load configuration from default file
        /// </summary>
        [Obsolete("Use LoadApplicationConfigurationAsync instead.")]
        public static Task<ApplicationConfiguration> LoadApplicationConfiguration(
            this ApplicationInstance application,
            bool silent)
        {
            return application.LoadApplicationConfigurationAsync(silent).AsTask();
        }

        /// <summary>
        /// Check the application instance certificates.
        /// </summary>
        [Obsolete("Use CheckApplicationInstanceCertificatesAsync instead.")]
        public static Task<bool> CheckApplicationInstanceCertificates(this ApplicationInstance application, bool silent)
        {
            return application.CheckApplicationInstanceCertificatesAsync(silent).AsTask();
        }

        /// <summary>
        /// Check the application instance certificates.
        /// </summary>
        [Obsolete("Use CheckApplicationInstanceCertificatesAsync instead.")]
        public static Task<bool> CheckApplicationInstanceCertificates(
            this ApplicationInstance application,
            bool silent,
            ushort lifeTimeInMonths,
            CancellationToken ct = default)
        {
            return application.CheckApplicationInstanceCertificatesAsync(silent, lifeTimeInMonths, ct).AsTask();
        }

        /// <summary>
        /// Delete the application instance certificates.
        /// </summary>
        [Obsolete("Use DeleteApplicationInstanceCertificateAsync instead.")]
        public static Task DeleteApplicationInstanceCertificate(
            this ApplicationInstance application,
            string[]? profileIds = null,
            CancellationToken ct = default)
        {
            return application.DeleteApplicationInstanceCertificateAsync(profileIds, ct).AsTask();
        }
    }
}
