// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if !NETSTANDARD2_1 && !NET472

// This source code is intentionally copied from the .NET core runtime to close
// a gap in the .NET 4.6 and the .NET Core 2.x runtime implementations.
// original code is located here:
// https://github.com/dotnet/runtime/blob/master/src/libraries/System.Security.Cryptography.X509Certificates/src/System/Security/Cryptography/X509Certificates/X509SignatureGenerator.cs
#pragma warning disable CS1591 // Suppress missing XML comments to preserve original code

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Security.Certificates
{
    public abstract class X509SignatureGenerator
    {
        private PublicKey _publicKey;

        public PublicKey PublicKey
        {
            get
            {
                if (_publicKey == null)
                {
                    _publicKey = BuildPublicKey();
                }

                return _publicKey;
            }
        }

        public abstract byte[] GetSignatureAlgorithmIdentifier(HashAlgorithmName hashAlgorithm);
        public abstract byte[] SignData(byte[] data, HashAlgorithmName hashAlgorithm);
        protected abstract PublicKey BuildPublicKey();
#if NOT_SUPPORTED
        public static X509SignatureGenerator CreateForECDsa(ECDsa key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            return new ECDsaX509SignatureGenerator(key);
        }
#endif
        public static X509SignatureGenerator CreateForRSA(RSA key, RSASignaturePadding signaturePadding)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (signaturePadding == null)
                throw new ArgumentNullException(nameof(signaturePadding));

            if (signaturePadding == RSASignaturePadding.Pkcs1)
                return new RSAPkcs1X509SignatureGenerator(key);
#if NOT_SUPPORTED
            if (signaturePadding.Mode == RSASignaturePaddingMode.Pss)
                return new RSAPssX509SignatureGenerator(key, signaturePadding);
#endif
            throw new ArgumentException("Specified padding mode is not valid for this algorithm.");
        }
    }
}
#endif
