/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// The certificate store root the hosting layers use when no PkiRoot is configured.
    /// </summary>
    public static class DefaultPkiRoot
    {
        /// <summary>
        /// Returns <c>{LocalApplicationData}/OPC Foundation/{applicationName}/pki</c>.
        /// The shared temporary directory is not used because on Linux/macOS other
        /// local users can pre-create it and plant trusted certificates or read the
        /// private key. Earlier versions used it; when certificate stores exist there
        /// but not at the returned root, a warning is logged so an upgrade does not
        /// silently replace the application certificate.
        /// </summary>
        /// <param name="applicationName">The application name.</param>
        /// <param name="logger">Optional logger for the upgrade warning.</param>
        /// <exception cref="ArgumentNullException"><paramref name="applicationName"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException">No per-user application-data
        /// directory is available (e.g. HOME is not set); configure the PkiRoot.</exception>
        public static string Get(string applicationName, ILogger? logger = null)
        {
            if (applicationName == null)
            {
                throw new ArgumentNullException(nameof(applicationName));
            }

            string appData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrEmpty(appData))
            {
                throw ServiceResultException.ConfigurationError(
                    "No per-user application data directory is available for the " +
                    "certificate stores. Configure the PkiRoot.");
            }

            string pkiRoot = Path.Combine(appData, "OPC Foundation", applicationName, "pki");
            if (logger != null && !Directory.Exists(pkiRoot))
            {
                string legacyPkiRoot = Path.Combine(
                    Path.GetTempPath(),
                    "OPC Foundation",
                    applicationName,
                    "pki");
                if (Directory.Exists(legacyPkiRoot))
                {
                    logger.CertificateStoresFoundInLegacyTempRoot(legacyPkiRoot, pkiRoot);
                }
            }

            return pkiRoot;
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="DefaultPkiRoot"/>.
    /// </summary>
    internal static partial class DefaultPkiRootLog
    {
        [LoggerMessage(EventId = ConfigurationEventIds.DefaultPkiRoot + 0, Level = LogLevel.Warning,
            Message = "Certificate stores of an earlier version exist in the temporary directory " +
                "{LegacyPkiRoot}, but the default PKI root is now {PkiRoot} and new certificates are " +
                "created there. Configure PkiRoot, or move the stores, to keep the existing " +
                "application certificate and trust lists.")]
        public static partial void CertificateStoresFoundInLegacyTempRoot(
            this ILogger logger,
            [FilePath] string legacyPkiRoot,
            [FilePath] string pkiRoot);
    }
}
