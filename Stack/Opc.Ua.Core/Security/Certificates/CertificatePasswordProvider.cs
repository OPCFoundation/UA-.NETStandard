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
