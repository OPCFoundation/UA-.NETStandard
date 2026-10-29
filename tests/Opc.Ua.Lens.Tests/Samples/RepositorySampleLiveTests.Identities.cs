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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.UserDatabase;
using UaLens.Connection;
using UaLens.Subscriptions;
using ISession = Opc.Ua.Client.ISession;

namespace UaLens.Tests.Samples
{
    public sealed partial class RepositorySampleLiveTests
    {
        /// <summary>
        /// Qualifies secure sessions, key rotation, eligibility rejection and recovery against an owned server.
        /// </summary>
        [TestCase(false, SecurityPolicies.Basic256Sha256, false)]
        [TestCase(true, SecurityPolicies.Basic256Sha256, false)]
        [TestCase(false, SecurityPolicies.Aes128_Sha256_RsaOaep, false)]
        [TestCase(true, SecurityPolicies.Aes128_Sha256_RsaOaep, false)]
        [TestCase(false, SecurityPolicies.Aes256_Sha256_RsaPss, false)]
        [TestCase(true, SecurityPolicies.Aes256_Sha256_RsaPss, false)]
        [TestCase(false, SecurityPolicies.Basic256Sha256, true)]
        [TestCase(true, SecurityPolicies.Basic256Sha256, true)]
        [Explicit("Hosts an owned secure-only server with ephemeral username and X.509 user identities.")]
        [Category("RepositorySampleProbe")]
        public async Task PrivateAuthenticatedIdentitiesSurviveEngineReplacementWithoutAnonymousFallbackAsync(
            bool certificateIdentity,
            string securityPolicy,
            bool reverseConnect)
        {
            string root = Path.Combine(Path.GetTempPath(), "UaLens-owned-identity-" + Guid.NewGuid());
            string serverPki = Path.Combine(root, "server");
            Directory.CreateDirectory(root);
            byte[] password = Encoding.UTF8.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            byte[] wrongPassword = Encoding.UTF8.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            var manager = new CertificateManager(telemetry);
            try
            {
                using Certificate userCertificate = CertificateBuilder.Create("CN=UaLens owned operator")
                    .SetRSAKeySize(2048).CreateForRSA();
                using var port = new TcpListener(IPAddress.Loopback, 0);
                port.Start();
                int number = ((IPEndPoint)port.LocalEndpoint).Port;
                using var listenerPort = new TcpListener(IPAddress.Loopback, 0);
                listenerPort.Start();
                int listenerNumber = ((IPEndPoint)listenerPort.LocalEndpoint).Port;
                listenerPort.Stop();
                port.Stop();
                string endpointUrl = Utils.ReplaceLocalhost($"opc.tcp://localhost:{number}/UaLensOwnedIdentity");
                string listenerUrl = $"opc.tcp://localhost:{listenerNumber}";
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var started = new OwnedCompanionStartup();
                HostApplicationBuilder builder = Host.CreateApplicationBuilder();
                builder.Logging.ClearProviders();
                builder.Services.AddSingleton<IServerStartupTask>(started);
                var database = new LinqUserDatabase();
                Assert.That(database.CreateUser("owned-operator", password, [], default,
                    "Owned identity probe"), Is.True);
                builder.Services.AddSingleton<IUserDatabase>(database);
                builder.Services.AddOpcUa().AddSecureServer(options =>
                {
                    options.ApplicationName = "UaLensOwnedIdentity";
                    options.ApplicationUri = "urn:localhost:UaLensOwnedIdentity";
                    options.ProductUri = "urn:opcfoundation.org:UaLensOwnedIdentity";
                    options.PkiRoot = serverPki;
                    options.AutoAcceptUntrustedCertificates = false;
                    options.EndpointUrls.Add(endpointUrl);
                    if (reverseConnect)
                    {
                        options.ReverseConnect = new ServerReverseConnectOptions
                        {
                            ConnectIntervalMs = 100,
                            ConnectTimeoutMs = 10000
                        };
                        options.ReverseConnect.Clients.Add(new ServerReverseConnectClientOptions
                        {
                            EndpointUrl = listenerUrl,
                            Timeout = 10000
                        });
                    }
                }).AddCertificateManager(manager);
                using IHost host = builder.Build();
                try
                {
                    await host.StartAsync(deadline.Token).ConfigureAwait(false);
                    await started.Ready.WaitAsync(deadline.Token).ConfigureAwait(false);
                    string userStorePath = Path.Combine(root, "user-identity");
                    using (ICertificateStore keys = new CertificateStoreIdentifier(
                        userStorePath, CertificateStoreType.Directory, false).OpenStore(telemetry))
                    {
                        await keys.AddAsync(userCertificate, ct: deadline.Token).ConfigureAwait(false);
                    }
                    var reference = new CertificateIdentityReference
                    {
                        SourceId = "owned-user",
                        PasswordSourceId = "owned-access",
                        Thumbprint = userCertificate.Thumbprint
                    };
                    using var identities = new ConnectionIdentityConfiguration(
                    [
                        new ConfiguredCertificateSource(reference.SourceId, "Owned user certificate",
                            new CertificateIdentifier
                            {
                                StoreType = CertificateStoreType.Directory,
                                StorePath = userStorePath
                            }, manager.CertificateProvider,
                            [new ConfiguredCertificatePasswordSource(reference.PasswordSourceId,
                                "Owned isolated key store", new CertificatePasswordProvider())])
                    ]);
                    var backend = new StackConnectionBackend(telemetry,
                        token => AppConfig.BuildAsync(telemetry, Path.Combine(root, "client"), token));
                    await using (backend.ConfigureAwait(false))
                    {
                        var connection = new ConnectionService(telemetry, null, backend,
                            new ProfileCredentialProvider(configuration: identities));
                        await using (connection.ConfigureAwait(false))
                        {
                            ApplicationConfiguration configuration = await connection.GetConfigAsync(deadline.Token)
                                .ConfigureAwait(false);
                            ArrayOf<EndpointDescription> endpoints = await backend.DiscoverAsync(
                                configuration, endpointUrl, deadline.Token).ConfigureAwait(false);
                            EndpointDescription endpoint = endpoints.ToList().Single(value =>
                                value.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                                value.SecurityPolicyUri == securityPolicy);
                            Assert.That(endpoints.ToList().Any(
                                value => value.SecurityMode == MessageSecurityMode.None), Is.False);
                            Assert.That(endpoint.UserIdentityTokens.ToList().Any(
                                policy => policy.TokenType == UserTokenType.Anonymous), Is.False);
                            using CertificateEntry own = manager
                                .AcquireApplicationCertificateBySecurityPolicy(securityPolicy)
                                ?? throw new AssertionException("The owned server certificate is missing.");
                            Assert.That(endpoint.ServerCertificate.Span.SequenceEqual(own.Certificate.RawData),
                                Is.True);
                            await TrustOwnedPeersAsync(configuration, endpoint, serverPki, telemetry, deadline.Token)
                                .ConfigureAwait(false);
                            UserTokenType type = certificateIdentity
                                ? UserTokenType.Certificate : UserTokenType.UserName;
                            UserTokenPolicy policy = endpoint.UserIdentityTokens.ToList()
                                .Single(value => value.TokenType == type);
                            ConnectionProfile profile = ConnectionProfile.Create(endpoint, policy,
                                SubscriptionEngineKind.ChannelV2, identityName: "owned-operator",
                                certificateIdentity: certificateIdentity ? reference : null);
                            if (reverseConnect)
                            {
                                var reverse = new ReverseConnectionProfile
                                {
                                    EndpointUrl = profile.EndpointUrl,
                                    ServerUri = profile.ServerApplicationUri!,
                                    ListenerUrl = listenerUrl,
                                    WaitTimeoutSeconds = 10,
                                    HoldTimeSeconds = 5
                                };
                                profile = profile with { ReverseConnection = reverse };
                                await backend.ReverseConnections.StartAsync(reverse, deadline.Token)
                                    .ConfigureAwait(false);
                                Assert.That(backend.ReverseConnections.Snapshot.Phase,
                                    Is.EqualTo(ReverseConnectionPhase.Listening));
                            }
                            Func<Task> deniedConnect = certificateIdentity
                                ? () => connection.ConnectAsync(profile, ct: deadline.Token)
                                : () => ConnectOwnedUserNameAsync(connection, endpoint, profile,
                                    wrongPassword, deadline.Token);
                            await Assert.ThatAsync(deniedConnect, Throws.InstanceOf<ServiceResultException>()
                                .With.Property(nameof(ServiceResultException.StatusCode))
                                .EqualTo(certificateIdentity
                                    ? StatusCodes.BadIdentityTokenRejected
                                    : StatusCodes.BadUserAccessDenied)).ConfigureAwait(false);
                            Assert.That(connection.CurrentSession, Is.Null);
                            if (certificateIdentity)
                            {
                                using ICertificateStore users = manager.OpenTrustedStore(TrustListIdentifier.Users);
                                using var publicUser = Certificate.FromRawData(userCertificate.RawData);
                                await users.AddAsync(publicUser, ct: deadline.Token).ConfigureAwait(false);
                                manager.NotifyTrustListChanged(TrustListIdentifier.Users,
                                    trustChanged: true, crlChanged: false);
                            }
                            if (certificateIdentity)
                            {
                                await connection.ConnectAsync(profile, ct: deadline.Token).ConfigureAwait(false);
                            }
                            else
                            {
                                await ConnectOwnedUserNameAsync(
                                    connection, endpoint, profile, password, deadline.Token)
                                    .ConfigureAwait(false);
                            }
                            ISession original = connection.CurrentSession ??
                                throw new AssertionException("The authenticated session is missing.");
                            Assert.That(original.Identity.TokenType, Is.EqualTo(type));
                            await RequireOwnedReadAsync(original, deadline.Token).ConfigureAwait(false);
                            if (reverseConnect)
                            {
                                await Assert.ThatAsync(() => backend.ReverseConnections.StopAsync(deadline.Token),
                                    Throws.InvalidOperationException).ConfigureAwait(false);
                                Assert.That(connection.CurrentSession, Is.SameAs(original));
                            }
                            await connection.ReconnectAsync(SubscriptionEngineKind.Classic, deadline.Token)
                                .ConfigureAwait(false);
                            ISession replacement = connection.CurrentSession ??
                                throw new AssertionException("The replacement session is missing.");
                            Assert.That(replacement, Is.Not.SameAs(original));
                            Assert.That(replacement.Identity.TokenType, Is.EqualTo(type));
                            Assert.That(connection.Profile!.SecurityMode,
                                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                            Assert.That(connection.Profile.SecurityPolicyUri, Is.EqualTo(securityPolicy));
                            await RequireOwnedReadAsync(replacement, deadline.Token).ConfigureAwait(false);
                            await connection.DisconnectAsync().ConfigureAwait(false);
                            Assert.That(connection.CurrentSession, Is.Null);
                            Assert.That(connection.Profile!.IdentityType, Is.EqualTo(type));
                            if (certificateIdentity)
                            {
                                await RequireOwnedKeyRotationAsync(connection, profile, userCertificate, manager,
                                    userStorePath, telemetry, deadline.Token).ConfigureAwait(false);
                                await RequireOwnedInvalidUserKeysAsync(
                                    connection, userStorePath, telemetry, deadline.Token).ConfigureAwait(false);
                            }
                            if (reverseConnect)
                            {
                                await connection.StopReverseListenerAsync(deadline.Token).ConfigureAwait(false);
                                Assert.That(backend.ReverseConnections.Snapshot.Phase,
                                    Is.EqualTo(ReverseConnectionPhase.Stopped));
                                using var releasedPort = new TcpListener(IPAddress.Loopback, listenerNumber);
                                releasedPort.Start();
                                releasedPort.Stop();
                            }
                        }
                    }
                }
                finally
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await host.StopAsync(cleanup.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    await manager.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(password);
                    CryptographicOperations.ZeroMemory(wrongPassword);
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private static async Task RequireOwnedReadAsync(ISession session, CancellationToken token)
        {
            DataValue value = await session.ReadValueAsync(VariableIds.Server_ServerStatus_CurrentTime, token)
                .ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out DateTimeUtc _), Is.True);
        }

        private static async Task ConnectOwnedUserNameAsync(
            ConnectionService connection,
            EndpointDescription endpoint,
            ConnectionProfile profile,
            byte[] password,
            CancellationToken token)
        {
            var identity = new UserIdentity(profile.IdentityName!, password) { PolicyId = profile.UserTokenPolicyId };
            ConnectionCredentials credentials = await ConnectionCredentials.FromIdentityAsync(profile, identity, token)
                .ConfigureAwait(false);
            var selection = new ConnectionSelection(endpoint, profile, credentials);
            await using (selection.ConfigureAwait(false))
            {
                await connection.ConnectAsync(selection, ct: token).ConfigureAwait(false);
            }
        }

        private static async Task RequireOwnedKeyRotationAsync(
            ConnectionService connection,
            ConnectionProfile profile,
            Certificate original,
            CertificateManager manager,
            string userStorePath,
            ITelemetryContext telemetry,
            CancellationToken token)
        {
            using Certificate replacement = CertificateBuilder.Create(original.Subject!)
                .SetRSAKeySize(2048).CreateForRSA();
            Assert.That(replacement.Thumbprint, Is.Not.EqualTo(original.Thumbprint));
            using (ICertificateStore keys = new CertificateStoreIdentifier(
                userStorePath, CertificateStoreType.Directory, false).OpenStore(telemetry))
            {
                await keys.AddAsync(replacement, ct: token).ConfigureAwait(false);
            }
            using (ICertificateStore users = manager.OpenTrustedStore(TrustListIdentifier.Users))
            {
                Assert.That(await users.DeleteAsync(original.Thumbprint!, token).ConfigureAwait(false), Is.True);
                using var publicReplacement = Certificate.FromRawData(replacement.RawData);
                await users.AddAsync(publicReplacement, ct: token).ConfigureAwait(false);
            }
            manager.NotifyTrustListChanged(TrustListIdentifier.Users, trustChanged: true, crlChanged: false);
            await Assert.ThatAsync(() => connection.ConnectAsync(profile, ct: token),
                Throws.InstanceOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadIdentityTokenRejected)).ConfigureAwait(false);
            Assert.That(connection.CurrentSession, Is.Null);
            var rotated = profile with
            {
                CertificateIdentity = profile.CertificateIdentity! with { Thumbprint = replacement.Thumbprint }
            };
            await connection.ConnectAsync(rotated, ct: token).ConfigureAwait(false);
            ISession session = connection.CurrentSession ??
                throw new AssertionException("The rotated session is missing.");
            Assert.That(session.Identity.TokenType, Is.EqualTo(UserTokenType.Certificate));
            Assert.That(connection.Profile!.CertificateIdentity!.Thumbprint, Is.EqualTo(replacement.Thumbprint));
            await RequireOwnedReadAsync(session, token).ConfigureAwait(false);
            await connection.DisconnectAsync().ConfigureAwait(false);
            Assert.That(connection.CurrentSession, Is.Null);
        }

        private static async Task RequireOwnedInvalidUserKeysAsync(
            ConnectionService connection, string userStorePath, ITelemetryContext telemetry, CancellationToken token)
        {
            ConnectionProfile valid = connection.Profile ??
                throw new AssertionException("The successfully rotated identity profile is missing.");
            using ICertificateStore keys = new CertificateStoreIdentifier(
                userStorePath, CertificateStoreType.Directory, false).OpenStore(telemetry);
            for (int kind = 0; kind < 3; kind++)
            {
                var builder = CertificateBuilder.Create("CN=UaLens ineligible user key");
                DateTime now = DateTime.UtcNow;
                if (kind == 0)
                {
                    builder.SetNotBefore(now.AddDays(-2)).SetNotAfter(now.AddDays(-1));
                }
                else if (kind == 1)
                {
                    builder.SetNotBefore(now.AddDays(1)).SetNotAfter(now.AddDays(2));
                }
                else
                {
                    builder.AddExtension(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment, true));
                }
                using Certificate certificate = builder.SetRSAKeySize(2048).CreateForRSA();
                await keys.AddAsync(certificate, ct: token).ConfigureAwait(false);
                using CertificateCollection stored = await keys.EnumerateAsync(token).ConfigureAwait(false);
                Assert.That(stored.Any(value => value.Thumbprint == certificate.Thumbprint), Is.True);
                var rejected = valid with
                {
                    CertificateIdentity = valid.CertificateIdentity! with { Thumbprint = certificate.Thumbprint }
                };
                await Assert.ThatAsync(() => connection.ConnectAsync(rejected, ct: token),
                    Throws.InstanceOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.Bad)
                        .And.Message.Contains(nameof(ConnectionIdentityException))
                        .And.Message.Contains(kind < 2
                            ? "expired or not yet valid"
                            : "selected certificate/private key is absent"),
                    $"Ineligible certificate partition {kind} must be rejected before identity use.")
                    .ConfigureAwait(false);
                Assert.That(connection.CurrentSession, Is.Null);
                Assert.That(connection.Profile!.IdentityType, Is.EqualTo(UserTokenType.Certificate));
                Assert.That(connection.Profile.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                Assert.That(connection.Profile.CertificateIdentity!.Thumbprint, Is.EqualTo(certificate.Thumbprint));
                Assert.That(certificate.HasPrivateKey, Is.True, "The failed acquisition must not dispose this owner.");
            }
            await connection.ConnectAsync(valid, ct: token).ConfigureAwait(false);
            ISession recovered = connection.CurrentSession ??
                throw new AssertionException("The valid identity did not recover after rejected acquisitions.");
            Assert.That(connection.Profile!.CertificateIdentity!.Thumbprint,
                Is.EqualTo(valid.CertificateIdentity!.Thumbprint));
            await RequireOwnedReadAsync(recovered, token).ConfigureAwait(false);
            await connection.DisconnectAsync().ConfigureAwait(false);
        }
    }
}
