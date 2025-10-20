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
using System.Security.Cryptography.X509Certificates;
#if !NETSTANDARD2_0
using System.Runtime.InteropServices;
#endif

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Debug utils
    /// </summary>
    public static class X509PfxUtilsDebug
    {
        /// <summary>
        /// Get key file name
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public static string GetKeyFileName(X509Certificate2 cert)
        {
#if NETSTANDARD2_0
            return string.Empty;
#else
            if (cert == null ||
                !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return string.Empty;
            }

            // CSP handle
            IntPtr hProvider = IntPtr.Zero;
            // Do we need to free the CSP ?
            bool freeProvider = false;
            const uint acquireFlags = 0;
            int keyNumber = 0;
            string keyFileName = null;
            byte[] keyFileBytes;

            //
            // Determine whether there is private key information
            // available for this certificate in the key store
            //
            if (CryptAcquireCertificatePrivateKey(cert.Handle,
                acquireFlags,
                IntPtr.Zero,
                ref hProvider,
                ref keyNumber,
                ref freeProvider))
            {
                // Native Memory for the CRYPT_KEY_PROV_INFO structure
                IntPtr pBytes = IntPtr.Zero;
                // Native Memory size
                int cbBytes = 0;

                try
                {
                    if (CryptGetProvParam(
                        hProvider,
                        CryptGetProvParamType.PP_UNIQUE_CONTAINER,
                        IntPtr.Zero,
                        ref cbBytes,
                        0))
                    {
                        pBytes = Marshal.AllocHGlobal(cbBytes);

                        if (CryptGetProvParam(
                            hProvider,
                            CryptGetProvParamType.PP_UNIQUE_CONTAINER,
                            pBytes,
                            ref cbBytes,
                            0))
                        {
                            keyFileBytes = new byte[cbBytes];

                            Marshal.Copy(pBytes, keyFileBytes, 0, cbBytes);

                            // Copy everything except tailing null byte
                            keyFileName = System.Text.Encoding.ASCII.GetString(
                                keyFileBytes,
                                0,
                                keyFileBytes.Length - 1);
                        }
                    }
                }
                finally
                {
                    if (freeProvider)
                    {
                        CryptReleaseContext(hProvider, 0);
                    }

                    //
                    // Free our native memory
                    //
                    if (pBytes != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(pBytes);
                    }
                }
            }
            return keyFileName ?? string.Empty;
#endif
        }

        /// <summary>
        /// Get key file directory
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public static string GetKeyFileDirectory(string keyFileName)
        {
#if NETSTANDARD2_0
            return string.Empty;
#else
            if (string.IsNullOrEmpty(keyFileName) ||
                !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return string.Empty;
            }

            // Look up All User profile from environment variable
            string allUserProfile = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);

            // set up searching directory
            string machineKeyDir = allUserProfile + "\\Microsoft\\Crypto\\RSA\\MachineKeys";

            // Search the key file
            string[] fs = System.IO.Directory.GetFiles(machineKeyDir, keyFileName);

            // If found
            if (fs.Length > 0)
            {
                return machineKeyDir;
            }

            // Next try current user profile
            string currentUserProfile = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData);

            // Search all subdirectories.
            string userKeyDir = currentUserProfile + "\\Microsoft\\Crypto\\RSA\\";

            fs = System.IO.Directory.GetDirectories(userKeyDir);
            if (fs.Length > 0)
            {
                // for each sub directory
                foreach (string keyDir in fs)
                {
                    fs = System.IO.Directory.GetFiles(keyDir, keyFileName);
                    if (fs.Length != 0)
                    {
                        // found
                        return keyDir;
                    }
                }
            }
            throw new InvalidOperationException("Unable to locate private key file directory");
#endif
        }

#if !NETSTANDARD2_0
        [DllImport("crypt32", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool CryptAcquireCertificatePrivateKey(
            IntPtr pCert,
            uint dwFlags,
            IntPtr pvReserved,
            ref IntPtr phCryptProv,
            ref int pdwKeySpec,
            ref bool pfCallerFreeProv);

        [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool CryptGetProvParam(
            IntPtr hCryptProv,
            CryptGetProvParamType dwParam,
            IntPtr pvData,
            ref int pcbData,
            uint dwFlags);

        [DllImport("advapi32", SetLastError = true)]
        internal static extern bool CryptReleaseContext(
            IntPtr hProv,
            uint dwFlags);

        internal enum CryptGetProvParamType
        {
            PP_ENUMALGS = 1,
            PP_ENUMCONTAINERS = 2,
            PP_IMPTYPE = 3,
            PP_NAME = 4,
            PP_VERSION = 5,
            PP_CONTAINER = 6,
            PP_CHANGE_PASSWORD = 7,
            /// <summary>
            /// get/set security descriptor of keyset
            /// </summary>
            PP_KEYSET_SEC_DESCR = 8,
            /// <summary>
            /// for retrieving certificates from tokens
            /// </summary>
            PP_CERTCHAIN = 9,
            PP_KEY_TYPE_SUBTYPE = 10,
            PP_PROVTYPE = 16,
            PP_KEYSTORAGE = 17,
            PP_APPLI_CERT = 18,
            PP_SYM_KEYSIZE = 19,
            PP_SESSION_KEYSIZE = 20,
            PP_UI_PROMPT = 21,
            PP_ENUMALGS_EX = 22,
            PP_ENUMMANDROOTS = 25,
            PP_ENUMELECTROOTS = 26,
            PP_KEYSET_TYPE = 27,
            PP_ADMIN_PIN = 31,
            PP_KEYEXCHANGE_PIN = 32,
            PP_SIGNATURE_PIN = 33,
            PP_SIG_KEYSIZE_INC = 34,
            PP_KEYX_KEYSIZE_INC = 35,
            PP_UNIQUE_CONTAINER = 36,
            PP_SGC_INFO = 37,
            PP_USE_HARDWARE_RNG = 38,
            PP_KEYSPEC = 39,
            PP_ENUMEX_SIGNING_PROT = 40,
            PP_CRYPT_COUNT_KEY_USE = 41
        }
#endif
    }
}
