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
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy.Samples.Tests;

namespace Opc.Ua.WotCon.Samples.Tests
{
    /// <summary>
    /// Checks onboarding sample startup consent, encrypted bootstrap, and administrator-only ticket authorization.
    /// </summary>
    [TestFixture]
    [Category("Samples")]
    [NonParallelizable]
    public sealed class OnboardingSampleStartupTests
    {
        /// <summary>
        /// Verifies that onboarding help and rejected options exit without warnings or PKI initialization.
        /// </summary>
        [TestCase("OnboardingClient", "--help", 0)]
        [TestCase("OnboardingRegistrar", "--help", 0)]
        [TestCase("OnboardingClient", "--security-none", 1)]
        [TestCase("OnboardingRegistrar", "--security-none", 1)]
        [TestCase("OnboardingClient", "--auto-accept=invalid", 1)]
        [TestCase("OnboardingRegistrar", "--auto-accept=invalid", 1)]
        public async Task HelpAndErrorsDoNotInitializePkiAsync(string application, string argument, int exitCode)
        {
            string root = CreateRoot();
            try
            {
                await using var process = new SampleAppProcess(
                    application, Path.Combine("Gds", application), application,
                    [argument, "--pkiRoot", root],
                    new Dictionary<string, string?>
                    {
                        ["ONBOARDING_DEMO_USER"] = null,
                        ["ONBOARDING_DEMO_PASSWORD"] = null
                    });
                Assert.That(await process.WaitForExitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false), Is.True);
                Assert.That(process.ExitCode, Is.EqualTo(exitCode));
                Assert.That(process.ContainsLine("--help") || process.ContainsLine("Usage"), Is.True);
                Assert.That(process.ContainsLine("WARNING:"), Is.False);
                Assert.That(Directory.Exists(root), Is.False);
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        /// <summary>
        /// Verifies that bootstrap warnings require explicit trust consent and never enable None policy.
        /// </summary>
        [TestCase("OnboardingClient", false)]
        [TestCase("OnboardingClient", true)]
        [TestCase("OnboardingRegistrar", false)]
        [TestCase("OnboardingRegistrar", true)]
        public async Task BootstrapConsentIsExplicitAndDoesNotEnableNoneAsync(string application, bool consent)
        {
            string root = CreateRoot();
            try
            {
                await using var process = new SampleAppProcess(
                    application, Path.Combine("Gds", application), application,
                    [$"--auto-accept={consent}", "--pkiRoot", root, "AutoAcceptUntrustedCertificates=true"],
                    new Dictionary<string, string?>
                    {
                        ["ONBOARDING_DEMO_USER"] = null,
                        ["ONBOARDING_DEMO_PASSWORD"] = null
                    });
                Assert.That(await process.WaitForExitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false), Is.True);
                Assert.That(process.ExitCode, Is.EqualTo(1));
                Assert.That(process.ContainsLine("ONBOARDING_DEMO_USER"), Is.True);
                Assert.That(process.ContainsLine("WARNING: --auto-accept"), Is.EqualTo(consent));
                Assert.That(process.ContainsLine("WARNING: controlled bootstrap"), Is.EqualTo(consent));
                Assert.That(process.ContainsLine("SecurityPolicy None"), Is.False);
                Assert.That(Directory.Exists(root), Is.False);
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        /// <summary>
        /// Verifies encrypted onboarding rejects anonymous administration and permits the authenticated ticket
        /// lifecycle.
        /// </summary>
        [Test]
        public async Task ConsentedBootstrapRetainsEncryptedChannelAndTicketAuthorizationAsync()
        {
            string root = CreateRoot();
            string endpoint = $"opc.tcp://localhost:{TestPorts.GetFreePorts(1)[0]}/OnboardingRegistrar";
            var environment = new Dictionary<string, string?>
            {
                ["ONBOARDING_DEMO_USER"] = Guid.NewGuid().ToString("N"),
                ["ONBOARDING_DEMO_PASSWORD"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            };
            try
            {
                await using var registrar = new SampleAppProcess(
                    "registrar", Path.Combine("Gds", "OnboardingRegistrar"), "OnboardingRegistrar",
                    [
                        "--auto-accept", "--port", new Uri(endpoint).Port.ToString(CultureInfo.InvariantCulture),
                        "--pkiRoot", Path.Combine(root, "registrar")
                    ], environment);
                await registrar.WaitForLineAsync("ONBOARDING_REGISTRAR_READY", TimeSpan.FromSeconds(30))
                    .ConfigureAwait(false);
                await using (var anonymous = new SampleAppProcess(
                    "anonymous", Path.Combine("Gds", "OnboardingClient"), "OnboardingClient",
                    [
                        "--auto-accept", "--endpoint", endpoint, "--pkiRoot", Path.Combine(root, "client"),
                        "--anonymous", "true"
                    ], environment))
                {
                    Assert.That(await anonymous.WaitForExitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                        Is.True);
                    Assert.That(anonymous.ExitCode, Is.EqualTo(1));
                    Assert.That(anonymous.ContainsLine("BadUserAccessDenied"), Is.True);
                    Assert.That(anonymous.ContainsLine("ONBOARDING_DEMO_OK"), Is.False);
                }
                await using var administrator = new SampleAppProcess(
                    "administrator", Path.Combine("Gds", "OnboardingClient"), "OnboardingClient",
                    ["--auto-accept", "--endpoint", endpoint, "--pkiRoot", Path.Combine(root, "client")], environment);
                Assert.That(await administrator.WaitForExitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                    Is.True);
                Assert.That(administrator.ExitCode, Is.Zero);
                Assert.That(administrator.ContainsLine("ONBOARDING_DEMO_OK"), Is.True);
                Assert.That(administrator.ContainsLine("UNREGISTER_AGAIN BadNotFound"), Is.True);
                Assert.That(registrar.ContainsLine("[SignAndEncrypt/Basic256Sha256/Binary]"), Is.True);
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        private static string CreateRoot()
        {
            return Path.Combine(Path.GetTempPath(), nameof(OnboardingSampleStartupTests), Guid.NewGuid().ToString("N"));
        }

        private static void DeleteRoot(string root)
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
