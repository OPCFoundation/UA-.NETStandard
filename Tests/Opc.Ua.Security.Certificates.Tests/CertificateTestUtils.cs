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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Opc.Ua.Tests;

namespace Opc.Ua.Security.Certificates.Tests
{
    public class KeyHashPair : IFormattable
    {
        public KeyHashPair(ushort keySize, HashAlgorithmName hashAlgorithmName)
        {
            KeySize = keySize;
            HashAlgorithmName = hashAlgorithmName;
            if (hashAlgorithmName == HashAlgorithmName.SHA1)
            {
                HashSize = 160;
            }
            else if (hashAlgorithmName == HashAlgorithmName.SHA256)
            {
                HashSize = 256;
            }
            else if (hashAlgorithmName == HashAlgorithmName.SHA384)
            {
                HashSize = 384;
            }
            else if (hashAlgorithmName == HashAlgorithmName.SHA512)
            {
                HashSize = 512;
            }
        }

        public ushort KeySize;
        public ushort HashSize;
        public HashAlgorithmName HashAlgorithmName;

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return $"{KeySize}-{HashAlgorithmName}";
        }
    }

    public class KeyHashPairCollection : List<KeyHashPair>
    {
        public KeyHashPairCollection()
        {
        }

        public KeyHashPairCollection(IEnumerable<KeyHashPair> collection)
            : base(collection)
        {
        }

        public KeyHashPairCollection(int capacity)
            : base(capacity)
        {
        }

        public static KeyHashPairCollection ToJsonValidationDataCollection(KeyHashPair[] values)
        {
            return values != null ? [.. values] : [];
        }

        public void Add(ushort keySize, HashAlgorithmName hashAlgorithmName)
        {
            Add(new KeyHashPair(keySize, hashAlgorithmName));
        }
    }

    public class ECCurveHashPair : IFormattable
    {
        public ECCurveHashPair(ECCurve curve, HashAlgorithmName hashAlgorithmName)
        {
            Curve = curve;
            HashAlgorithmName = hashAlgorithmName;
            if (hashAlgorithmName == HashAlgorithmName.SHA1)
            {
                HashSize = 160;
            }
            else if (hashAlgorithmName == HashAlgorithmName.SHA256)
            {
                HashSize = 256;
            }
            else if (hashAlgorithmName == HashAlgorithmName.SHA384)
            {
                HashSize = 384;
            }
            else if (hashAlgorithmName == HashAlgorithmName.SHA512)
            {
                HashSize = 512;
            }
        }

        public ECCurve Curve { get; private set; }
        public ushort HashSize { get; }
        public HashAlgorithmName HashAlgorithmName { get; }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            try
            {
                string friendlyName = Curve.Oid?.FriendlyName ?? "Unknown";
                return $"{friendlyName}-{HashAlgorithmName}";
            }
            catch
            {
                return $"unknown-{HashAlgorithmName}";
            }
        }
    }

    public class ECCurveHashPairCollection : List<ECCurveHashPair>
    {
        public ECCurveHashPairCollection()
        {
        }

        public ECCurveHashPairCollection(IEnumerable<ECCurveHashPair> collection)
            : base(collection)
        {
        }

        public ECCurveHashPairCollection(int capacity)
            : base(capacity)
        {
        }

        public static ECCurveHashPairCollection ToJsonValidationDataCollection(
            ECCurveHashPair[] values)
        {
            return values != null ? [.. values] : [];
        }

        public void Add(ECCurve curve, HashAlgorithmName hashAlgorithmName)
        {
            Add(new ECCurveHashPair(curve, hashAlgorithmName));
        }
    }

    /// <summary>
    /// A CRL as test asset.
    /// </summary>
    public class CRLAsset : IAsset, IFormattable
    {
        public string Path { get; private set; }
        public byte[] Crl { get; private set; }

        public void Initialize(byte[] blob, string path)
        {
            Path = path;
            Crl = blob;
        }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            string file = System.IO.Path.GetFileName(Path);
            return $"{file}";
        }
    }

    /// <summary>
    /// A Certificate as test asset.
    /// </summary>
    public class CertificateAsset : IAsset, IFormattable
    {
        public string Path { get; private set; }
        public byte[] Cert { get; private set; }
        public X509Certificate2 X509Certificate { get; private set; }

        public void Initialize(byte[] blob, string path)
        {
            Path = path;
            Cert = blob;
            try
            {
                X509Certificate = X509CertificateLoader.LoadCertificateFromFile(path);
            }
            catch
            {
            }
        }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            string file = System.IO.Path.GetFileName(Path);
            return $"{file}";
        }
    }

    /// <summary>
    /// Test helpers.
    /// </summary>
    public static class CertificateTestUtils
    {
        public static string WriteCRL(X509CRL x509Crl)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.Append("Issuer:     ").AppendLine(x509Crl.Issuer)
                .Append("ThisUpdate: ").Append(x509Crl.ThisUpdate).AppendLine()
                .Append("NextUpdate: ").Append(x509Crl.NextUpdate).AppendLine()
                .AppendLine("RevokedCertificates:");
            foreach (RevokedCertificate revokedCert in x509Crl.RevokedCertificates)
            {
                stringBuilder
                    .AppendFormat(
                        CultureInfo.InvariantCulture,
                        "{0:20}, ",
                        revokedCert.SerialNumber)
                    .Append(revokedCert.RevocationDate)
                    .Append(", ");
                foreach (X509Extension entryExt in revokedCert.CrlEntryExtensions)
                {
                    stringBuilder.Append(entryExt.Format(false)).Append(' ');
                }
                stringBuilder.AppendLine(string.Empty);
            }
            stringBuilder.AppendLine("Extensions:");
            foreach (X509Extension extension in x509Crl.CrlExtensions)
            {
                stringBuilder.AppendLine(extension.Format(false));
            }
            return stringBuilder.ToString();
        }
    }
}
