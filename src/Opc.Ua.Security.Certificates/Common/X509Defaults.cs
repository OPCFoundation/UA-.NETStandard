// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// The defaults used in the library for Certificates.
    /// </summary>
    public static class X509Defaults
    {
        /// <summary>
        /// The default key size for RSA certificates in bits.
        /// </summary>
        /// <remarks>
        /// Supported values are 1024(deprecated), 2048, 3072 or 4096.
        /// </remarks>
        public static readonly ushort RSAKeySize = 2048;

        /// <summary>
        /// The min supported size for a RSA key.
        /// </summary>
        public static readonly ushort RSAKeySizeMin = 1024;

        /// <summary>
        /// The max supported size for a RSA key.
        /// </summary>
        public static readonly ushort RSAKeySizeMax = 4096;

        /// <summary>
        /// The default hash algorithm to use for signatures.
        /// </summary>
        /// <remarks>
        /// Supported values are SHA-1(deprecated) or 256, 384 and 512 for SHA-2.
        /// </remarks>
        public static readonly HashAlgorithmName HashAlgorithmName = HashAlgorithmName.SHA256;

        /// <summary>
        /// The default lifetime of certificates in months.
        /// </summary>
        public static readonly ushort LifeTime = 24;

        /// <summary>
        /// The recommended min serial numbers length in octets.
        /// </summary>
        public static readonly int SerialNumberLengthMin = 10;

        /// <summary>
        /// The max serial numbers length in octets.
        /// </summary>
        public static readonly int SerialNumberLengthMax = 20;
    }
}
