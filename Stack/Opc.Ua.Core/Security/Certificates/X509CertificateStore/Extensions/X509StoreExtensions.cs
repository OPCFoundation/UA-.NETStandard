/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography.X509Certificates;
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
        /// <returns>the crls as a byte array</returns>
        /// <exception cref="PlatformNotSupportedException">if not called on a supported OS (Windows >= XP)</exception>
        /// <exception cref="ArgumentNullException">if store is null</exception>
        public static byte[][] EnumerateCrls(this X509Store store)
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

            return X509CrlHelper.GetCrls(handle);
        }

        /// <summary>
        /// Adds a ASN1 encoded crl to the provided Windows X509 Store
        /// </summary>
        /// <param name="store">the Windows X509 Store to add the crl to</param>
        /// <param name="crl">the ASN1 encoded crl as byte array</param>
        /// <exception cref="PlatformNotSupportedException">if not called on a supported OS (Windows >= XP)</exception>
        /// <exception cref="ArgumentNullException">if store is null</exception>
        public static void AddCrl(this X509Store store, byte[] crl)
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

            X509CrlHelper.AddCrl(handle, crl);
        }
        /// <summary>
        /// Deletes the specified CRL from the provided X509 store
        /// </summary>
        /// <param name="store">the Windows X509 Store to delete the crl from</param>
        /// <param name="crl">the ASN1 encoded crl as byte array</param>
        /// <exception cref="PlatformNotSupportedException">if not called on a supported OS (Windows >= XP)</exception>
        /// <exception cref="ArgumentNullException">if store is null</exception>
        public static bool DeleteCrl(this X509Store store, byte[] crl)
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

            return X509CrlHelper.DeleteCrl(handle, crl);
        }
    }
}
