/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Security.Cryptography;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Defines internal helper functions to implement RSA cryptography.
    /// </summary>
    internal static class RsaUtils
    {
        /// <summary>
        /// Dispose RSA object only if not running on Mono runtime.
        /// Workaround due to a Mono bug in the X509Certificate2 implementation of RSA.
        /// see also: https://github.com/mono/mono/issues/6306
        /// On Mono GetRSAPrivateKey/GetRSAPublickey returns a reference instead of a disposable object.
        /// Calling Dispose on RSA makes the X509Certificate2 keys unusable on Mono.
        /// Only call dispose when using .Net and .Net Core runtimes.
        /// </summary>
        /// <param name="rsa">RSA object returned by GetRSAPublicKey/GetRSAPrivateKey</param>
        internal static void RSADispose(RSA rsa)
        {
            if (rsa != null &&
                !IsRunningOnMono())
            {
                rsa.Dispose();
            }
        }

        /// <summary>
        /// Lazy helper to allow runtime check for Mono.
        /// </summary>
        private static readonly Lazy<bool> IsRunningOnMonoValue = new Lazy<bool>(() => {
            return Type.GetType("Mono.Runtime") != null;
        });

        /// <summary>
        /// Determine if assembly uses mono runtime.
        /// </summary>
        /// <returns>true if running on Mono runtime</returns>
        public static bool IsRunningOnMono()
        {
            return IsRunningOnMonoValue.Value;
        }
    }
}
