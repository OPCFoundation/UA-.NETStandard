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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Conformance.Tests.Discovery;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// Conformance tests for the OPC UA Discovery Register conformance unit.
    /// Drives RegisterServer / RegisterServer2 against an in-process LDS
    /// (<see cref="LdsTestFixture"/>) and verifies state via FindServers.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("DiscoveryServices")]
    public class DiscoveryRegisterTests : LdsTestFixture
    {
        // The cert used by ClientFixture has ApplicationUri matching this value.
        // Per Part 12 §6.4.2 the LDS verifies cert.ApplicationUri == server.ServerUri
        // so positive tests use it as the registered ServerUri.
        private const string ClientApplicationUri =
            "urn:localhost:opcfoundation.org:TestClient";

        [Description("RegisterServer() default values; IsOnline=TRUE.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "001")]
        public async Task RegisterServerWithIsOnlineTrueAsync()
        {
            using RegistrationClient registration = await CreateRegistrationClientAsync()
                .ConfigureAwait(false);

            RegisteredServer server = NewServer(isOnline: true);

            await registration
                .RegisterServerAsync(null, server, CancellationToken.None)
                .ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers = await FindServersAsync().ConfigureAwait(false);
            AssertServerKnown(servers, ClientApplicationUri);
        }

        [Description("RegisterServer() default values; IsOnline=FALSE.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "002")]
        public async Task RegisterServerWithIsOnlineFalseAsync()
        {
            using RegistrationClient registration = await CreateRegistrationClientAsync()
                .ConfigureAwait(false);

            // First add (online) so we have something to remove.
            RegisteredServer online = NewServer(isOnline: true);
            await registration
                .RegisterServerAsync(null, online, CancellationToken.None)
                .ConfigureAwait(false);

            // Now mark offline — entry should be removed.
            RegisteredServer offline = NewServer(isOnline: false);
            await registration
                .RegisterServerAsync(null, offline, CancellationToken.None)
                .ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers = await FindServersAsync().ConfigureAwait(false);
            AssertServerNotKnown(servers, ClientApplicationUri);
        }

        [Description("RegisterServer() default values; gatewayServerUri is specified.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "003")]
        public async Task RegisterServerWithGatewayServerUriAsync()
        {
            using RegistrationClient registration = await CreateRegistrationClientAsync()
                .ConfigureAwait(false);

            RegisteredServer server = NewServer(isOnline: true);
            server.GatewayServerUri = "urn:localhost:opcfoundation.org:GatewayTestServer";

            await registration
                .RegisterServerAsync(null, server, CancellationToken.None)
                .ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers = await FindServersAsync().ConfigureAwait(false);
            AssertServerKnown(servers, ClientApplicationUri);
        }

        [Description("RegisterServer() default values; multiple discoveryUrls.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "004")]
        public async Task RegisterServerWithMultipleDiscoveryUrlsAsync()
        {
            using RegistrationClient registration = await CreateRegistrationClientAsync()
                .ConfigureAwait(false);

            RegisteredServer server = NewServer(isOnline: true);
            server.DiscoveryUrls = new[]
            {
                "opc.tcp://server-host-a:48010",
                "opc.tcp://server-host-b:48010",
                "opc.tcp://server-host-c:48010"
            };

            await registration
                .RegisterServerAsync(null, server, CancellationToken.None)
                .ConfigureAwait(false);

            ArrayOf<ApplicationDescription> servers = await FindServersAsync().ConfigureAwait(false);
            ApplicationDescription found = FindByUri(servers, ClientApplicationUri);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.DiscoveryUrls.Count, Is.EqualTo(3));
        }

        [Description("RegisterServer() default values; semaphoreFilePath exists; IsOnline=true.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "005")]
        public async Task RegisterServerWithSemaphoreFilePathAndIsOnlineTrueAsync()
        {
            string sem = Path.GetTempFileName();
            try
            {
                using RegistrationClient registration = await CreateRegistrationClientAsync()
                    .ConfigureAwait(false);

                RegisteredServer server = NewServer(isOnline: true);
                server.SemaphoreFilePath = sem;

                await registration
                    .RegisterServerAsync(null, server, CancellationToken.None)
                    .ConfigureAwait(false);

                ArrayOf<ApplicationDescription> servers = await FindServersAsync().ConfigureAwait(false);
                AssertServerKnown(servers, ClientApplicationUri);
            }
            finally
            {
                File.Delete(sem);
            }
        }

        [Description("RegisterServer() default values; semaphoreFilePath exists; IsOnline=false.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "006")]
        public async Task RegisterServerWithSemaphoreFilePathAndIsOnlineFalseAsync()
        {
            string sem = Path.GetTempFileName();
            try
            {
                using RegistrationClient registration = await CreateRegistrationClientAsync()
                    .ConfigureAwait(false);

                RegisteredServer server = NewServer(isOnline: true);
                server.SemaphoreFilePath = sem;
                await registration
                    .RegisterServerAsync(null, server, CancellationToken.None)
                    .ConfigureAwait(false);

                server.IsOnline = false;
                await registration
                    .RegisterServerAsync(null, server, CancellationToken.None)
                    .ConfigureAwait(false);

                ArrayOf<ApplicationDescription> servers = await FindServersAsync().ConfigureAwait(false);
                AssertServerNotKnown(servers, ClientApplicationUri);
            }
            finally
            {
                File.Delete(sem);
            }
        }

        [Description("RegisterServer() IsOnline=true; SemaphoreFilePath does not exist.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "007")]
        public async Task RegisterServerWithMissingSemaphoreFilePathAndIsOnlineTrueAsync()
        {
            string nonexistent = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            using RegistrationClient registration = await CreateRegistrationClientAsync()
                .ConfigureAwait(false);

            RegisteredServer server = NewServer(isOnline: true);
            server.SemaphoreFilePath = nonexistent;

            await registration
                .RegisterServerAsync(null, server, CancellationToken.None)
                .ConfigureAwait(false);

            // Per Part 12: a missing semaphore file means the server is not online.
            ArrayOf<ApplicationDescription> servers = await FindServersAsync().ConfigureAwait(false);
            AssertServerNotKnown(servers, ClientApplicationUri);
        }

        [Description("RegisterServer() default values; multiple times, each from a different secure channel.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "008")]
        public async Task RegisterServerMultipleTimesFromDifferentSecureChannelsAsync()
        {
            for (int i = 0; i < 3; i++)
            {
                using RegistrationClient registration = await CreateRegistrationClientAsync()
                    .ConfigureAwait(false);

                RegisteredServer server = NewServer(isOnline: true);
                await registration
                    .RegisterServerAsync(null, server, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            ArrayOf<ApplicationDescription> servers = await FindServersAsync().ConfigureAwait(false);
            AssertServerKnown(servers, ClientApplicationUri);
        }

        [Description("RegisterServer() default values; multiple servers, mix IsOnline=true/false.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "009")]
        public async Task RegisterServerMultipleServersWithMixedIsOnlineAsync()
        {
            // Without per-test certs we can only register the one server matching
            // the client cert ApplicationUri. Verify that a sequence of
            // online/offline transitions is handled correctly.
            using RegistrationClient registration = await CreateRegistrationClientAsync()
                .ConfigureAwait(false);

            await registration.RegisterServerAsync(null, NewServer(isOnline: true), CancellationToken.None)
                .ConfigureAwait(false);
            ArrayOf<ApplicationDescription> after1 = await FindServersAsync().ConfigureAwait(false);
            AssertServerKnown(after1, ClientApplicationUri);

            await registration.RegisterServerAsync(null, NewServer(isOnline: false), CancellationToken.None)
                .ConfigureAwait(false);
            ArrayOf<ApplicationDescription> after2 = await FindServersAsync().ConfigureAwait(false);
            AssertServerNotKnown(after2, ClientApplicationUri);

            await registration.RegisterServerAsync(null, NewServer(isOnline: true), CancellationToken.None)
                .ConfigureAwait(false);
            ArrayOf<ApplicationDescription> after3 = await FindServersAsync().ConfigureAwait(false);
            AssertServerKnown(after3, ClientApplicationUri);
        }

        [Description("RegisterServer() default values; multiple times, varied semaphoreFilePath.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "010")]
        public async Task RegisterServerMultipleTimesWithVariedSemaphoreFilePathAsync()
        {
            string sem = Path.GetTempFileName();
            try
            {
                using RegistrationClient registration = await CreateRegistrationClientAsync()
                    .ConfigureAwait(false);

                // Register with valid sem.
                RegisteredServer s1 = NewServer(isOnline: true);
                s1.SemaphoreFilePath = sem;
                await registration.RegisterServerAsync(null, s1, CancellationToken.None)
                    .ConfigureAwait(false);
                AssertServerKnown(await FindServersAsync().ConfigureAwait(false), ClientApplicationUri);

                // Re-register with invalid sem -> entry drops.
                RegisteredServer s2 = NewServer(isOnline: true);
                s2.SemaphoreFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                await registration.RegisterServerAsync(null, s2, CancellationToken.None)
                    .ConfigureAwait(false);
                AssertServerNotKnown(await FindServersAsync().ConfigureAwait(false), ClientApplicationUri);
            }
            finally
            {
                File.Delete(sem);
            }
        }

        [Description("RegisterServer() default values; multiple times; IsOnline=false.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "011")]
        public async Task RegisterServerMultipleTimesWithIsOnlineFalseAsync()
        {
            using RegistrationClient registration = await CreateRegistrationClientAsync()
                .ConfigureAwait(false);

            // Multiple offline registers with no prior online: nothing to remove,
            // the LDS should accept silently and FindServers shows nothing.
            for (int i = 0; i < 3; i++)
            {
                await registration
                    .RegisterServerAsync(null, NewServer(isOnline: false), CancellationToken.None)
                    .ConfigureAwait(false);
            }

            AssertServerNotKnown(await FindServersAsync().ConfigureAwait(false), ClientApplicationUri);
        }

        [Description("RegisterServer() default values; multiple times; IsOnline=False (already registered).")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "012")]
        public async Task RegisterServerRepeatedlyWithIsOnlineFalseAsync()
        {
            using RegistrationClient registration = await CreateRegistrationClientAsync()
                .ConfigureAwait(false);

            // Register online, then unregister 3x in a row. Should remain idempotent.
            await registration.RegisterServerAsync(null, NewServer(isOnline: true), CancellationToken.None)
                .ConfigureAwait(false);
            for (int i = 0; i < 3; i++)
            {
                await registration
                    .RegisterServerAsync(null, NewServer(isOnline: false), CancellationToken.None)
                    .ConfigureAwait(false);
            }

            AssertServerNotKnown(await FindServersAsync().ConfigureAwait(false), ClientApplicationUri);
        }

        [Description("RegisterServer() default values; multiple times, all SemaphorefilePath=null, except 1.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "013")]
        public async Task RegisterServerMultipleWithSingleSemaphoreFilePathAsync()
        {
            string sem = Path.GetTempFileName();
            try
            {
                using RegistrationClient registration = await CreateRegistrationClientAsync()
                    .ConfigureAwait(false);

                RegisteredServer first = NewServer(isOnline: true);
                first.SemaphoreFilePath = sem;
                await registration.RegisterServerAsync(null, first, CancellationToken.None)
                    .ConfigureAwait(false);

                for (int i = 0; i < 3; i++)
                {
                    await registration
                        .RegisterServerAsync(null, NewServer(isOnline: true), CancellationToken.None)
                        .ConfigureAwait(false);
                }

                AssertServerKnown(await FindServersAsync().ConfigureAwait(false), ClientApplicationUri);
            }
            finally
            {
                File.Delete(sem);
            }
        }

        [Description("RegisterServer() default values; multiple times, all SemaphorefilePath=null, except 1; repeated.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "014")]
        public async Task RegisterServerMultipleWithSingleSemaphoreFilePathRepeatedAsync()
        {
            string sem = Path.GetTempFileName();
            try
            {
                using RegistrationClient registration = await CreateRegistrationClientAsync()
                    .ConfigureAwait(false);

                for (int round = 0; round < 2; round++)
                {
                    RegisteredServer first = NewServer(isOnline: true);
                    first.SemaphoreFilePath = sem;
                    await registration.RegisterServerAsync(null, first, CancellationToken.None)
                        .ConfigureAwait(false);

                    for (int i = 0; i < 3; i++)
                    {
                        await registration
                            .RegisterServerAsync(null, NewServer(isOnline: true), CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }

                AssertServerKnown(await FindServersAsync().ConfigureAwait(false), ClientApplicationUri);
            }
            finally
            {
                File.Delete(sem);
            }
        }

        [Description("RegisterServer() default values; multiple server names while varying locale.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "017")]
        public async Task RegisterServerWithMultipleServerNamesVaryingLocaleAsync()
        {
            using RegistrationClient registration = await CreateRegistrationClientAsync()
                .ConfigureAwait(false);

            RegisteredServer server = NewServer(isOnline: true);
            server.ServerNames = new[]
            {
                new LocalizedText("en-US", "Test Server (English)"),
                new LocalizedText("de-DE", "Test-Server (Deutsch)"),
                new LocalizedText("fr-FR", "Serveur de test (Français)")
            };

            await registration
                .RegisterServerAsync(null, server, CancellationToken.None)
                .ConfigureAwait(false);

            // Verify locale-aware selection works through the find path.
            AssertServerKnown(await FindServersAsync().ConfigureAwait(false), ClientApplicationUri);
        }

        [Description("Register multiple Servers, each with a unique URI. Check filter on ServerUri.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "018")]
        public async Task RegisterMultipleServersWithUniqueUrisAndFilterByServerUriAsync()
        {
            // We can only register the cert-matching ServerUri via RegisterServer.
            // To exercise multi-server FindServers filtering we seed the store
            // directly with a second entry; this validates the LDS's
            // serverUris filter end-to-end via the Find path.
            using RegistrationClient registration = await CreateRegistrationClientAsync()
                .ConfigureAwait(false);

            await registration
                .RegisterServerAsync(null, NewServer(isOnline: true), CancellationToken.None)
                .ConfigureAwait(false);

            const string secondUri = "urn:localhost:opcfoundation.org:OtherTestServer";
            Lds.Store.SeedRegistration(new Opc.Ua.Lds.Server.RegistrationEntry
            {
                ServerUri = secondUri,
                ProductUri = "uri:test",
                ServerNames = { new LocalizedText("en-US", "Other Test Server") },
                ServerType = ApplicationType.Server,
                DiscoveryUrls = { "opc.tcp://other-host:48010" },
                IsOnline = true,
                LastSeenUtc = DateTime.UtcNow
            });

            // No filter -> at least 2 entries (LDS self + 2 registered).
            ArrayOf<ApplicationDescription> all = await FindServersAsync().ConfigureAwait(false);
            Assert.That(all.Count, Is.GreaterThanOrEqualTo(2));

            // Filter by first uri -> only that one (LDS self filtered out).
            ArrayOf<ApplicationDescription> filtered =
                await FindServersAsync(new[] { ClientApplicationUri }).ConfigureAwait(false);
            Assert.That(filtered.Count, Is.EqualTo(1));
            Assert.That(filtered[0].ApplicationUri, Is.EqualTo(ClientApplicationUri));

            // Filter by second uri -> only that one.
            ArrayOf<ApplicationDescription> filtered2 =
                await FindServersAsync(new[] { secondUri }).ConfigureAwait(false);
            Assert.That(filtered2.Count, Is.EqualTo(1));
            Assert.That(filtered2[0].ApplicationUri, Is.EqualTo(secondUri));
        }

        [Description("RegisterServer() default values; insecure channel.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "Err-001")]
        public async Task RegisterServerOverInsecureChannelReturnsErrorAsync()
        {
            // Use an unsecured channel — the LDS should reject the call.
            using RegistrationClient registration = await CreateRegistrationClientAsync(
                SecurityPolicies.None,
                MessageSecurityMode.None).ConfigureAwait(false);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await registration
                    .RegisterServerAsync(null, NewServer(isOnline: true), CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadSecurityChecksFailed));
        }

        [Description("RegisterServer() default values; ServerUri=empty; expect Bad_ServerUriInvalid.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "Err-002")]
        public async Task RegisterServerWithEmptyServerUriReturnsBadServerUriInvalidAsync()
        {
            using RegistrationClient registration = await CreateRegistrationClientAsync()
                .ConfigureAwait(false);

            RegisteredServer server = NewServer(isOnline: true);
            server.ServerUri = string.Empty;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await registration
                    .RegisterServerAsync(null, server, CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadServerUriInvalid));
        }

        [Description("RegisterServer() default values; ServerNames=empty; expect Bad_ServerNameMissing.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "Err-003")]
        public async Task RegisterServerWithEmptyServerNamesReturnsBadServerNameMissingAsync()
        {
            using RegistrationClient registration = await CreateRegistrationClientAsync()
                .ConfigureAwait(false);

            RegisteredServer server = NewServer(isOnline: true);
            server.ServerNames = Array.Empty<LocalizedText>();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await registration
                    .RegisterServerAsync(null, server, CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadServerNameMissing));
        }

        [Description("RegisterServer() default values; DiscoveryUrls=empty; expect Bad_DiscoveryUrlMissing.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "Err-004")]
        public async Task RegisterServerWithEmptyDiscoveryUrlsReturnsBadDiscoveryUrlMissingAsync()
        {
            using RegistrationClient registration = await CreateRegistrationClientAsync()
                .ConfigureAwait(false);

            RegisteredServer server = NewServer(isOnline: true);
            server.DiscoveryUrls = Array.Empty<string>();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await registration
                    .RegisterServerAsync(null, server, CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadDiscoveryUrlMissing));
        }

        [Description("RegisterServer() default values; ServerUri != ServerCertificate.ApplicationUri; expect Bad_ServerUriInvalid.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "Err-005")]
        public async Task RegisterServerWithMismatchedApplicationUriReturnsBadServerUriInvalidAsync()
        {
            using RegistrationClient registration = await CreateRegistrationClientAsync()
                .ConfigureAwait(false);

            RegisteredServer server = NewServer(isOnline: true);
            server.ServerUri = "urn:localhost:opcfoundation.org:DefinitelyNotMatchingTheCert";

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await registration
                    .RegisterServerAsync(null, server, CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadServerUriInvalid));
        }

        [Description("RegisterServer() default values; ServerType=CLIENT_1; expect Bad_InvalidArgument.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "Err-006")]
        public async Task RegisterServerWithClientServerTypeReturnsBadInvalidArgumentAsync()
        {
            using RegistrationClient registration = await CreateRegistrationClientAsync()
                .ConfigureAwait(false);

            RegisteredServer server = NewServer(isOnline: true);
            server.ServerType = ApplicationType.Client;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await registration
                    .RegisterServerAsync(null, server, CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Description("RegisterServer() default values; ServerType=invalid; expect Bad_InvalidArgument.")]
        [Test]
        [Property("ConformanceUnit", "Discovery Register")]
        [Property("Tag", "Err-007")]
        public async Task RegisterServerWithInvalidServerTypeReturnsBadInvalidArgumentAsync()
        {
            using RegistrationClient registration = await CreateRegistrationClientAsync()
                .ConfigureAwait(false);

            RegisteredServer server = NewServer(isOnline: true);
            server.ServerType = (ApplicationType)999;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await registration
                    .RegisterServerAsync(null, server, CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        // Helpers --------------------------------------------------------

        private static RegisteredServer NewServer(bool isOnline)
        {
            return new RegisteredServer
            {
                ServerUri = ClientApplicationUri,
                ProductUri = "http://opcfoundation.org/UA/TestClient",
                ServerNames = new[] { new LocalizedText("en-US", "Test Client (Mock Server)") },
                ServerType = ApplicationType.Server,
                DiscoveryUrls = new[] { "opc.tcp://test-server-host:48010" },
                IsOnline = isOnline
            };
        }

        private async Task<ArrayOf<ApplicationDescription>> FindServersAsync(string[] serverUris = null)
        {
            using DiscoveryClient discovery = await CreateDiscoveryClientAsync().ConfigureAwait(false);
            ArrayOf<string> filter = serverUris != null
                ? new ArrayOf<string>(serverUris)
                : default;
            return await discovery
                .FindServersAsync(filter, CancellationToken.None)
                .ConfigureAwait(false);
        }

        private static ApplicationDescription FindByUri(ArrayOf<ApplicationDescription> servers, string uri)
        {
            foreach (ApplicationDescription d in servers)
            {
                if (string.Equals(d.ApplicationUri, uri, StringComparison.Ordinal))
                {
                    return d;
                }
            }
            return null;
        }

        private static void AssertServerKnown(ArrayOf<ApplicationDescription> servers, string uri)
        {
            ApplicationDescription d = FindByUri(servers, uri);
            Assert.That(d, Is.Not.Null, $"Expected FindServers to include {uri}.");
        }

        private static void AssertServerNotKnown(ArrayOf<ApplicationDescription> servers, string uri)
        {
            ApplicationDescription d = FindByUri(servers, uri);
            Assert.That(d, Is.Null, $"Expected FindServers to NOT include {uri}.");
        }
    }
}
