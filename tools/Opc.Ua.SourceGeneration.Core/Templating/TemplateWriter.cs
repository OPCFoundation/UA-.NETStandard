// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

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
            Emit(text.ToString(), isValue: false);
        }

        /// <inheritdoc/>
        public void Write(string? text)
        {
            WriteWhiteSpaceIfNeeded();
            Emit(text ?? string.Empty, isValue: false);
        }

        /// <summary>
        /// Write a value substituted into a template, such as a token
        /// replacement. Unlike template text, a value that lands on a line
        /// that is already a comment cannot end that comment: see
        /// <see cref="Emit(string, bool)"/>.
        /// </summary>
        public void WriteValue(string? value)
        {
            WriteWhiteSpaceIfNeeded();
            Emit(value ?? string.Empty, isValue: true);
        }

        /// <inheritdoc/>
        public void Write(string format, object arg1)
        {
            WriteWhiteSpaceIfNeeded();
            EmitFormatted(format, [arg1]);
        }

        /// <inheritdoc/>
        public void Write(string format, object arg1, object arg2)
        {
            WriteWhiteSpaceIfNeeded();
            EmitFormatted(format, [arg1, arg2]);
        }

        /// <inheritdoc/>
        public void Write(string format, object arg1, object arg2, object arg3)
        {
            WriteWhiteSpaceIfNeeded();
            EmitFormatted(format, [arg1, arg2, arg3]);
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
            Emit(text ?? string.Empty, isValue: false);
            WriteNewLine(int.MaxValue);
        }

        /// <inheritdoc/>
        public void WriteLine(string text, params object?[] args)
        {
            WriteWhiteSpaceIfNeeded();
            EmitFormatted(text, args ?? []);
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
                m_lineIsComment = false;
                m_lastChar = '\0';
            }
            if (m_writeSpaceCount > 0)
            {
                m_writer.Write(new string(' ', m_writeSpaceCount));
                m_writeSpaceCount = 0;
            }
        }

        /// <summary>
        /// Formats invariantly: the sink is generated source code and schema
        /// documents, which must not pick up the machine's culture (a culture
        /// specific minus sign or decimal separator would make the generated
        /// output invalid). The format is generator text, the arguments are
        /// values: a string argument that contains a line break is written
        /// through the value rule of <see cref="Emit(string, bool)"/>.
        /// </summary>
        private void EmitFormatted(string? format, object?[] args)
        {
            format ??= string.Empty;

            // Non-formattable arguments (e.g. an XmlQualifiedName) are written
            // as their text, which may come from the model as well.
            string?[] values = new string?[args.Length];
            bool hasLineBreakArgument = false;
            for (int ii = 0; ii < args.Length; ii++)
            {
                string? text = args[ii] as string ??
                    (args[ii] is { } value and not IFormattable ? value.ToString() : null);
                if (text != null && ContainsLineTerminator(text))
                {
                    values[ii] = text;
                    hasLineBreakArgument = true;
                }
            }

            if (!hasLineBreakArgument)
            {
                Emit(string.Format(CultureInfo.InvariantCulture, format, args), isValue: false);
                return;
            }

            // Substitute a marker for each such argument so the text around it
            // is still written as generator text and the argument as a value.
            object?[] markedArgs = new object?[args.Length];
            for (int ii = 0; ii < args.Length; ii++)
            {
                markedArgs[ii] = values[ii] != null
                    ? string.Concat(kArgumentStart.ToString(), ii.ToString(CultureInfo.InvariantCulture), kArgumentEnd.ToString())
                    : args[ii];
            }

            string formatted = string.Format(CultureInfo.InvariantCulture, format, markedArgs);
            int position = 0;
            while (position < formatted.Length)
            {
                int start = formatted.IndexOf(
                    kArgumentStart.ToString(), position, StringComparison.Ordinal);
                int end = start < 0 ? -1 : formatted.IndexOf(
                    kArgumentEnd.ToString(), start, StringComparison.Ordinal);
                if (end < 0)
                {
                    Emit(formatted.Substring(position), isValue: false);
                    break;
                }
                Emit(formatted.Substring(position, start - position), isValue: false);
                int index = 0;
                for (int ii = start + 1; ii < end && index <= args.Length; ii++)
                {
                    char digit = formatted[ii];
                    index = digit is >= '0' and <= '9' ? (index * 10) + (digit - '0') : int.MaxValue;
                }
                if (index < values.Length && values[index] != null)
                {
                    Emit(values[index]!, isValue: true);
                }
                else
                {
                    // Not one of the markers (an argument happened to contain
                    // the private-use marker characters): write it as text.
                    Emit(formatted.Substring(start, end - start + 1), isValue: false);
                }
                position = end + 1;
            }
        }

        /// <summary>
        /// Writes <paramref name="text"/> while tracking whether the current
        /// output line is a comment.
        /// </summary>
        /// <remarks>
        /// Model text (descriptions, browse names, URIs) is substituted into
        /// <c>//</c> and <c>///</c> comment lines. A line terminator in such a
        /// value would end the comment and turn the rest of the value into
        /// code of the consumer's assembly, so:
        /// <list type="bullet">
        /// <item>NEL, LINE SEPARATOR and PARAGRAPH SEPARATOR, which C# treats as
        /// line terminators but which never belong in generated code (string
        /// literals escape them), are always written as a space.</item>
        /// <item>A CR or LF inside a value that starts on a line that is
        /// already a comment is written as a space, unless the value itself
        /// continues the comment on the next line (the next line starts with
        /// <c>//</c>).</item>
        /// </list>
        /// Whether a line is a comment is decided conservatively: any
        /// <c>//</c> on the line counts, including one inside a string literal,
        /// which only means a value there loses a line break it cannot
        /// legitimately contain.
        /// </remarks>
        private void Emit(string text, bool isValue)
        {
            if (text.Length == 0)
            {
                return;
            }

            bool commentBeforeValue = isValue && m_lineIsComment;
            StringBuilder? builder = null;
            for (int ii = 0; ii < text.Length; ii++)
            {
                char c = text[ii];
                char output = c;
                if (c is '\u0085' or '\u2028' or '\u2029')
                {
                    output = ' ';
                }
                else if (c is '\r' or '\n')
                {
                    if (commentBeforeValue && !NextLineIsComment(text, ii))
                    {
                        output = ' ';
                    }
                    else
                    {
                        m_lineIsComment = false;
                        m_lastChar = '\0';
                    }
                }

                if (output == ' ' && c != ' ')
                {
                    builder ??= new StringBuilder(text, 0, ii, text.Length);
                }
                if (output is not ('\r' or '\n'))
                {
                    if (output == '/' && m_lastChar == '/')
                    {
                        m_lineIsComment = true;
                    }
                    m_lastChar = output;
                }
                builder?.Append(output);
            }
            m_writer.Write(builder?.ToString() ?? text);
        }

        /// <summary>
        /// Returns whether the text after the line break at
        /// <paramref name="index"/> starts (after a CR LF pair and indentation)
        /// with a comment marker.
        /// </summary>
        private static bool NextLineIsComment(string text, int index)
        {
            int ii = index + 1;
            if (text[index] == '\r' && ii < text.Length && text[ii] == '\n')
            {
                ii++;
            }
            while (ii < text.Length && text[ii] is ' ' or '\t')
            {
                ii++;
            }
            return ii + 1 < text.Length && text[ii] == '/' && text[ii + 1] == '/';
        }

        private static bool ContainsLineTerminator(string text)
        {
            for (int ii = 0; ii < text.Length; ii++)
            {
                if (text[ii] is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029')
                {
                    return true;
                }
            }
            return false;
        }

        private const char kArgumentStart = '\uE000';
        private const char kArgumentEnd = '\uE001';
        private int m_writeSpaceCount;
        private int m_newLineCount;
        private bool m_lineIsComment;
        private char m_lastChar;
        private readonly TextWriter m_writer;
        private readonly bool m_leaveOpen;
        private readonly Stack<int> m_indentCharCount;
    }
}
