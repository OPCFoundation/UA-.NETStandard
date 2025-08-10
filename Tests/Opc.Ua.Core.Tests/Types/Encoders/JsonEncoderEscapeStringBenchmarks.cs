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
using Newtonsoft.Json;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
#if NET6_0_OR_GREATER
using System.Text.Encodings.Web;
using System.Text.Json;
#endif

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
        [
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
        ];

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
                EscapedStringLegacy(s_testString);
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
                EscapedStringLegacyPlus(s_testString);
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
                EscapeString(s_testString);
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
                EscapeStringSpan(s_testString);
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
                EscapeStringSpanChars(s_testString);
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
                EscapeStringSpanCharsInline(s_testString);
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
                EscapeStringSpanCharsInlineConst(s_testString);
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
                EscapeStringSpanIndex(s_testString);
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
                EscapeStringSpanDict(s_testString);
            }
            m_streamWriter.Flush();
        }

        /// <summary>
        /// Using NewtonSoft, which first converts to string.
        /// </summary>
        [Benchmark]
        public void EscapeStringNewtonSoft()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapeStringNewtonSoft(s_testString);
            }
            m_streamWriter.Flush();
        }

#if NET6_0_OR_GREATER
        /// <summary>
        /// A new implementation using ReadOnlySpan and Dictionary.
        /// </summary>
        [Benchmark]
        public void EscapeStringSystemTextJson()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapeStringSystemTextJson(s_testString);
            }
            m_streamWriter.Flush();
        }
#endif

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
        [NonParallelizable]
        public void EscapeStringValidation(string name, int index)
        {
            m_memoryStream = new RecyclableMemoryStream(m_memoryManager);
            m_streamWriter = new StreamWriter(m_memoryStream, new UTF8Encoding(false), m_streamSize, false);

            s_testString = EscapeTestStrings[index];
            TestContext.Out.WriteLine(s_testString);
            char[] testArray = s_testString.ToCharArray();

            m_memoryStream.Position = 0;
            EscapedStringLegacy(s_testString);
            m_streamWriter.Flush();
            byte[] resultLegacy = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultLegacy));

            m_memoryStream.Position = 0;
            EscapedStringLegacyPlus(s_testString);
            m_streamWriter.Flush();
            byte[] resultLegacyPlus = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultLegacyPlus));

            m_memoryStream.Position = 0;
            EscapeString(s_testString);
            m_streamWriter.Flush();
            byte[] result = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(result));

            m_memoryStream.Position = 0;
            EscapeStringSpan(s_testString);
            m_streamWriter.Flush();
            byte[] resultSpan = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSpan));

            m_memoryStream.Position = 0;
            EscapeStringSpanChars(s_testString);
            m_streamWriter.Flush();
            byte[] resultSpanChars = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSpanChars));

            m_memoryStream.Position = 0;
            EscapeStringSpanCharsInline(s_testString);
            m_streamWriter.Flush();
            byte[] resultSpanCharsInline = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSpanCharsInline));

            m_memoryStream.Position = 0;
            EscapeStringSpanCharsInlineConst(s_testString);
            m_streamWriter.Flush();
            byte[] resultSpanCharsInlineConst = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSpanCharsInlineConst));

            m_memoryStream.Position = 0;
            EscapeStringSpanIndex(s_testString);
            m_streamWriter.Flush();
            byte[] resultSpanIndex = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSpanIndex));

            m_memoryStream.Position = 0;
            EscapeStringSpanDict(s_testString);
            m_streamWriter.Flush();
            byte[] resultSpanDict = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSpanDict));

            m_memoryStream = new RecyclableMemoryStream(m_memoryManager);
            m_streamWriter = new StreamWriter(m_memoryStream, new UTF8Encoding(false), m_streamSize, false);
            EscapeStringNewtonSoft(s_testString);
            m_streamWriter.Flush();
            byte[] resultNewtonSoft = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultNewtonSoft));

#if NET6_0_OR_GREATER
            m_memoryStream = new RecyclableMemoryStream(m_memoryManager);
            EscapeStringSystemTextJson(s_testString);
            byte[] resultSystemTextJson = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSystemTextJson));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSystemTextJson));
