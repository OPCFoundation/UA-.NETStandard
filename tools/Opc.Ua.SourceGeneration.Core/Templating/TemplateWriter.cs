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
using System.Collections.Generic;
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
        public void Write(string text)
        {
            WriteWhiteSpaceIfNeeded();
            m_writer.Write(text ?? string.Empty);
        }

        /// <inheritdoc/>
        public void Write(string format, object arg1)
        {
            WriteWhiteSpaceIfNeeded();
            m_writer.Write(format ?? string.Empty, arg1);
        }

        /// <inheritdoc/>
        public void Write(string format, object arg1, object arg2)
        {
            WriteWhiteSpaceIfNeeded();
            m_writer.Write(format ?? string.Empty, arg1, arg2);
        }

        /// <inheritdoc/>
        public void Write(string format, object arg1, object arg2, object arg3)
        {
            WriteWhiteSpaceIfNeeded();
            m_writer.Write(format ?? string.Empty, arg1, arg2, arg3);
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
        public void WriteLine(string text, params object[] args)
        {
            WriteWhiteSpaceIfNeeded();
            m_writer.Write(text ?? string.Empty, args ?? []);
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
