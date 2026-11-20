/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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

        public string ToString(string? format, IFormatProvider? formatProvider)
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

        public string ToString(string? format, IFormatProvider? formatProvider)
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
        public string Path { get; private set; } = null!;
        public byte[] Crl { get; private set; } = null!;

        public void Initialize(byte[] blob, string path)
        {
            Path = path;
            Crl = blob;
        }

        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            string file = System.IO.Path.GetFileName(Path);
            return $"{file}";
        }
    }

    /// <summary>
    /// A Certificate as test asset.
    /// </summary>
    public sealed class CertificateAsset : IAsset, IFormattable, IDisposable
    {
        public string Path { get; private set; } = null!;
        public byte[] Cert { get; private set; } = null!;
        public Certificate X509Certificate { get; private set; } = null!;

        public void Initialize(byte[] blob, string path)
        {
            Path = path;
            Cert = blob;
            try
            {
                X509Certificate = new Certificate(path);
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            X509Certificate?.Dispose();
            X509Certificate = null!;
        }

        public string ToString(string? format, IFormatProvider? formatProvider)
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
