// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Defines constants for key user token policies.
    /// </summary>
    public partial class UserTokenPolicy : IFormattable
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
        /// Returns the string representation of the object.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            if (format == null)
            {
                return string.Format(formatProvider, "{0}", ToString());
            }

            throw new FormatException(CoreUtils.Format(
                "Invalid format string: '{0}'.",
                format));
        }
    }
}
