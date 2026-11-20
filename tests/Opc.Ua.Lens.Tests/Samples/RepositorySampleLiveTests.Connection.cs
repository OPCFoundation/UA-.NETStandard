// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Connection;
using UaLens.Plugins.Companions;
using UaLens.Samples;

namespace UaLens.Tests.Samples
{
    public sealed partial class RepositorySampleLiveTests
    {
        private static async Task WithOwnedCompanionSessionAsync(
            RepositorySampleSnapshot ready, string runParent, ITelemetryContext telemetry,
            Func<CompanionContext, CancellationToken, Task> verify)
        {
            RepositorySampleEvidence evidence = ready.Evidence ??
                throw new AssertionException("The owned simulator returned no identity evidence.");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            string clientPki = Path.Combine(runParent, "client");
            try
            {
                var backend = new StackConnectionBackend(
                    telemetry, token => AppConfig.BuildAsync(telemetry, clientPki, token));
                await using (backend.ConfigureAwait(false))
                {
                    var connection = new ConnectionService(
                        telemetry, null, backend, new ProfileCredentialProvider());
                    await using (connection.ConfigureAwait(false))
                    {
                        ApplicationConfiguration configuration = await connection.GetConfigAsync(deadline.Token)
                            .ConfigureAwait(false);
                        ArrayOf<EndpointDescription> endpoints = await backend.DiscoverAsync(
                            configuration, evidence.Endpoint.AbsoluteUri, deadline.Token).ConfigureAwait(false);
                        EndpointDescription endpoint = endpoints.ToList().Single(candidate =>
                            candidate.EndpointUrl == evidence.Endpoint.AbsoluteUri &&
                            candidate.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                            candidate.SecurityPolicyUri == SecurityPolicies.Basic256Sha256);
                        Assert.That(ByteString.From(SHA256.HashData(endpoint.ServerCertificate.Span)),
                            Is.EqualTo(evidence.CertificateSha256));
                        await TrustOwnedPeersAsync(configuration, endpoint,
                            ready.PrivatePkiRoot ?? throw new AssertionException("The private server PKI is absent."),
                            telemetry, deadline.Token).ConfigureAwait(false);
                        UserTokenPolicy policy = endpoint.UserIdentityTokens.ToList()
                            .First(candidate => candidate.TokenType == UserTokenType.Anonymous);
                        var identity = new UserIdentity(new AnonymousIdentityToken())
                        {
                            PolicyId = policy.PolicyId ?? throw new AssertionException("The token policy has no ID.")
                        };
                        await connection.ConnectAsync(new ConnectionOptions
                        {
                            EndpointUrl = endpoint.EndpointUrl ??
                                throw new AssertionException("The selected endpoint has no URL."),
                            UseSecurity = true,
                            Engine = SubscriptionEngineKind.ChannelV2
                        }, endpoint, identity,
                            (_, error) => throw new AssertionException("Unexpected trust request: " + error.StatusCode),
                            deadline.Token).ConfigureAwait(false);
                        ISession session = connection.CurrentSession ??
                            throw new AssertionException("No secure sample session was created.");
                        var context = new CompanionContext(session, telemetry);
                        await verify(context, deadline.Token).ConfigureAwait(false);
                        Assert.That(session.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                        await connection.DisconnectAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                if (Directory.Exists(clientPki))
                {
                    Directory.Delete(clientPki, recursive: true);
                }
            }
        }
    }
}
