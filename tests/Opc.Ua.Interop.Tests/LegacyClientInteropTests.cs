/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Interop.Tests
{
    /// <summary>
    /// A 1.5.x client (the legacy peer in client mode) against this stack's
    /// reference server. The peer runs once per configuration and reports
    /// each check separately; every check is its own test result here.
    /// </summary>
    [TestFixture]
    [Category("Interop")]
    [NonParallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class LegacyClientInteropTests
    {
        private static readonly TimeSpan s_clientTimeout = TimeSpan.FromMinutes(4);

        /// <summary>
        /// The session-level checks every security configuration runs.
        /// </summary>
        private static readonly string[] s_basicChecks =
        [
            "Connect",
            "NamespaceArray",
            "ReadServerStatusStructure",
            "BrowseObjectsFolder",
            "TranslateBrowsePath",
            "ReadScalars",
            "ReadAttributes",
            "WriteAndReadBack",
            "CallMethods",
            "Subscription",
            "CloseSession"
        ];

        /// <summary>
        /// Checks of complex types, message sizes and service limits; they do
        /// not depend on the security configuration and run once.
        /// </summary>
        private static readonly string[] s_extendedChecks =
        [
            "Connect",
            "ComplexTypes",
            "LargeArrayRoundTrip",
            "LargeByteStringRoundTrip",
            "OversizedRequestRejected",
            "BrowseContinuationPoints",
            "ReadManyNodes",
            "CloseSession"
        ];

        private static readonly Dictionary<string, (string Policy, MessageSecurityMode Mode, bool UserName)>
            s_securityConfigurations = new()
            {
                ["None"] = (SecurityPolicies.None, MessageSecurityMode.None, false),
                ["Basic256Sha256-Sign"] = (SecurityPolicies.Basic256Sha256, MessageSecurityMode.Sign, false),
                ["Basic256Sha256-SignAndEncrypt"] =
                    (SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt, false),
                ["Basic256Sha256-SignAndEncrypt-UserName"] =
                    (SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt, true),
                ["Aes128_Sha256_RsaOaep-SignAndEncrypt"] =
                    (SecurityPolicies.Aes128_Sha256_RsaOaep, MessageSecurityMode.SignAndEncrypt, false),
                ["Aes256_Sha256_RsaPss-SignAndEncrypt"] =
                    (SecurityPolicies.Aes256_Sha256_RsaPss, MessageSecurityMode.SignAndEncrypt, false),
                ["Aes256_Sha256_RsaPss-SignAndEncrypt-UserName"] =
                    (SecurityPolicies.Aes256_Sha256_RsaPss, MessageSecurityMode.SignAndEncrypt, true)
            };

        private readonly Dictionary<string, Task<PeerRun>> m_runs = new(StringComparer.Ordinal);
        private ServerFixture<ReferenceServer> m_serverFixture;
        private string m_pkiRoot;
        private string m_serverUrl;

        public static IEnumerable<TestCaseData> BasicCases()
        {
            foreach (string configuration in s_securityConfigurations.Keys)
            {
                foreach (string check in s_basicChecks)
                {
                    yield return new TestCaseData(configuration, check);
                }
            }
        }

        public static IEnumerable<TestCaseData> ExtendedCases()
        {
            return s_extendedChecks.Select(check => new TestCaseData(check));
        }

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_pkiRoot = InteropPki.CreateRoot();
            // All node managers: the TestData model holds the custom structure
            // types the complex type check decodes. Operation limits: the read
            // limit check crosses MaxNodesPerRead.
            m_serverFixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AutoAccept = true,
                SecurityNone = true,
                AllNodeManagers = true,
                OperationLimits = true
            };
            await m_serverFixture.LoadConfigurationAsync(InteropPki.ServerPki(m_pkiRoot)).ConfigureAwait(false);
            // The fixture only advertises anonymous logon; the user name cases
            // log on as a user of the reference server's user database.
            m_serverFixture.Config.ServerConfiguration.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.UserName);
            await m_serverFixture.StartAsync().ConfigureAwait(false);
            m_serverUrl = string.Format(
                CultureInfo.InvariantCulture,
                "opc.tcp://localhost:{0}/{1}",
                m_serverFixture.Port,
                nameof(ReferenceServer));
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            foreach (Task<PeerRun> run in m_runs.Values)
            {
                try
                {
                    await run.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Already reported by the tests that awaited it.
                }
            }
            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }
            InteropPki.Delete(m_pkiRoot);
        }

        /// <summary>
        /// A session-level check of the 1.5 client in one security
        /// configuration: discovery and session, namespace array, structure
        /// decoding, browse, translate, read, write, method calls,
        /// subscription and close.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(BasicCases))]
        public async Task LegacyClientCheckAsync(string configuration, string check)
        {
            (string policy, MessageSecurityMode mode, bool userName) = s_securityConfigurations[configuration];
            var arguments = new List<string>
            {
                "--policy", policy,
                "--mode", mode.ToString(),
                "--checks", string.Join(",", s_basicChecks)
            };
            if (userName)
            {
                // A user of the reference server's user database.
                arguments.AddRange(["--user", "user1", "--password", "password"]);
            }
            PeerRun run = await GetRunAsync(configuration, arguments).ConfigureAwait(false);
            run.AssertPassed(check);
        }

        /// <summary>
        /// The 1.5 client loads and decodes the custom data types of the 2.0
        /// server (structures, unions, optional fields, enumerations) and
        /// writes them back; sends messages much larger than a chunk; crosses
        /// the server's MaxByteStringLength and MaxNodesPerRead; and pages a
        /// browse with continuation points.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(ExtendedCases))]
        public async Task LegacyClientExtendedCheckAsync(string check)
        {
            PeerRun run = await GetRunAsync(
                "extended",
                [
                    "--policy", SecurityPolicies.Basic256Sha256,
                    "--mode", nameof(MessageSecurityMode.SignAndEncrypt),
                    "--checks", string.Join(",", s_extendedChecks)
                ]).ConfigureAwait(false);
            run.AssertPassed(check);
        }

        /// <summary>
        /// The 1.5 client keeps reading through a security token renewal of
        /// a SignAndEncrypt channel. Both stacks revise the token lifetime to
        /// at least 60 s and the client renews at 75 %, so this runs about a
        /// minute.
        /// </summary>
        [Test]
        public async Task LegacyClientTokenRenewalAsync()
        {
            PeerRun run = await GetRunAsync(
                "token-renewal",
                [
                    "--policy", SecurityPolicies.Basic256Sha256,
                    "--mode", nameof(MessageSecurityMode.SignAndEncrypt),
                    "--checks", "TokenRenewal",
                    "--token-lifetime", "60000",
                    "--token-test-seconds", "65"
                ]).ConfigureAwait(false);
            run.AssertPassed("Connect");
            run.AssertPassed("TokenRenewal");
            run.AssertPassed("CloseSession");
        }

        /// <summary>
        /// Starts the peer for a configuration once and shares the result
        /// between the tests that report its checks.
        /// </summary>
        private Task<PeerRun> GetRunAsync(string key, List<string> arguments)
        {
            if (!m_runs.TryGetValue(key, out Task<PeerRun> run))
            {
                arguments.InsertRange(0,
                [
                    "client",
                    "--url", m_serverUrl,
                    "--pki", InteropPki.ClientPki(m_pkiRoot)
                ]);
                run = PeerRun.ExecuteAsync(s_clientTimeout, [.. arguments]);
                m_runs[key] = run;
            }
            return run;
        }
    }

    /// <summary>
    /// The outcome of a peer run in client mode.
    /// </summary>
    public sealed class PeerRun
    {
        public int ExitCode { get; private set; }
        public string Output { get; private set; }
        public IReadOnlyList<PeerCheckResult> Results { get; private set; }

        /// <summary>
        /// Asserts that the run completed (exit code 0 or 1 - all checks
        /// passed or some failed - and a SUMMARY line) and that the named
        /// check passed. A crash of the peer after its last result, e.g. while
        /// it disposes its session, fails every check of the run.
        /// </summary>
        public void AssertPassed(string check)
        {
            Assert.That(
                ExitCode,
                Is.AnyOf(0, 1),
                "The 1.5 client did not complete normally." + Environment.NewLine + Output);
            Assert.That(
                Output,
                Does.Contain("SUMMARY "),
                "The 1.5 client did not complete its checks." + Environment.NewLine + Output);
            PeerCheckResult.AssertPassed(Results, check, Output);
        }

        public static async Task<PeerRun> ExecuteAsync(TimeSpan timeout, params string[] arguments)
        {
            (LegacyPeerProcess peer, int exitCode) = await LegacyPeerProcess.RunAsync(timeout, arguments)
                .ConfigureAwait(false);
            using (peer)
            {
                return new PeerRun
                {
                    ExitCode = exitCode,
                    Output = peer.Output,
                    Results = peer.Results
                };
            }
        }
    }
}
