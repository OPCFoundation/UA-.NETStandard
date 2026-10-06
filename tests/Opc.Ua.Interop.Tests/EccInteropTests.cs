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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Interop.Tests
{
    /// <summary>
    /// The ECC security policies between the stacks, both directions, with
    /// anonymous logon and with a user name token, whose password the
    /// client encrypts with the ECC secret of the policy. Both sides use
    /// their default RSA and ECC application certificates.
    /// </summary>
    [TestFixture]
    [PeerDifferences]
    [Category("Interop")]
    [NonParallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class EccInteropTests
    {
        /// <summary>
        /// Long enough for two renewals of a 60 s token at 75 %.
        /// </summary>
        private const int kAeadRenewalSeconds = 100;

        private static readonly TimeSpan s_peerTimeout = TimeSpan.FromMinutes(3);

        /// <summary>
        /// The ECC policies of OPC UA 1.05 that 1.5.x implements; every
        /// peer of the ECC fixture is expected to offer them.
        /// </summary>
        private static readonly string[] s_legacyPolicies =
        [
            SecurityPolicies.ECC_nistP256,
            SecurityPolicies.ECC_nistP384,
            SecurityPolicies.ECC_brainpoolP256r1,
            SecurityPolicies.ECC_brainpoolP384r1
        ];

        /// <summary>
        /// ECC policies that 1.5.x does not implement: the authenticated
        /// encryption (AesGcm, ChaChaPoly) and Edwards curve policies. They
        /// run only against a peer that declares them in the policies of its
        /// PEER-INFO line, e.g. open62541 built with OpenSSL.
        /// </summary>
        private static readonly string[] s_declaredPolicies =
        [
            SecurityPolicies.ECC_nistP256_AesGcm,
            SecurityPolicies.ECC_nistP256_ChaChaPoly,
            SecurityPolicies.ECC_curve25519,
            SecurityPolicies.ECC_curve448
        ];

        /// <summary>
        /// The ECC policies with SignAndEncrypt (anonymous and user name)
        /// and Sign.
        /// </summary>
        public static IEnumerable<TestCaseData> EccCases()
        {
            foreach (string policy in s_legacyPolicies.Concat(s_declaredPolicies))
            {
                string name = policy.Substring(policy.LastIndexOf('#') + 1);
                yield return new TestCaseData(policy, MessageSecurityMode.SignAndEncrypt, false)
                    .SetArgDisplayNames(name, "SignAndEncrypt", "Anonymous");
                yield return new TestCaseData(policy, MessageSecurityMode.SignAndEncrypt, true)
                    .SetArgDisplayNames(name, "SignAndEncrypt", "UserName");
                // Sign only derives its keys with a different HKDF salt length
                // than SignAndEncrypt (Part 6, 6.8.1), except with
                // authenticated encryption, whose tag needs the keys.
                yield return new TestCaseData(policy, MessageSecurityMode.Sign, false)
                    .SetArgDisplayNames(name, "Sign", "Anonymous");
            }
        }

        private ITelemetryContext m_telemetry;
        private string m_pkiRoot;
        private LegacyPeerProcess m_legacyServer;
        private string m_legacyServerUrl;
        private ClientFixture m_client;
        private ArrayOf<EndpointDescription> m_legacyEndpoints;
        private ServerFixture<ReferenceServer> m_server;
        private string m_serverUrl;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = InteropPki.CreateRoot();

            (m_legacyServer, m_legacyServerUrl) = await LegacyPeerProcess
                .StartServerAsync(InteropPki.ServerPki(m_pkiRoot) + "l", s_peerTimeout, "--ecc", "true")
                .ConfigureAwait(false);

            m_client = new ClientFixture(telemetry: m_telemetry);
            await m_client.LoadClientConfigurationAsync(InteropPki.ClientPki(m_pkiRoot)).ConfigureAwait(false);
            m_legacyEndpoints = await m_client.GetEndpointsAsync(new Uri(m_legacyServerUrl)).ConfigureAwait(false);

            m_server = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            await m_server.LoadConfigurationAsync(InteropPki.ServerPki(m_pkiRoot)).ConfigureAwait(false);
            m_server.Config.ServerConfiguration.UserTokenPolicies += new UserTokenPolicy(UserTokenType.UserName);
            await m_server.StartAsync().ConfigureAwait(false);
            m_serverUrl = string.Format(
                CultureInfo.InvariantCulture,
                "opc.tcp://localhost:{0}/{1}",
                m_server.Port,
                nameof(ReferenceServer));
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            try
            {
                if (m_server != null)
                {
                    await m_server.StopAsync().ConfigureAwait(false);
                }
                if (m_client != null)
                {
                    await m_client.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await InteropPki.StopAndDeleteAsync(m_legacyServer, m_pkiRoot).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The 2.0 client opens a session on an ECC endpoint of the 1.5
        /// server and reads the server state.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(EccCases))]
        public async Task ClientToLegacyServerAsync(string policy, MessageSecurityMode mode, bool userName)
        {
            if (!m_client.Config.SecurityConfiguration.SupportedSecurityPolicies.Contains(policy))
            {
                Assert.Ignore($"The 2.0 client does not support {policy} on this platform.");
            }
            RequirePeerPolicy(policy);
            EndpointDescription description = m_legacyEndpoints.ToArray()
                .FirstOrDefault(e => e.SecurityPolicyUri == policy && e.SecurityMode == mode);
            if (description == null)
            {
                Assert.Ignore($"The 1.5 server does not offer {policy}/{mode} on this platform.");
            }
            IUserIdentity identity = userName
                ? new UserIdentity("interop", System.Text.Encoding.UTF8.GetBytes("interop-password"))
                : null;
            var endpoint = new ConfiguredEndpoint(null, description, EndpointConfiguration.Create(m_client.Config));
            ISession session = await m_client.ConnectAsync(endpoint, identity).ConfigureAwait(false);
            try
            {
                Assert.That(session.Endpoint.SecurityPolicyUri, Is.EqualTo(policy));
                DataValue state = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(state.StatusCode), Is.True, state.StatusCode.ToString());
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                session.Dispose();
            }
        }

        /// <summary>
        /// The 1.5 client opens a session on an ECC endpoint of the 2.0
        /// reference server and runs its read, write and call checks.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(EccCases))]
        public async Task LegacyClientToServerAsync(string policy, MessageSecurityMode mode, bool userName)
        {
            bool offered = m_server.Config.ServerConfiguration.SecurityPolicies.ToArray()
                .Any(p => p.SecurityPolicyUri == policy && p.SecurityMode == mode);
            if (!offered)
            {
                Assert.Ignore($"The 2.0 server does not offer {policy}/{mode} on this platform.");
            }
            RequirePeerPolicy(policy);
            var arguments = new List<string>
            {
                "client",
                "--url", m_serverUrl,
                "--pki", InteropPki.ClientPki(m_pkiRoot) + "l",
                "--ecc", "true",
                "--policy", policy,
                "--mode", mode.ToString(),
                "--checks", "NamespaceArray,ReadScalars,WriteAndReadBack,CallMethods"
            };
            if (userName)
            {
                arguments.AddRange(["--user", "user1", "--password", "password"]);
            }
            PeerRun run = await PeerRun.ExecuteAsync(s_peerTimeout, [.. arguments]).ConfigureAwait(false);
            foreach (string check in new[]
            {
                "Connect", "NamespaceArray", "ReadScalars", "WriteAndReadBack", "CallMethods", "CloseSession"
            })
            {
                run.AssertPassed(check);
            }
        }

        /// <summary>
        /// Ignores a policy beyond the 1.5.x ECC policies unless the peer
        /// declares it; the peer server and the peer client are the same
        /// build, so the server's declaration covers both directions.
        /// </summary>
        private void RequirePeerPolicy(string policy)
        {
            if (s_legacyPolicies.Contains(policy))
            {
                return;
            }
            PeerInfo info = m_legacyServer.Info;
            if (info == null || !info.DeclaresPolicy(policy))
            {
                Assert.Ignore($"The peer ({info?.Stack ?? "unknown stack"}) does not declare {policy}.");
            }
        }

        /// <summary>
        /// The 2.0 client keeps reading on an ECC_nistP256 SignAndEncrypt
        /// channel of the 1.5 server through a security token renewal. ECC
        /// renewals derive the new keys with HKDF; the ECC policies of OPC UA
        /// 1.05 do not chain the previous secret into them. Both stacks revise
        /// the lifetime to at least 60 s and the client renews at 75 %, so
        /// this runs about a minute.
        /// </summary>
        [Test]
        [CancelAfter(180_000)]
        public Task ClientToLegacyServerTokenRenewalAsync(CancellationToken ct)
        {
            return ClientToLegacyServerTokenRenewalAsync(SecurityPolicies.ECC_nistP256, 65, 0, ct);
        }

        /// <summary>
        /// The 1.5 client keeps reading on an ECC_nistP256 SignAndEncrypt
        /// channel of the 2.0 server through a security token renewal.
        /// </summary>
        [Test]
        public Task LegacyClientToServerTokenRenewalAsync()
        {
            return LegacyClientToServerTokenRenewalAsync(SecurityPolicies.ECC_nistP256, 65, 0);
        }

        /// <summary>
        /// The authenticated encryption ECC policies, whose renewals chain
        /// the previous key material into the HKDF of the new keys
        /// (SecureChannelEnhancements, Part 6, 6.7.4 and 6.8).
        /// </summary>
        public static IEnumerable<TestCaseData> AeadRenewalCases()
        {
            foreach (string policy in new[]
            {
                SecurityPolicies.ECC_nistP256_AesGcm,
                SecurityPolicies.ECC_nistP256_ChaChaPoly
            })
            {
                yield return new TestCaseData(policy).SetArgDisplayNames(policy.Substring(policy.LastIndexOf('#') + 1));
            }
        }

        /// <summary>
        /// The 2.0 client keeps reading on an authenticated encryption
        /// SignAndEncrypt channel of the peer server through two security
        /// token renewals (at about 45 s and 90 s), so the second renewal
        /// chains from key material that was itself chained.
        /// </summary>
        [Test]
        [CancelAfter(240_000)]
        [TestCaseSource(nameof(AeadRenewalCases))]
        public Task ClientToLegacyServerAeadTokenRenewalAsync(string policy)
        {
            return ClientToLegacyServerTokenRenewalAsync(
                policy, kAeadRenewalSeconds, 3, TestContext.CurrentContext.CancellationToken);
        }

        /// <summary>
        /// The peer client keeps reading on an authenticated encryption
        /// SignAndEncrypt channel of the 2.0 server through two security
        /// token renewals.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(AeadRenewalCases))]
        public Task LegacyClientToServerAeadTokenRenewalAsync(string policy)
        {
            return LegacyClientToServerTokenRenewalAsync(policy, kAeadRenewalSeconds, 2);
        }

        private async Task ClientToLegacyServerTokenRenewalAsync(
            string policy,
            int seconds,
            int minimumTokens,
            CancellationToken ct)
        {
            if (!m_client.Config.SecurityConfiguration.SupportedSecurityPolicies.Contains(policy))
            {
                Assert.Ignore($"The 2.0 client does not support {policy} on this platform.");
            }
            RequirePeerPolicy(policy);
            EndpointDescription description = m_legacyEndpoints.ToArray().FirstOrDefault(e =>
                e.SecurityPolicyUri == policy && e.SecurityMode == MessageSecurityMode.SignAndEncrypt);
            if (description == null)
            {
                Assert.Ignore($"The peer server does not offer {policy}/SignAndEncrypt.");
            }

            await using var client = new ClientFixture(telemetry: m_telemetry);
            await client.LoadClientConfigurationAsync(InteropPki.ClientPki(m_pkiRoot)).ConfigureAwait(false);
            client.Config.TransportQuotas.SecurityTokenLifetime = 60_000;
            client.SessionTimeout = 120_000;
            var endpoint = new ConfiguredEndpoint(null, description, EndpointConfiguration.Create(client.Config));
            ISession session = await client.ConnectAsync(endpoint).ConfigureAwait(false);
            try
            {
                var tokens = new List<uint>();
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                int reads = 0;
                while (elapsed.Elapsed < TimeSpan.FromSeconds(seconds))
                {
                    DataValue value = await session
                        .ReadValueAsync(VariableIds.Server_ServerStatus_CurrentTime, ct)
                        .ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(value.StatusCode), Is.True,
                        $"read {reads} after {elapsed.Elapsed.TotalSeconds:F0} s (tokens {string.Join(",", tokens)}): " +
                        value.StatusCode);
                    uint tokenId = (session.TransportChannel as ISecureChannel)?.CurrentToken?.TokenId ?? 0;
                    if (tokens.Count == 0 || tokens[^1] != tokenId)
                    {
                        tokens.Add(tokenId);
                    }
                    reads++;
                    await Task.Delay(500, ct).ConfigureAwait(false);
                }
                TestContext.Out.WriteLine($"{reads} reads with the security tokens {string.Join(",", tokens)}");
                Assert.That(tokens, Has.Count.GreaterThanOrEqualTo(minimumTokens),
                    "the security token was not renewed often enough");
            }
            finally
            {
                await session.CloseAsync(ct).ConfigureAwait(false);
                session.Dispose();
            }
        }

        /// <summary>
        /// minimumRenewals: passed as --min-renewals to a peer client that
        /// counts its renewals (the open62541 peer); 0 checks only that the
        /// reads keep working.
        /// </summary>
        private async Task LegacyClientToServerTokenRenewalAsync(string policy, int seconds, int minimumRenewals)
        {
            bool offered = m_server.Config.ServerConfiguration.SecurityPolicies.ToArray()
                .Any(p => p.SecurityPolicyUri == policy && p.SecurityMode == MessageSecurityMode.SignAndEncrypt);
            if (!offered)
            {
                Assert.Ignore($"The 2.0 server does not offer {policy} on this platform.");
            }
            RequirePeerPolicy(policy);
            var arguments = new List<string>
            {
                "client",
                "--url", m_serverUrl,
                "--pki", InteropPki.ClientPki(m_pkiRoot) + "l",
                "--ecc", "true",
                "--policy", policy,
                "--mode", nameof(MessageSecurityMode.SignAndEncrypt),
                "--checks", "TokenRenewal",
                "--token-lifetime", "60000",
                "--token-test-seconds", seconds.ToString(CultureInfo.InvariantCulture)
            };
            if (minimumRenewals > 0)
            {
                arguments.AddRange(["--min-renewals", minimumRenewals.ToString(CultureInfo.InvariantCulture)]);
            }
            PeerRun run = await PeerRun.ExecuteAsync(s_peerTimeout, [.. arguments]).ConfigureAwait(false);
            run.AssertPassed("Connect");
            run.AssertPassed("TokenRenewal");
            run.AssertPassed("CloseSession");
        }
    }
}
