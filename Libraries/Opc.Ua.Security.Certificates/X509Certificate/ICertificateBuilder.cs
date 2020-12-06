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

    public interface ICertificateBuilder
        : ICertificateBuilderConfig
        , ICertificateBuilderSetIssuer
        , ICertificateBuilderParameter
        , ICertificateBuilderCreate
        , IX509Certificate
    {
    }

    public interface ICertificateBuilderIssuer
        : ICertificateBuilderPublicKey
        , ICertificateBuilderCreate
        , ICertificateBuilderParameter
    {
    }

    public interface ICertificateBuilderPublicKey
        : ICertificateBuilderRSAPublicKey
#if NETSTANDARD2_1
        , ICertificateBuilderECDsaPublicKey
#endif
    {
    }

    public interface ICertificateBuilderParameter
        : ICertificateBuilderRSAParameter
#if NETSTANDARD2_1
        , ICertificateBuilderECCParameter
#endif
    { }

    public interface ICertificateBuilderCreate
        : ICertificateBuilderCreateForRSA
#if NETSTANDARD2_1
        , ICertificateBuilderCreateForECDsa
#endif
    {

    }

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
        /// not exceed <see cref="Defaults.SerialNumberLengthMax"/> octets.
        /// </remarks>
        /// <param name="length"></param>
        ICertificateBuilder SetSerialNumberLength(int length);

        /// <summary>
        /// Set the length of the serial number.
        /// </summary>
        /// <remarks>
        /// The length of the serial number shall
        /// not exceed <see cref="Defaults.SerialNumberLengthMax"/> octets.
        /// </remarks>
        /// <param name="serialNumber">The serial number as an array of bytes in little endian order.</param>
        ICertificateBuilder SetSerialNumber(byte[] serialNumber);

        ICertificateBuilder CreateSerialNumber();

        ICertificateBuilder SetNotBefore(DateTime notBefore);

        ICertificateBuilder SetNotAfter(DateTime notAfter);

        ICertificateBuilder SetLifeTime(TimeSpan lifeTime);

        ICertificateBuilder SetLifeTime(ushort lifeTime);

        ICertificateBuilder SetHashAlgorithm(HashAlgorithmName hashAlgorithmName);

        ICertificateBuilder SetCAConstraint(int pathLengthConstraint = -1);

        ICertificateBuilder AddExtension(X509Extension extension);
    }

    public interface ICertificateBuilderSetIssuer
    {
        ICertificateBuilderIssuer SetIssuer(X509Certificate2 issuerCertificate);
    }

    public interface ICertificateBuilderRSAParameter
    {
        ICertificateBuilderCreateForRSA SetRSAKeySize(int keySize);
    }

#if NETSTANDARD2_1
    public interface ICertificateBuilderECCParameter
    {
        ICertificateBuilderCreateForECDsa SetECCurve(ECCurve curve);
    }
#endif

    public interface ICertificateBuilderRSAPublicKey
    {
        ICertificateBuilderCreateForRSA SetRSAPublicKey(byte[] publicKey);

        ICertificateBuilderCreateForRSA SetRSAPublicKey(RSA publicKey);
    }

#if NETSTANDARD2_1
    public interface ICertificateBuilderECDsaPublicKey
    {
        ICertificateBuilderCreateForECDsa SetECDsaPublicKey(byte[] publicKey);

        ICertificateBuilderCreateForECDsa SetECDsaPublicKey(ECDsa publicKey);
    }
#endif

    public interface ICertificateBuilderCreateForRSA
    {
        /// <summary>
        /// Create the RSA certificate with signature.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        X509Certificate2 CreateForRSA();

        /// <summary>
        /// Create the RSA certificate with signature.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        X509Certificate2 CreateForRSA(X509SignatureGenerator generator);
    }

#if NETSTANDARD2_1
    public interface ICertificateBuilderCreateForECDsa
    {
        /// <summary>
        /// Create the RSA certificate with signature.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        X509Certificate2 CreateForECDsa();

        /// <summary>
        /// Create the ECC certificate with signature using an external generator.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        X509Certificate2 CreateForECDsa(X509SignatureGenerator generator);
    }
#endif
}
