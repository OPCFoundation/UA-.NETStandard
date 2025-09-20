// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if !NETSTANDARD2_1 && !NET472_OR_GREATER && !NET5_0_OR_GREATER

// This source code is intentionally copied from the .NET core runtime to close
// a gap in .NET standard 2.0 runtime implementations.
// original code is located here:
// https://github.com/dotnet/runtime/blob/master/src/libraries/
// System.Security.Cryptography.X509Certificates/src/System/Security/
// Cryptography/X509Certificates/RSAPkcs1X509SignatureGenerator.cs

using System.Diagnostics;
using System.Formats.Asn1;
using Opc.Ua.Security.Certificates;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace System.Security.Cryptography.X509Certificates
{
    public abstract class X509SignatureGenerator
    {
        private PublicKey m_publicKey;

        public PublicKey PublicKey => m_publicKey ??= BuildPublicKey();

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

        public static X509SignatureGenerator CreateForRSA(
            RSA key,
            RSASignaturePadding signaturePadding)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (signaturePadding == null)
            {
                throw new ArgumentNullException(nameof(signaturePadding));
            }

            if (signaturePadding == RSASignaturePadding.Pkcs1)
            {
                return new RSAPkcs1X509SignatureGenerator(key);
            }
#if NOT_SUPPORTED
            if (signaturePadding.Mode == RSASignaturePaddingMode.Pss)
                return new RSAPssX509SignatureGenerator(key, signaturePadding);
#endif
            throw new ArgumentException("Specified padding mode is not valid for this algorithm.");
        }
    }

    internal sealed class RSAPkcs1X509SignatureGenerator : X509SignatureGenerator
    {
        private readonly RSA m_key;

        internal RSAPkcs1X509SignatureGenerator(RSA key)
        {
            Debug.Assert(key != null);

            m_key = key;
        }

        public override byte[] SignData(byte[] data, HashAlgorithmName hashAlgorithm)
        {
            return m_key.SignData(data, hashAlgorithm, RSASignaturePadding.Pkcs1);
        }

        protected override PublicKey BuildPublicKey()
        {
            return BuildPublicKey(m_key);
        }

        internal static PublicKey BuildPublicKey(RSA rsa)
        {
            var oid = new Oid(Oids.Rsa);

            // The OID is being passed to everything here because that's what
            // X509Certificate2.PublicKey does.
            return new PublicKey(
                oid,
                // Encode the DER-NULL even though it is OPTIONAL, because everyone else does.
                //
                // This is due to one version of the ASN.1 not including OPTIONAL, and that was
                // the version that got predominately implemented for RSA. Now it's convention.
                new AsnEncodedData(oid, [0x05, 0x00]),
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

            var writer = new AsnWriter(AsnEncodingRules.DER);
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

            var writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.WriteKeyParameterInteger(rsaParameters.Modulus);
            writer.WriteKeyParameterInteger(rsaParameters.Exponent);
            writer.PopSequence();
            return writer.Encode();
        }
    }
}
#endif
