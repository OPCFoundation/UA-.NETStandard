// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using NUnit.Framework;
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

        [TestCase(FormatKind.Pcap, "pcap")]
        [TestCase(FormatKind.PcapNg, "pcapng")]
        [TestCase(FormatKind.Json, "json")]
        [TestCase(FormatKind.Csv, "csv")]
        [TestCase(FormatKind.Text, "text")]
        [TestCase(FormatKind.ServiceTimeline, "service-timeline")]
        public void ToWireNameReturnsCanonicalName(FormatKind kind, string expected)
        {
            Assert.That(kind.ToWireName(), Is.EqualTo(expected));
        }
    }
}
