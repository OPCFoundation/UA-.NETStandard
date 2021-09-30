/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// The certificate builder interface.
    /// </summary>
    public interface ICertificateBuilder
        : ICertificateBuilderConfig
        , ICertificateBuilderPublicKey
        , ICertificateBuilderSetIssuer
        , ICertificateBuilderParameter
        , ICertificateBuilderCreateForRSA
        , IX509Certificate
    { }

    /// <summary>
    /// The interface to set an issuer.
    /// </summary>
    public interface ICertificateBuilderIssuer
        : ICertificateBuilderPublicKey
        , ICertificateBuilderCreateForRSA
        , ICertificateBuilderParameter
        , ICertificateBuilderCreateGenerator
    { }

    /// <summary>
    /// The interface to set a public key.
    /// </summary>
    public interface ICertificateBuilderPublicKey
        : ICertificateBuilderRSAPublicKey
#if ECC_SUPPORT
        , ICertificateBuilderECDsaPublicKey
#endif
    { }

    /// <summary>
    /// The interface to set key parameters.
    /// </summary>
    public interface ICertificateBuilderParameter
        : ICertificateBuilderRSAParameter
#if ECC_SUPPORT
        , ICertificateBuilderECCParameter
#endif
    { }

    /// <summary>
    /// The interface to create a certificate.
    /// </summary>
    public interface ICertificateBuilderCreate
        : ICertificateBuilderCreateForRSA
#if ECC_SUPPORT
        , ICertificateBuilderCreateForECDsa
#endif
    { }

    /// <summary>
    /// The interface to use a signature generator.
    /// </summary>
    public interface ICertificateBuilderCreateGenerator
        : ICertificateBuilderCreateForRSAGenerator
#if ECC_SUPPORT
        , ICertificateBuilderCreateForECDsaGenerator
#endif
    { }

    /// <summary>
    /// The interface to create a RSA based certifcate.
    /// </summary>
    public interface ICertificateBuilderCreateForRSAAny
        : ICertificateBuilderCreateForRSA
        , ICertificateBuilderCreateForRSAGenerator
    { }

#if ECC_SUPPORT
    /// <summary>
    /// The interface to create a ECDSA based certifcate.
    /// </summary>
    public interface ICertificateBuilderCreateForECDsaAny
        : ICertificateBuilderCreateForECDsa
        , ICertificateBuilderCreateForECDsaGenerator
    { }
#endif

    /// <summary>
    /// The interface to set the mandatory certificate
    /// fields for a certificate builder.
    /// </summary>
    public interface ICertificateBuilderConfig
    {
        /// <summary>
        /// Set the length of the serial number.
        /// </summary>
        /// <remarks>
        /// The length of the serial number shall
        /// not exceed <see cref="X509Defaults.SerialNumberLengthMax"/> octets.
        /// </remarks>
        /// <param name="length"></param>
        ICertificateBuilder SetSerialNumberLength(int length);

        /// <summary>
        /// Set the value of the serial number directly
        /// using a byte array.
        /// </summary>
        /// <remarks>
        /// The length of the serial number shall
        /// not exceed <see cref="X509Defaults.SerialNumberLengthMax"/> octets.
        /// </remarks>
        /// <param name="serialNumber">The serial number as an array of bytes in little endian order.</param>
        ICertificateBuilder SetSerialNumber(byte[] serialNumber);

        /// <summary>
        /// Create a new serial number and preserve
        /// it until the certificate is created.
        /// </summary>
        /// <remarks>
        /// The serial number may be needed to create an extension.
        /// This function makes it available before the
        /// cert is created.
        /// </remarks>
        ICertificateBuilder CreateSerialNumber();

        /// <summary>
        /// Set the date when the certificate becomes valid.
        /// </summary>
        /// <param name="notBefore">The date.</param>
        ICertificateBuilder SetNotBefore(DateTime notBefore);

        /// <summary>
        /// Set the certificate expiry date.
        /// </summary>
        /// <param name="notAfter">The date after which the certificate is expired.</param>
        ICertificateBuilder SetNotAfter(DateTime notAfter);

        /// <summary>
        /// Set the lifetime of the certificate using Timespan.
        /// </summary>
        /// <param name="lifeTime">The lifetime as <see creftype="Timespan"/>.</param>
        ICertificateBuilder SetLifeTime(TimeSpan lifeTime);

        /// <summary>
        /// Set the lifetime of the certificate in month starting now.
        /// </summary>
        /// <param name="months">The lifetime in months.</param>
        ICertificateBuilder SetLifeTime(ushort months);

        /// <summary>
        /// Set the hash algorithm to use for the signature.
        /// </summary>
        /// <param name="hashAlgorithmName">The hash algorithm name.</param>
        ICertificateBuilder SetHashAlgorithm(HashAlgorithmName hashAlgorithmName);

        /// <summary>
        /// Set the CA flag and the path length constraints of the certificate.
        /// </summary>
        /// <param name="pathLengthConstraint">
        /// The path length constraint to use.
        /// -1 corresponds to None, other values constrain the chain length.
        /// </param>
        ICertificateBuilder SetCAConstraint(int pathLengthConstraint = -1);

        /// <summary>
        /// Add an extension to the certificate in addition to the default extensions.
        /// </summary>
        /// <remarks>
        /// By default the following X509 extensions are added to a certificate,
        /// some depending on certificate type:
        /// CA/SubCA/OPC UA application:
        ///     X509BasicConstraintsExtension
        ///     X509SubjectKeyIdentifierExtension
        ///     X509AuthorityKeyIdentifierExtension
        ///     X509KeyUsageExtension
        /// OPC UA application:
        ///     X509SubjectAltNameExtension
        ///     X509EnhancedKeyUsageExtension
        /// Adding a default extension to the list overrides the default
        /// value of the extensions.
        /// Adding an extension with a already existing Oid overrides
        /// the existing extension in the list.
        /// </remarks>
        /// <param name="extension">The extension to add</param>
        ICertificateBuilder AddExtension(X509Extension extension);
    }

    /// <summary>
    /// The interface to select an issuer for the cert builder.
    /// </summary>
    public interface ICertificateBuilderSetIssuer
    {
        /// <summary>
        /// Set the issuer certificate which is used to sign the certificate.
        /// </summary>
        /// <remarks>
        /// The issuer certificate must contain a private key which matches
        /// the selected sign algorithm if no generator is avilable.
        /// If a <see cref="X509SignatureGenerator"/> is used for signing the
        /// the issuer certificate can be set with a public key to create
        /// the X509 extensions.
        /// </remarks>
        /// <param name="issuerCertificate">The issuer certificate.</param>
        ICertificateBuilderIssuer SetIssuer(X509Certificate2 issuerCertificate);
    }

    /// <summary>
    /// The interface to select the RSA key size parameter.
    /// </summary>
    public interface ICertificateBuilderRSAParameter
    {
        /// <summary>
        /// Set the RSA key size in bits.
        /// </summary>
        /// <param name="keySize">The size of the RSA key.</param>
        ICertificateBuilderCreateForRSAAny SetRSAKeySize(ushort keySize);
    }

