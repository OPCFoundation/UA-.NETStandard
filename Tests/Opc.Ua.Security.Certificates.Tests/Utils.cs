/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Org.BouncyCastle.X509;

namespace Opc.Ua.Security.Certificates.Tests
{
    #region KeyHashPair Helper
    public class KeyHashPair : IFormattable
    {
        public KeyHashPair(ushort keySize, HashAlgorithmName hashAlgorithmName)
        {
            KeySize = keySize;
            HashAlgorithmName = hashAlgorithmName;
        }

        public ushort KeySize;
        public HashAlgorithmName HashAlgorithmName;

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return $"{KeySize}-{HashAlgorithmName}";
        }
    }

    public class KeyHashPairCollection : List<KeyHashPair>
    {
        public KeyHashPairCollection() { }
        public KeyHashPairCollection(IEnumerable<KeyHashPair> collection) : base(collection) { }
        public KeyHashPairCollection(int capacity) : base(capacity) { }
        public static KeyHashPairCollection ToJsonValidationDataCollection(KeyHashPair[] values)
        {
            return values != null ? new KeyHashPairCollection(values) : new KeyHashPairCollection();
        }

        public void Add(ushort keySize, HashAlgorithmName hashAlgorithmName)
        {
            Add(new KeyHashPair(keySize, hashAlgorithmName));
        }
    }
    #endregion

    #region Asset Helpers
    public interface IAsset
    {
        void Initialize(byte[] crl, string path);
    }

    /// <summary>
    /// A CRL as test asset.
    /// </summary>
    public class CRLAsset : IAsset, IFormattable
    {
        public CRLAsset() { }

        public string Path;
        public byte[] Crl;
        public X509Crl X509Crl;

        public void Initialize(byte[] crl, string path)
        {
            Path = path;
            Crl = crl;
            try
            {
                X509CrlParser parser = new X509CrlParser();
                X509Crl = parser.ReadCrl(crl);
            }
            catch
            { }
        }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            var file = System.IO.Path.GetFileName(Path);
            return $"{file}";
        }
    }

    public class CertificateAsset : IAsset, IFormattable
    {
        public CertificateAsset() { }

        public string Path;
        public byte[] Cert;
        public X509Certificate2 X509Certificate;

        public void Initialize(byte[] cert, string path)
        {
            Path = path;
            Cert = cert;
            try
            {
                X509Certificate = new X509Certificate2(path);
            }
            catch
            { }
        }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            var file = System.IO.Path.GetFileName(Path);
            return $"{file}";
        }
    }

    /// <summary>
    /// Create a collection of test assets.
    /// </summary>
    public class AssetCollection<T> : List<T> where T : IAsset, new()
    {
        public AssetCollection() { }
        public AssetCollection(IEnumerable<T> collection) : base(collection) { }
        public AssetCollection(int capacity) : base(capacity) { }
        public static AssetCollection<T> ToAssetCollection(T[] values)
        {
            return values != null ? new AssetCollection<T>(values) : new AssetCollection<T>();
        }

        public AssetCollection(IEnumerable<string> filelist) : base()
        {
            foreach (var file in filelist)
            {
                Add(file);
            }
        }

        public void Add(string path)
        {
            byte[] crl = File.ReadAllBytes(path);
            T asset = new T();
            asset.Initialize(crl, path);
            Add(asset);
        }
    }
    #endregion

    /// <summary>
    /// Test helpers.
    /// </summary>
    public static class Utils
    {

        #region Public Methods
        public static string WriteCRL(X509CRL x509Crl)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine($"Issuer:     {x509Crl.Issuer}");
            stringBuilder.AppendLine($"ThisUpdate: {x509Crl.ThisUpdate}");
            stringBuilder.AppendLine($"NextUpdate: {x509Crl.NextUpdate}");
            stringBuilder.AppendLine($"RevokedCertificates:");
            foreach (var revokedCert in x509Crl.RevokedCertificates)
            {
                stringBuilder.Append($"{revokedCert.SerialNumber:20}, {revokedCert.RevocationDate}, ");
                foreach (var entryExt in revokedCert.CrlEntryExtensions)
                {
                    stringBuilder.Append($"{entryExt.Format(false)} ");
                }
                stringBuilder.AppendLine("");
            }
            stringBuilder.AppendLine($"Extensions:");
            foreach (var extension in x509Crl.CrlExtensions)
            {
                stringBuilder.AppendLine($"{extension.Format(false)}");
            }
            return stringBuilder.ToString();
        }

        /// <summary>
        /// Helper to convert a bouncy castle DN.
        /// </summary>
        public static string GetIssuer(X509Crl crl)
        {
            // a few conversions to match System.Security conventions
            string issuerDN = crl.IssuerDN.ToString();
            // replace state ST= with S= 
            issuerDN = issuerDN.Replace("ST=", "S=");
            // reverse DN order to match System.Security
            List<string> issuerList = X509Utils.ParseDistinguishedName(issuerDN);
            issuerList.Reverse();
            return string.Join(", ", issuerList);
        }
        #endregion
    }

}
