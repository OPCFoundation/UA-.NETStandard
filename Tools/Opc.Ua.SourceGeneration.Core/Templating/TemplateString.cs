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
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Template string
    /// </summary>
    internal sealed class TemplateString
    {
        /// <summary>
        /// Empty string
        /// </summary>
        public static readonly TemplateString Empty = string.Empty;

        /// <summary>
        /// Parsed formattable string
        /// </summary>
        public ParsedTemplateString ParsedTemplate { get; }

        /// <summary>
        /// Private constructor
        /// </summary>
        private TemplateString(ParsedTemplateString parsedTemplate)
        {
            ParsedTemplate = parsedTemplate;
        }

        /// <summary>
        /// Create from string
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static implicit operator TemplateString(string value)
        {
            return new TemplateString(
                ParsedTemplateString.FromString(value ?? string.Empty));
        }

        /// <summary>
        /// Create from interpolated string parser
        /// </summary>
        /// <param name="parser"></param>
        /// <returns></returns>
        public static TemplateString Parse(TemplateParser parser)
        {
            return new TemplateString(parser.Parsed);
        }
    }

    /// <summary>
    /// Parse template using compiler
    /// </summary>
    [InterpolatedStringHandler]
    internal readonly struct TemplateParser
    {
        /// <inheritdoc/>
        public TemplateParser(int literalLength, int formattedCount)
        {
            Parsed = new ParsedTemplateString(literalLength, formattedCount);
        }

        /// <inheritdoc/>
        public ParsedTemplateString Parsed { get; }

        /// <inheritdoc/>
        public readonly void AppendLiteral(string s)
        {
            Parsed.AddLiteral(s);
        }

        /// <inheritdoc/>
        public readonly void AppendFormatted<T>(T t)
        {
            Parsed.AddFormatted(t.ToString(), typeof(T));
        }

        /// <inheritdoc/>
        public string GetFormattedText()
        {
            return Parsed.ToString(CultureInfo.InvariantCulture); // Return string.empty
        }
    }

    /// <summary>
    /// A parsed template string
    /// </summary>
    internal class ParsedTemplateString : FormattableString
    {
        public const int MaxLiteralLength = 1 * 1024 * 1024;
        public const int MaxFormattedCount = 16 * 1024;

        /// <summary>
        /// Created parsed template
        /// </summary>
        public ParsedTemplateString(int literalLength, int formattedCount)
        {
            if (literalLength is > MaxLiteralLength or < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(literalLength));
            }
            if (formattedCount is > MaxFormattedCount or < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(formattedCount));
            }
            LiteralLength = literalLength;
            FormattedCount = formattedCount;
            m_operations = new List<Op>(formattedCount * 2);
        }

        /// <summary>
        /// Create from raw string. Null is treated as empty string
        /// </summary>
        public static ParsedTemplateString FromString(string rawString)
        {
            rawString ??= string.Empty;
            var parsed = new ParsedTemplateString(rawString.Length, 0);
            parsed.AddLiteral(rawString);
            return parsed;
        }

        /// <summary>
        /// Number of literals in the string
        /// </summary>
        public int LiteralLength { get; }

        /// <summary>
        /// Number of tokens in the string
        /// </summary>
        public int FormattedCount { get; }

        /// <summary>
        /// Is multi line string
        /// </summary>
        public bool IsMultiLine
            => m_operations.Any(o => o.Type == OpType.LineBreak);

        /// <summary>
        /// Get formatting instructions
        /// </summary>
        public IReadOnlyList<Op> Operations => m_operations;

        /// <inheritdoc/>
        public override int ArgumentCount => GetArguments().Length;

        /// <inheritdoc/>
        public override string Format
            => string.Concat([.. m_operations.Select(o => o.Item)]);

        /// <inheritdoc/>
        public override object GetArgument(int index)
        {
            return GetArguments()[index];
        }

        /// <inheritdoc/>
        public override object[] GetArguments()
        {
            return [.. m_operations
                .Where(o => o.Type is OpType.Token or OpType.Value)
                .Select(o => o.Item)];
        }

        /// <inheritdoc/>
        public override string ToString(IFormatProvider formatProvider)
        {
            return string.Format(formatProvider, Format, GetArguments());
        }

        /// <summary>
        /// Add a literal
        /// </summary>
        internal void AddLiteral(string item)
        {
            int strStartIdx = 0;
            int strEndIdx = 0;
            for (int i = 0; i < item.Length; i++)
            {
                if (item[i] == '\r' &&
                    i + 1 < item.Length &&
                    item[i + 1] == '\n')
                {
                    // Strip out windows line feed
                    continue;
                }
                if (item[i] == '\n')
                {
                    if (strStartIdx != strEndIdx)
                    {
                        Add(item[strStartIdx..strEndIdx]);
                    }
                    m_operations.Add(new Op(
                        OpType.LineBreak,
                        Environment.NewLine,
                        m_curOffset,
                        m_curLine));
                    m_curOffset = 0; // Reset offset at line break
                    strEndIdx = strStartIdx = i + 1;
                    m_curLine++;
                    continue;
                }
                strEndIdx++;
            }
            if (strStartIdx == strEndIdx)
            {
                return;
            }
            Add(strStartIdx == 0 && strEndIdx == item.Length - 1 ?
                item : // Do not allocate unnecessary substring
                item[strStartIdx..strEndIdx]);
            void Add(string part)
            {
                if (part.Length == 0)
                {
                    return;
                }
                m_operations.Add(new Op(
                    IsAllSpaces(part) ? OpType.WhiteSpace : OpType.Literal,
                    part,
                    m_curOffset,
                    m_curLine));
                m_curOffset += part.Length;
            }
        }

        /// <summary>
        /// Add formatted item
        /// </summary>
        internal void AddFormatted(string item, Type type)
        {
            m_operations.Add(new Op(
                type == typeof(string) ? OpType.Token : OpType.Value,
                item,
                m_curOffset,
                m_curLine));
            m_curOffset += item.Length;
        }

        private static bool IsAllSpaces(string item)
        {
            for (int i = 0; i < item.Length; i++)
            {
                if (item[i] != ' ')
                {
                    return false;
                }
            }
            return true;
        }

        internal enum OpType
        {
            Literal,
            WhiteSpace,
            LineBreak,
            Value,
            Token
        }

        /// <summary>
        /// Track the operations in the parsed string
        /// </summary>
        internal sealed record class Op(
            OpType Type,
            string Item,
            int Offset,
            int LineNumber);

        private readonly List<Op> m_operations;
        private int m_curOffset;
        private int m_curLine;
    }
}
