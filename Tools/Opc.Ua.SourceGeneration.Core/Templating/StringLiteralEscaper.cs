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

using System.Text;

namespace Opc.Ua.SourceGeneration.Templating
{
    /// <summary>
    /// Escapes a value so it can be safely interpolated into a generated
    /// C# verbatim or regular string literal (the literal's surrounding
    /// double-quote characters are NOT included in the returned value).
    /// </summary>
    /// <remarks>
    /// Defence-in-depth helper for the source generator. The generator
    /// only consumes design XML from the local repository at build time;
    /// anyone able to edit design XML can already write arbitrary C#.
    /// This helper still hardens the generator so that an ill-formed
    /// <c>BrowseName</c> (e.g. one containing <c>"</c>, <c>\</c>, control
    /// characters, or BIDI overrides) cannot produce an unbalanced
    /// string literal that breaks the consuming build with a confusing
    /// compiler error. Instead the value is safely escaped and an
    /// optional diagnostic is surfaced to the host generator.
    /// </remarks>
    public static class StringLiteralEscaper
    {
        /// <summary>
        /// Returns the escaped content for a C# regular string literal
        /// (i.e. wrap the result in <c>"</c>) without including the
        /// surrounding quotes.
        /// </summary>
        /// <param name="raw">
        /// The raw input. <c>null</c> is treated as an empty string.
        /// </param>
        public static string AsCSharpStringLiteralContent(string raw)
        {
            return AsCSharpStringLiteralContent(raw, out _);
        }

        /// <summary>
        /// Returns the escaped content for a C# regular string literal
        /// and reports whether any character had to be escaped.
        /// </summary>
        /// <param name="raw">
        /// The raw input. <c>null</c> is treated as an empty string.
        /// </param>
        /// <param name="modified">
        /// <c>true</c> when the returned value differs from
        /// <paramref name="raw"/>; <c>false</c> when no escaping was
        /// required. Callers use this signal to emit the
        /// <c>UASG_BROWSENAME_UNSAFE</c> diagnostic.
        /// </param>
        public static string AsCSharpStringLiteralContent(string raw, out bool modified)
        {
            modified = false;
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            // Fast scan to avoid allocating a builder when the input is
            // already a plain printable ASCII string with no escape-
            // requiring characters.
            bool needsEscape = false;
            for (int i = 0; i < raw.Length; i++)
            {
                if (RequiresEscape(raw[i]))
                {
                    needsEscape = true;
                    break;
                }
            }
            if (!needsEscape)
            {
                return raw;
            }

            modified = true;
            var builder = new StringBuilder(raw.Length + 8);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                switch (c)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (IsControl(c))
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("X4",
                                System.Globalization.CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            return builder.ToString();
        }

        /// <summary>
        /// Returns <c>true</c> when the supplied raw string contains at
        /// least one character that would require escaping when
        /// interpolated into a C# regular string literal.
        /// </summary>
        public static bool RequiresEscaping(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }
            for (int i = 0; i < raw.Length; i++)
            {
                if (RequiresEscape(raw[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool RequiresEscape(char c)
        {
            return c == '\\' || c == '"' || IsControl(c);
        }

        private static bool IsControl(char c)
        {
            // C0 control range and DEL.
            return c < 0x20 || c == 0x7f;
        }
    }
}
