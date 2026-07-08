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

using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Formats;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Tests.Formats
{
    [TestFixture]
    public sealed class FormatKindExtensionsTests
    {
        [TestCase("pcap", FormatKind.Pcap)]
        [TestCase("PCAP", FormatKind.Pcap)]
        [TestCase("pcapng", FormatKind.PcapNg)]
        [TestCase("json", FormatKind.Json)]
        [TestCase("csv", FormatKind.Csv)]
        [TestCase("text", FormatKind.Text)]
        [TestCase("service-timeline", FormatKind.ServiceTimeline)]
        [TestCase("servicetimeline", FormatKind.ServiceTimeline)]
        [TestCase("timeline", FormatKind.ServiceTimeline)]
        public void TryParseAcceptsCanonicalAndAliasNames(string input, FormatKind expected)
        {
            bool ok = input.TryParse(out FormatKind actual);

            Assert.That(ok, Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [TestCase("  json  ")]
        [TestCase("JSON")]
        public void TryParseIsCaseInsensitiveAndTrimsWhitespace(string input)
        {
            bool ok = input.TryParse(out FormatKind actual);

            Assert.That(ok, Is.True);
            Assert.That(actual, Is.EqualTo(FormatKind.Json));
        }

        [TestCase("")]
        [TestCase("xml")]
        [TestCase("unknown-format")]
        public void TryParseReturnsFalseAndDefaultPcapForUnknownInput(string input)
        {
            bool ok = input.TryParse(out FormatKind actual);

            Assert.That(ok, Is.False);
            Assert.That(actual, Is.EqualTo(FormatKind.Pcap),
                "Per the contract the out parameter defaults to Pcap when parsing fails.");
        }

        [Test]
        public void TryParseHandlesNullStringAsUnknown()
        {
            const string? input = null;

            bool ok = input.TryParse(out FormatKind actual);

            Assert.That(ok, Is.False);
            Assert.That(actual, Is.EqualTo(FormatKind.Pcap));
        }
    }
}
