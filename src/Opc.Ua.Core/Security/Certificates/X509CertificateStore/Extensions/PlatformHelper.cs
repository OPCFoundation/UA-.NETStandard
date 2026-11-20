// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.X509StoreExtensions
{
    /// <summary>
    /// Static Helper class to retrieve if executed on a specific operating system
    /// </summary>
    internal static class PlatformHelper
    {
        private static bool? s_isWindowsWithCrlSupport;

        /// <summary>
        /// True if OS Windows and Version >= Windows XP
        /// </summary>
        /// <returns>True if Crl Support is given in the system X509 Store</returns>
        public static bool IsWindowsWithCrlSupport()
        {
            if (s_isWindowsWithCrlSupport != null)
            {
                return s_isWindowsWithCrlSupport.Value;
            }
            OperatingSystem version = Environment.OSVersion;
            s_isWindowsWithCrlSupport =
                version.Platform == PlatformID.Win32NT &&
                ((version.Version.Major > 5) ||
                    (version.Version.Major == 5 && version.Version.Minor >= 1));
            return s_isWindowsWithCrlSupport.Value;
        }
    }
}
