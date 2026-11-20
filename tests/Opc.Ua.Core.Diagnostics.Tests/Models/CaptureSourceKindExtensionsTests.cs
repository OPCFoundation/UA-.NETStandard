/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Tests.Models
{
    [TestFixture]
    public sealed class CaptureSourceKindExtensionsTests
    {
        [TestCase("nic", CaptureSourceKind.Nic)]
        [TestCase("inproc-client", CaptureSourceKind.InProcessClient)]
        [TestCase("InProcessClient", CaptureSourceKind.InProcessClient)]
        [TestCase("in-process-client", CaptureSourceKind.InProcessClient)]
        [TestCase("inproc-server", CaptureSourceKind.InProcessServer)]
        [TestCase("InProcessServer", CaptureSourceKind.InProcessServer)]
        [TestCase("replay", CaptureSourceKind.Replay)]
        public void TryParseAcceptsCanonicalAndAliasNames(
            string input,
            CaptureSourceKind expected)
        {
            bool parsed = input.TryParse(out CaptureSourceKind actual);

            Assert.That(parsed, Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [TestCase(CaptureSourceKind.Nic, "nic")]
        [TestCase(CaptureSourceKind.InProcessClient, "inproc-client")]
        [TestCase(CaptureSourceKind.InProcessServer, "inproc-server")]
        [TestCase(CaptureSourceKind.Replay, "replay")]
        public void ToWireNameReturnsCanonicalName(CaptureSourceKind kind, string expected)
        {
            Assert.That(kind.ToWireName(), Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("unknown")]
        public void TryParseRejectsUnknownNames(string input)
        {
            Assert.That(input.TryParse(out _), Is.False);
        }
    }
}
