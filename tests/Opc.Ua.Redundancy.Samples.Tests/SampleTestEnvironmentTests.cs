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

using System.Collections.Generic;
using NUnit.Framework;

namespace Opc.Ua.Redundancy.Samples.Tests
{
    [TestFixture]
    [Category("Unit")]
    internal sealed class SampleTestEnvironmentTests
    {
        [Test]
        public void BuildFastDemoUsesLoopbackEndpointAndInsecureDemoKey()
        {
            IReadOnlyDictionary<string, string?> env = SampleTestEnvironment.BuildFastDemo();
            Assert.Multiple(() =>
            {
                Assert.That(
                    env.TryGetValue("PUBSUB_ENDPOINT", out string? endpoint),
                    Is.True);
                Assert.That(endpoint, Is.Not.Null.And.StartsWith("opc.udp://127.0.0.1:"),
                    "PUBSUB_ENDPOINT must use the loopback address with a dynamically allocated port.");
                Assert.That(
                    env.TryGetValue("HA_INSECURE", out string? insecure),
                    Is.True);
                Assert.That(insecure, Is.EqualTo("true"));
            });
        }

        [Test]
        public void BuildFastDemoAllocatesDistinctUdpPortsPerCall()
        {
            IReadOnlyDictionary<string, string?> envA = SampleTestEnvironment.BuildFastDemo();
            IReadOnlyDictionary<string, string?> envB = SampleTestEnvironment.BuildFastDemo();

            Assert.That(envA.TryGetValue("PUBSUB_ENDPOINT", out string? endpointA), Is.True);
            Assert.That(envB.TryGetValue("PUBSUB_ENDPOINT", out string? endpointB), Is.True);
            Assert.That(endpointA, Is.Not.EqualTo(endpointB),
                "Each BuildFastDemo call must produce a unique endpoint so parallel children do not share a port.");
        }
    }
}
