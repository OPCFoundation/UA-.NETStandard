/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Redaction;

namespace Opc.Ua.Types.Redaction
{
    /// <summary>
    /// Sample implementation of the redaction strategy.
    /// </summary>
    public class SimpleRedactionStrategy : IRedactionStrategy
    {
        private const int kDefaultMinLength = 8;
        private const int kDefaultMaxLength = 16;
        private const char kReplacementChar = '*';

        private readonly int m_minLength;
        private readonly int m_maxLength;


        /// <summary>
        /// Creates a new instance of the <see cref="SimpleRedactionStrategy"/> with default lengths.
        /// </summary>
        public SimpleRedactionStrategy() : this(kDefaultMinLength, kDefaultMaxLength)
        { }

        /// <summary>
        /// Creates a new instance of the <see cref="SimpleRedactionStrategy"/> with default lengths.
        /// </summary>
        public SimpleRedactionStrategy(int minLength, int maxLength)
        {
            if (minLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minLength), "Must be a non-negative number");
            }

            if (maxLength < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxLength), "Must be a non-negative number, or -1");
            }

            m_minLength = minLength;
            m_maxLength = maxLength;
        }

        /// <inheritdoc/>
        public string Redact(object value)
        {
            if (value == null)
            {
                return "null";
            }

            if (value is Uri uri)
            {
                return RedactUri(uri);
            }

            if (value is UriBuilder uriBuilder)
            {
                return RedactUri(uriBuilder.Uri);
            }

            if (value is Exception exception)
            {
                return RedactException(exception);
            }

            string valueString = value.ToString();

            if (valueString.Length < m_minLength)
            {
                return new string(kReplacementChar, m_minLength);
            }

            if (m_maxLength == -1 || valueString.Length <= m_maxLength)
            {
                return new string(kReplacementChar, valueString.Length);
            }

            return new string(kReplacementChar, m_maxLength);
        }

        private string RedactException(Exception exception)
        {
            return "An exception of type " + exception.GetType() + " was redacted because it may contain sensitive information.";
        }

        private string RedactUri(Uri uri)
        {
            if (!uri.IsWellFormedOriginalString() || !uri.IsAbsoluteUri || string.IsNullOrEmpty(uri.Host))
            {
                return Redact(uri.ToString());
            }

            string redactedHost = Redact(uri.Host);

            StringBuilder sb = new StringBuilder()
                .Append(uri.Scheme)
                .Append(Uri.SchemeDelimiter)
                .Append(redactedHost);

            if (!uri.IsDefaultPort)
            {
                sb.Append(':').Append(uri.Port);
            }

            if (uri.LocalPath != null && !string.Equals(uri.LocalPath, "/", StringComparison.Ordinal))
            {
                sb.Append(uri.LocalPath);
            }

            return sb.ToString();
        }
    }
}
