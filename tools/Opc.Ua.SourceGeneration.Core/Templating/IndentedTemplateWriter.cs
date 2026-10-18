/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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