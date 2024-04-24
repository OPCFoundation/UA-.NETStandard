/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using Microsoft.IO;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    [TestFixture, Category("JsonEncoder")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [NonParallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser(printSource: true)]
    public class JsonEncoderEscapeStringBenchmarks
    {
        public const int InnerLoops = 100;
        [DatapointSource]
        [Params(1, 2, 3, 4, 5, 6, 7, 8, 9, 10)]
        public int StringVariantIndex { get; set; } = 1;

        [DatapointSource]
        // for benchmarking with different escaped strings
        public static readonly string[] EscapeTestStrings =
        {
            // The use case without escape characters, plain text
                "The quick brown fox jumps over the lazy dog.",
            // The use case with many control characters escaped, 1 char spaces
                "\" \n \r \t \b \f \\ ",
            // The use case with many control characters escaped, 2 char spaces
                "  \"  \n  \r  \t  \b  \f  \\  ",
            // The use case with many control characters escaped, 3 char spaces
                "   \"   \n   \r   \t   \b   \f   \\   ",
            // The use case with many control characters escaped, 5 char spaces
                "     \"     \n     \r     \t     \b     \f     \\     ",
            // The use case with many binary characters escaped, 1 char spaces
                "\0 \x01 \x02 \x03 \x04 ",
            // The use case with many binary characters escaped, 2 char spaces
                "  \0  \x01  \x02  \x03  \x04  ",
            // The use case with many binary characters escaped, 3 char spaces
                "   \0   \x01   \x02   \x03   \x04   ",
            // The use case with many binary characters escaped, 5 char spaces
                "     \0     \x01     \x02     \x03     \x04     ",
            // The use case with all escape characters and a long string
                "Ascii characters, special characters \n \b & control characters \0 \x04 ␀ ␁ ␂ ␃ ␄. This is a test.",
        };

        /// <summary>
        /// Benchmark encoding of the previous implementation.
        /// </summary>
        [Benchmark(Baseline = true)]
        public void EscapeStringLegacy()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapedStringLegacy(m_testString);
            }
            m_streamWriter.Flush();
        }

        /// <summary>
        /// Benchmark encoding of the previous implementation with snall improvement for binary encoding.
        /// </summary>
        [Benchmark]
        public void EscapeStringLegacyPlus()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapedStringLegacyPlus(m_testString);
            }
            m_streamWriter.Flush();
        }

        /// <summary>
        /// A new implementation using StringBuilder.
        /// </summary>
        [Benchmark]
        public void EscapeStringStringBuilder()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapeString(m_testString);
            }
            m_streamWriter.Flush();
        }

        /// <summary>
        /// A new implementation using ReadOnlySpan.
        /// </summary>
        [Benchmark]
        public void EscapeStringSpan()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapeStringSpan(m_testString);
            }
            m_streamWriter.Flush();
        }

        /// <summary>
        /// A new implementation using ReadOnlySpan and char write.
        /// </summary>
        [Benchmark]
        public void EscapeStringSpanChars()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapeStringSpanChars(m_testString);
            }
            m_streamWriter.Flush();
        }

        /// <summary>
        /// A new implementation using ReadOnlySpan and char write.
        /// </summary>
        [Benchmark]
        public void EscapeStringSpanCharsInline()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapeStringSpanCharsInline(m_testString);
            }
            m_streamWriter.Flush();
        }

        /// <summary>
        /// A new implementation using ReadOnlySpan and char write with const arrays.
        /// </summary>
        [Benchmark]
        public void EscapeStringSpanCharsInlineConst()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapeStringSpanCharsInlineConst(m_testString);
            }
            m_streamWriter.Flush();
        }

        /// <summary>
        /// A new implementation using ReadOnlySpan and IndexOf.
        /// </summary>
        [Benchmark]
        public void EscapeStringSpanIndex()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapeStringSpanIndex(m_testString);
            }
            m_streamWriter.Flush();
        }

        /// <summary>
        /// A new implementation using ReadOnlySpan and Dictionary.
        /// </summary>
        [Benchmark]
        public void EscapeStringSpanDict()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapeStringSpanDict(m_testString);
            }
            m_streamWriter.Flush();
        }

        [Theory]
        [TestCase("No Escape chars", 0)]
        [TestCase("control chars escaped, 1 char space", 1)]
        [TestCase("control chars escaped, 2 char spaces", 2)]
        [TestCase("control chars escaped, 3 char spaces", 3)]
        [TestCase("control chars escaped, 5 char spaces", 4)]
        [TestCase("binary chars escaped, 1 char space", 5)]
        [TestCase("binary chars escaped, 2 char spaces", 6)]
        [TestCase("binary chars escaped, 3 char spaces", 7)]
        [TestCase("binary chars escaped, 5 char spaces", 8)]
        [TestCase("all escape chars and long string", 9)]
        public void EscapeStringValidation(string name, int index)
        {
            m_testString = EscapeTestStrings[index];
            TestContext.Out.WriteLine(m_testString);
            var testArray = m_testString.ToCharArray();

            m_memoryStream.Position = 0;
            EscapeStringLegacy();
            m_streamWriter.Flush();
            byte[] resultLegacy = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultLegacy));

            m_memoryStream.Position = 0;
            EscapeStringLegacyPlus();
            m_streamWriter.Flush();
            byte[] resultLegacyPlus = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultLegacyPlus));

            m_memoryStream.Position = 0;
            EscapeStringStringBuilder();
            m_streamWriter.Flush();
            byte[] result = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(result));

            m_memoryStream.Position = 0;
            EscapeStringSpan(m_testString);
            m_streamWriter.Flush();
            byte[] resultSpan = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSpan));

            m_memoryStream.Position = 0;
            EscapeStringSpanChars(m_testString);
            m_streamWriter.Flush();
            byte[] resultSpanChars = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSpanChars));

            m_memoryStream.Position = 0;
            EscapeStringSpanCharsInline(m_testString);
            m_streamWriter.Flush();
            byte[] resultSpanCharsInline = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSpanCharsInline));

            m_memoryStream.Position = 0;
            EscapeStringSpanCharsInlineConst(m_testString);
            m_streamWriter.Flush();
            byte[] resultSpanCharsInlineConst = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSpanCharsInlineConst));

            m_memoryStream.Position = 0;
            EscapeStringSpanIndex(m_testString);
            m_streamWriter.Flush();
            byte[] resultSpanIndex = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSpanIndex));

            m_memoryStream.Position = 0;
            EscapeStringSpanDict(m_testString);
            m_streamWriter.Flush();
            byte[] resultSpanDict = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSpanDict));

            Assert.IsTrue(Utils.IsEqual(resultLegacy, result));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultLegacyPlus));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSpan));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSpanChars));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSpanCharsInline));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSpanCharsInlineConst));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSpanIndex));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSpanDict));
        }

        #region Test Setup
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_memoryManager = new RecyclableMemoryStreamManager();
            m_memoryStream = new RecyclableMemoryStream(m_memoryManager);
            m_streamWriter = new StreamWriter(m_memoryStream, new UTF8Encoding(false), m_streamSize, false);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_streamWriter?.Dispose();
            m_streamWriter = null;
            m_memoryStream?.Dispose();
            m_memoryStream = null;
            m_memoryManager = null;
        }
        #endregion

        #region Benchmark Setup
        /// <summary>4
        /// Set up some variables for benchmarks.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            m_memoryManager = new RecyclableMemoryStreamManager();
            m_memoryStream = new RecyclableMemoryStream(m_memoryManager);
            m_streamWriter = new StreamWriter(m_memoryStream, Encoding.UTF8, m_streamSize, false);
            m_testString = EscapeTestStrings[StringVariantIndex - 1];
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            m_streamWriter?.Dispose();
            m_streamWriter = null;
            m_memoryStream?.Dispose();
            m_memoryStream = null;
            m_memoryManager = null;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Version used previously in JsonEncoder.
        /// </summary>
        private void EscapedStringLegacy(string value)
        {
            foreach (char ch in value)
            {
                bool found = false;

                for (int ii = 0; ii < m_specialChars.Length; ii++)
                {
                    if (m_specialChars[ii] == ch)
                    {
                        m_streamWriter.Write('\\');
                        m_streamWriter.Write(m_substitution[ii]);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    if (ch < 32)
                    {
                        m_streamWriter.Write("\\u");
                        m_streamWriter.Write("{0:X4}", (int)ch);
                        continue;
                    }

                    m_streamWriter.Write(ch);
                }
            }
        }

        /// <summary>
        /// Version used previously in JsonEncoder plus improvement of binary encoding.
        /// </summary>
        /// <remarks>
        /// For the underlying stream writer it is faster to write two chars than a 2 char string.
        /// </remarks>
        private void EscapedStringLegacyPlus(string value)
        {
            foreach (char ch in value)
            {
                bool found = false;

                for (int ii = 0; ii < m_specialChars.Length; ii++)
                {
                    if (m_specialChars[ii] == ch)
                    {
                        m_streamWriter.Write('\\');
                        m_streamWriter.Write(m_substitution[ii]);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    if (ch < 32)
                    {
                        m_streamWriter.Write('\\');
                        m_streamWriter.Write('u');
                        m_streamWriter.Write(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                        continue;
                    }

                    m_streamWriter.Write(ch);
                }
            }
        }

        /// <summary>
        /// Using a span to escape the string, write strings to stream writer if possible.
        /// </summary>
        private void EscapeStringSpan(string value)
        {
            ReadOnlySpan<char> charSpan = value.AsSpan();
            int lastOffset = 0;

            for (int i = 0; i < charSpan.Length; i++)
            {
                bool found = false;
                char ch = charSpan[i];

                for (int ii = 0; ii < m_specialChars.Length; ii++)
                {
                    if (m_specialChars[ii] == ch)
                    {
                        if (lastOffset < i)
                        {
#if NETCOREAPP2_1_OR_GREATER
                            m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset));
#else
                            m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset).ToString());
#endif
                        }
                        lastOffset = i + 1;
                        m_streamWriter.Write(m_substitutionStrings[ii]);
                        found = true;
                        break;
                    }
                }

                if (!found && ch < 32)
                {
                    if (lastOffset < i)
                    {
#if NETCOREAPP2_1_OR_GREATER
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset));
#else
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset).ToString());
#endif
                    }
                    lastOffset = i + 1;
                    m_streamWriter.Write("\\u");
                    m_streamWriter.Write(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
            }
            if (lastOffset == 0)
            {
                m_streamWriter.Write(value);
            }
            else if (lastOffset < charSpan.Length)
            {
#if NETCOREAPP2_1_OR_GREATER
                m_streamWriter.Write(charSpan.Slice(lastOffset, charSpan.Length - lastOffset));
#else
                m_streamWriter.Write(charSpan.Slice(lastOffset, charSpan.Length - lastOffset).ToString());
#endif
            }
        }

        /// <summary>
        /// Use span to escape the string, write only chars to stream writer.
        /// </summary>
        /// <param name="value"></param>
        private void EscapeStringSpanChars(string value)
        {
            ReadOnlySpan<char> charSpan = value.AsSpan();

            int lastOffset = 0;
            for (int i = 0; i < charSpan.Length; i++)
            {
                bool found = false;
                char ch = charSpan[i];

                for (int ii = 0; ii < m_specialChars.Length; ii++)
                {
                    if (m_specialChars[ii] == ch)
                    {
                        if (lastOffset < i)
                        {
#if NETCOREAPP2_1_OR_GREATER
                            m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset));
#else
                            m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset).ToString());
#endif
                        }
                        lastOffset = i + 1;
                        m_streamWriter.Write('\\');
                        m_streamWriter.Write(m_substitution[ii]);
                        found = true;
                        break;
                    }
                }

                if (!found && ch < 32)
                {
                    if (lastOffset < i - 1)
                    {
#if NETCOREAPP2_1_OR_GREATER
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset));
#else
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset).ToString());
#endif
                    }
                    else
                    {
                        while (lastOffset < i)
                        {
                            m_streamWriter.Write(charSpan[lastOffset++]);
                        }
                    }
                    lastOffset = i + 1;
                    m_streamWriter.Write('\\');
                    m_streamWriter.Write('u');
                    m_streamWriter.Write(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
            }

            if (lastOffset == 0)
            {
#if NETCOREAPP2_1_OR_GREATER
                m_streamWriter.Write(charSpan);
#else
                m_streamWriter.Write(value);
#endif
            }
            else if (lastOffset < charSpan.Length)
            {
#if NETCOREAPP2_1_OR_GREATER
                m_streamWriter.Write(charSpan.Slice(lastOffset, charSpan.Length - lastOffset));
#else
                m_streamWriter.Write(charSpan.Slice(lastOffset, charSpan.Length - lastOffset).ToString());
#endif
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteSpan(ref int lastOffset, ReadOnlySpan<char> valueSpan, int index)
        {
            if (lastOffset < index - 2)
            {
#if NETCOREAPP2_1_OR_GREATER
                m_streamWriter.Write(valueSpan.Slice(lastOffset, index - lastOffset));
#else
                m_streamWriter.Write(valueSpan.Slice(lastOffset, index - lastOffset).ToString());
#endif
            }
            else
            {
                while (lastOffset < index)
                {
                    m_streamWriter.Write(valueSpan[lastOffset++]);
                }
            }
            lastOffset = index + 1;
        }

        /// <summary>
        /// Write only chars to stream writer, inline the write sequence for readability.
        /// </summary>
        /// <param name="value"></param>
        private void EscapeStringSpanCharsInline(string value)
        {
            ReadOnlySpan<char> charSpan = value.AsSpan();
            int lastOffset = 0;

            for (int i = 0; i < charSpan.Length; i++)
            {
                bool found = false;
                char ch = charSpan[i];

                for (int ii = 0; ii < m_specialChars.Length; ii++)
                {
                    if (m_specialChars[ii] == ch)
                    {
                        WriteSpan(ref lastOffset, charSpan, i);
                        m_streamWriter.Write('\\');
                        m_streamWriter.Write(m_substitution[ii]);
                        found = true;
                        break;
                    }
                }

                if (!found && ch < 32)
                {
                    WriteSpan(ref lastOffset, charSpan, i);
                    m_streamWriter.Write('\\');
                    m_streamWriter.Write('u');
                    m_streamWriter.Write(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
            }

            if (lastOffset == 0)
            {
#if NETCOREAPP2_1_OR_GREATER
                m_streamWriter.Write(charSpan);
#else
                m_streamWriter.Write(value);
#endif
            }
            else
            {
                WriteSpan(ref lastOffset, charSpan, charSpan.Length);
            }
        }

        // create version of EscapeStringSpanCharsInline that references cosnt arrays
        private void EscapeStringSpanCharsInlineConst(string value)
        {
            ReadOnlySpan<char> charSpan = value.AsSpan();
            int lastOffset = 0;

            for (int i = 0; i < charSpan.Length; i++)
            {
                bool found = false;
                char ch = charSpan[i];

                for (int ii = 0; ii < m_specialCharsConst.Length; ii++)
                {
                    if (m_specialCharsConst[ii] == ch)
                    {
                        WriteSpan(ref lastOffset, charSpan, i);
                        m_streamWriter.Write('\\');
                        m_streamWriter.Write(m_substitutionConst[ii]);
                        found = true;
                        break;
                    }
                }

                if (!found && ch < 32)
                {
                    WriteSpan(ref lastOffset, charSpan, i);
                    m_streamWriter.Write('\\');
                    m_streamWriter.Write('u');
                    m_streamWriter.Write(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
            }

            if (lastOffset == 0)
            {
                m_streamWriter.Write(value);
            }
            else
            {
                WriteSpan(ref lastOffset, charSpan, charSpan.Length);
            }
        }


        private void EscapeStringSpanIndex(string value)
        {
            ReadOnlySpan<char> charSpan = value.AsSpan();

            int lastOffset = 0;
            for (int i = 0; i < charSpan.Length; i++)
            {
                char ch = charSpan[i];

                int index = m_specialString.IndexOf(ch);
                if (index >= 0)
                {
                    if (lastOffset < i)
                    {
#if NETCOREAPP2_1_OR_GREATER
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset));
#else
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset).ToString());
#endif
                    }
                    lastOffset = i + 1;
                    m_streamWriter.Write(m_substitutionStrings[index]);
                    continue;
                }

                if (ch < 32)
                {
                    if (lastOffset < i)
                    {
#if NETCOREAPP2_1_OR_GREATER
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset));
#else
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset).ToString());
#endif
                    }
                    lastOffset = i + 1;
                    m_streamWriter.Write('\\');
                    m_streamWriter.Write('u');
                    m_streamWriter.Write(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
            }
            if (lastOffset == 0)
            {
                m_streamWriter.Write(value);
            }
            else if (lastOffset < charSpan.Length)
            {
#if NETCOREAPP2_1_OR_GREATER
                m_streamWriter.Write(charSpan.Slice(lastOffset, charSpan.Length - lastOffset));
#else
                m_streamWriter.Write(charSpan.Slice(lastOffset, charSpan.Length - lastOffset).ToString());
#endif
            }
        }

        private void EscapeStringSpanDict(string value)
        {
            ReadOnlySpan<char> charSpan = value.AsSpan();

            int lastOffset = 0;
            for (int i = 0; i < charSpan.Length; i++)
            {
                char ch = charSpan[i];

                if (m_replace.TryGetValue(ch, out string escapeSequence))
                {
                    if (lastOffset < i)
                    {
#if NETCOREAPP2_1_OR_GREATER
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset));
#else
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset).ToString());
#endif
                    }
                    lastOffset = i + 1;
                    m_streamWriter.Write(escapeSequence);
                    continue;
                }

                if (ch < 32)
                {
                    if (lastOffset < i)
                    {
#if NETCOREAPP2_1_OR_GREATER
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset));
#else
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset).ToString());
#endif
                    }
                    lastOffset = i + 1;
                    m_streamWriter.Write('\\');
                    m_streamWriter.Write('u');
                    m_streamWriter.Write(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
            }
            if (lastOffset == 0)
            {
                m_streamWriter.Write(value);
            }
            else if (lastOffset < charSpan.Length)
            {
#if NETCOREAPP2_1_OR_GREATER
                m_streamWriter.Write(charSpan.Slice(lastOffset, charSpan.Length - lastOffset));
#else
                m_streamWriter.Write(charSpan.Slice(lastOffset, charSpan.Length - lastOffset).ToString());
#endif
            }
        }

        private void EscapeString(string value)
        {
            StringBuilder stringBuilder = new StringBuilder(value.Length * 2);

            foreach (char ch in value)
            {
                if (m_replace.TryGetValue(ch, out string escapeSequence))
                {
                    stringBuilder.Append(escapeSequence);
                }
                else if (ch < 32)
                {
                    stringBuilder.Append("\\u");
                    stringBuilder.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
                else
                {
                    stringBuilder.Append(ch);
                }
            }
            m_streamWriter.Write(stringBuilder);
        }
        #endregion

        #region Private Fields
        private static string m_testString;
        private RecyclableMemoryStreamManager m_memoryManager;
        private RecyclableMemoryStream m_memoryStream;
        private StreamWriter m_streamWriter;
        private int m_streamSize = 1024;
        private static readonly string m_specialString = "\"\\\n\r\t\b\f";

        // Declare static readonly characters for the special characters
        private static readonly char sro_quotation = '\"';
        private static readonly char sro_backslash = '\\';
        private static readonly char sro_newline = '\n';
        private static readonly char sro_return = '\r';
        private static readonly char sro_tab = '\t';
        private static readonly char sro_backspace = '\b';
        private static readonly char sro_formfeed = '\f';
        private static readonly char[] m_specialChars = new char[] { sro_quotation, sro_backslash, sro_newline, sro_return, sro_tab, sro_backspace, sro_formfeed };

        // Declare static readonly characters for the substitution characters
        private static readonly char sro_quotationSub = '\"';
        private static readonly char sro_backslashSub = '\\';
        private static readonly char sro_newlineSub = 'n';
        private static readonly char sro_returnSub = 'r';
        private static readonly char sro_tabSub = 't';
        private static readonly char sro_backspaceSub = 'b';
        private static readonly char sro_formfeedSub = 'f';
        private static readonly char[] m_substitution = new char[] { sro_quotationSub, sro_backslashSub, sro_newlineSub, sro_returnSub, sro_tabSub, sro_backspaceSub, sro_formfeedSub };

        // Special characters as const
        private const char s_quotation = '\"';
        private const char s_backslash = '\\';
        private const char s_newline = '\n';
        private const char s_return = '\r';
        private const char s_tab = '\t';
        private const char s_backspace = '\b';
        private const char s_formfeed = '\f';

        private static readonly char[] m_specialCharsConst = new char[] { s_quotation, s_backslash, s_newline, s_return, s_tab, s_backspace, s_formfeed };

        // Substitution as const
        private const char s_quotationSub = '\"';
        private const char s_backslashSub = '\\';
        private const char s_newlineSub = 'n';
        private const char s_returnSub = 'r';
        private const char s_tabSub = 't';
        private const char s_backspaceSub = 'b';
        private const char s_formfeedSub = 'f';

        private static readonly char[] m_substitutionConst = new char[] { s_quotationSub, s_backslashSub, s_newlineSub, s_returnSub, s_tabSub, s_backspaceSub, s_formfeedSub };

        private static readonly string[] m_substitutionStrings = new string[] { "\\\"", "\\\\", "\\n", "\\r", "\\t", "\\b", "\\f" };
        private static readonly Dictionary<char, string> m_replace = new Dictionary<char, string>
        {
            {  '\"', "\\\"" },
            {  '\\', "\\\\" },
            { '\n', "\\n" },
            { '\r', "\\r" },
            { '\t', "\\t" },
            { '\b', "\\b" },
            { '\f', "\\f" }
        };
        #endregion
    }
}
