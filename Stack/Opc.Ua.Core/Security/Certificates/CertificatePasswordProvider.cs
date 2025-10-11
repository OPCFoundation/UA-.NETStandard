/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// An interface for a password provider for certificate private keys.
    /// </summary>
    public interface ICertificatePasswordProvider
    {
        /// <summary>
        /// Return the password for a certificate private key.
        /// </summary>
        /// <param name="certificateIdentifier">The certificate identifier for which the password is needed.</param>
        char[] GetPassword(CertificateIdentifier certificateIdentifier);
    }

    /// <summary>
    /// The default certificate password provider implementation.
    /// </summary>
    public class CertificatePasswordProvider : ICertificatePasswordProvider
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public CertificatePasswordProvider()
        {
            m_password = [];
        }

        /// <summary>
        /// Constructor which takes a raw or UTF8 encoded password. If not utf8
        /// the buffer is assumed raw token and will be base64 encoded.
        /// </summary>
        /// <param name="password">The raw password.</param>
        /// <param name="isUtf8String">Whether the password is utf8 string</param>
        public CertificatePasswordProvider(byte[] password, bool isUtf8String = true)
        {
            if (password != null)
            {
                if (isUtf8String)
                {
                    m_password = Encoding.UTF8.GetString(password).ToCharArray();
                }
                else
                {
                    char[] charToken = new char[password.Length * 3];
                    int length = Convert.ToBase64CharArray(
                        password,
                        0,
                        password.Length,
                        charToken,
                        0,
                        Base64FormattingOptions.None);
                    char[] passcode = new char[length];
                    charToken.CopyTo(passcode, 0);
                    Array.Clear(charToken, 0, charToken.Length);
                    m_password = passcode;
                }
            }
            else
            {
                m_password = [];
            }
        }

        /// <summary>
        /// Constructor which takes a password string
        /// </summary>
        /// <param name="password"></param>
        public CertificatePasswordProvider(ReadOnlySpan<char> password)
        {
            if (!password.IsEmpty && !password.IsWhiteSpace())
            {
                m_password = password.ToArray();
            }
            else
            {
                m_password = [];
            }
        }

        /// <summary>
        /// Return the password used for the certificate.
        /// </summary>
        public char[] GetPassword(CertificateIdentifier certificateIdentifier)
        {
            return m_password;
        }

        private readonly char[] m_password;
    }
}
