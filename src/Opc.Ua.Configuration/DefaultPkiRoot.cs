/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Logging;

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
            string legacyPkiRoot,
            string pkiRoot);
    }
}
