/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/


using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Opc.Ua.Security.Certificates.X509
{
    public static class X509Utils
    {

        /// <summary>
        /// Returns the size of the public key and disposes RSA key.
        /// </summary>
        /// <param name="certificate">The certificate</param>
        public static int GetRSAPublicKeySize(X509Certificate2 certificate)
        {
            RSA rsaPublicKey = null;
            try
            {
                rsaPublicKey = certificate.GetRSAPublicKey();
                return rsaPublicKey.KeySize;
            }
            finally
            {
                RsaUtils.RSADispose(rsaPublicKey);
            }
        }

        /// <summary>
        /// Compares two distinguished names.
        /// </summary>
        public static bool CompareDistinguishedName(string name1, string name2)
        {
            // check for simple equality.
            if (String.Compare(name1, name2, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }

            // parse the names.
            List<string> fields1 = ParseDistinguishedName(name1);
            List<string> fields2 = ParseDistinguishedName(name2);

            // can't be equal if the number of fields is different.
            if (fields1.Count != fields2.Count)
            {
                return false;
            }

            // sort to ensure similar entries are compared
            fields1.Sort(StringComparer.OrdinalIgnoreCase);
            fields2.Sort(StringComparer.OrdinalIgnoreCase);

            // compare each.
            for (int ii = 0; ii < fields1.Count; ii++)
            {
                if (String.Compare(fields1[ii], fields2[ii], StringComparison.OrdinalIgnoreCase) != 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Compares two distinguished names.
        /// </summary>
        public static bool CompareDistinguishedName(X509Certificate2 certificate, List<string> parsedName)
        {
            // can't compare if the number of fields is 0.
            if (parsedName.Count == 0)
            {
                return false;
            }

            // parse the names.
            List<string> certificateName = ParseDistinguishedName(certificate.Subject);

            // can't be equal if the number of fields is different.
            if (parsedName.Count != certificateName.Count)
            {
                return false;
            }

            // sort to ensure similar entries are compared
            parsedName.Sort(StringComparer.OrdinalIgnoreCase);
            certificateName.Sort(StringComparer.OrdinalIgnoreCase);

            // compare each entry
            for (int ii = 0; ii < parsedName.Count; ii++)
            {
                if (String.Compare(parsedName[ii], certificateName[ii], StringComparison.OrdinalIgnoreCase) != 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Parses a distingushed name.
        /// </summary>
        public static List<string> ParseDistinguishedName(string name)
        {
            List<string> fields = new List<string>();

            if (String.IsNullOrEmpty(name))
            {
                return fields;
            }

            // determine the delimiter used.
            char delimiter = ',';
            bool found = false;
            bool quoted = false;

            for (int ii = name.Length - 1; ii >= 0; ii--)
            {
                char ch = name[ii];

                if (ch == '"')
                {
                    quoted = !quoted;
                    continue;
                }

                if (!quoted && ch == '=')
                {
                    ii--;

                    while (ii >= 0 && Char.IsWhiteSpace(name[ii])) ii--;
                    while (ii >= 0 && (Char.IsLetterOrDigit(name[ii]) || name[ii] == '.')) ii--;
                    while (ii >= 0 && Char.IsWhiteSpace(name[ii])) ii--;

                    if (ii >= 0)
                    {
                        delimiter = name[ii];
                    }

                    break;
                }
            }

            StringBuilder buffer = new StringBuilder();

            string key = null;
            string value = null;
            found = false;

            for (int ii = 0; ii < name.Length; ii++)
            {
                while (ii < name.Length && Char.IsWhiteSpace(name[ii])) ii++;

                if (ii >= name.Length)
                {
                    break;
                }

                char ch = name[ii];

                if (found)
                {
                    char end = delimiter;

                    if (ii < name.Length && name[ii] == '"')
                    {
                        ii++;
                        end = '"';
                    }

                    while (ii < name.Length)
                    {
                        ch = name[ii];

                        if (ch == end)
                        {
                            while (ii < name.Length && name[ii] != delimiter) ii++;
                            break;
                        }

                        buffer.Append(ch);
                        ii++;
                    }

                    value = buffer.ToString().TrimEnd();
                    found = false;

                    buffer.Length = 0;
                    buffer.Append(key);
                    buffer.Append('=');

                    if (value.IndexOfAny(new char[] { '/', ',', '=' }) != -1)
                    {
                        if (value.Length > 0 && value[0] != '"')
                        {
                            buffer.Append('"');
                        }

                        buffer.Append(value);

                        if (value.Length > 0 && value[value.Length - 1] != '"')
                        {
                            buffer.Append('"');
                        }
                    }
                    else
                    {
                        buffer.Append(value);
                    }

                    fields.Add(buffer.ToString());
                    buffer.Length = 0;
                }

                else
                {
                    while (ii < name.Length)
                    {
                        ch = name[ii];

                        if (ch == '=')
                        {
                            break;
                        }

                        buffer.Append(ch);
                        ii++;
                    }

                    key = buffer.ToString().TrimEnd().ToUpperInvariant();
                    buffer.Length = 0;
                    found = true;
                }
            }

            return fields;
        }


        /// <summary>
        /// Verify RSA key pair of two certificates.
        /// </summary>
        public static bool VerifyRSAKeyPair(
            X509Certificate2 certWithPublicKey,
            X509Certificate2 certWithPrivateKey,
            bool throwOnError = false)
        {
            bool result = false;
            RSA rsaPrivateKey = null;
            RSA rsaPublicKey = null;
            try
            {
                // verify the public and private key match
                rsaPrivateKey = certWithPrivateKey.GetRSAPrivateKey();
#if NETSTANDARD2_1
                // on .NET Core 3 
                rsaPrivateKey.ExportParameters(true);
#endif
                rsaPublicKey = certWithPublicKey.GetRSAPublicKey();
                X509KeyUsageFlags keyUsage = X509Extensions.GetKeyUsage(certWithPublicKey);
                if ((keyUsage & X509KeyUsageFlags.DataEncipherment) != 0)
                {
                    result = VerifyRSAKeyPairCrypt(rsaPublicKey, rsaPrivateKey);
                }
                else if ((keyUsage & X509KeyUsageFlags.DigitalSignature) != 0)
                {
                    result = VerifyRSAKeyPairSign(rsaPublicKey, rsaPrivateKey);
                }
                else
                {
                    throw new CryptographicException("Don't know how to verify the public/private key pair.");
                }
            }
            catch (Exception)
            {
                if (throwOnError)
                {
                    throwOnError = false;
                    throw;
                }
            }
            finally
            {
                RsaUtils.RSADispose(rsaPrivateKey);
                RsaUtils.RSADispose(rsaPublicKey);
                if (!result && throwOnError)
                {
                    throw new CryptographicException("The public/private key pair in the certficates do not match.");
                }
            }
            return result;
        }

        /// <summary>
        /// Verify the signature of a self signed certificate.
        /// </summary>
        public static bool VerifySelfSigned(X509Certificate2 cert)
        {
            try
            {
                //TODO
                Org.BouncyCastle.X509.X509Certificate bcCert = new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(cert.RawData);
                bcCert.Verify(bcCert.GetPublicKey());
            }
            catch
            {
                return false;
            }
            return true;
        }

        private static bool VerifyRSAKeyPairCrypt(
            RSA rsaPublicKey,
            RSA rsaPrivateKey)
        {
            Test.RandomSource randomSource = new Test.RandomSource();
            int blockSize = RsaUtils.GetPlainTextBlockSize(rsaPrivateKey, RsaUtils.Padding.OaepSHA1);
            byte[] testBlock = new byte[blockSize];
            randomSource.NextBytes(testBlock, 0, blockSize);
            byte[] encryptedBlock = rsaPublicKey.Encrypt(testBlock, RSAEncryptionPadding.OaepSHA1);
            byte[] decryptedBlock = rsaPrivateKey.Decrypt(encryptedBlock, RSAEncryptionPadding.OaepSHA1);
            if (decryptedBlock != null)
            {
                return Utils.IsEqual(testBlock, decryptedBlock);
            }
            return false;
        }

        private static bool VerifyRSAKeyPairSign(
            RSA rsaPublicKey,
            RSA rsaPrivateKey)
        {
            Opc.Ua.Test.RandomSource randomSource = new Opc.Ua.Test.RandomSource();
            int blockSize = RsaUtils.GetPlainTextBlockSize(rsaPrivateKey, RsaUtils.Padding.OaepSHA1);
            byte[] testBlock = new byte[blockSize];
            randomSource.NextBytes(testBlock, 0, blockSize);
            byte[] signature = rsaPrivateKey.SignData(testBlock, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
            return rsaPublicKey.VerifyData(testBlock, signature, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        }
    }
}
