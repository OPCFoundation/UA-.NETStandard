// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            if (m_atLineStart)
            {
                m_inner.Write(kIndent);
                m_atLineStart = false;
            }
            m_inner.Write(text);
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

        // The formatted overloads forward format and arguments separately, so
        // the inner writer can tell generator text from model values (see
        // TemplateWriter.Emit).

        /// <inheritdoc/>
        public void Write(string format, object arg1)
        {
            WriteIndentIfNeeded(
                string.Format(CultureInfo.InvariantCulture, format ?? string.Empty, arg1));
            m_inner.Write(format!, arg1);
        }

        /// <inheritdoc/>
        public void Write(string format, object arg1, object arg2)
        {
            WriteIndentIfNeeded(
                string.Format(CultureInfo.InvariantCulture, format ?? string.Empty, arg1, arg2));
            m_inner.Write(format!, arg1, arg2);
        }

        /// <inheritdoc/>
        public void Write(string format, object arg1, object arg2, object arg3)
        {
            WriteIndentIfNeeded(string.Format(
                CultureInfo.InvariantCulture, format ?? string.Empty, arg1, arg2, arg3));
            m_inner.Write(format!, arg1, arg2, arg3);
        }

        private void WriteIndentIfNeeded(string formatted)
        {
            if (m_atLineStart && !string.IsNullOrEmpty(formatted))
            {
                m_inner.Write(kIndent);
                m_atLineStart = false;
            }
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
            if (m_atLineStart &&
                !string.IsNullOrEmpty(
                    string.Format(CultureInfo.InvariantCulture, text ?? string.Empty, args ?? [])))
            {
                m_inner.Write(kIndent);
            }
            m_inner.WriteLine(text!, args ?? []);
            m_atLineStart = true;
        }

        private const string kIndent = "    ";
        private readonly ITemplateWriter m_inner;
        private bool m_atLineStart = true;
    }
}
