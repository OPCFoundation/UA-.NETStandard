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
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Redundancy.Samples.Tests
{
    /// <summary>
    /// Checks redundancy sample startup guards, independent security consent, and isolated demo endpoints.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    internal sealed class SampleTestEnvironmentTests
    {
        /// <summary>
        /// Verifies that OPC UA trust and None-policy warnings depend on explicit flags, not HA or host settings.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, true)]
        public async Task OpcUaSecurityOptInsAreIndependentOfHaConfigurationAsync(
            bool autoAccept, bool securityNone, bool haInsecure)
        {
            string root = Path.Combine(Path.GetTempPath(), "RedundantServerOptionsTests", Guid.NewGuid().ToString("N"));
            await using var process = new SampleAppProcess(
                "options", Path.Combine("Redundancy", "RedundantServer"), "RedundantServer",
                [
                    $"--auto-accept={autoAccept}", $"--security-none={securityNone}",
                    "AutoAcceptUntrustedCertificates=true", "IncludeUnsecurePolicyNone=true"
                ],
                new Dictionary<string, string?>
                {
                    ["HA_PKI_ROOT"] = root,
                    ["HA_INSECURE"] = haInsecure.ToString(),
                    ["HA_MODE"] = "ap",
                    // Fail before constructing a host; no cluster or PKI is needed to test CLI projection.
                    ["HA_RECORD_KEY"] = "not-base64"
                });
            Assert.That(await process.WaitForExitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false), Is.True);
            Assert.That(process.ExitCode, Is.Not.Zero);
            Assert.That(process.ContainsLine("FormatException"), Is.True);
            Assert.That(process.ContainsLine("WARNING: --auto-accept"), Is.EqualTo(autoAccept));
            Assert.That(process.ContainsLine("WARNING: --security-none"), Is.EqualTo(securityNone));
            Assert.That(Directory.Exists(root), Is.False);
        }

        /// <summary>
        /// Verifies that help and invalid options exit before HA configuration or PKI initialization.
        /// </summary>
        [TestCase("--help", 0)]
        [TestCase("--unknown-option", 1)]
        [TestCase("--auto-accept=invalid", 1)]
        [TestCase("--security-none=invalid", 1)]
        public async Task HelpAndParseErrorsDoNotInitializeHaOrPkiAsync(string argument, int expectedExitCode)
        {
            string root = Path.Combine(Path.GetTempPath(), "RedundantServerOptionsTests", Guid.NewGuid().ToString("N"));
            await using var process = new SampleAppProcess(
                "parse", Path.Combine("Redundancy", "RedundantServer"), "RedundantServer", [argument],
                new Dictionary<string, string?>
                {
                    ["HA_PKI_ROOT"] = root,
                    ["HA_RECORD_KEY"] = "not-base64"
                });
            Assert.That(await process.WaitForExitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false), Is.True);
            Assert.That(process.ExitCode, Is.EqualTo(expectedExitCode));
            Assert.That(process.ContainsLine("WARNING:"), Is.False);
            Assert.That(process.ContainsLine("FormatException"), Is.False);
            Assert.That(Directory.Exists(root), Is.False);
        }

        /// <summary>
        /// Verifies that the fast demo uses a dynamically allocated loopback endpoint and explicit insecure HA mode.
        /// </summary>
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

        /// <summary>
        /// Verifies that historian startup rejects unsupported HA topology or unprotected shared records before PKI
        /// setup.
        /// </summary>
        [TestCase("aa", "strong", "strongly consistent active/passive topology")]
        [TestCase("ap", "eventual", "strongly consistent active/passive topology")]
        [TestCase("ap", "strong", "requires protected shared records")]
        public async Task HistorianOptionRetainsUpstreamTopologyAndProtectionGuardsAsync(
            string mode, string consistency, string expectedError)
        {
            string root = Path.Combine(Path.GetTempPath(), "RedundantServerOptionsTests", Guid.NewGuid().ToString("N"));
            await using var process = new SampleAppProcess(
                "historian-options", Path.Combine("Redundancy", "RedundantServer"), "RedundantServer",
                ["--HA_HISTORIAN=true", "--auto-accept=false", "--security-none=false"],
                new Dictionary<string, string?>
                {
                    ["HA_PKI_ROOT"] = root,
                    ["HA_MODE"] = mode,
                    ["HA_CONSISTENCY"] = consistency,
                    ["HA_INSECURE"] = "true",
                    ["HA_RECORD_KEY"] = null
                });
            Assert.That(await process.WaitForExitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false), Is.True);
            Assert.That(process.ExitCode, Is.EqualTo(1));
            Assert.That(process.ContainsLine(expectedError), Is.True);
            Assert.That(process.ContainsLine("WARNING: --auto-accept"), Is.False);
            Assert.That(process.ContainsLine("WARNING: --security-none"), Is.False);
            Assert.That(Directory.Exists(root), Is.False);
        }

        /// <summary>
        /// Verifies that separate fast-demo environments receive distinct UDP endpoints.
        /// </summary>
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
