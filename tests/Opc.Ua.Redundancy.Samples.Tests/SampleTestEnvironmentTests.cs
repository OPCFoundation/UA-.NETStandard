/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
