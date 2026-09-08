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

using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Bindings.OpcUa;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    [TestFixture]
    public sealed class OpcUaWotExactSecurityTests
    {
        [Test]
        public void ExactChannelRequirementRejectsMismatchingSessionWithoutAFloor()
        {
            Mock<ISession> session = CreateSession(MessageSecurityMode.None, SecurityPolicies.None);
            var registry = new WotProtocolBinderRegistry(
                [new OpcUaBindingPlanner()],
                [new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    ConstrainedSessionFactory = (_, _) => new ValueTask<ISession>(session.Object)
                })],
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
            WotCompiledForm form = Prepare(registry, """
                "channel": {
                  "scheme": "uav:channelsec",
                  "uav:securityMode": "SignAndEncrypt",
                  "uav:securityPolicy": "Aes256_Sha256_RsaPss"
                }
                """, "channel");

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await registry.OpenChannelAsync(form).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeRejected));
            session.Verify(s => s.Dispose(), Times.Once);
        }

        [Test]
        public async Task ConstrainedFactoryReceivesExactSecurityRequirements()
        {
            OpcUaWotSessionRequest? captured = null;
            Mock<ISession> session = CreateSession(
                MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Aes256_Sha256_RsaPss);
            var registry = new WotProtocolBinderRegistry(
                [new OpcUaBindingPlanner()],
                [new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    ConstrainedSessionFactory = (request, _) =>
                    {
                        captured = request;
                        return new ValueTask<ISession>(session.Object);
                    }
                })],
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
            WotCompiledForm form = Prepare(registry, """
                "channel": {
                  "scheme": "uav:channelsec",
                  "uav:securityMode": "SignAndEncrypt",
                  "uav:securityPolicy": "Aes256_Sha256_RsaPss"
                }
                """, "channel");

            await using IWotBindingChannel channel = await registry.OpenChannelAsync(form).ConfigureAwait(false);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.SecurityRequirements.Count, Is.EqualTo(1));
            Assert.That(captured.SecurityRequirements[0].SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(captured.SecurityRequirements[0].SecurityPolicyUri,
                Is.EqualTo(SecurityPolicies.Aes256_Sha256_RsaPss));
            Assert.That(captured.MinimumSecurity, Is.Null);
        }

        [Test]
        public async Task DiscoverySelectsTheExactEndpointRatherThanAStrongerMismatch()
        {
            var matching = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.Sign,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            var stronger = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss
            };
            EndpointDescription? selected = null;
            var registry = new WotProtocolBinderRegistry(
                [new OpcUaBindingPlanner()],
                [new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    EndpointDiscovery = (_, _) =>
                        new ValueTask<ArrayOf<EndpointDescription>>([stronger, matching]),
                    SelectedEndpointSessionFactory = (endpoint, _, _) =>
                    {
                        selected = endpoint;
                        return new ValueTask<ISession>(CreateSession(endpoint.SecurityMode, endpoint.SecurityPolicyUri)
                            .Object);
                    }
                })],
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
            WotCompiledForm form = Prepare(registry, """
                "channel": {
                  "scheme": "uav:channelsec",
                  "uav:securityMode": "Sign",
                  "uav:securityPolicy": "Basic256Sha256"
                }
                """, "channel");

            await using IWotBindingChannel channel = await registry.OpenChannelAsync(form).ConfigureAwait(false);

            Assert.That(selected, Is.SameAs(matching));
        }

        [TestCase(UserTokenType.Anonymous, false)]
        [TestCase(UserTokenType.UserName, true)]
        public async Task CombinedAuthenticationChecksTheActualSessionIdentity(UserTokenType tokenType, bool accepted)
        {
            Mock<ISession> session = CreateSession(
                MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Aes256_Sha256_RsaPss, tokenType);
            var registry = new WotProtocolBinderRegistry(
                [new OpcUaBindingPlanner()],
                [new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    ConstrainedSessionFactory = (_, _) => new ValueTask<ISession>(session.Object)
                })],
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
            WotCompiledForm form = Prepare(registry, """
                "channel": {
                  "scheme": "uav:channelsec",
                  "uav:securityMode": "SignAndEncrypt",
                  "uav:securityPolicy": "Aes256_Sha256_RsaPss"
                },
                "identity": { "scheme": "uav:authentication", "uav:userIdentityToken": "UserName" },
                "combined": { "scheme": "combo", "allOf": ["channel", "identity"] }
                """, "combined");

            if (accepted)
            {
                await using IWotBindingChannel channel = await registry.OpenChannelAsync(form).ConfigureAwait(false);
                Assert.That(channel.Form, Is.SameAs(form));
            }
            else
            {
                ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await registry.OpenChannelAsync(form).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            }
            session.Verify(s => s.Dispose(), Times.Once);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OneOfRetainsBothCompleteSecurityAlternatives(bool stronger)
        {
            Mock<ISession> session = stronger
                ? CreateSession(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Aes256_Sha256_RsaPss)
                : CreateSession(MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256);
            OpcUaWotSessionRequest? captured = null;
            var registry = new WotProtocolBinderRegistry(
                [new OpcUaBindingPlanner()],
                [new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    ConstrainedSessionFactory = (request, _) =>
                    {
                        captured = request;
                        return new ValueTask<ISession>(session.Object);
                    }
                })],
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
            WotCompiledForm form = Prepare(registry, """
                "signed": {
                  "scheme": "uav:channelsec",
                  "uav:securityMode": "Sign",
                  "uav:securityPolicy": "Basic256Sha256"
                },
                "encrypted": {
                  "scheme": "uav:channelsec",
                  "uav:securityMode": "SignAndEncrypt",
                  "uav:securityPolicy": "Aes256_Sha256_RsaPss"
                },
                "choice": { "scheme": "combo", "oneOf": ["signed", "encrypted"] }
                """, "choice");

            await using IWotBindingChannel channel = await registry.OpenChannelAsync(form).ConfigureAwait(false);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.SecurityRequirements.Count, Is.EqualTo(2));
            Assert.That(captured.SecurityRequirements[0].SecurityMode, Is.EqualTo(MessageSecurityMode.Sign));
            Assert.That(captured.SecurityRequirements[1].SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OneOfKeepsSecurityFloorsWithinTheirAlternative(bool matches)
        {
            Mock<ISession> session = matches
                ? CreateSession(MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256)
                : CreateSession(MessageSecurityMode.None, SecurityPolicies.None);
            var registry = new WotProtocolBinderRegistry(
                [new OpcUaBindingPlanner()],
                [new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    ConstrainedSessionFactory = (_, _) => new ValueTask<ISession>(session.Object)
                })],
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
            WotCompiledForm form = Prepare(registry, """
                "signed": {
                  "scheme": "uav:channelsec",
                  "uav:securityMode": "Sign",
                  "uav:securityPolicy": "Basic256Sha256"
                },
                "autoStrong": {
                  "scheme": "auto",
                  "uav:minimumSecurity": {
                    "uav:securityMode": "SignAndEncrypt",
                    "uav:securityPolicy": "Aes256_Sha256_RsaPss"
                  }
                },
                "choice": { "scheme": "combo", "oneOf": ["signed", "autoStrong"] }
                """, "choice");

            if (matches)
            {
                await using IWotBindingChannel channel = await registry.OpenChannelAsync(form).ConfigureAwait(false);
                Assert.That(form.SecurityFloor, Is.Null);
                Assert.That(form.OpcUaSecurityRequirements[1].MinimumSecurity, Is.Not.Null);
                Assert.That(form.OpcUaSecurityRequirements[1].MinimumSecurity!.SecurityMode,
                    Is.EqualTo("SignAndEncrypt"));
            }
            else
            {
                ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await registry.OpenChannelAsync(form).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeRejected));
            }
        }

        [Test]
        public async Task DiscoveryFiltersEndpointsByTheRequiredTokenKind()
        {
            var unsupported = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss,
                UserIdentityTokens = [new UserTokenPolicy { TokenType = UserTokenType.Anonymous }]
            };
            var supported = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.Sign,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                UserIdentityTokens = [new UserTokenPolicy { TokenType = UserTokenType.UserName }]
            };
            EndpointDescription? selected = null;
            var registry = new WotProtocolBinderRegistry(
                [new OpcUaBindingPlanner()],
                [new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    EndpointDiscovery = (_, _) =>
                        new ValueTask<ArrayOf<EndpointDescription>>([unsupported, supported]),
                    SelectedEndpointSessionFactory = (endpoint, _, _) =>
                    {
                        selected = endpoint;
                        return new ValueTask<ISession>(CreateSession(
                            endpoint.SecurityMode, endpoint.SecurityPolicyUri, UserTokenType.UserName).Object);
                    }
                })],
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
            WotCompiledForm form = Prepare(registry,
                """
                "identity": { "scheme": "uav:authentication", "uav:userIdentityToken": "UserName" }
                """, "identity");

            await using IWotBindingChannel channel = await registry.OpenChannelAsync(form).ConfigureAwait(false);

            Assert.That(selected, Is.SameAs(supported));
        }

        [Test]
        public async Task IssuedTokenAcquisitionReferenceReachesTheSessionFactory()
        {
            OpcUaWotSessionRequest? captured = null;
            Mock<ISession> session = CreateSession(
                MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Aes256_Sha256_RsaPss, UserTokenType.IssuedToken);
            var registry = new WotProtocolBinderRegistry(
                [new OpcUaBindingPlanner()],
                [new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    ConstrainedSessionFactory = (request, _) =>
                    {
                        captured = request;
                        return new ValueTask<ISession>(session.Object);
                    }
                })],
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
            WotCompiledForm form = Prepare(registry, """
                "identity": {
                  "scheme": "uav:authentication",
                  "uav:userIdentityToken": "IssuedToken",
                  "uav:issueToken": "tokenProvider"
                },
                "tokenProvider": { "scheme": "oauth2", "in": "header", "name": "Authorization" }
                """, "identity");

            await using IWotBindingChannel channel = await registry.OpenChannelAsync(form).ConfigureAwait(false);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.SecurityRequirements.Count, Is.EqualTo(1));
            WotCredentialReference? reference = captured.SecurityRequirements[0].IssueTokenReference;
            Assert.That(reference, Is.Not.Null);
            Assert.That(reference!.SchemeName, Is.EqualTo("tokenProvider"));
            Assert.That(reference.Scheme, Is.EqualTo(WotSecurityScheme.OAuth2));
            Assert.That(reference.BindingUri, Is.EqualTo(OpcUaBindingPlanner.BindingUri));
            Assert.That(reference.Endpoint, Is.EqualTo("opc.tcp://localhost:4840"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ImpossibleChannelConstraintsFailDuringCompilation(bool conflictsWithFloor)
        {
            var registry = new WotProtocolBinderRegistry([new OpcUaBindingPlanner()], []);
            string definitions = conflictsWithFloor
                ? """
                "channel": {
                  "scheme": "uav:channelsec", "uav:securityMode": "Sign", "uav:securityPolicy": "Basic256Sha256"
                },
                "auto": { "scheme": "auto", "uav:minimumSecurity": { "uav:securityMode": "SignAndEncrypt" } },
                "combined": { "scheme": "combo", "allOf": ["channel", "auto"] }
                """
                : """
                "combined": {
                  "scheme": "uav:channelsec", "uav:securityMode": "None", "uav:securityPolicy": "Basic256Sha256"
                }
                """;

            WotBindingPlan plan = PreparePlan(registry, definitions, "combined");

            Assert.That(plan.CompiledForms, Is.Empty);
            Assert.That(plan.Diagnostics.Any(diagnostic =>
                diagnostic.IsError && diagnostic.Code == WotBindingDiagnosticCode.ConflictingFields), Is.True);
        }

        [TestCase(""" "root": { "scheme": "combo", "allOf": ["missing"] } """)]
        [TestCase(""" "root": { "scheme": "combo", "allOf": ["root"] } """)]
        [TestCase(""" "root": { "scheme": "combo", "allOf": ["nosec_sc"], "oneOf": ["nosec_sc"] } """)]
        [TestCase(""" "root": { "scheme": "combo", "oneOf": [] } """)]
        [TestCase(""" "root": { "scheme": "combo", "allOf": [false] } """)]
        [TestCase(""" "root": { "scheme": "uav:channelsec", "uav:securityMode": "Sign" } """)]
        [TestCase(""" "root": { "scheme": "uav:authentication", "uav:userIdentityToken": "Password" } """)]
        [TestCase(
            """
            "root": { "scheme": "uav:authentication",
              "uav:userIdentityToken": "IssuedToken", "uav:issueToken": "missing" }
            """)]
        [TestCase(
            """
            "root": { "scheme": "uav:authentication", "uav:userIdentityToken": "IssuedToken", "uav:issueToken": "root" }
            """)]
        [TestCase(
            """
            "root": { "scheme": "uav:authentication",
              "uav:userIdentityToken": "UserName", "uav:issueToken": "nosec_sc" }
            """)]
        public void InvalidRequiredSecurityGraphCannotBecomeAnUnconstrainedForm(string definitions)
        {
            var registry = new WotProtocolBinderRegistry([new OpcUaBindingPlanner()], []);

            WotBindingPlan plan = PreparePlan(registry, definitions, "root");

            Assert.That(plan.CompiledForms, Is.Empty);
            Assert.That(plan.Diagnostics.Any(diagnostic => diagnostic.IsError), Is.True);
        }

        [Test]
        public void AlternativeLimitIsReportedInsteadOfTruncatingRequirements()
        {
            var registry = new WotProtocolBinderRegistry(
                [new OpcUaBindingPlanner()], [], bounds: new WotBindingBounds { MaxSecurityAlternatives = 1 });
            WotBindingPlan plan = PreparePlan(registry, """
                "a": { "scheme": "uav:authentication", "uav:userIdentityToken": "Anonymous" },
                "b": { "scheme": "uav:authentication", "uav:userIdentityToken": "UserName" },
                "root": { "scheme": "combo", "oneOf": ["a", "b"] }
                """, "root");

            Assert.That(plan.CompiledForms, Is.Empty);
            Assert.That(plan.Diagnostics.Any(diagnostic =>
                diagnostic.IsError && diagnostic.Code == WotBindingDiagnosticCode.BoundsExceeded), Is.True);
        }

        [Test]
        public void ExactRequirementsRequireAConstraintAwareFactoryBeforeConnecting()
        {
            bool connected = false;
            var registry = new WotProtocolBinderRegistry(
                [new OpcUaBindingPlanner()],
                [new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    SessionFactory = (_, _) =>
                    {
                        connected = true;
                        return new ValueTask<ISession>(CreateSession(
                            MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256).Object);
                    }
                })],
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
            WotCompiledForm form = Prepare(registry, """
                "channel": {
                  "scheme": "uav:channelsec", "uav:securityMode": "Sign", "uav:securityPolicy": "Basic256Sha256"
                }
                """, "channel");

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await registry.OpenChannelAsync(form).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(connected, Is.False);
        }

        [Test]
        public void ASessionCannotMixTheChannelAndIdentityOfDifferentAlternatives()
        {
            Mock<ISession> session = CreateSession(
                MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Aes256_Sha256_RsaPss, UserTokenType.UserName);
            var registry = new WotProtocolBinderRegistry(
                [new OpcUaBindingPlanner()],
                [new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    ConstrainedSessionFactory = (_, _) => new ValueTask<ISession>(session.Object)
                })],
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
            WotCompiledForm form = Prepare(registry, """
                "encrypted": {
                  "scheme": "uav:channelsec",
                  "uav:securityMode": "SignAndEncrypt", "uav:securityPolicy": "Aes256_Sha256_RsaPss"
                },
                "signed": {
                  "scheme": "uav:channelsec", "uav:securityMode": "Sign", "uav:securityPolicy": "Basic256Sha256"
                },
                "anonymous": { "scheme": "uav:authentication", "uav:userIdentityToken": "Anonymous" },
                "username": { "scheme": "uav:authentication", "uav:userIdentityToken": "UserName" },
                "first": { "scheme": "combo", "allOf": ["encrypted", "anonymous"] },
                "second": { "scheme": "combo", "allOf": ["signed", "username"] },
                "root": { "scheme": "combo", "oneOf": ["first", "second"] }
                """, "root");

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await registry.OpenChannelAsync(form).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            session.Verify(s => s.Dispose(), Times.Once);
        }

        [TestCase(1, false)]
        [TestCase(2, true)]
        public void SecurityGraphDepthUsesTheConfiguredBoundary(int maximumDepth, bool accepted)
        {
            var registry = new WotProtocolBinderRegistry(
                [new OpcUaBindingPlanner()], [], bounds: new WotBindingBounds { MaxSecurityDepth = maximumDepth });

            WotBindingPlan plan = PreparePlan(registry, """
                "root": { "scheme": "combo", "allOf": ["identity"] },
                "identity": { "scheme": "uav:authentication", "uav:userIdentityToken": "Anonymous" }
                """, "root");

            Assert.That(plan.CompiledForms, Has.Length.EqualTo(accepted ? 1 : 0));
            Assert.That(plan.Diagnostics.Any(diagnostic =>
                diagnostic.IsError && diagnostic.Code == WotBindingDiagnosticCode.BoundsExceeded),
                Is.EqualTo(!accepted));
        }

        private static Mock<ISession> CreateSession(
            MessageSecurityMode mode, string? policy, UserTokenType? tokenType = null)
        {
            var session = new Mock<ISession>();
            session.SetupGet(s => s.ConfiguredEndpoint).Returns(new ConfiguredEndpoint(
                null,
                new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost:4840",
                    SecurityMode = mode,
                    SecurityPolicyUri = policy,
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy { PolicyId = "anonymous", TokenType = UserTokenType.Anonymous },
                        new UserTokenPolicy { PolicyId = "username", TokenType = UserTokenType.UserName }
                    ]
                },
                null));
            if (tokenType.HasValue)
            {
                var identity = new Mock<IUserIdentity>();
                identity.SetupGet(i => i.TokenType).Returns(tokenType.Value);
                session.SetupGet(s => s.Identity).Returns(identity.Object);
            }
            return session;
        }

        private static WotCompiledForm Prepare(
            WotProtocolBinderRegistry registry,
            string definitions,
            string security)
        {
            WotBindingPlan plan = PreparePlan(registry, definitions, security);
            Assert.That(plan.Diagnostics.Where(diagnostic => diagnostic.IsError), Is.Empty);
            return plan.CompiledForms.Single();
        }

        private static WotBindingPlan PreparePlan(
            WotProtocolBinderRegistry registry,
            string definitions,
            string security)
        {
            string document = $$"""
                {
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "title": "Exact channel constraints",
                  "securityDefinitions": { {{definitions}} },
                  "security": "{{security}}",
                  "properties": {
                    "value": {
                      "type": "integer",
                      "forms": [{ "href": "opc.tcp://localhost:4840/?id=i%3D2258", "op": "readproperty" }]
                    }
                  }
                }
                """;
            return registry.Prepare(WotBindingPlanRequest.FromDocument(
                "exact-security", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(document)));
        }
    }
}
