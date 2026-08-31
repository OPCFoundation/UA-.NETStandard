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
 *
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
using System.Text;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// The ordering <i>OPC UA — WoT Binding</i> §12.6 requires of the
    /// <c>ViewVersion</c> canonicalization.
    /// </summary>
    /// <remarks>
    /// The clause sorts the portable member identifiers by Unicode code point.
    /// UTF-16 code-unit order is not the same order, so a Server sorting
    /// ordinally computes a different <c>ViewVersion</c> from a conforming one
    /// whenever a member carries a supplementary character.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotViewVersionOrderingTests
    {
        [Test]
        public void CodePointOrderPlacesSupplementaryCharactersLast()
        {
            // U+1F600 GRINNING FACE is stored as the surrogate pair D83D DE00,
            // and U+FF21 FULLWIDTH LATIN CAPITAL A as the single unit FF21.
            // By code point the emoji is far above; by UTF-16 code unit D83D
            // is below FF21, so the two orders disagree.
            const string supplementary = "\U0001F600";
            const string fullwidth = "\uFF21";

            Assert.Multiple(() =>
            {
                Assert.That(
                    Math.Sign(StringComparer.Ordinal.Compare(supplementary, fullwidth)),
                    Is.EqualTo(-1),
                    "UTF-16 code-unit order sorts the surrogate pair first.");
                Assert.That(
                    Math.Sign(WotCodePointComparer.Instance.Compare(supplementary, fullwidth)),
                    Is.EqualTo(1),
                    "Unicode code-point order sorts U+1F600 after U+FF21.");
            });
        }

        [Test]
        public void CodePointOrderMatchesUtf8ByteOrder()
        {
            // UTF-8 byte order is code point order by construction, so it is an
            // independent oracle for the reweighting the comparer performs.
            string[] samples =
            [
                string.Empty,
                "\u0001",
                "A",
                "Ab",
                "a",
                "\u00e9",
                "\u07ff",
                "\u0800",
                "\ud7ff",
                "\ue000",
                "\uff21",
                "\ufffd",
                "\U00010000",
                "\U0001F600",
                "\U0010FFFF",
                "ns=1;s=\U0001F600",
                "ns=1;s=\uFF21"
            ];

            for (int ii = 0; ii < samples.Length; ii++)
            {
                for (int jj = 0; jj < samples.Length; jj++)
                {
                    int expected = Math.Sign(CompareUtf8(samples[ii], samples[jj]));
                    int actual = Math.Sign(
                        WotCodePointComparer.Instance.Compare(samples[ii], samples[jj]));
                    Assert.That(
                        actual,
                        Is.EqualTo(expected),
                        $"Comparing '{Escape(samples[ii])}' with '{Escape(samples[jj])}'.");
                }
            }
        }

        [Test]
        public void CodePointOrderHandlesNullsAndIdentity()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WotCodePointComparer.Instance.Compare(null, null), Is.Zero);
                Assert.That(WotCodePointComparer.Instance.Compare(null, "a"), Is.LessThan(0));
                Assert.That(WotCodePointComparer.Instance.Compare("a", null), Is.GreaterThan(0));
                Assert.That(WotCodePointComparer.Instance.Compare("a", "a"), Is.Zero);
            });
        }

        [Test]
        public void SortingByCodePointDiffersFromSortingOrdinally()
        {
            List<string> byCodePoint = ["\U0001F600", "\uFF21", "A"];
            List<string> ordinal = [.. byCodePoint];

            byCodePoint.Sort(WotCodePointComparer.Instance);
            ordinal.Sort(StringComparer.Ordinal);

            Assert.That(byCodePoint, Is.EqualTo(s_expectedCodePointOrder));
            Assert.That(ordinal, Is.Not.EqualTo(byCodePoint));
        }

        private static int CompareUtf8(string left, string right)
        {
            byte[] leftBytes = Encoding.UTF8.GetBytes(left);
            byte[] rightBytes = Encoding.UTF8.GetBytes(right);
            int shared = Math.Min(leftBytes.Length, rightBytes.Length);
            for (int ii = 0; ii < shared; ii++)
            {
                if (leftBytes[ii] != rightBytes[ii])
                {
                    return leftBytes[ii] - rightBytes[ii];
                }
            }
            return leftBytes.Length - rightBytes.Length;
        }

        private static string Escape(string value)
        {
            var builder = new StringBuilder();
            foreach (char unit in value)
            {
                builder.Append("\\u")
                    .Append(((int)unit).ToString(
                        "x4",
                        System.Globalization.CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private static readonly string[] s_expectedCodePointOrder =
            ["A", "\uFF21", "\U0001F600"];
    }
}
