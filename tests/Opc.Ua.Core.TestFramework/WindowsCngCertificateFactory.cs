/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable
using System;
using System.Runtime.InteropServices;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.TestFramework
{
    /// <summary>
    /// Creates certificates whose private keys live in a Windows key storage
    /// provider and are marked non extractable.
    /// </summary>
    /// <remarks>
    /// This is the one hardware path that needs no third party dependency. When
    /// a TPM is present the key is created in the Platform Crypto Provider and
    /// is bound to the machine; otherwise it falls back to the software key
    /// storage provider, which still produces a genuinely non extractable key
    /// and therefore still exercises the code paths that matter.
    /// <para>
    /// Note that keys created here are attached with
    /// <c>X509Certificate2.CopyWithPrivateKey</c>, which succeeds because
    /// <see cref="RSACng"/> is one of the two implementations the Windows
    /// certificate layer recognises. Providers that are not CNG backed must use
    /// <see cref="Certificate.CopyWithDetachedPrivateKey(RSA, bool)"/> instead.
    /// </para>
    /// </remarks>
#if NET5_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    public static class WindowsCngCertificateFactory
    {
        /// <summary>
        /// Gets a value indicating whether the platform can create CNG keys.
        /// </summary>
        public static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>
        /// Gets a value indicating whether the machine exposes a TPM through the
        /// Platform Crypto Provider.
        /// </summary>
        public static bool IsTpmAvailable
        {
            get
            {
                if (!IsSupported)
                {
                    return false;
                }

                try
                {
                    return ProbePlatformCryptoProvider();
                }
                catch (CryptographicException)
                {
                    return false;
                }
                catch (PlatformNotSupportedException)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// The Platform Crypto Provider, which is backed by the TPM.
        /// </summary>
        /// <remarks>
        /// <c>CngProvider.MicrosoftPlatformCryptoProvider</c> only exists on
        /// .NET Core and later, so the provider is named explicitly to keep the
        /// factory usable on .NET Framework as well.
        /// </remarks>
        private static CngProvider PlatformCryptoProvider { get; }
            = new CngProvider("Microsoft Platform Crypto Provider");

        /// <summary>
        /// Creates a self signed certificate whose RSA key is held by a Windows
        /// key storage provider and cannot be exported.
        /// </summary>
        /// <param name="subjectName">The subject name of the certificate.</param>
        /// <param name="keyName">
        /// The name the key is stored under. Must be unique; the caller is
        /// responsible for deleting it with <see cref="DeleteKey"/>.
        /// </param>
        /// <param name="useTpm">
        /// <c>true</c> to require the Platform Crypto Provider, <c>false</c> to
        /// use the software key storage provider.
        /// </param>
        /// <param name="keySizeInBits">The RSA key size.</param>
        /// <returns>The certificate, with its non extractable key attached.</returns>
        public static Certificate CreateRsaCertificate(
            string subjectName,
            string keyName,
            bool useTpm = false,
            int keySizeInBits = 2048)
        {
            CngKey key = CreateKey(keyName, useTpm, keySizeInBits);
            try
            {
                using var rsa = new RSACng(key);
                var request = new CertificateRequest(
                    subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using X509Certificate2 certificate = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
                return Certificate.From(X509CertificateLoader.LoadCertificate(certificate.RawData))
                    .CopyWithDetachedPrivateKey(new RSACng(CngKey.Open(keyName)));
            }
            catch
            {
                key.Delete();
                throw;
            }
            finally
            {
                key.Dispose();
            }
        }

        /// <summary>
        /// Deletes a key previously created by this factory.
        /// </summary>
        /// <param name="keyName">The key name.</param>
        public static void DeleteKey(string keyName)
        {
            try
            {
                using CngKey key = CngKey.Open(keyName);
                key.Delete();
            }
            catch (CryptographicException)
            {
                // Already gone; nothing to clean up.
            }
        }

        private static CngKey CreateKey(string keyName, bool useTpm, int keySizeInBits)
        {
            var parameters = new CngKeyCreationParameters
            {
                // The whole point: the private key can never be read back out.
                ExportPolicy = CngExportPolicies.None,
                KeyCreationOptions = CngKeyCreationOptions.None,
                Provider = useTpm
                    ? PlatformCryptoProvider
                    : CngProvider.MicrosoftSoftwareKeyStorageProvider,
                KeyUsage = CngKeyUsages.Signing | CngKeyUsages.Decryption
            };

            parameters.Parameters.Add(
                new CngProperty(
                    "Length",
                    BitConverter.GetBytes(keySizeInBits),
                    CngPropertyOptions.None));

            return CngKey.Create(CngAlgorithm.Rsa, keyName, parameters);
        }

        private static bool ProbePlatformCryptoProvider()
        {
            string probeName = "opcua-tpm-probe-" + Guid.NewGuid().ToString("N");
            try
            {
                using CngKey key = CreateKey(probeName, useTpm: true, keySizeInBits: 2048);
                key.Delete();
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }
    }
}
