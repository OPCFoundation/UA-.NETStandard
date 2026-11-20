// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Opc.Ua.X509StoreExtensions.Internal;

namespace Opc.Ua.X509StoreExtensions
{
    /// <summary>
    /// Extension Methods for the Windows X509 Store to add CRL Support to .NET
    /// </summary>
    internal static class X509StoreExtensions
    {
        /// <summary>
        /// Enumerate all Crls in a Windows X509 Store
        /// </summary>
        /// <param name="store">the Windows X509 Store to retrieve the crls from</param>
        /// <param name="logger">A contextual logger to log to</param>
        /// <returns>the crls as a byte array</returns>
        /// <exception cref="PlatformNotSupportedException">if not called on a supported OS (Windows >= XP)</exception>
        /// <exception cref="ArgumentNullException">if store is null</exception>
        public static byte[][] EnumerateCrls(this X509Store store, ILogger logger)
        {
            if (!PlatformHelper.IsWindowsWithCrlSupport())
            {
                throw new PlatformNotSupportedException();
            }
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            IntPtr handle = store.StoreHandle;

            return X509CrlHelper.GetCrls(handle, logger);
        }

        /// <summary>
        /// Adds a ASN1 encoded crl to the provided Windows X509 Store
        /// </summary>
        /// <param name="store">the Windows X509 Store to add the crl to</param>
        /// <param name="crl">the ASN1 encoded crl as byte array</param>
        /// <param name="logger">A contextual logger to log to</param>
        /// <exception cref="PlatformNotSupportedException">if not called on a
        /// supported OS (Windows >= XP)</exception>
        /// <exception cref="ArgumentNullException">if store is null</exception>
        public static void AddCrl(this X509Store store, byte[] crl, ILogger logger)
        {
            if (!PlatformHelper.IsWindowsWithCrlSupport())
            {
                throw new PlatformNotSupportedException();
            }

            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }
            if (crl == null || crl.Length == 0)
            {
                throw new ArgumentNullException(nameof(crl));
            }

            IntPtr handle = store.StoreHandle;

            X509CrlHelper.AddCrl(handle, crl, logger);
        }

        /// <summary>
        /// Deletes the specified CRL from the provided X509 store
        /// </summary>
        /// <param name="store">the Windows X509 Store to delete the crl from</param>
        /// <param name="crl">the ASN1 encoded crl as byte array</param>
        /// <param name="logger">A contextual logger to log to</param>
        /// <exception cref="PlatformNotSupportedException">if not called on a
        /// supported OS (Windows >= XP)</exception>
        /// <exception cref="ArgumentNullException">if store is null</exception>
        public static bool DeleteCrl(this X509Store store, byte[] crl, ILogger logger)
        {
            if (!PlatformHelper.IsWindowsWithCrlSupport())
            {
                throw new PlatformNotSupportedException();
            }
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            IntPtr handle = store.StoreHandle;

            return X509CrlHelper.DeleteCrl(handle, crl, logger);
        }
    }
}
