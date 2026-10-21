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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpAuthorization
{
    /// <summary>
    /// Tests transport identity, revocation, and real DI/filter composition without starting an MCP server.
    /// Stream.Null exists only to construct the SDK's public request context.
    /// </summary>
    [TestFixture]
    public sealed class McpFoundationRequestFilterTests
    {
        [SetUp]
        public void SetUp()
        {
            m_callers = new McpCallerContext();
            m_cancellation = new CancellationTokenSource();
            m_provider = null;
            m_write = null;
            m_model = new McpAuthorizationModel();
            m_model.AddTypeDefinition(s_target, new ExpandedNodeId(ObjectTypeIds.RoleType));
            m_model.Session.Setup(session => session.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, ArrayOf<WriteValue> items, CancellationToken token) =>
                {
                    // A willing server must not hide a missing host authorization check.
                    m_model.Mutations.Add("Write");
                    m_write = new WriteRequest(header, items, token);
                    return ValueTask.FromResult(new WriteResponse
                    {
                        ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good },
                        Results = items.ConvertAll(_ => StatusCodes.Good)
                    });
                });
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            try
            {
                if (m_provider is not null)
                {
                    await m_provider.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_cancellation.Dispose();
            }
        }

        [Test]
        public void CaptureCallerRejectsNullContinuation()
        {
            Assert.That(() => McpExecutionFilters.CaptureCaller(null!),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("next"));
            Assert.That(m_callers.Current, Is.Null);
            Assert.That(m_callers.HasRequestScope, Is.False);
            m_model.VerifyNoMutations();
        }

        [TestCase("ConfigureServices", "services")]
        [TestCase("OptionsServices", "services")]
        [TestCase("Options", "options")]
        [TestCase("Execution", "options.Execution")]
        [TestCase("IdentityBuilder", "builder")]
        [TestCase("FiltersBuilder", "mcpServerBuilder")]
        public void CoreRegistrationRejectsNullDependencies(string dependency, string parameterName)
        {
            var services = new ServiceCollection();
            Action register = dependency switch
            {
                "ConfigureServices" => () => OpcUaMcpCoreExtensions.AddOpcUaMcpCore(
                    null!, options => options.Execution = new McpExecutionOptions()),
                "OptionsServices" => () => OpcUaMcpCoreExtensions.AddOpcUaMcpCore(null!, new OpcUaMcpOptions()),
                "Options" => () => services.AddOpcUaMcpCore((OpcUaMcpOptions)null!),
                "Execution" => () => services.AddOpcUaMcpCore(new OpcUaMcpOptions { Execution = null! }),
                "IdentityBuilder" => () => OpcUaMcpCoreExtensions.WithOpcUaMcpCallerIdentity(null!),
                "FiltersBuilder" => () => OpcUaMcpCoreExtensions.WithOpcUaMcpFilters(null!),
                _ => throw new ArgumentOutOfRangeException(nameof(dependency))
            };

            Assert.That(() => register(),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName))
                    .EqualTo(parameterName));

            Assert.That(m_callers.Current, Is.Null);
            Assert.That(m_callers.HasRequestScope, Is.False);
            m_model.VerifyNoMutations();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CoreRegistrationPreservesInjectedPolicyAndCallerContextAsync(bool configureOverload)
        {
            var policy = new Mock<IMcpOperationPolicy>(MockBehavior.Strict);
            policy.Setup(value => value.RequireCaller()).Returns(s_caller);
            policy.Setup(value => value.RequireSessionAccess(kSession)).Returns(s_caller);
            policy.Setup(value => value.RequireSession(McpCapability.IdentityAdministration, kSession))
                .Returns(() =>
                {
                    Assert.That(m_callers.Current, Is.EqualTo(s_caller));
                    Assert.That(m_callers.HasRequestScope, Is.True);
                    return s_caller;
                });
            var services = new ServiceCollection();
            services.AddSingleton(m_callers);
            services.AddSingleton<IMcpFixedSessionContract>(new FixedMockSessionContract(m_model.Session.Object));
            services.AddSingleton(policy.Object);
            if (configureOverload)
            {
                // FromEnvironment runs before this callback. An invalid ambient transfer limit is a host precondition.
                services.AddOpcUaMcpCore(options => options.Execution = new McpExecutionOptions());
            }
            else
            {
                services.AddOpcUaMcpCore(new OpcUaMcpOptions { Execution = new McpExecutionOptions() });
            }
            AddServer(services).WithOpcUaMcpCallerIdentity();
            m_provider = services.BuildServiceProvider();
            OpcUaSessionManager manager = await RegisterModelAsync().ConfigureAwait(false);
            Assert.That(m_provider.GetRequiredService<McpCallerContext>(), Is.SameAs(m_callers));
            Assert.That(m_provider.GetRequiredService<IMcpOperationPolicy>(), Is.SameAs(policy.Object));
            Assert.That(manager.OperationPolicy, Is.SameAs(policy.Object));
            RequestContext<CallToolRequestParams> request = CreateRequest(TrustedPrincipal());
            CallToolResult expected = Success();
            int calls = 0;
            McpRequestHandler<CallToolRequestParams, CallToolResult> handler = RegisteredFilter()(
                async (forwarded, ct) =>
                {
                    calls++;
                    AssertForwarded(forwarded, request, ct);
                    IServiceProvider requestServices = forwarded.Services ??
                        throw new InvalidOperationException("The forwarded request has no services.");
                    Assert.That(requestServices.GetRequiredService<McpCallerContext>(), Is.SameAs(m_callers));
                    Assert.That(requestServices.GetRequiredService<OpcUaSessionManager>(), Is.SameAs(manager));
                    await manager.UaOperationGuard.AuthorizeTargetAsync(
                        manager.GetSessionInfoOrThrow(), s_target, false, ct).ConfigureAwait(false);
                    return expected;
                });

            CallToolResult result = await handler(request, m_cancellation.Token).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(result, Is.SameAs(expected));
            AssertNoScope();
            policy.Verify(value => value.RequireCaller(), Times.Once);
            policy.Verify(value => value.RequireSessionAccess(kSession), Times.Once);
            policy.Verify(value => value.RequireSession(McpCapability.IdentityAdministration, "Prod-A"), Times.Once);
            policy.VerifyNoOtherCalls();
            Assert.That(m_model.Browses.Select(query => query.Request.NodeId), Does.Contain(s_target));
            m_model.VerifyNoMutations();
        }

        [TestCase("LocalCallerId")]
        [TestCase("HostBacking")]
        [TestCase("GrantSlot")]
        [TestCase("SessionBacking")]
        public async Task CoreRegistrationSnapshotsOriginalExecutionOptionsAsync(string mutation)
        {
            McpCapability[] host = [McpCapability.IdentityAdministration];
            string[] sessions = [kSession];
            McpCallerGrant[] grants =
            [
                new McpCallerGrant
                {
                    Caller = s_local,
                    Capabilities = [McpCapability.IdentityAdministration],
                    SessionNames = new ArrayOf<string>(sessions.AsMemory())
                }
            ];
            var original = new McpExecutionOptions
            {
                LocalCallerId = s_local.Subject,
                EnabledCapabilities = new ArrayOf<McpCapability>(host.AsMemory()),
                CallerGrants = new ArrayOf<McpCallerGrant>(grants.AsMemory())
            };
            var services = new ServiceCollection();
            services.AddSingleton(m_callers);
            services.AddSingleton<IMcpFixedSessionContract>(new FixedMockSessionContract(m_model.Session.Object));
            services.AddOpcUaMcpCore(new OpcUaMcpOptions { Execution = original });
            AddServer(services).WithOpcUaMcpCallerIdentity();

            // Mutate actual caller-owned storage after registration, before any resolution.
            switch (mutation)
            {
                case "LocalCallerId":
                    original.LocalCallerId = "Local-B";
                    Assert.That(original.LocalCallerId, Is.EqualTo("Local-B"));
                    break;
                case "HostBacking":
                    host[0] = McpCapability.CertificateAdministration;
                    Assert.That(original.EnabledCapabilities[0], Is.EqualTo(McpCapability.CertificateAdministration));
                    break;
                case "GrantSlot":
                    grants[0] = new McpCallerGrant
                    {
                        Caller = s_local with { Subject = "Local-B" },
                        Capabilities = [McpCapability.IdentityAdministration],
                        SessionNames = [kSession]
                    };
                    Assert.That(original.CallerGrants[0].Caller.Subject, Is.EqualTo("Local-B"));
                    break;
                case "SessionBacking":
                    sessions[0] = "Prod-B";
                    Assert.That(original.CallerGrants[0].SessionNames[0], Is.EqualTo("Prod-B"));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }
            m_provider = services.BuildServiceProvider();
            McpExecutionOptions snapshot = m_provider.GetRequiredService<McpExecutionOptions>();
            Assert.That(snapshot, Is.Not.SameAs(original));
            Assert.That(snapshot.LocalCallerId, Is.EqualTo("Local-A"));
            Assert.That(snapshot.EnabledCapabilities.ToArray(),
                Is.EqualTo([McpCapability.IdentityAdministration]));
            Assert.That(snapshot.CallerGrants.Count, Is.EqualTo(1));
            Assert.That(snapshot.CallerGrants[0].Caller, Is.EqualTo(s_local));
            Assert.That(snapshot.CallerGrants[0].Capabilities.ToArray(),
                Is.EqualTo([McpCapability.IdentityAdministration]));
            Assert.That(snapshot.CallerGrants[0].SessionNames.ToArray(), Is.EqualTo(s_expectedSessions));
            OpcUaSessionManager manager = await RegisterModelAsync().ConfigureAwait(false);
            RequestContext<CallToolRequestParams> request = CreateRequest(null);
            CallToolResult expected = Success();
            int calls = 0;
            McpRequestHandler<CallToolRequestParams, CallToolResult> handler = RegisteredFilter()(
                (forwarded, ct) =>
                {
                    calls++;
                    AssertForwarded(forwarded, request, ct);
                    Assert.That(m_callers.Current, Is.EqualTo(s_local));
                    Assert.That(m_callers.HasRequestScope, Is.True);
                    Assert.That(manager.OperationPolicy.RequireSession(McpCapability.IdentityAdministration, "prod-a"),
                        Is.EqualTo(s_local));
                    Assert.That(manager.OperationPolicy.IsEnabled(McpCapability.CertificateAdministration), Is.False);
                    AssertPolicyDenied(() => manager.OperationPolicy.RequireSession(
                        McpCapability.IdentityAdministration, "Prod-B"), kSessionDenied);
                    using (m_callers.EnterTrustedCaller(s_local with { Subject = "Local-B" }))
                    {
                        AssertPolicyDenied(() => manager.OperationPolicy.RequireSession(
                            McpCapability.IdentityAdministration, kSession), kGrantDenied);
                    }
                    return PrivilegedWriteAsync(manager, expected, ct);
                });

            CallToolResult result = await handler(request, m_cancellation.Token).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(result, Is.SameAs(expected));
            AssertAllowedWrite();
            AssertNoScope();
            Assert.That(manager.OperationPolicy.RequireCaller(), Is.EqualTo(s_local));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CaptureCallerKeepsFrozenLocalIdentityAfterResolvedOptionsMutationAsync(bool registered)
        {
            McpCallerIdentity otherLocal = s_local with { Subject = "Local-B" };
            McpExecutionOptions options = GrantedOptions(s_local, otherLocal);
            options.LocalCallerId = "Local-A";
            BuildProvider(options);
            OpcUaSessionManager manager = await RegisterModelAsync().ConfigureAwait(false);
            Assert.That(manager.OperationPolicy.RequireCaller(), Is.EqualTo(s_local));
            using (m_callers.EnterTrustedCaller(otherLocal))
            {
                Assert.That(manager.OperationPolicy.RequireSession(McpCapability.IdentityAdministration, kSession),
                    Is.EqualTo(otherLocal));
            }

            // The policy has already frozen Local-A. A mutable DI snapshot must not select a different owner.
            McpExecutionOptions exposedOptions = m_provider!.GetRequiredService<McpExecutionOptions>();
            exposedOptions.LocalCallerId = "Local-B";
            Assert.That(exposedOptions.LocalCallerId, Is.EqualTo("Local-B"));
            Assert.That(manager.OperationPolicy.RequireCaller(), Is.EqualTo(s_local));
            RequestContext<CallToolRequestParams> request = CreateRequest(null);
            CallToolResult expected = Success();
            McpCallerIdentity? observed = null;
            McpCallerIdentity? authorized = null;
            int calls = 0;
            McpRequestHandler<CallToolRequestParams, CallToolResult> handler = SelectFilter(registered)(
                (forwarded, ct) =>
                {
                    calls++;
                    AssertForwarded(forwarded, request, ct);
                    observed = m_callers.Current;
                    authorized = manager.OperationPolicy.RequireSession(
                        McpCapability.IdentityAdministration, kSession);
                    return PrivilegedWriteAsync(manager, expected, ct);
                });

            CallToolResult result = await handler(request, m_cancellation.Token).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(observed, Is.EqualTo(new McpCallerIdentity("Local-A", McpCallerContext.LocalIssuer, true)));
                Assert.That(authorized, Is.EqualTo(s_local));
                Assert.That(result, Is.SameAs(expected));
                AssertAllowedWrite();
                AssertNoScope();
                Assert.That(manager.OperationPolicy.RequireCaller(), Is.EqualTo(s_local));
            }
        }

        [TestCase(false, ClaimTypes.NameIdentifier)]
        [TestCase(true, ClaimTypes.NameIdentifier)]
        [TestCase(false, "sub")]
        [TestCase(true, "sub")]
        public async Task CaptureCallerForwardsTrustedIdentityAndOriginalRequestAsync(bool registered, string claimType)
        {
            BuildProvider(GrantedOptions(s_caller));
            OpcUaSessionManager manager = await RegisterModelAsync().ConfigureAwait(false);
            RequestContext<CallToolRequestParams> request = CreateRequest(TrustedPrincipal(claimType));
            CallToolRequestParams parameters = request.Params!;
            CallToolResult expected = Success();
            int calls = 0;
            using IDisposable outer = m_callers.EnterTrustedCaller(s_outer);
            McpRequestHandler<CallToolRequestParams, CallToolResult> handler = SelectFilter(registered)(
                (forwarded, ct) =>
                {
                    calls++;
                    AssertForwarded(forwarded, request, ct);
                    Assert.That(forwarded.Params, Is.SameAs(parameters));
                    Assert.That(forwarded.Params!.Arguments!["owner"].GetString(), Is.EqualTo("Attacker"));
                    Assert.That(forwarded.Params.Arguments["issuer"].GetString(), Is.EqualTo("urn:untrusted"));
                    Assert.That(forwarded.Params.Arguments["role"].GetString(), Is.EqualTo("Administrator"));
                    Assert.That(m_callers.Current!.Subject, Is.EqualTo("Caller-A"));
                    Assert.That(m_callers.Current.Issuer, Is.EqualTo("urn:test:issuer-a"));
                    Assert.That(m_callers.Current.IsLocal, Is.False);
                    Assert.That(m_callers.HasRequestScope, Is.True);
                    Assert.That(manager.OperationPolicy.RequireSession(McpCapability.IdentityAdministration, kSession),
                        Is.EqualTo(s_caller));
                    return PrivilegedWriteAsync(manager, expected, ct);
                });

            CallToolResult result = await handler(request, m_cancellation.Token).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(result, Is.SameAs(expected));
            Assert.That(result.IsError, Is.False);
            AssertAllowedWrite();
            AssertOuterScope();
        }

        [TestCaseSource(nameof(RoleClaimCases))]
        public async Task CaptureCallerRoleNameAndOwnerClaimsDoNotGrantAuthorityAsync(
            bool registered, string claims)
        {
            McpExecutionOptions options = GrantedOptions(s_caller, s_local);
            options.LocalCallerId = s_local.Subject;
            BuildProvider(options);
            OpcUaSessionManager manager = await RegisterModelAsync().ConfigureAwait(false);
            ClaimsPrincipal user = claims switch
            {
                "RolesOnly" => new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Role, "Administrator")], "TrustedTransport")),
                "DisplayNameOnly" => new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "Caller-A")], "TrustedTransport")),
                "AnonymousOwner" => new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "Caller-A", ClaimValueTypes.String, "urn:test:issuer-a")])),
                "AuthenticatedAdministrator" => TrustedPrincipal(subject: "Caller-B"),
                _ => throw new ArgumentOutOfRangeException(nameof(claims))
            };
            RequestContext<CallToolRequestParams> request = CreateRequest(user, claimGrantedOwner: true);
            int calls = 0;
            using IDisposable outer = m_callers.EnterTrustedCaller(s_caller);
            Assert.That(manager.OperationPolicy.RequireSession(McpCapability.IdentityAdministration, kSession),
                Is.EqualTo(s_caller));
            McpRequestHandler<CallToolRequestParams, CallToolResult> handler = SelectFilter(registered)(
                (forwarded, ct) =>
                {
                    calls++;
                    AssertForwarded(forwarded, request, ct);
                    Assert.That(forwarded.Params!.Arguments!["owner"].GetString(), Is.EqualTo("Caller-A"));
                    Assert.That(forwarded.Params.Arguments["issuer"].GetString(), Is.EqualTo("urn:test:issuer-a"));
                    Assert.That(m_callers.Current, Is.EqualTo(claims == "AuthenticatedAdministrator"
                        ? s_caller with { Subject = "Caller-B" } : null));
                    Assert.That(m_callers.HasRequestScope, Is.True);
                    return PrivilegedWriteAsync(manager, Success(), ct);
                });

            CallToolResult result = await handler(request, m_cancellation.Token).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
            AssertServiceError(result, StatusCodes.BadUserAccessDenied, "BadUserAccessDenied",
                kRegistrationDenied);
            Assert.That(m_callers.Current, Is.SameAs(s_caller));
            Assert.That(m_callers.HasRequestScope, Is.True);
            m_model.VerifyNoMutations();
        }

        [TestCaseSource(nameof(NullUserCases))]
        public async Task CaptureCallerDistinguishesNullUserFromAnonymousPrincipalAsync(
            bool registered, string userKind)
        {
            bool optIn = userKind != "NullWithoutOptIn";
            bool expectedLocal = userKind == "NullWithOptIn";
            McpExecutionOptions options = GrantedOptions(s_local);
            options.LocalCallerId = optIn ? "Local-A" : null;
            BuildProvider(options);
            OpcUaSessionManager manager = await RegisterModelAsync().ConfigureAwait(false);
            RequestContext<CallToolRequestParams> request = CreateRequest(userKind == "AnonymousWithOptIn"
                ? new ClaimsPrincipal(new ClaimsIdentity()) : null);
            CallToolResult expected = Success();
            int calls = 0;
            McpRequestHandler<CallToolRequestParams, CallToolResult> handler = SelectFilter(registered)(
                (forwarded, ct) =>
                {
                    calls++;
                    AssertForwarded(forwarded, request, ct);
                    Assert.That(m_callers.HasRequestScope, Is.True);
                    Assert.That(m_callers.Current, Is.EqualTo(expectedLocal ? s_local : null));
                    if (expectedLocal)
                    {
                        Assert.That(m_callers.Current!.Issuer, Is.EqualTo(McpCallerContext.LocalIssuer));
                        Assert.That(m_callers.Current.IsLocal, Is.True);
                    }
                    return PrivilegedWriteAsync(manager, expected, ct);
                });

            CallToolResult result = await handler(request, m_cancellation.Token).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
            if (expectedLocal)
            {
                Assert.That(result, Is.SameAs(expected));
                AssertAllowedWrite();
            }
            else
            {
                AssertServiceError(result, StatusCodes.BadUserAccessDenied, "BadUserAccessDenied", kRegistrationDenied);
                m_model.VerifyNoMutations();
            }
            AssertNoScope();
            if (optIn)
            {
                Assert.That(manager.OperationPolicy.RequireCaller(), Is.EqualTo(s_local));
            }
            else
            {
                AssertPolicyDenied(() => manager.OperationPolicy.RequireCaller(), kNoCaller);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CaptureCallerAllowsUnprivilegedAnonymousContinuationAsync(bool registered)
        {
            McpExecutionOptions options = GrantedOptions(s_local);
            options.LocalCallerId = "Local-A";
            BuildProvider(options);
            _ = AssertSharedServices();
            RequestContext<CallToolRequestParams> request = CreateRequest(new ClaimsPrincipal(new ClaimsIdentity()));
            CallToolResult expected = Success();
            int calls = 0;
            using IDisposable outer = m_callers.EnterTrustedCaller(s_outer);
            McpRequestHandler<CallToolRequestParams, CallToolResult> handler = SelectFilter(registered)(
                (forwarded, ct) =>
                {
                    calls++;
                    AssertForwarded(forwarded, request, ct);
                    Assert.That(m_callers.Current, Is.Null);
                    Assert.That(m_callers.HasRequestScope, Is.True);
                    return ValueTask.FromResult(expected);
                });

            CallToolResult result = await handler(request, m_cancellation.Token).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(result, Is.SameAs(expected));
            Assert.That(result.IsError, Is.False);
            Assert.That(((TextContentBlock)result.Content[0]).Text, Is.EqualTo("continuation-completed"));
            AssertOuterScope();
            m_model.VerifyNoMutations();
        }

        [TestCaseSource(nameof(ServiceFailureCases))]
        public async Task CaptureCallerTranslatesExactServiceFailureAsync(
            bool registered, StatusCode status, string symbolic, bool derived)
        {
            BuildProvider(GrantedOptions(s_caller));
            OpcUaSessionManager manager = AssertSharedServices();
            RequestContext<CallToolRequestParams> request = CreateRequest(TrustedPrincipal());
            ServiceResultException failure = derived
                ? new ModelRefusalException(status, "Rejected request at the model boundary.")
                : new ServiceResultException(status, "Rejected request at the model boundary.");
            int calls = 0;
            McpRequestHandler<CallToolRequestParams, CallToolResult> handler = SelectFilter(registered)(
                (forwarded, ct) =>
                {
                    calls++;
                    AssertForwarded(forwarded, request, ct);
                    Assert.That(manager.OperationPolicy.RequireCaller(), Is.EqualTo(s_caller));
                    Assert.That(m_callers.HasRequestScope, Is.True);
                    return ValueTask.FromException<CallToolResult>(failure);
                });

            CallToolResult result = await handler(request, m_cancellation.Token).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
            AssertServiceError(result, status, symbolic, "Rejected request at the model boundary.");
            AssertNoScope();
            m_model.VerifyNoMutations();
        }

        [TestCaseSource(nameof(ContinuationOutcomeCases))]
        public async Task CaptureCallerRestoresScopeAfterEveryContinuationOutcomeAsync(
            bool registered, string outcome)
        {
            McpExecutionOptions options = GrantedOptions(s_caller, s_outer, s_local);
            options.LocalCallerId = "Local-A";
            BuildProvider(options);
            OpcUaSessionManager manager = AssertSharedServices();
            bool anonymous = outcome == "AnonymousSuccess";
            RequestContext<CallToolRequestParams> request = CreateRequest(anonymous
                ? new ClaimsPrincipal(new ClaimsIdentity()) : TrustedPrincipal());
            CallToolResult expected = Success();
            var serviceFailure = new ServiceResultException(StatusCodes.BadNotSupported, "Continuation refused.");
            var invalidFailure = new InvalidOperationException("Continuation bug.");
            var cancelledFailure = new OperationCanceledException(
                "Continuation cancelled.", null, m_cancellation.Token);
            int calls = 0;
            using (m_callers.EnterTrustedCaller(s_outer))
            {
                AssertOuterScope();
                McpRequestHandler<CallToolRequestParams, CallToolResult> handler = SelectFilter(registered)(
                    (forwarded, ct) =>
                    {
                        calls++;
                        AssertForwarded(forwarded, request, ct);
                        Assert.That(m_callers.Current, Is.EqualTo(anonymous ? null : s_caller));
                        Assert.That(m_callers.HasRequestScope, Is.True);
                        if (anonymous)
                        {
                            AssertPolicyDenied(() => manager.OperationPolicy.RequireCaller(), kNoCaller);
                        }
                        else
                        {
                            Assert.That(manager.OperationPolicy.RequireCaller(), Is.EqualTo(s_caller));
                        }
                        return outcome switch
                        {
                            "ServiceFailure" => ValueTask.FromException<CallToolResult>(serviceFailure),
                            "InvalidOperation" => ValueTask.FromException<CallToolResult>(invalidFailure),
                            "Cancellation" => ValueTask.FromException<CallToolResult>(cancelledFailure),
                            _ => ValueTask.FromResult(expected)
                        };
                    });

                if (outcome == "InvalidOperation")
                {
                    InvalidOperationException exception = await CaptureAsync<InvalidOperationException>(
                        async () => _ = await handler(request, m_cancellation.Token).ConfigureAwait(false))
                        .ConfigureAwait(false);
                    Assert.That(exception, Is.SameAs(invalidFailure));
                }
                else if (outcome == "Cancellation")
                {
                    OperationCanceledException exception = await CaptureAsync<OperationCanceledException>(
                        async () => _ = await handler(request, m_cancellation.Token).ConfigureAwait(false))
                        .ConfigureAwait(false);
                    Assert.That(exception, Is.SameAs(cancelledFailure));
                    Assert.That(exception.CancellationToken, Is.EqualTo(m_cancellation.Token));
                }
                else
                {
                    CallToolResult result = await handler(request, m_cancellation.Token).ConfigureAwait(false);
                    if (outcome == "ServiceFailure")
                    {
                        AssertServiceError(result, StatusCodes.BadNotSupported, "BadNotSupported",
                            "Continuation refused.");
                    }
                    else
                    {
                        Assert.That(result, Is.SameAs(expected));
                    }
                }
                Assert.That(calls, Is.EqualTo(1));
                AssertOuterScope();
                Assert.That(manager.OperationPolicy.RequireCaller(), Is.SameAs(s_outer));
            }
            AssertNoScope();
            Assert.That(manager.OperationPolicy.RequireCaller(), Is.EqualTo(s_local));
            m_model.VerifyNoMutations();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CaptureCallerRevokesCompletedRequestInCapturedChildAsync(bool registered)
        {
            McpExecutionOptions options = GrantedOptions(s_caller, s_outer, s_local);
            options.LocalCallerId = "Local-A";
            BuildProvider(options);
            OpcUaSessionManager manager = AssertSharedServices();
            RequestContext<CallToolRequestParams> request = CreateRequest(TrustedPrincipal());
            CallToolResult expected = Success();
            var captured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? child = null;
            int calls = 0;
            using (m_callers.EnterTrustedCaller(s_outer))
            {
                McpRequestHandler<CallToolRequestParams, CallToolResult> handler = SelectFilter(registered)(
                    async (forwarded, ct) =>
                    {
                        calls++;
                        AssertForwarded(forwarded, request, ct);
                        Assert.That(m_callers.Current, Is.EqualTo(s_caller));
                        child = Task.Run(async () =>
                        {
                            try
                            {
                                Assert.That(m_callers.Current, Is.EqualTo(s_caller));
                                Assert.That(m_callers.HasRequestScope, Is.True);
                                Assert.That(manager.OperationPolicy.RequireSession(
                                    McpCapability.IdentityAdministration, kSession), Is.EqualTo(s_caller));
                            }
                            finally
                            {
                                // Even an assertion failure before the wait must release the filter's handshake.
                                captured.TrySetResult(true);
                            }
                            await release.Task.ConfigureAwait(false);
                            Assert.That(m_callers.Current, Is.Null);
                            Assert.That(m_callers.HasRequestScope, Is.True);
                            AssertPolicyDenied(() => manager.OperationPolicy.RequireCaller(), kNoCaller);
                            AssertPolicyDenied(() => manager.OperationPolicy.RequireSession(
                                McpCapability.IdentityAdministration, kSession), kNoCaller);
                        }, CancellationToken.None);
                        await captured.Task.ConfigureAwait(false);
                        return expected;
                    });
                try
                {
                    CallToolResult result = await handler(request, m_cancellation.Token).ConfigureAwait(false);
                    Assert.That(result, Is.SameAs(expected));
                    Assert.That(calls, Is.EqualTo(1));
                    AssertOuterScope();
                    Assert.That(manager.OperationPolicy.RequireCaller(), Is.SameAs(s_outer));
                }
                finally
                {
                    // Completion/disposal happens before the child is allowed to try the captured request authority.
                    release.TrySetResult(true);
                    if (child is not null)
                    {
                        await child.ConfigureAwait(false);
                    }
                }
            }
            AssertNoScope();
            Assert.That(manager.OperationPolicy.RequireCaller(), Is.EqualTo(s_local));
            m_model.VerifyNoMutations();
        }

        [TestCaseSource(nameof(MissingServiceCases))]
        public async Task CaptureCallerRejectsMissingServicesOrRegistrationsAsync(bool registered, string missing)
        {
            BuildProvider(GrantedOptions(s_caller));
            _ = AssertSharedServices();
            var services = new ServiceCollection();
            if (missing == "CallerContext")
            {
                services.AddSingleton(new McpExecutionOptions());
            }
            if (missing == "ExecutionOptions")
            {
                services.AddSingleton(m_callers);
            }
            await using ServiceProvider incomplete = services.BuildServiceProvider();
            RequestContext<CallToolRequestParams> request = CreateRequest(TrustedPrincipal());
            request.Services = missing == "Services" ? null : incomplete;
            string expected = missing switch
            {
                "Services" => "The MCP request has no service provider.",
                "CallerContext" => "No service for type 'Opc.Ua.Mcp.McpCallerContext' has been registered.",
                "ExecutionOptions" => "No service for type 'Opc.Ua.Mcp.McpExecutionOptions' has been registered.",
                _ => throw new ArgumentOutOfRangeException(nameof(missing))
            };
            int calls = 0;
            using IDisposable outer = m_callers.EnterTrustedCaller(s_outer);
            McpRequestHandler<CallToolRequestParams, CallToolResult> handler = SelectFilter(registered)((_, _) =>
            {
                calls++;
                return ValueTask.FromResult(Success());
            });

            InvalidOperationException exception = await CaptureAsync<InvalidOperationException>(
                async () => _ = await handler(request, m_cancellation.Token).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.That(exception, Is.TypeOf<InvalidOperationException>());
            Assert.That(exception.Message, Is.EqualTo(expected));
            Assert.That(calls, Is.Zero);
            AssertOuterScope();
            m_model.VerifyNoMutations();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CaptureCallerPreCancellationDoesNotInvokeNextAsync(bool registered)
        {
            BuildProvider(GrantedOptions(s_caller));
            _ = AssertSharedServices();
            RequestContext<CallToolRequestParams> request = CreateRequest(TrustedPrincipal());
            int calls = 0;
            using IDisposable outer = m_callers.EnterTrustedCaller(s_outer);
            McpRequestHandler<CallToolRequestParams, CallToolResult> handler = SelectFilter(registered)((_, _) =>
            {
                calls++;
                return ValueTask.FromResult(Success());
            });
            await m_cancellation.CancelAsync().ConfigureAwait(false);
            OperationCanceledException? failure = null;

            try
            {
                _ = await handler(request, m_cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                failure = exception;
            }

            // G03: forwarding a cancelled token to an unaware continuation is not a cancellation barrier.
            using (Assert.EnterMultipleScope())
            {
                Assert.That(calls, Is.Zero);
                Assert.That(failure, Is.TypeOf<OperationCanceledException>());
                if (failure is not null)
                {
                    Assert.That(failure.CancellationToken, Is.EqualTo(m_cancellation.Token));
                }
                AssertOuterScope();
                m_model.VerifyNoMutations();
            }
        }

        [TestCase("CoreOnly")]
        [TestCase("ProfileSetOnly")]
        [TestCase("ConnectionOnly")]
        [TestCase("RepeatedIdentity")]
        [TestCase("CoreThenIdentity")]
        [TestCase("IdentityThenCore")]
        [TestCase("FiltersThenCoreThenIdentity")]
        [TestCase("IdentityThenCoreThenFilters")]
        [TestCase("RepeatedBroaderFilters")]
        public async Task CallerFilterIsRegisteredExactlyOnceAcrossCompositionsAsync(string composition)
        {
            var services = new ServiceCollection();
            services.AddSingleton(m_callers);
            services.AddSingleton<IMcpFixedSessionContract>(new FixedMockSessionContract(m_model.Session.Object));
            services.AddOpcUaMcpCore(new OpcUaMcpOptions { Execution = GrantedOptions(s_caller) });
            IMcpServerBuilder builder = AddServer(services);
            switch (composition)
            {
                case "CoreOnly":
                    builder.WithOpcUaCoreTools(McpToolProfile.Full);
                    break;
                case "ProfileSetOnly":
                    builder.WithOpcUaCoreTools(McpToolProfileSet.Empty);
                    break;
                case "ConnectionOnly":
                    builder.WithOpcUaConnectionTools();
                    break;
                case "RepeatedIdentity":
                    builder.WithOpcUaMcpCallerIdentity().WithOpcUaMcpCallerIdentity();
                    break;
                case "CoreThenIdentity":
                    builder.WithOpcUaCoreTools(McpToolProfile.Full).WithOpcUaMcpCallerIdentity();
                    break;
                case "IdentityThenCore":
                    builder.WithOpcUaMcpCallerIdentity().WithOpcUaCoreTools(McpToolProfile.Full);
                    break;
                case "FiltersThenCoreThenIdentity":
                    builder.WithOpcUaMcpFilters().WithOpcUaCoreTools(McpToolProfile.Full).WithOpcUaMcpCallerIdentity();
                    break;
                case "IdentityThenCoreThenFilters":
                    builder.WithOpcUaMcpCallerIdentity().WithOpcUaCoreTools(McpToolProfile.Full).WithOpcUaMcpFilters();
                    break;
                case "RepeatedBroaderFilters":
                    builder.WithOpcUaMcpFilters().WithOpcUaMcpFilters().WithOpcUaMcpCallerIdentity();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(composition));
            }
            m_provider = services.BuildServiceProvider();
            OpcUaSessionManager manager = await RegisterModelAsync().ConfigureAwait(false);
            IList<McpRequestFilter<CallToolRequestParams, CallToolResult>> filters =
                m_provider.GetRequiredService<IOptions<McpServerOptions>>().Value.Filters.Request.CallToolFilters;
            McpRequestFilter<CallToolRequestParams, CallToolResult> capture = McpExecutionFilters.CaptureCaller;
            Assert.That(filters.Count(filter => filter == capture), Is.EqualTo(1));
            if (composition.Contains("Filters", StringComparison.Ordinal))
            {
                Assert.That(filters.Any(filter => filter != capture), Is.True);
            }
            // Exercise the actual delegate from options, not a freshly constructed substitute.
            McpRequestFilter<CallToolRequestParams, CallToolResult> installed =
                filters.Single(filter => filter == capture);
            RequestContext<CallToolRequestParams> request = CreateRequest(TrustedPrincipal());
            CallToolResult expected = Success();
            int calls = 0;
            McpRequestHandler<CallToolRequestParams, CallToolResult> handler = installed((forwarded, ct) =>
            {
                calls++;
                AssertForwarded(forwarded, request, ct);
                Assert.That(m_callers.Current, Is.EqualTo(s_caller));
                Assert.That(m_callers.HasRequestScope, Is.True);
                Assert.That(manager.OperationPolicy.RequireSession(McpCapability.IdentityAdministration, kSession),
                    Is.EqualTo(s_caller));
                return PrivilegedWriteAsync(manager, expected, ct);
            });

            CallToolResult result = await handler(request, m_cancellation.Token).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(result, Is.SameAs(expected));
            AssertAllowedWrite();
            AssertNoScope();
            Assert.That(filters.Count(filter => filter == capture), Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CaptureCallerTranslatesAmbiguousIdentityBeforeContinuationAsync(bool registered)
        {
            BuildProvider(GrantedOptions(s_caller, s_outer));
            OpcUaSessionManager manager = await RegisterModelAsync().ConfigureAwait(false);
            var user = new ClaimsPrincipal(
            [
                new ClaimsIdentity(
                    [new Claim("sub", "Caller-A", ClaimValueTypes.String, "urn:test:issuer-a")], "FirstTransport"),
                new ClaimsIdentity(
                    [new Claim("sub", "Caller-A", ClaimValueTypes.String, "urn:test:other")], "SecondTransport")
            ]);
            RequestContext<CallToolRequestParams> request = CreateRequest(user, claimGrantedOwner: true);
            int calls = 0;
            using IDisposable outer = m_callers.EnterTrustedCaller(s_outer);
            McpRequestHandler<CallToolRequestParams, CallToolResult> handler = SelectFilter(registered)(
                (forwarded, ct) =>
                {
                    calls++;
                    AssertForwarded(forwarded, request, ct);
                    return PrivilegedWriteAsync(manager, Success(), ct);
                });

            CallToolResult result = await handler(request, m_cancellation.Token).ConfigureAwait(false);

            Assert.That(calls, Is.Zero);
            AssertServiceError(result, StatusCodes.BadUserAccessDenied, "BadUserAccessDenied",
                "The request has ambiguous authenticated identities.");
            AssertOuterScope();
            Assert.That(manager.OperationPolicy.RequireCaller(), Is.SameAs(s_outer));
            Assert.That(m_model.Browses, Is.Empty);
            Assert.That(m_model.CacheQueries, Is.Empty);
            m_model.VerifyNoMutations();
        }

        private static IEnumerable<TestCaseData> RoleClaimCases()
        {
            foreach (bool registered in new[] { false, true })
            {
                foreach (string claims in new[]
                {
                    "RolesOnly", "DisplayNameOnly", "AnonymousOwner", "AuthenticatedAdministrator"
                })
                {
                    yield return new TestCaseData(registered, claims);
                }
            }
        }

        private static IEnumerable<TestCaseData> NullUserCases()
        {
            foreach (bool registered in new[] { false, true })
            {
                yield return new TestCaseData(registered, "NullWithOptIn");
                yield return new TestCaseData(registered, "NullWithoutOptIn");
                yield return new TestCaseData(registered, "AnonymousWithOptIn");
            }
        }

        private static IEnumerable<TestCaseData> ServiceFailureCases()
        {
            foreach (bool registered in new[] { false, true })
            {
                yield return new TestCaseData(
                    registered, StatusCodes.BadUserAccessDenied, "BadUserAccessDenied", false);
                yield return new TestCaseData(registered, StatusCodes.BadNotSupported, "BadNotSupported", false);
                yield return new TestCaseData(
                    registered, StatusCodes.BadSecurityModeInsufficient, "BadSecurityModeInsufficient", false);
                yield return new TestCaseData(
                    registered, StatusCodes.BadEncodingLimitsExceeded, "BadEncodingLimitsExceeded", false);
                yield return new TestCaseData(registered, StatusCodes.BadUserAccessDenied, "BadUserAccessDenied", true);
            }
        }

        private static IEnumerable<TestCaseData> ContinuationOutcomeCases()
        {
            foreach (bool registered in new[] { false, true })
            {
                foreach (string outcome in new[]
                {
                    "Success", "ServiceFailure", "InvalidOperation", "Cancellation", "AnonymousSuccess"
                })
                {
                    yield return new TestCaseData(registered, outcome);
                }
            }
        }

        private static IEnumerable<TestCaseData> MissingServiceCases()
        {
            foreach (bool registered in new[] { false, true })
            {
                yield return new TestCaseData(registered, "Services");
                yield return new TestCaseData(registered, "CallerContext");
                yield return new TestCaseData(registered, "ExecutionOptions");
            }
        }

        private void BuildProvider(McpExecutionOptions options)
        {
            var services = new ServiceCollection();
            services.AddSingleton(m_callers);
            services.AddSingleton<IMcpFixedSessionContract>(new FixedMockSessionContract(m_model.Session.Object));
            services.AddOpcUaMcpCore(new OpcUaMcpOptions { Execution = options });
            AddServer(services).WithOpcUaMcpCallerIdentity();
            m_provider = services.BuildServiceProvider();
        }

        private static IMcpServerBuilder AddServer(IServiceCollection services)
        {
            return services.AddMcpServer().WithStreamServerTransport(Stream.Null, Stream.Null);
        }

        private OpcUaSessionManager AssertSharedServices()
        {
            Assert.That(m_provider, Is.Not.Null);
            Assert.That(m_provider!.GetRequiredService<McpCallerContext>(), Is.SameAs(m_callers));
            OpcUaSessionManager manager = m_provider.GetRequiredService<OpcUaSessionManager>();
            Assert.That(manager.OperationPolicy, Is.SameAs(m_provider.GetRequiredService<IMcpOperationPolicy>()));
            return manager;
        }

        private async Task<OpcUaSessionManager> RegisterModelAsync()
        {
            OpcUaSessionManager manager = AssertSharedServices();
            _ = await manager.RegisterExistingSessionAsync(
                kSession, m_model.Session.Object, "UserName", m_cancellation.Token).ConfigureAwait(false);
            Assert.That(manager.GetSessionInfoOrThrow().Name, Is.EqualTo("Prod-A"));
            Assert.That(manager.GetSessionInfoOrThrow().OwnsSession, Is.False);
            Assert.That(manager.GetSessionOrThrow(), Is.SameAs(m_model.Session.Object));
            return manager;
        }

        private RequestContext<CallToolRequestParams> CreateRequest(
            ClaimsPrincipal? user, bool claimGrantedOwner = false)
        {
            using var arguments = JsonDocument.Parse(claimGrantedOwner
                ? /*lang=json,strict*/
                """
                {"owner":"Caller-A","issuer":"urn:test:issuer-a","role":"Administrator","callerId":"Local-A"}
                """
                : /*lang=json,strict*/
                """
                {"owner":"Attacker","issuer":"urn:untrusted","role":"Administrator","callerId":"Local-A"}
                """);
            return new RequestContext<CallToolRequestParams>(
                m_provider!.GetRequiredService<McpServer>(),
                new JsonRpcRequest { Method = "tools/call", Id = new RequestId("request-41") },
                new CallToolRequestParams
                {
                    Name = "WriteValue",
                    Arguments = arguments.RootElement.EnumerateObject()
                        .ToDictionary(property => property.Name, property => property.Value.Clone())
                })
            {
                Services = m_provider,
                User = user
            };
        }

        private McpRequestFilter<CallToolRequestParams, CallToolResult> SelectFilter(bool registered)
        {
            return registered ? RegisteredFilter() : McpExecutionFilters.CaptureCaller;
        }

        private McpRequestFilter<CallToolRequestParams, CallToolResult> RegisteredFilter()
        {
            McpRequestFilter<CallToolRequestParams, CallToolResult> expected = McpExecutionFilters.CaptureCaller;
            McpRequestFilter<CallToolRequestParams, CallToolResult>[] matches = [.. m_provider!
                .GetRequiredService<IOptions<McpServerOptions>>().Value.Filters.Request.CallToolFilters
                .Where(filter => filter == expected)];
            Assert.That(matches, Has.Length.EqualTo(1));
            return matches[0];
        }

        private static ClaimsPrincipal TrustedPrincipal(
            string claimType = ClaimTypes.NameIdentifier,
            string subject = "Caller-A",
            string issuer = "urn:test:issuer-a")
        {
            return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(claimType, subject, ClaimValueTypes.String, issuer),
                new Claim(ClaimTypes.Role, "Administrator"),
                new Claim(ClaimTypes.Name, "A misleading display name")
            ], "TrustedTransport"));
        }

        private static McpExecutionOptions GrantedOptions(params McpCallerIdentity[] callers)
        {
            return new McpExecutionOptions
            {
                EnabledCapabilities = [McpCapability.IdentityAdministration],
                CallerGrants = callers.Select(caller => new McpCallerGrant
                {
                    Caller = caller,
                    Capabilities = [McpCapability.IdentityAdministration],
                    SessionNames = [kSession]
                }).ToArray()
            };
        }

        private static async ValueTask<CallToolResult> PrivilegedWriteAsync(
            OpcUaSessionManager manager, CallToolResult result, CancellationToken token)
        {
            OpcUaSessionManager.SessionInfo session = manager.GetSessionInfoOrThrow();
            ArrayOf<WriteValue> writes =
            [
                new WriteValue
                {
                    NodeId = s_target,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                }
            ];
            await manager.UaOperationGuard.AuthorizeWritesAsync(session, writes, token).ConfigureAwait(false);
            WriteResponse response = await session.Session.WriteAsync(null, writes, token).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].Code, Is.EqualTo(StatusCodes.Good.Code));
            return result;
        }

        private void AssertAllowedWrite()
        {
            Assert.That(m_model.Mutations, Is.EqualTo(s_expectedMutations));
            Assert.That(m_write, Is.Not.Null);
            WriteRequest wire = m_write!;
            Assert.That(wire.Header, Is.Null);
            Assert.That(wire.Token, Is.EqualTo(m_cancellation.Token));
            Assert.That(wire.Token.CanBeCanceled, Is.True);
            Assert.That(wire.Items.Count, Is.EqualTo(1));
            Assert.That(wire.Items[0].NodeId, Is.EqualTo(new NodeId("RoleValue", 1)));
            Assert.That(wire.Items[0].AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(wire.Items[0].Value.StatusCode.Code, Is.EqualTo(StatusCodes.Good.Code));
            Variant value = wire.Items[0].Value.WrappedValue;
            Assert.That(value.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(value.TryGetValue(out int number), Is.True);
            Assert.That(number, Is.EqualTo(42));
            m_model.Session.Verify(session => session.WriteAsync(null, wire.Items, m_cancellation.Token), Times.Once);
            Assert.That(m_model.Browses.Select(query => query.Request.NodeId), Does.Contain(s_target));
            Assert.That(m_model.Browses.All(query => query.Token == m_cancellation.Token), Is.True);
        }

        private void AssertForwarded(
            RequestContext<CallToolRequestParams> forwarded, RequestContext<CallToolRequestParams> original,
            CancellationToken token)
        {
            Assert.That(forwarded, Is.SameAs(original));
            Assert.That(forwarded.Services, Is.SameAs(m_provider));
            IServiceProvider requestServices = forwarded.Services ??
                throw new InvalidOperationException("The forwarded request has no services.");
            Assert.That(requestServices.GetRequiredService<McpCallerContext>(), Is.SameAs(m_callers));
            Assert.That(requestServices.GetRequiredService<OpcUaSessionManager>().OperationPolicy,
                Is.SameAs(requestServices.GetRequiredService<IMcpOperationPolicy>()));
            Assert.That(token, Is.EqualTo(m_cancellation.Token));
            Assert.That(token.CanBeCanceled, Is.True);
        }

        private void AssertNoScope()
        {
            Assert.That(m_callers.Current, Is.Null);
            Assert.That(m_callers.HasRequestScope, Is.False);
        }

        private void AssertOuterScope()
        {
            Assert.That(m_callers.Current, Is.SameAs(s_outer));
            Assert.That(m_callers.HasRequestScope, Is.True);
        }

        private static CallToolResult Success()
        {
            return new CallToolResult
            {
                IsError = false,
                Content = [new TextContentBlock { Text = "continuation-completed" }]
            };
        }

        private static void AssertServiceError(
            CallToolResult result, StatusCode status, string symbolic, string message)
        {
            Assert.That(result.IsError, Is.True);
            Assert.That(result.StructuredContent.HasValue, Is.True);
            JsonElement structured = result.StructuredContent!.Value;
            AssertErrorPayload(structured, status, symbolic, message);
            Assert.That(result.Content, Has.Count.EqualTo(1));
            Assert.That(result.Content[0], Is.TypeOf<TextContentBlock>());
            using var text = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text);
            AssertErrorPayload(text.RootElement, status, symbolic, message);
            Assert.That(text.RootElement.GetRawText(), Is.EqualTo(structured.GetRawText()));
        }

        private static void AssertErrorPayload(JsonElement payload, StatusCode status, string symbolic, string message)
        {
            Assert.That(payload.EnumerateObject().Select(property => property.Name).ToArray(),
                Is.EquivalentTo(s_errorProperties));
            Assert.That(payload.GetProperty("error").ValueKind, Is.EqualTo(JsonValueKind.True));
            Assert.That(payload.GetProperty("statusCode").GetString(), Is.EqualTo(symbolic));
            Assert.That(payload.GetProperty("statusCodeValue").ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(payload.GetProperty("statusCodeValue").GetUInt32(), Is.EqualTo(status.Code));
            Assert.That(payload.GetProperty("message").GetString(), Is.EqualTo(message));
        }

        private static void AssertPolicyDenied(Action action, string message)
        {
            ServiceResultException? caught = null;
            try
            {
                action();
            }
            catch (ServiceResultException exception)
            {
                caught = exception;
            }
            Assert.That(caught, Is.Not.Null);
            Assert.That(caught!.StatusCode.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(caught.Message, Is.EqualTo(message));
        }

        private static async Task<TException> CaptureAsync<TException>(Func<Task> action)
            where TException : Exception
        {
            TException? caught = null;
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (TException exception)
            {
                caught = exception;
            }
            Assert.That(caught, Is.Not.Null, $"Expected the exact {typeof(TException).Name} failure.");
            return caught!;
        }

        private sealed record WriteRequest(RequestHeader? Header, ArrayOf<WriteValue> Items, CancellationToken Token);

        /// <summary>
        /// Declares only this fixture's fixed mock transport; it grants no authority or generation atomicity.
        /// </summary>
        private sealed class FixedMockSessionContract(ISession session) : IMcpFixedSessionContract
        {
            public bool IsFixed(OpcUaSessionManager.SessionInfo registration)
            {
                return registration is not null && ReferenceEquals(registration.Session, session);
            }
        }

        private sealed class ModelRefusalException : ServiceResultException
        {
            public ModelRefusalException()
            {
            }

            public ModelRefusalException(string message)
                : base(message)
            {
            }

            public ModelRefusalException(string message, Exception innerException)
                : base(message, innerException)
            {
            }

            public ModelRefusalException(StatusCode code, string message)
                : base(code, message)
            {
            }

            public ModelRefusalException(Exception e, StatusCode defaultCode)
                : base(e, defaultCode)
            {
            }

            public ModelRefusalException(StatusCode statusCode)
                : base(statusCode)
            {
            }

            public ModelRefusalException(StatusCode statusCode, LocalizedText message)
                : base(statusCode, message)
            {
            }

            public ModelRefusalException(StatusCode statusCode, Exception e)
                : base(statusCode, e)
            {
            }

            public ModelRefusalException(StatusCode statusCode, string message, Exception e)
                : base(statusCode, message, e)
            {
            }

            public ModelRefusalException(StatusCode statusCode, LocalizedText message, Exception e)
                : base(statusCode, message, e)
            {
            }

            public ModelRefusalException(ServiceResult status)
                : base(status)
            {
            }
        }

        private const string kSession = "Prod-A";
        private const string kNoCaller = "No trusted MCP caller identity is available.";
        private const string kGrantDenied = "The caller has no grant for the IdentityAdministration capability.";
        private const string kRegistrationDenied =
            "The caller cannot replace, remove or borrow this UA session registration.";

        private const string kSessionDenied =
            "The caller has no privileged-operation grant for the selected UA session.";

        private static readonly string[] s_expectedSessions = ["Prod-A"];
        private static readonly string[] s_expectedMutations = ["Write"];
        private static readonly string[] s_errorProperties = ["error", "statusCode", "statusCodeValue", "message"];
        private static readonly NodeId s_target = new("RoleValue", 1);
        private static readonly McpCallerIdentity s_caller = new("Caller-A", "urn:test:issuer-a");
        private static readonly McpCallerIdentity s_outer = new("Outer-A", "urn:test:outer");
        private static readonly McpCallerIdentity s_local = new("Local-A", McpCallerContext.LocalIssuer, IsLocal: true);
        private McpCallerContext m_callers = null!;
        private McpAuthorizationModel m_model = null!;
        private CancellationTokenSource m_cancellation = null!;
        private ServiceProvider? m_provider;
        private WriteRequest? m_write;
    }
}
#endif