#if ECC_SUPPORT
    /// <summary>
    /// The interface to select the ECCurve.
    /// </summary>
    public interface ICertificateBuilderECCParameter
    {
        /// <summary>
        /// Set the ECC Curve parameter.
        /// </summary>
        /// <param name="curve">The ECCurve.</param>
        ICertificateBuilderCreateForECDsaAny SetECCurve(ECCurve curve);
    }
#endif

    /// <summary>
    /// The interface to set a RSA public key for a certificate.
    /// </summary>
    public interface ICertificateBuilderRSAPublicKey
    {
        /// <summary>
        /// Set the public key using a ASN.1 encoded byte array.
        /// </summary>
        /// <param name="publicKey">The public key as encoded byte array.</param>
        ICertificateBuilderCreateForRSAAny SetRSAPublicKey(byte[] publicKey);

        /// <summary>
        /// Set the public key using a RSA public key.
        /// </summary>
        /// <param name="publicKey">The RSA public key.</param>
        ICertificateBuilderCreateForRSAAny SetRSAPublicKey(RSA publicKey);
    }

#if ECC_SUPPORT
    /// <summary>
    /// The interface to set a ECDSA public key for a certificate.
    /// </summary>
    public interface ICertificateBuilderECDsaPublicKey
    {
        /// <summary>
        /// Set the public key using a ASN.1 encoded byte array.
        /// </summary>
        /// <param name="publicKey">The public key as encoded byte array.</param>
        ICertificateBuilderCreateForECDsaAny SetECDsaPublicKey(byte[] publicKey);

        /// <summary>
        /// Set the public key using a ECDSA public key.
        /// </summary>
        /// <param name="publicKey">The ECDsa public key.</param>
        ICertificateBuilderCreateForECDsaAny SetECDsaPublicKey(ECDsa publicKey);
    }
#endif

    /// <summary>
    /// The interface to create a certificate using the RSA algorithm.
    /// </summary>
    public interface ICertificateBuilderCreateForRSA
    {
        /// <summary>
        /// Create the RSA certificate with signature.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        X509Certificate2 CreateForRSA();
    }

    /// <summary>
    /// The interface to create a certificate using a signature generator.
    /// </summary>
    public interface ICertificateBuilderCreateForRSAGenerator
    {
        /// <summary>
        /// Create the RSA certificate with signature using an external generator.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        X509Certificate2 CreateForRSA(X509SignatureGenerator generator);
    }

#if ECC_SUPPORT
    /// <summary>
    /// The interface to create a certificate using the ECDSA algorithm.
    /// </summary>
    public interface ICertificateBuilderCreateForECDsa
    {
        /// <summary>
        /// Create the ECC certificate with signature.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        X509Certificate2 CreateForECDsa();
    }

    /// <summary>
    /// The interface to create a certificate using a signature generator for ECDSA.
    /// </summary>
    public interface ICertificateBuilderCreateForECDsaGenerator
    {
        /// <summary>
        /// Create the ECDSA certificate with signature using an external generator.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        X509Certificate2 CreateForECDsa(X509SignatureGenerator generator);
    }
#endif
}
