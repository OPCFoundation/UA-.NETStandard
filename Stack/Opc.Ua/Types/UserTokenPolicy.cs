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

namespace Opc.Ua
{
    /// <summary>
    /// Defines constants for key user token policies.
    /// </summary>
    public partial class UserTokenPolicy : IFormattable, IEquatable<UserTokenPolicy>
    {
        /// <summary>
        /// Creates an empty token policy with the specified token type.
        /// </summary>
        public UserTokenPolicy(UserTokenType tokenType)
        {
            Initialize();
            m_tokenType = tokenType;
        }

        /// <summary>
        /// Returns the object formatted as a string.
        /// </summary>
        public override string ToString()
        {
            return m_tokenType.ToString();
        }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return string.Format(formatProvider, "{0}", ToString());
            }

            throw new FormatException(CoreUtils.Format(
                "Invalid format string: '{0}'.",
                format));
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as UserTokenPolicy);
        }

        /// <inheritdoc/>
        public bool Equals(UserTokenPolicy other)
        {            
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return
                TokenType == other.TokenType &&
                string.Equals(SecurityPolicyUri, other.SecurityPolicyUri, StringComparison.Ordinal) &&
                string.Equals(IssuedTokenType, other.IssuedTokenType, StringComparison.Ordinal) &&
                string.Equals(IssuerEndpointUrl, other.IssuerEndpointUrl, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                TokenType,
                SecurityPolicyUri,
                IssuedTokenType,
                IssuerEndpointUrl);
        }

        /// <inheritdoc/>
        public static bool operator ==(UserTokenPolicy left, UserTokenPolicy right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (ReferenceEquals(left, null))
            {
                return false;
            }

            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(UserTokenPolicy left, UserTokenPolicy right)
        {
            return !(left == right);
        }
    }
}
