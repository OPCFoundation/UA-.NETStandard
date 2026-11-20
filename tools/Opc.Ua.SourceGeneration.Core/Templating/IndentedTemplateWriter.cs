/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Globalization;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Writes every line to an inner writer indented by four more spaces,
    /// for code emitted into a nested block.
    /// </summary>
    internal sealed class IndentedTemplateWriter : ITemplateWriter
    {
        /// <summary>
        /// Create the writer
        /// </summary>
        public IndentedTemplateWriter(ITemplateWriter inner)
        {
            m_inner = inner;
        }

        /// <inheritdoc/>
        public void Write(char text)
        {
            Write(text.ToString());
        }

        /// <inheritdoc/>
        public void Write(string text)
        {
            if (m_atLineStart && !string.IsNullOrEmpty(text))
            {
                m_inner.Write(kIndent);
                m_atLineStart = false;
            }
            m_inner.Write(text);
        }

        /// <inheritdoc/>
        public void Write(string format, object arg1)
        {
            Write(string.Format(CultureInfo.InvariantCulture, format ?? string.Empty, arg1));
        }

        /// <inheritdoc/>
        public void Write(string format, object arg1, object arg2)
        {
            Write(string.Format(CultureInfo.InvariantCulture, format ?? string.Empty, arg1, arg2));
        }

        /// <inheritdoc/>
        public void Write(string format, object arg1, object arg2, object arg3)
        {
            Write(string.Format(
                CultureInfo.InvariantCulture, format ?? string.Empty, arg1, arg2, arg3));
        }

        /// <inheritdoc/>
        public void WriteLine()
        {
            m_inner.WriteLine();
            m_atLineStart = true;
        }

        /// <inheritdoc/>
        public void WriteLine(string text)
        {
            if (m_atLineStart && !string.IsNullOrEmpty(text))
            {
                m_inner.Write(kIndent);
            }
            m_inner.WriteLine(text);
            m_atLineStart = true;
        }

        /// <inheritdoc/>
        public void WriteLine(string text, params object?[] args)
        {
            WriteLine(string.Format(CultureInfo.InvariantCulture, text ?? string.Empty, args ?? []));
        }

        private const string kIndent = "    ";
        private readonly ITemplateWriter m_inner;
        private bool m_atLineStart = true;
    }
}
