// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if !NETSTANDARD2_1 && !NET472_OR_GREATER && !NET5_0_OR_GREATER

// This source code is intentionally copied from the .NET core runtime to close
// a gap in the .NET 4.6 and the .NET Core 2.x runtime implementations.
// original code is located here:
// https://github.com/dotnet/runtime/blob/master/src/libraries/System.Security.Cryptography.X509Certificates/src/System/Security/Cryptography/X509Certificates/RSAPkcs1X509SignatureGenerator.cs
#pragma warning disable CS1591 // Suppress missing XML comments to preserve original code

using System;
using System.Diagnostics;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Security.Certificates
{
    internal sealed class RSAPkcs1X509SignatureGenerator : X509SignatureGenerator
    {
        private readonly RSA _key;

        internal RSAPkcs1X509SignatureGenerator(RSA key)
        {
            Debug.Assert(key != null);

            _key = key;
        }

        public override byte[] SignData(byte[] data, HashAlgorithmName hashAlgorithm)
        {
            return _key.SignData(data, hashAlgorithm, RSASignaturePadding.Pkcs1);
        }

        protected override PublicKey BuildPublicKey()
        {
            return BuildPublicKey(_key);
        }

        internal static PublicKey BuildPublicKey(RSA rsa)
        {
            Oid oid = new Oid(Oids.Rsa);

            // The OID is being passed to everything here because that's what
            // X509Certificate2.PublicKey does.
            return new PublicKey(
                oid,
                // Encode the DER-NULL even though it is OPTIONAL, because everyone else does.
                //
                // This is due to one version of the ASN.1 not including OPTIONAL, and that was
                // the version that got predominately implemented for RSA. Now it's convention.
                new AsnEncodedData(oid, new byte[] { 0x05, 0x00 }),
                new AsnEncodedData(oid, ExportRSAPublicKey(rsa)));
        }

        public override byte[] GetSignatureAlgorithmIdentifier(HashAlgorithmName hashAlgorithm)
        {
            string oid;

            if (hashAlgorithm == HashAlgorithmName.SHA256)
            {
                oid = Oids.RsaPkcs1Sha256;
            }
            else if (hashAlgorithm == HashAlgorithmName.SHA384)
            {
                oid = Oids.RsaPkcs1Sha384;
            }
            else if (hashAlgorithm == HashAlgorithmName.SHA512)
            {
                oid = Oids.RsaPkcs1Sha512;
            }
            else
            {
                throw new ArgumentOutOfRangeException(
                    nameof(hashAlgorithm),
                    hashAlgorithm,
                    $"'{hashAlgorithm.Name}' is not a known hash algorithm.");
            }

            AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.WriteObjectIdentifier(oid);
            writer.WriteNull();
            writer.PopSequence();
            return writer.Encode();
        }

        private static byte[] ExportRSAPublicKey(RSA rsa)
        {
            RSAParameters rsaParameters = rsa.ExportParameters(false);

            if (rsaParameters.Modulus == null || rsaParameters.Exponent == null)
            {
                throw new CryptographicException("Invalid RSA Parameters.");
            }

            AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.WriteKeyParameterInteger(rsaParameters.Modulus);
            writer.WriteKeyParameterInteger(rsaParameters.Exponent);
            writer.PopSequence();
            return writer.Encode();
        }
    }
}
#endif
