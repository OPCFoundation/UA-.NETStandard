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
