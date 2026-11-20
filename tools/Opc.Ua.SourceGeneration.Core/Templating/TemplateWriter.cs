// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Template writer that handles indentation and line break management.
    /// </summary>
    internal sealed class TemplateWriter : IDisposable, ITemplateWriter
    {
        /// <summary>
        /// Create template
        /// </summary>
        public TemplateWriter(TextWriter writer, bool leaveOpen = true)
        {
            m_writer = writer;
            m_leaveOpen = leaveOpen;
            m_indentCharCount = new Stack<int>();
            m_indentCharCount.Push(0);
        }

        /// <summary>
        /// Indent character count
        /// </summary>
        public int IndentationCharCount => m_indentCharCount.Peek();

        /// <inheritdoc/>
        public void Dispose()
        {
            while (m_newLineCount > 0)
            {
                m_writer.Write(Environment.NewLine);
                m_newLineCount--;
            }
            if (!m_leaveOpen)
            {
                m_writer.Dispose();
            }
        }

        /// <summary>
        /// Increases the current indentation level by the
        /// specified number of characters.
        /// </summary>
        /// <param name="charCount">The number of characters
        /// to add to the current indentation level. Must be
        /// zero or greater.</param>
        public void PushIndentChars(int charCount)
        {
            m_indentCharCount.Push(IndentationCharCount + charCount);
        }

        /// <summary>
        /// Decreases the current indentation level back
        /// </summary>
        public void PopIndentation()
        {
            if (m_indentCharCount.Count > 1)
            {
                m_indentCharCount.Pop();
                TrimLineBreak(0);
            }
        }

        /// <summary>
        /// Trim any whitespace and line breaks on the current line.
        /// </summary>
        public void TrimLineBreak(int maxNewLines = 1)
        {
            if (m_newLineCount > maxNewLines)
            {
                m_newLineCount = maxNewLines;
            }
        }

        /// <inheritdoc/>
        public void Write(char text)
        {
            WriteWhiteSpaceIfNeeded();
            m_writer.Write(text);
        }

        /// <inheritdoc/>
        public void Write(string? text)
        {
            WriteWhiteSpaceIfNeeded();
            m_writer.Write(text ?? string.Empty);
        }

        /// <inheritdoc/>
        public void Write(string format, object arg1)
        {
            WriteWhiteSpaceIfNeeded();
            m_writer.Write(
                string.Format(CultureInfo.InvariantCulture, format ?? string.Empty, arg1));
        }

        /// <inheritdoc/>
        public void Write(string format, object arg1, object arg2)
        {
            WriteWhiteSpaceIfNeeded();
            m_writer.Write(
                string.Format(CultureInfo.InvariantCulture, format ?? string.Empty, arg1, arg2));
        }

        /// <inheritdoc/>
        public void Write(string format, object arg1, object arg2, object arg3)
        {
            WriteWhiteSpaceIfNeeded();
            m_writer.Write(string.Format(
                CultureInfo.InvariantCulture, format ?? string.Empty, arg1, arg2, arg3));
        }

        /// <inheritdoc/>
        public void WriteLine()
        {
            WriteNewLine(2); // do not write more than 2 new lines - make configurable
        }

        /// <inheritdoc/>
        public void WriteLine(string text)
        {
            WriteWhiteSpaceIfNeeded();
            m_writer.Write(text ?? string.Empty);
            WriteNewLine(int.MaxValue);
        }

        /// <inheritdoc/>
        public void WriteLine(string text, params object?[] args)
        {
            WriteWhiteSpaceIfNeeded();
            // Always format invariantly: the sink is generated source code and
            // schema documents, which must not pick up the machine's culture
            // (a culture specific minus sign or decimal separator would make the
            // generated output invalid).
            m_writer.Write(
                string.Format(CultureInfo.InvariantCulture, text ?? string.Empty, args ?? []));
            WriteNewLine(int.MaxValue);
        }

        /// <summary>
        /// Write whitespace
        /// </summary>
        public void WriteWhiteSpace(int charCount)
        {
            // Queue up spaces to write on next write to drop trailing whitespace
            m_writeSpaceCount += charCount;
        }

        /// <summary>
        /// Begin new line
        /// </summary>
        public bool WriteNewLine(int maxNewLines)
        {
            if (m_newLineCount >= maxNewLines)
            {
                return false;
            }
            m_newLineCount++;
            // Reset to current indent level
            m_writeSpaceCount = IndentationCharCount;
            return true;
        }

        /// <summary>
        /// Write indent if needed.
        /// </summary>
        private void WriteWhiteSpaceIfNeeded()
        {
            // Write pending spaces.
            while (m_newLineCount > 0)
            {
                m_writer.Write(Environment.NewLine);
                m_newLineCount--;
            }
            if (m_writeSpaceCount > 0)
            {
                m_writer.Write(new string(' ', m_writeSpaceCount));
                m_writeSpaceCount = 0;
            }
        }

        private int m_writeSpaceCount;
        private int m_newLineCount;
        private readonly TextWriter m_writer;
        private readonly bool m_leaveOpen;
        private readonly Stack<int> m_indentCharCount;
    }
}
