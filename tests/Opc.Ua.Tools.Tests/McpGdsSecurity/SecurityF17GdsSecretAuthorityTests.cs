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

#if NET10_0
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds;
using Opc.Ua.Mcp;
using Opc.Ua.Tools.Tests.McpCompanion;
using GdsMethods = Opc.Ua.Gds.MethodIds;

namespace Opc.Ua.Tools.Tests.McpGdsSecurity
{
    /// <summary>
    /// Exercises real GDS consumers, generated proxies and SDK session captures without a live endpoint.
    /// </summary>
    [TestFixture]
    public sealed class SecurityF17GdsSecretAuthorityTests
    {
        /// <summary>
        /// An approved password dispatch works once; completed replacement or endpoint substitution cannot inherit it.
        /// </summary>
        [TestCase("Registration")]
        [TestCase("Endpoint")]
        [TestCase("Certificate")]
        [Category("SecurityRegression")]
        public async Task SecurityF17GdsPasswordCannotFollowCompletedTargetSubstitution(string substitution)
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            await using GdsTestContext replacement = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            fixture.Store.Seed("password", "approved-gds-password");
            int lookups = 0;
            fixture.Store.AfterGet = () =>
            {
                lookups++;
                return ValueTask.CompletedTask;
            };
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(fixture.Remote.Id("request")));
            using (fixture.Enter())
            {
                CallToolResult approved = await PasswordAsync(fixture).ConfigureAwait(false);
                Assert.That(approved.IsError, Is.False);
                fixture.Remote.AssertCall(fixture.Remote.Id("directory"),
                    GdsMethods.CertificateDirectoryType_StartNewKeyPairRequest,
                    Variant.From(fixture.Remote.Id("application")), Variant.From(NodeId.Null),
                    Variant.From(ObjectTypeIds.RsaSha256ApplicationCertificateType), Variant.From("CN=Requested"),
                    Variant.From(ArrayOf<string>.Empty), Variant.From("PFX"), Variant.From("approved-gds-password"));
                Assert.That(lookups, Is.EqualTo(1));
                Assert.That(fixture.Store.Views, Has.Count.EqualTo(1));
                Assert.That(fixture.Store.Views[0].Disposed, Is.True);
                JsonElement output = RemoteModelTestSession.Payload(approved);
                Assert.That(output.GetProperty("resourceBinding").GetString(), Is.EqualTo(fixture.DirectoryResource));
                Assert.That(output.GetRawText(), Does.Not.Contain("approved-gds-password"));
            }
            fixture.Remote.Calls.Clear();
            fixture.Connection.Requests.Clear();
            replacement.Connection.Requests.Clear();
            fixture.Store.Views.Clear();
            lookups = 0;
            if (substitution == "Registration")
            {
                replacement.Connection.Managed.ConfiguredEndpoint.Description.EndpointUrl =
                    "opc.tcp://replacement.invalid:4840";
                await fixture.Manager.RegisterExistingSessionAsync(
                    "selected", replacement.Connection.Managed, "Anonymous").ConfigureAwait(false);
            }
            else if (substitution == "Endpoint")
            {
                fixture.Connection.Managed.ConfiguredEndpoint.Description.EndpointUrl =
                    "opc.tcp://replacement.invalid:4840";
            }
            else
            {
                fixture.Connection.Managed.ConfiguredEndpoint.Description.ServerCertificate =
                    ByteString.From(new byte[] { 7, 8, 9 });
            }
            using (fixture.Enter())
            {
                CallToolResult refused = await PasswordAsync(fixture).ConfigureAwait(false);
                RemoteModelTestSession.Error(refused, StatusCodes.BadUserAccessDenied);
                Assert.That(RemoteModelTestSession.Payload(refused).GetRawText(),
                    Does.Not.Contain("approved-gds-password"));
            }
            Assert.That(lookups, Is.Zero);
            Assert.That(fixture.Store.Views, Is.Empty);
            Assert.That(fixture.Store.SetTokens, Is.Empty);
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(replacement.Remote.Calls, Is.Empty);
            Assert.That(fixture.Connection.Requests.OfType<CallRequest>(), Is.Empty);
            Assert.That(replacement.Connection.Requests.OfType<CallRequest>(), Is.Empty);
        }

        /// <summary>
        /// Public GDS reads retain their display/audience values on caller-created targets; secret paths fail closed.
        /// </summary>
        [TestCase("Password", false)]
        [TestCase("Password", true)]
        [TestCase("Tickets", true)]
        [TestCase("RequestorData", true)]
        [TestCase("RefreshAlias", true)]
        [TestCase("KeyStart", true)]
        [TestCase("KeyIssuance", true)]
        [TestCase("Nonce", true)]
        [TestCase("PrivateKey", true)]
        [Category("SecurityRegression")]
        public async Task SecurityF17GdsCallerTargetsKeepPublicReadsButCannotUseProtectedSecrets(
            string operation, bool autoAccept)
        {
            bool files = operation is "Nonce" or "PrivateKey";
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(
                files: files, callerCreated: true, autoAcceptCertificates: autoAccept).ConfigureAwait(false);
            fixture.Store.Seed("password", "password-private");
            fixture.Store.Seed("ticket", "ticket-private");
            fixture.Store.Seed("auth-data", "authorization-private");
            fixture.Store.Seed("nonce", new string('n', 32));
            int lookups = 0;
            fixture.Store.AfterGet = () =>
            {
                lookups++;
                return ValueTask.CompletedTask;
            };
            using IDisposable caller = fixture.Enter();
            GdsCredentialTools credentials = fixture.Services.GetRequiredService<GdsCredentialTools>();
            CallToolResult metadata = await credentials.InspectServiceAsync(
                fixture.Remote.Id("key-service").ToString(), GdsCredentialServiceKind.KeyCredential, "selected")
                .ConfigureAwait(false);
            Assert.That(metadata.IsError, Is.False);
            string publicIssuer = "opcua:urn:test:gds-server#" +
                NodeId.ToExpandedNodeId(fixture.Remote.Id("key-service"), fixture.Remote.Context.NamespaceUris);
            Assert.That(RemoteModelTestSession.Payload(metadata).GetProperty("issuerResource").GetString(),
                Is.EqualTo(publicIssuer));
            fixture.Remote.Reply(StatusCodes.Good, Variant.FromStructure(new ApplicationRecordDataType
            {
                ApplicationId = fixture.Remote.Id("application"),
                ApplicationUri = kAudience
            }));
            CallToolResult publicRead = await fixture.Services.GetRequiredService<GdsDirectoryTools>()
                .GetApplicationAsync(fixture.Remote.Id("directory").ToString(),
                    fixture.Remote.Id("application").ToString(), "selected").ConfigureAwait(false);
            Assert.That(publicRead.IsError, Is.False);
            Assert.That(RemoteModelTestSession.Payload(publicRead).GetProperty("resourceBinding").GetString(),
                Is.EqualTo(fixture.DirectoryResource));
            fixture.Remote.AssertCall(fixture.Remote.Id("directory"), GdsMethods.DirectoryType_GetApplication,
                Variant.From(fixture.Remote.Id("application")));
            fixture.Remote.Calls.Clear();
            fixture.Connection.Requests.Clear();
            string? keyReference = null;
            if (operation == "PrivateKey")
            {
                await WritePublicCertificateAsync(fixture).ConfigureAwait(false);
                keyReference = (await fixture.Secrets.StoreIssuedAsync(
                    ByteString.From(new byte[] { 1, 2, 3 }), McpCapability.CertificateAdministration,
                    "certificate-private-key", "opcua-app:urn:test:gds-server",
                    fixture.Clock.GetUtcNow().AddMinutes(1)).ConfigureAwait(false)).Reference;
                fixture.Store.SetTokens.Clear();
            }
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(fixture.Remote.Id("unapproved-request")));
            GdsCertificateTools certificates = fixture.Services.GetRequiredService<GdsCertificateTools>();
            CallToolResult denied = operation switch
            {
                "Password" => await PasswordAsync(fixture).ConfigureAwait(false),
                "Tickets" => await fixture.Services.GetRequiredService<GdsOnboardingTools>().RegisterTicketsAsync(
                    fixture.Remote.Id("registrar").ToString(), new GdsTicketInput
                    {
                        TicketReferences = ["alias:ticket-a"]
                    }, "selected").ConfigureAwait(false),
                "RequestorData" => await credentials.StartTokenRequestAsync(
                    fixture.Remote.Id("auth-service").ToString(), kAudience, "jwt", "alias:auth-data", "selected")
                    .ConfigureAwait(false),
                "RefreshAlias" => await credentials.RefreshTokenAsync(
                    fixture.Remote.Id("auth-service").ToString(), kAudience, "alias:auth-data", "selected")
                    .ConfigureAwait(false),
                "KeyStart" => await credentials.StartKeyRequestAsync(
                    fixture.Remote.Id("key-service").ToString(),
                    new GdsKeyCredentialInput { ApplicationUri = "urn:test:client" }, "selected")
                    .ConfigureAwait(false),
                "KeyIssuance" => await credentials.FinishKeyRequestAsync(
                    fixture.Remote.Id("key-service").ToString(), fixture.Remote.Id("request").ToString(), "selected")
                    .ConfigureAwait(false),
                "Nonce" => await certificates.CreateCsrAsync(
                    fixture.Remote.Id("configuration").ToString(), new GdsCsrInput
                    {
                        CertificateTypeId = ObjectTypeIds.RsaSha256ApplicationCertificateType.ToString(),
                        SubjectName = "CN=Requested",
                        RegeneratePrivateKey = true,
                        NonceReference = "alias:nonce"
                    }, "request.csr", "selected").ConfigureAwait(false),
                _ => await certificates.StageUpdateAsync(
                    fixture.Remote.Id("configuration").ToString(), new GdsPushCertificateInput
                    {
                        CertificateTypeId = ObjectTypeIds.RsaSha256ApplicationCertificateType.ToString(),
                        CertificatePath = "public.der",
                        PrivateKeyFormat = "PEM",
                        PrivateKeyReference = keyReference
                    }, "selected").ConfigureAwait(false)
            };
            RemoteModelTestSession.Error(denied, StatusCodes.BadUserAccessDenied);
            Assert.That(lookups, Is.Zero);
            Assert.That(fixture.Store.Views, Is.Empty);
            Assert.That(fixture.Store.SetTokens, Is.Empty);
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(fixture.Connection.Requests.OfType<CallRequest>(), Is.Empty);
        }

        /// <summary>
        /// Challenges, proofs, issuance and rotation retain a third-party audience separate from issuer authority.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF17GdsTokenFlowKeepsAudienceSeparateFromBoundIssuerAndRejectsReplacement()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            await using GdsTestContext replacement = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            fixture.Store.Seed("auth-data", "requestor-private");
            var requestId = Guid.NewGuid();
            string refreshReference;
            string issuer;
            using (fixture.Enter())
            {
                issuer = McpSecretReferences.GetResourceBinding(
                    fixture.Manager.GetSessionInfoOrThrow("selected"), fixture.Remote.Id("auth-service"));
                fixture.Remote.Reply(StatusCodes.Good,
                    Variant.From(ByteString.From(Encoding.UTF8.GetBytes("challenge-private"))),
                    Variant.From((Uuid)requestId));
                GdsCredentialTools tools = fixture.Services.GetRequiredService<GdsCredentialTools>();
                CallToolResult started = await tools.StartTokenRequestAsync(
                    fixture.Remote.Id("auth-service").ToString(), kAudience, "jwt", "alias:auth-data", "selected")
                    .ConfigureAwait(false);
                Assert.That(started.IsError, Is.False);
                fixture.Remote.AssertCall(fixture.Remote.Id("auth-service"),
                    GdsMethods.AuthorizationServiceType_StartRequestToken, Variant.From(kAudience),
                    Variant.From("jwt"), Variant.From(ByteString.From(Encoding.UTF8.GetBytes("requestor-private"))));
                JsonElement challenge = RemoteModelTestSession.Payload(started).GetProperty("serviceData");
                Assert.That(challenge.GetProperty("resource").GetString(), Is.EqualTo(kAudience));
                Assert.That(challenge.GetProperty("issuer").GetString(), Is.EqualTo(issuer));
                McpSecretReference proof = await fixture.Secrets.StoreIssuedAsync(
                    ByteString.From(new byte[] { 1, 2, 3, 4 }), McpCapability.CredentialIssuance,
                    "identity-proof", issuer, fixture.Clock.GetUtcNow().AddMinutes(5)).ConfigureAwait(false);
                McpSecretReference signature = await fixture.Secrets.StoreIssuedAsync(
                    ByteString.From(new byte[] { 9, 8 }), McpCapability.CredentialIssuance,
                    "identity-signature", issuer, fixture.Clock.GetUtcNow().AddMinutes(5)).ConfigureAwait(false);
                ReplyTokens(fixture, "access-private", "refresh-private");
                CallToolResult finished = await tools.FinishTokenRequestAsync(
                    fixture.Remote.Id("auth-service").ToString(), requestId.ToString(),
                    new GdsTokenFinishInput
                    {
                        Proof = new GdsTokenProof
                        {
                            Kind = GdsIdentityKind.UserName,
                            UserName = "public-user",
                            PolicyId = "chosen",
                            CredentialReference = proof.Reference,
                            SignatureReference = signature.Reference
                        }
                    }, "selected").ConfigureAwait(false);
                Assert.That(finished.IsError, Is.False);
                CallMethodRequest finish = fixture.Remote.Calls[1];
                Assert.That(finish.InputArguments[2].TryGetStructure<UserNameIdentityToken>(
                    fixture.Remote.Context, out UserNameIdentityToken? identity), Is.True);
                Assert.That(identity!.Password, Is.EqualTo(ByteString.From(new byte[] { 1, 2, 3, 4 })));
                Assert.That(finish.InputArguments[3].TryGetStructure<SignatureData>(
                    fixture.Remote.Context, out SignatureData? signed), Is.True);
                Assert.That(signed!.Signature, Is.EqualTo(ByteString.From(new byte[] { 9, 8 })));
                JsonElement issued = RemoteModelTestSession.Payload(finished);
                AssertIssuerAndAudience(issued.GetProperty("accessToken"), issuer);
                AssertIssuerAndAudience(issued.GetProperty("refreshToken"), issuer);
                refreshReference = issued.GetProperty("refreshToken").GetProperty("reference").GetString()!;
                ReplyTokens(fixture, "rotated-access-private", "rotated-refresh-private");
                CallToolResult rotated = await tools.RefreshTokenAsync(
                    fixture.Remote.Id("auth-service").ToString(), kAudience, refreshReference, "selected")
                    .ConfigureAwait(false);
                Assert.That(rotated.IsError, Is.False);
                CallMethodRequest rotation = fixture.Remote.Calls[2];
                Assert.That(rotation.InputArguments,
                    Is.EqualTo([Variant.From(kAudience), Variant.From("refresh-private")]));
                JsonElement renewed = RemoteModelTestSession.Payload(rotated);
                AssertIssuerAndAudience(renewed.GetProperty("accessToken"), issuer);
                AssertIssuerAndAudience(renewed.GetProperty("refreshToken"), issuer);
                Assert.That(renewed.GetRawText(), Does.Not.Contain("rotated-access-private")
                    .And.Not.Contain("rotated-refresh-private"));
                refreshReference = renewed.GetProperty("refreshToken").GetProperty("reference").GetString()!;
            }
            fixture.Remote.Calls.Clear();
            fixture.Connection.Requests.Clear();
            replacement.Connection.Requests.Clear();
            fixture.Store.Views.Clear();
            fixture.Store.SetTokens.Clear();
            int lookups = 0;
            fixture.Store.AfterGet = () =>
            {
                lookups++;
                return ValueTask.CompletedTask;
            };
            replacement.Connection.Managed.ConfiguredEndpoint.Description.EndpointUrl =
                "opc.tcp://replacement.invalid:4840";
            await fixture.Manager.RegisterExistingSessionAsync(
                "selected", replacement.Connection.Managed, "Anonymous").ConfigureAwait(false);
            using (fixture.Enter())
            {
                CallToolResult denied = await fixture.Services.GetRequiredService<GdsCredentialTools>()
                    .RefreshTokenAsync(fixture.Remote.Id("auth-service").ToString(), kAudience,
                        refreshReference, "selected").ConfigureAwait(false);
                RemoteModelTestSession.Error(denied, StatusCodes.BadUserAccessDenied);
                Assert.That(fixture.Secrets.RequireReference(
                    refreshReference, kAudience, McpCapability.CredentialIssuance).Issuer, Is.EqualTo(issuer));
            }
            Assert.That(lookups, Is.Zero);
            Assert.That(fixture.Store.Views, Is.Empty);
            Assert.That(fixture.Store.SetTokens, Is.Empty);
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(replacement.Remote.Calls, Is.Empty);
            Assert.That(fixture.Connection.Requests.OfType<CallRequest>(), Is.Empty);
            Assert.That(replacement.Connection.Requests.OfType<CallRequest>(), Is.Empty);
        }

        /// <summary>
        /// A pending request cannot send proof to a changed certificate on an otherwise unchanged registration.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF17GdsPendingTokenProofRejectsChangedCertificateBeforeLookupOrDispatch()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            fixture.Store.Seed("auth-data", "proof-private");
            using IDisposable caller = fixture.Enter();
            var id = Guid.NewGuid();
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(default(ByteString)), Variant.From((Uuid)id));
            GdsCredentialTools tools = fixture.Services.GetRequiredService<GdsCredentialTools>();
            CallToolResult started = await tools.StartTokenRequestAsync(
                fixture.Remote.Id("auth-service").ToString(), kAudience, "jwt", sessionName: "selected")
                .ConfigureAwait(false);
            Assert.That(started.IsError, Is.False);
            fixture.Remote.Calls.Clear();
            fixture.Connection.Requests.Clear();
            fixture.Connection.Managed.ConfiguredEndpoint.Description.ServerCertificate =
                ByteString.From(new byte[] { 11, 12 });
            int lookups = 0;
            fixture.Store.AfterGet = () =>
            {
                lookups++;
                return ValueTask.CompletedTask;
            };
            CallToolResult denied = await tools.FinishTokenRequestAsync(
                fixture.Remote.Id("auth-service").ToString(), id.ToString(), new GdsTokenFinishInput
                {
                    Proof = new GdsTokenProof
                    {
                        Kind = GdsIdentityKind.UserName,
                        UserName = "public-user",
                        CredentialReference = "alias:auth-data"
                    }
                }, "selected").ConfigureAwait(false);
            RemoteModelTestSession.Error(denied, StatusCodes.BadUserAccessDenied);
            Assert.That(lookups, Is.Zero);
            Assert.That(fixture.Store.Views, Is.Empty);
            Assert.That(fixture.Store.SetTokens, Is.Empty);
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(fixture.Connection.Requests.OfType<CallRequest>(), Is.Empty);
        }

        /// <summary>
        /// Acknowledged issuance keeps its captured origin even when the host retires the registration before return.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF17GdsCommittedIssuanceRetainsCapturedIssuerAfterRegistrationRetirement()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            OpcUaSessionManager.SessionInfo registration = fixture.Manager.GetSessionInfoOrThrow("selected");
            string issuer = McpSecretReferences.GetResourceBinding(registration, fixture.Remote.Id("key-service"));
            var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Remote.Session.Setup(session => session.CallAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (RequestHeader? _, ArrayOf<CallMethodRequest> calls, CancellationToken _) =>
                {
                    Assert.That(calls.Count, Is.EqualTo(1));
                    Assert.That(calls[0].MethodId,
                        Is.EqualTo(fixture.Remote.Model(GdsMethods.KeyCredentialServiceType_FinishRequest)));
                    fixture.Remote.Calls.Add(calls[0]);
                    dispatched.TrySetResult();
                    await reply.Task.ConfigureAwait(false);
                    return new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new CallMethodResult
                            {
                                OutputArguments =
                                [
                                    Variant.From("credential-committed"),
                                    Variant.From(ByteString.From(Encoding.UTF8.GetBytes("issued-key-private"))),
                                    Variant.From("public-thumbprint"), Variant.From(SecurityPolicies.Basic256Sha256),
                                    Variant.From(ArrayOf<NodeId>.Empty)
                                ]
                            }
                        ]
                    };
                });
            Task<CallToolResult> pending = CollectAsync();
            _ = await Task.WhenAny(dispatched.Task, pending).ConfigureAwait(false);
            if (!dispatched.Task.IsCompleted)
            {
                _ = await pending.ConfigureAwait(false);
            }
            try
            {
                _ = await fixture.Manager.DisconnectAsync("selected").ConfigureAwait(false);
                Assert.That(() => McpSecretReferences.GetResourceBinding(
                    registration, fixture.Remote.Id("key-service")), Throws.TypeOf<ServiceResultException>());
            }
            finally
            {
                reply.TrySetResult();
            }
            CallToolResult committed = await pending.ConfigureAwait(false);
            Assert.That(committed.IsError, Is.False);
            JsonElement issued = RemoteModelTestSession.Payload(committed).GetProperty("credential");
            Assert.That(issued.GetProperty("issuer").GetString(), Is.EqualTo(issuer));
            Assert.That(issued.GetProperty("resource").GetString(), Is.EqualTo("urn:resource"));
            Assert.That(fixture.Store.SetTokens, Has.Count.EqualTo(1));
            using (fixture.Enter())
            {
                using ISecret retained = await fixture.Secrets.ResolveAsync(
                    issued.GetProperty("reference").GetString()!, "urn:resource", McpCapability.CredentialIssuance)
                    .ConfigureAwait(false);
                Assert.That(Encoding.UTF8.GetString(retained.Bytes), Is.EqualTo("issued-key-private"));
            }
            Assert.That(RemoteModelTestSession.Payload(committed).GetRawText(), Does.Not.Contain("issued-key-private"));

            async Task<CallToolResult> CollectAsync()
            {
                using IDisposable caller = fixture.Enter();
                return await fixture.Services.GetRequiredService<GdsCredentialTools>().FinishKeyRequestAsync(
                    fixture.Remote.Id("key-service").ToString(), fixture.Remote.Id("request").ToString(), "selected")
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Invokes the real password-bearing typed certificate-directory consumer.
        /// </summary>
        private static Task<CallToolResult> PasswordAsync(GdsTestContext fixture)
        {
            return fixture.Services.GetRequiredService<GdsCertificateTools>().StartKeyPairRequestAsync(
                fixture.Remote.Id("directory").ToString(), new GdsKeyPairInput
                {
                    ApplicationId = fixture.Remote.Id("application").ToString(),
                    CertificateTypeId = ObjectTypeIds.RsaSha256ApplicationCertificateType.ToString(),
                    SubjectName = "CN=Requested",
                    PrivateKeyFormat = "PFX",
                    PasswordReference = "alias:password"
                }, "selected");
        }

        /// <summary>
        /// Supplies real protocol token tuples with deterministic server expiries.
        /// </summary>
        private static void ReplyTokens(GdsTestContext fixture, string access, string refresh)
        {
            DateTimeUtc accessExpiry = fixture.Clock.GetUtcNow().AddMinutes(5).UtcDateTime;
            DateTimeUtc refreshExpiry = fixture.Clock.GetUtcNow().AddMinutes(10).UtcDateTime;
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(access), Variant.From(accessExpiry),
                Variant.From(refresh), Variant.From(refreshExpiry));
        }

        /// <summary>
        /// Checks separate audience and issuer fields instead of conflating the two identities.
        /// </summary>
        private static void AssertIssuerAndAudience(JsonElement reference, string issuer)
        {
            Assert.That(reference.GetProperty("resource").GetString(), Is.EqualTo(kAudience));
            Assert.That(reference.GetProperty("issuer").GetString(), Is.EqualTo(issuer));
        }

        /// <summary>
        /// Writes only a test-owned public certificate, never private material or host PKI.
        /// </summary>
        private static async Task WritePublicCertificateAsync(GdsTestContext fixture)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=F17 Public Test Only", key, HashAlgorithmName.SHA256);
            using X509Certificate2 certificate = request.CreateSelfSigned(
                fixture.Clock.GetUtcNow().AddMinutes(-1), fixture.Clock.GetUtcNow().AddDays(1));
            await File.WriteAllBytesAsync(Path.Combine(fixture.Root!, "public.der"),
                certificate.Export(X509ContentType.Cert)).ConfigureAwait(false);
        }

        /// <summary>Third-party token audience, deliberately unrelated to the issuing server's URI or node.</summary>
        private const string kAudience = "https://resource.example.test:9443/api?scope=plant";
    }
}
#endif
