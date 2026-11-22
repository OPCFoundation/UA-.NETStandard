// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        public static string AsCSharpStringLiteralContent(string? raw)
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
        public static string AsCSharpStringLiteralContent(string? raw, out bool modified)
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
                        if (IsControl(c) || IsNewLine(c))
                        {
                            builder.Append("\\u")
                                .Append(((int)c).ToString("X4",
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
            return c == '\\' || c == '"' || IsControl(c) || IsNewLine(c);
        }

        private static bool IsControl(char c)
        {
            // C0 control range and DEL.
            return c is < (char)0x20 or (char)0x7f;
        }

        private static bool IsNewLine(char c)
        {
            // Besides CR and LF (C0), C# treats NEL, LINE SEPARATOR and
            // PARAGRAPH SEPARATOR as new-line characters, which terminate a
            // regular string literal (CS1010).
            return c is '\u0085' or '\u2028' or '\u2029';
        }
    }
}
