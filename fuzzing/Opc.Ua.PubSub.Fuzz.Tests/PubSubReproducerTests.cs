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

using System;
using System.IO;
using NUnit.Framework;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Curated reproducers under Assets/Repo, replayed with the resource oracles.
    /// </summary>
    [TestFixture]
    [Category("Fuzzing")]
    public sealed class PubSubReproducerTests
    {
        /// <summary>
        /// U1-1: a UADP discovery request whose array count claims about 2^31 elements
        /// in a 9 byte frame made the decoder allocate gigabytes before reading them.
        /// </summary>
        private const string kDiscoveryCountReproducer = "crash-a37cbc149037fca2713b51983af60846703fce99";

        [Test]
        public void DiscoveryRequestCountReproducerIsSoftRejectedWithinTheAllocationBudget()
        {
            byte[] input = LoadReproducer(kDiscoveryCountReproducer);
            PubSubNetworkMessageContext context = FuzzableCode.NewContext();

            // DecodeUadp runs the decode under the allocation oracle, so a decoder that
            // sizes the array from the count fails here with a ResourceBudgetException.
            PubSubNetworkMessage decoded = FuzzableCode.DecodeUadp(input, context);

            Assert.That(decoded, Is.Null);
            Assert.That(
                context.Diagnostics.Read(PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages),
                Is.EqualTo(1));
        }

        [Test]
        public void DiscoveryRequestCountReproducerPassesEveryTarget()
        {
            byte[] input = LoadReproducer(kDiscoveryCountReproducer);

            Assert.DoesNotThrow(() => FuzzableCode.LibfuzzUadpNetworkMessageDecode(input));
            Assert.DoesNotThrow(() => FuzzableCode.LibfuzzUadpChunkReassembly(input));
            Assert.DoesNotThrow(() => FuzzableCode.LibfuzzPubSubJsonDecode(input));
        }

        private static byte[] LoadReproducer(string name)
        {
            return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "Repo", name));
        }
    }
}