#endif

            Assert.IsTrue(Utils.IsEqual(resultLegacy, result));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultLegacyPlus));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSpan));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSpanChars));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSpanCharsInline));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSpanCharsInlineConst));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSpanIndex));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSpanDict));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultNewtonSoft));
        }

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

        /// <summary>4
        /// Set up some variables for benchmarks.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            m_memoryManager = new RecyclableMemoryStreamManager();
            m_memoryStream = new RecyclableMemoryStream(m_memoryManager);
            m_streamWriter = new StreamWriter(m_memoryStream, Encoding.UTF8, m_streamSize, false);
            s_testString = EscapeTestStrings[StringVariantIndex - 1];
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

        /// <summary>
        /// Version used previously in JsonEncoder.
        /// </summary>
        private void EscapedStringLegacy(string value)
        {
            foreach (char ch in value)
            {
                bool found = false;

                for (int ii = 0; ii < s_specialChars.Length; ii++)
                {
                    if (s_specialChars[ii] == ch)
                    {
                        m_streamWriter.Write('\\');
                        m_streamWriter.Write(s_substitution[ii]);
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

                for (int ii = 0; ii < s_specialChars.Length; ii++)
                {
                    if (s_specialChars[ii] == ch)
                    {
                        m_streamWriter.Write('\\');
                        m_streamWriter.Write(s_substitution[ii]);
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

                for (int ii = 0; ii < s_specialChars.Length; ii++)
                {
                    if (s_specialChars[ii] == ch)
                    {
                        if (lastOffset < i)
                        {
                            m_streamWriter.Write(charSpan[lastOffset..i]);
                        }
                        lastOffset = i + 1;
                        m_streamWriter.Write(s_substitutionStrings[ii]);
                        found = true;
                        break;
                    }
                }

                if (!found && ch < 32)
                {
                    if (lastOffset < i)
                    {
                        m_streamWriter.Write(charSpan[lastOffset..i]);
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
                m_streamWriter.Write(charSpan[lastOffset..]);
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

                for (int ii = 0; ii < s_specialChars.Length; ii++)
                {
                    if (s_specialChars[ii] == ch)
                    {
                        if (lastOffset < i)
                        {
                            m_streamWriter.Write(charSpan[lastOffset..i]);
                        }
                        lastOffset = i + 1;
                        m_streamWriter.Write('\\');
                        m_streamWriter.Write(s_substitution[ii]);
                        found = true;
                        break;
                    }
                }

                if (!found && ch < 32)
                {
                    if (lastOffset < i - 1)
                    {
                        m_streamWriter.Write(charSpan[lastOffset..i]);
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
                m_streamWriter.Write(charSpan);
            }
            else if (lastOffset < charSpan.Length)
            {
                m_streamWriter.Write(charSpan[lastOffset..]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteSpan(ref int lastOffset, ReadOnlySpan<char> valueSpan, int index)
        {
            if (lastOffset < index - 2)
            {
                m_streamWriter.Write(valueSpan[lastOffset..index]);
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

                for (int ii = 0; ii < s_specialChars.Length; ii++)
                {
                    if (s_specialChars[ii] == ch)
                    {
                        WriteSpan(ref lastOffset, charSpan, i);
                        m_streamWriter.Write('\\');
                        m_streamWriter.Write(s_substitution[ii]);
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
                m_streamWriter.Write(charSpan);
            }
            else
            {
                WriteSpan(ref lastOffset, charSpan, charSpan.Length);
            }
        }

        /// <summary>
        /// create version of EscapeStringSpanCharsInline that references cosnt arrays
        /// </summary>
        /// <param name="value"></param>
        private void EscapeStringSpanCharsInlineConst(string value)
        {
            ReadOnlySpan<char> charSpan = value.AsSpan();
            int lastOffset = 0;

            for (int i = 0; i < charSpan.Length; i++)
            {
                bool found = false;
                char ch = charSpan[i];

                for (int ii = 0; ii < s_specialCharsConst.Length; ii++)
                {
                    if (s_specialCharsConst[ii] == ch)
                    {
                        WriteSpan(ref lastOffset, charSpan, i);
                        m_streamWriter.Write('\\');
                        m_streamWriter.Write(s_substitutionConst[ii]);
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

                int index = kSpecialString.IndexOf(ch, StringComparison.Ordinal);
                if (index >= 0)
                {
                    if (lastOffset < i)
                    {
                        m_streamWriter.Write(charSpan[lastOffset..i]);
                    }
                    lastOffset = i + 1;
                    m_streamWriter.Write(s_substitutionStrings[index]);
                    continue;
                }

                if (ch < 32)
                {
                    if (lastOffset < i)
                    {
                        m_streamWriter.Write(charSpan[lastOffset..i]);
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
                m_streamWriter.Write(charSpan[lastOffset..]);
            }
        }

        private void EscapeStringSpanDict(string value)
        {
            ReadOnlySpan<char> charSpan = value.AsSpan();

            int lastOffset = 0;
            for (int i = 0; i < charSpan.Length; i++)
            {
                char ch = charSpan[i];

                if (s_replace.TryGetValue(ch, out string escapeSequence))
                {
                    if (lastOffset < i)
                    {
                        m_streamWriter.Write(charSpan[lastOffset..i]);
                    }
                    lastOffset = i + 1;
                    m_streamWriter.Write(escapeSequence);
                    continue;
                }

                if (ch < 32)
                {
                    if (lastOffset < i)
                    {
                        m_streamWriter.Write(charSpan[lastOffset..i]);
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
                m_streamWriter.Write(charSpan[lastOffset..]);
            }
        }

        private void EscapeStringNewtonSoft(string value)
        {
            string newtonSoftConvertedText = JsonConvert.ToString(value);
            newtonSoftConvertedText = newtonSoftConvertedText[1..^1];
            m_streamWriter.Write(newtonSoftConvertedText);
        }

#if NET6_0_OR_GREATER
        private void EscapeStringSystemTextJson(string value)
        {
            var jsonEncodedText = JsonEncodedText.Encode(s_testString, JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
            m_memoryStream.Write(jsonEncodedText.EncodedUtf8Bytes);
        }
#endif

        private void EscapeString(string value)
        {
            var stringBuilder = new StringBuilder(value.Length * 2);

            foreach (char ch in value)
            {
                if (s_replace.TryGetValue(ch, out string escapeSequence))
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

        private static string s_testString;
        private RecyclableMemoryStreamManager m_memoryManager;
        private RecyclableMemoryStream m_memoryStream;
        private StreamWriter m_streamWriter;
        private readonly int m_streamSize = 1024;
        private const string kSpecialString = "\"\\\n\r\t\b\f";

        /// <summary>
        /// Declare static readonly characters for the special characters
        /// </summary>
        private const char kSroQuotation = '\"';
        private const char kSroBackslash = '\\';
        private const char kSroNewline = '\n';
        private const char kSroReturn = '\r';
        private const char kSroTab = '\t';
        private const char kSroBackspace = '\b';
        private const char kSroFormfeed = '\f';
        private static readonly char[] s_specialChars =
        [
            kSroQuotation,
            kSroBackslash,
            kSroNewline,
            kSroReturn,
            kSroTab,
            kSroBackspace,
            kSroFormfeed,
        ];

        /// <summary>
        /// Declare static readonly characters for the substitution characters
        /// </summary>
        private const char kSro_quotationSub = '\"';
        private const char kSro_backslashSub = '\\';
        private const char kSro_newlineSub = 'n';
        private const char kSro_returnSub = 'r';
        private const char kSro_tabSub = 't';
        private const char kSro_backspaceSub = 'b';
        private const char kSro_formfeedSub = 'f';
        private static readonly char[] s_substitution =
        [
            kSro_quotationSub,
            kSro_backslashSub,
            kSro_newlineSub,
            kSro_returnSub,
            kSro_tabSub,
            kSro_backspaceSub,
            kSro_formfeedSub,
        ];

        /// <summary>
        /// Special characters as const
        /// </summary>
        private const char kQuotation = '\"';
        private const char kBackslash = '\\';
        private const char kNewline = '\n';
        private const char kReturn = '\r';
        private const char kTab = '\t';
        private const char kBackspace = '\b';
        private const char kFormfeed = '\f';

        private static readonly char[] s_specialCharsConst =
        [
            kQuotation,
            kBackslash,
            kNewline,
            kReturn,
            kTab,
            kBackspace,
            kFormfeed,
        ];

        /// <summary>
        /// Substitution as const
        /// </summary>
        private const char kQuotationSub = '\"';
        private const char kBackslashSub = '\\';
        private const char kNewlineSub = 'n';
        private const char kReturnSub = 'r';
        private const char kTabSub = 't';
        private const char kBackspaceSub = 'b';
        private const char kFormfeedSub = 'f';

        private static readonly char[] s_substitutionConst =
        [
            kQuotationSub,
            kBackslashSub,
            kNewlineSub,
            kReturnSub,
            kTabSub,
            kBackspaceSub,
            kFormfeedSub,
        ];

        private static readonly string[] s_substitutionStrings = ["\\\"", "\\\\", "\\n", "\\r", "\\t", "\\b", "\\f"];
        private static readonly Dictionary<char, string> s_replace = new()
        {
            { '\"', "\\\"" },
            { '\\', "\\\\" },
            { '\n', "\\n" },
            { '\r', "\\r" },
            { '\t', "\\t" },
            { '\b', "\\b" },
            { '\f', "\\f" },
        };
    }
}
