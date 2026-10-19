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
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Tools.Tests.McpCompanion;
using Opc.Ua.Tools.Tests.McpCompanion.Workflows;

namespace Opc.Ua.Tools.Tests.McpAuthorization
{
    /// <summary>
    /// Exercises completed cross-caller registration changes and the real password workflow over recorded UA calls.
    /// </summary>
    [TestFixture]
    public sealed class SecurityF17SessionAuthorityTests
    {
        /// <summary>
        /// Another caller cannot replace, disconnect or borrow a host's or another caller's connection.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The approved test endpoint has no URL.
        /// </exception>
        [TestCase(false, "Prod-A")]
        [TestCase(false, "pRoD-a")]
        [TestCase(true, "Prod-A")]
        [TestCase(true, "pRoD-a")]
        [Category("SecurityRegression")]
        public async Task SecurityF17OtherCallerCannotRetargetGrantedSession(bool hostProvisioned, string name)
        {
            await using SecurityF17SessionContext fixture = await SecurityF17SessionContext.CreateAsync()
                .ConfigureAwait(false);
            if (hostProvisioned)
            {
                await fixture.RegisterHostAsync(fixture.Approved).ConfigureAwait(false);
            }
            else
            {
                using IDisposable owner = fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Victim);
                fixture.Connector.Next = fixture.Approved;
                _ = await ConnectionTools.ConnectWithOptionsAsync(fixture.Manager,
                    fixture.Approved.Session.Object.ConfiguredEndpoint.Description.EndpointUrl ??
                    throw new InvalidOperationException("The approved test endpoint has no URL."),
                    name: "Prod-A").ConfigureAwait(false);
            }
            OpcUaSessionManager.SessionInfo original = fixture.Manager.GetSessionInfoOrThrow("Prod-A");
            int connections = fixture.Connector.Calls;
            using (fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Attacker))
            {
                string denied = await ConnectionTools.ConnectWithOptionsAsync(
                    fixture.Manager, "opc.tcp://attacker.invalid:4840", autoAcceptCerts: true, name: name)
                    .ConfigureAwait(false);
                using var document = JsonDocument.Parse(denied);
                Assert.That(document.RootElement.GetProperty("statusCode").GetString(),
                    Is.EqualTo("BadUserAccessDenied"));
                foreach (string? selected in new string?[] { name, null })
                {
                    ServiceResultException? disconnect = Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await fixture.Manager.DisconnectAsync(selected).ConfigureAwait(false));
                    Assert.That(disconnect!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                }
                ServiceResultException? provision = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await fixture.Manager.RegisterExistingSessionAsync(
                        name, fixture.Evil.Session.Object, "Anonymous").ConfigureAwait(false));
                Assert.That(provision!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(() => fixture.Manager.GetSessionOrThrow(name),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadUserAccessDenied));
                CallToolResult later = await fixture.Users.ChangePasswordAsync("alias:old", "alias:new", name)
                    .ConfigureAwait(false);
                RemoteModelTestSession.Error(later, StatusCodes.BadUserAccessDenied);
                Assert.That(fixture.Manager.GetAllSessions(), Is.Empty);
            }
            if (hostProvisioned)
            {
                using IDisposable granted = fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Victim);
                Assert.That(fixture.Manager.GetSessionOrThrow(name), Is.SameAs(fixture.Approved.Session.Object));
                ServiceResultException? disconnect = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await fixture.Manager.DisconnectAsync(name).ConfigureAwait(false));
                Assert.That(disconnect!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied),
                    "A use grant must not become authority to remove the host's registration.");
                string replace = await ConnectionTools.ConnectWithOptionsAsync(
                    fixture.Manager, "opc.tcp://attacker.invalid:4840", autoAcceptCerts: true, name: name)
                    .ConfigureAwait(false);
                using var denied = JsonDocument.Parse(replace);
                Assert.That(denied.RootElement.GetProperty("statusCode").GetString(),
                    Is.EqualTo("BadUserAccessDenied"));
            }
            Assert.That(fixture.Manager.GetSessionInfoOrThrow("Prod-A"), Is.SameAs(original));
            Assert.That(fixture.Connector.Calls, Is.EqualTo(connections), "Denial precedes endpoint connection.");
            Assert.That(fixture.Manager.Configuration, Is.Null, "Denial must not initialize host PKI.");
            Assert.That(fixture.SecretReads, Is.Zero);
            Assert.That(fixture.Approved.Calls, Is.Empty);
            Assert.That(fixture.Evil.Calls, Is.Empty);
            fixture.Approved.Session.Verify(session => session.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
            fixture.Approved.Session.Verify(session => session.Dispose(), Times.Never);
        }

        /// <summary>
        /// Reuse occurs to completion before the victim's later workflow, not during an in-flight dispatch.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF17CompletedNameReuseCannotInheritVictimSecrets()
        {
            await using SecurityF17SessionContext fixture = await SecurityF17SessionContext.CreateAsync()
                .ConfigureAwait(false);
            await fixture.RegisterHostAsync(fixture.Approved).ConfigureAwait(false);
            string original = fixture.Manager.GetSessionInfoOrThrow().RegistrationId;
            using (fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Victim))
            {
                fixture.AssertSuccessfulPasswords(await fixture.Users.ChangePasswordAsync(
                    "alias:old", "alias:new", "Prod-A").ConfigureAwait(false));
            }
            fixture.ResetObservations();
            _ = await fixture.Manager.DisconnectAsync("Prod-A").ConfigureAwait(false);
            fixture.Connector.Next = fixture.Evil;
            using (fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Attacker))
            {
                string completed = await ConnectionTools.ConnectWithOptionsAsync(
                    fixture.Manager, "opc.tcp://attacker.invalid:4840", autoAcceptCerts: true, name: "Prod-A")
                    .ConfigureAwait(false);
                Assert.That(completed, Does.StartWith("Connected to "));
                Assert.That(fixture.Manager.GetSessionInfoOrThrow().RegistrationId, Is.Not.EqualTo(original));
                fixture.Evil.Values[(fixture.Evil.Id("ordinary"), Attributes.Value)] = new DataValue(Variant.From(17));
                ReadResponse ordinary = await fixture.Manager.GetSessionOrThrow().ReadAsync(
                    null, 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = fixture.Evil.Id("ordinary"), AttributeId = Attributes.Value }],
                    CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(ordinary.Results[0].WrappedValue, Is.EqualTo(Variant.From(17)));
            }
            using (fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Victim))
            {
                CallToolResult later = await fixture.Users.ChangePasswordAsync(
                    "alias:old", "alias:new", "prod-a").ConfigureAwait(false);
                RemoteModelTestSession.Error(later, StatusCodes.BadUserAccessDenied);
                Assert.That(RemoteModelTestSession.Payload(later).GetRawText(), Does.Not.Contain("test-only"));
            }
            Assert.That(fixture.SecretReads, Is.Zero);
            Assert.That(fixture.Approved.Calls, Is.Empty);
            Assert.That(fixture.Evil.Calls, Is.Empty);
            Assert.That(fixture.Evil.Reads, Has.Count.EqualTo(1), "Non-secret owner access remains usable.");
        }

        /// <summary>
        /// Caller-created connections cannot use a host's resource aliases even with their own capability/name grants.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF17CallerCreatedTargetCannotConsumeProtectedAliases(bool autoAccept)
        {
            await using SecurityF17SessionContext fixture = await SecurityF17SessionContext.CreateAsync()
                .ConfigureAwait(false);
            fixture.Connector.Next = fixture.Evil;
            using IDisposable caller = fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Victim);
            _ = await ConnectionTools.ConnectWithOptionsAsync(
                fixture.Manager, "opc.tcp://attacker.invalid:4840", autoAcceptCerts: autoAccept, name: "Prod-A")
                .ConfigureAwait(false);
            CallToolResult refused = await fixture.Users.ChangePasswordAsync("alias:old", "alias:new", "Prod-A")
                .ConfigureAwait(false);
            RemoteModelTestSession.Error(refused, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.SecretReads, Is.Zero);
            Assert.That(fixture.Evil.Calls, Is.Empty);
            Assert.That(fixture.Manager.GetSessionOrThrow(), Is.SameAs(fixture.Evil.Session.Object));
        }

        /// <summary>
        /// A later explicitly host-approved replacement still cannot inherit an already-bound alias or old view.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF17ReplacementInvalidatesPinnedAliasAndCapturedRegistration(
            bool sameAuthenticatedTarget)
        {
            await using SecurityF17SessionContext fixture = await SecurityF17SessionContext.CreateAsync()
                .ConfigureAwait(false);
            await fixture.RegisterHostAsync(fixture.Approved).ConfigureAwait(false);
            OpcUaSessionManager.SessionInfo old = fixture.Manager.GetSessionInfoOrThrow();
            await using McpMutationSession capture = await McpMutationSession.CaptureAsync(
                fixture.Manager, old, CancellationToken.None).ConfigureAwait(false);
            using (fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Victim))
            {
                fixture.AssertSuccessfulPasswords(await fixture.Users.ChangePasswordAsync(
                    "alias:old", "alias:new", "Prod-A").ConfigureAwait(false));
            }
            fixture.ResetObservations();
            if (sameAuthenticatedTarget)
            {
                fixture.Evil.Session.Object.ConfiguredEndpoint.Description.EndpointUrl =
                    fixture.Approved.Session.Object.ConfiguredEndpoint.Description.EndpointUrl;
            }
            await fixture.RegisterHostAsync(fixture.Evil).ConfigureAwait(false);
            using (fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Victim))
            {
                CallToolResult later = await fixture.Users.ChangePasswordAsync("alias:old", "alias:new", "Prod-A")
                    .ConfigureAwait(false);
                RemoteModelTestSession.Error(later, StatusCodes.BadUserAccessDenied);
                Assert.That(() => McpSecretReferences.GetResourceBinding(capture.Access, ObjectIds.UserManagement),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(() => capture.VerifyBeforeDispatch(CancellationToken.None),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadSessionIdInvalid));
            }
            Assert.That(fixture.SecretReads, Is.Zero);
            Assert.That(fixture.Evil.Calls, Is.Empty);
            Assert.That(fixture.Manager.GetSessionInfoOrThrow().RegistrationId, Is.Not.EqualTo(old.RegistrationId));
        }

        /// <summary>
        /// Changing certificate, endpoint address or channel security cannot preserve secret eligibility.
        /// </summary>
        [TestCase("Certificate")]
        [TestCase("Endpoint")]
        [TestCase("Security")]
        [TestCase("RegistrationEndpoint")]
        [Category("SecurityRegression")]
        public async Task SecurityF17AuthenticatedTargetChangeCannotReuseBinding(string changed)
        {
            await using SecurityF17SessionContext fixture = await SecurityF17SessionContext.CreateAsync()
                .ConfigureAwait(false);
            await fixture.RegisterHostAsync(fixture.Approved).ConfigureAwait(false);
            EndpointDescription endpoint = fixture.Approved.Session.Object.ConfiguredEndpoint.Description;
            switch (changed)
            {
                case "Certificate":
                    endpoint.ServerCertificate = ByteString.From(new byte[] { 4, 5, 6 });
                    break;
                case "Endpoint":
                    endpoint.EndpointUrl = "opc.tcp://replacement.invalid:4840";
                    break;
                case "RegistrationEndpoint":
                    fixture.Manager.GetSessionInfoOrThrow().Endpoint.EndpointUrl =
                        "opc.tcp://replacement.invalid:4840";
                    break;
                default:
                    endpoint.SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss;
                    break;
            }
            using IDisposable caller = fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Victim);
            CallToolResult refused = await fixture.Users.ChangePasswordAsync("alias:old", "alias:new", "Prod-A")
                .ConfigureAwait(false);
            RemoteModelTestSession.Error(refused, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.SecretReads, Is.Zero);
            Assert.That(fixture.Approved.Calls, Is.Empty);
        }

        /// <summary>
        /// A trusted host registration retains legacy alias compatibility and exact secret-safe workflow behavior.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF17ApprovedHostSessionDispatchesOnlyExactPasswords()
        {
            await using SecurityF17SessionContext fixture = await SecurityF17SessionContext.CreateAsync()
                .ConfigureAwait(false);
            await fixture.RegisterHostAsync(fixture.Approved).ConfigureAwait(false);
            OpcUaSessionManager.SessionInfo info = fixture.Manager.GetSessionInfoOrThrow();
            using IDisposable caller = fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Victim);
            fixture.AssertSuccessfulPasswords(await fixture.Users.ChangePasswordAsync(
                "alias:old", "alias:new", "prod-a").ConfigureAwait(false));
            Assert.That(fixture.SecretReads, Is.EqualTo(2));
            Assert.That(fixture.SecretViews, Has.All.Property(nameof(WorkflowTestSecret.Disposed)).EqualTo(true));
            Assert.That(fixture.Evil.Calls, Is.Empty);
            Assert.That(info.WithSession(info.Session).RegistrationId, Is.EqualTo(info.RegistrationId));
            fixture.Approved.Session.Verify(session => session.Dispose(), Times.Never);
        }

        /// <summary>
        /// The real workflow factory must preserve authority when it captures an SDK managed-session generation.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF17WorkflowCapturePreservesApprovedRegistrationBinding()
        {
            await using McpGenericGenerationTestContext fixture =
                await McpGenericGenerationTestContext.CreateAsync().ConfigureAwait(false);
            OpcUaSessionManager.SessionInfo registration = fixture.Manager.GetSessionInfoOrThrow();
            await using IWorkflowSessionScope captured = await new WorkflowSessionFactory()
                .CaptureAsync(registration).ConfigureAwait(false);
            Assert.That(captured.Registration, Is.SameAs(registration));
            Assert.That(captured.Access.RegistrationId, Is.EqualTo(registration.RegistrationId));
            Assert.That(captured.Access.Session, Is.Not.SameAs(registration.Session));
            Assert.That(McpSecretReferences.GetResourceBinding(captured.Access, ObjectIds.UserManagement),
                Is.EqualTo(McpSecretReferences.GetResourceBinding(registration, ObjectIds.UserManagement)));
        }

        /// <summary>
        /// A legacy custom capability policy does not implicitly grant access to a borrowed host session.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF17CustomPolicyMustExplicitlyAuthorizeHostSessionBorrowing()
        {
            await using SecurityF17SessionContext fixture = await SecurityF17SessionContext.CreateAsync()
                .ConfigureAwait(false);
            var services = new ServiceCollection();
            services.AddSingleton(fixture.Callers);
            services.AddOpcUaMcpCore(new OpcUaMcpOptions());
            services.AddSingleton<IMcpOperationPolicy>(new CallerOnlyPolicy());
            await using ServiceProvider provider = services.BuildServiceProvider();
            OpcUaSessionManager manager = provider.GetRequiredService<OpcUaSessionManager>();
            await manager.RegisterExistingSessionAsync("Prod-A", fixture.Approved.Session.Object, "UserName")
                .ConfigureAwait(false);
            Assert.That(manager.GetSessionOrThrow(), Is.SameAs(fixture.Approved.Session.Object),
                "Trusted direct host usage does not require an MCP borrow grant.");
            using IDisposable caller = fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Victim);
            var users = new UserWorkflowTools(manager, new WorkflowOptions(), fixture.Secrets);
            CallToolResult refused = await users.ChangePasswordAsync("alias:old", "alias:new", "Prod-A")
                .ConfigureAwait(false);
            RemoteModelTestSession.Error(refused, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.SecretReads, Is.Zero);
            Assert.That(fixture.Approved.Calls, Is.Empty);
        }

        /// <summary>
        /// Owners may replace their own unapproved connection, but cannot carry its authority across registrations.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF17SameOwnerReplacementRetiresOriginalRegistration()
        {
            await using SecurityF17SessionContext fixture = await SecurityF17SessionContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Victim);
            fixture.Connector.Next = fixture.Approved;
            _ = await ConnectionTools.ConnectWithOptionsAsync(
                fixture.Manager, "opc.tcp://approved.invalid:4840", name: "Prod-A").ConfigureAwait(false);
            OpcUaSessionManager.SessionInfo previous = fixture.Manager.GetSessionInfoOrThrow();
            fixture.Connector.Next = fixture.Evil;
            _ = await ConnectionTools.ConnectWithOptionsAsync(
                fixture.Manager, "opc.tcp://attacker.invalid:4840", autoAcceptCerts: true, name: "pRoD-a")
                .ConfigureAwait(false);
            Assert.That(fixture.Manager.GetSessionInfoOrThrow().RegistrationId,
                Is.Not.EqualTo(previous.RegistrationId));
            fixture.Approved.Session.Verify(session => session.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
            fixture.Approved.Session.Verify(session => session.Dispose(), Times.Once);
            CallToolResult refused = await fixture.Users.ChangePasswordAsync("alias:old", "alias:new", "Prod-A")
                .ConfigureAwait(false);
            RemoteModelTestSession.Error(refused, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.SecretReads, Is.Zero);
            Assert.That(fixture.Evil.Calls, Is.Empty);
        }

        /// <summary>
        /// Merely re-registering the same autoaccepted connection in host code cannot launder its trust provenance.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF17AutoacceptanceCannotBeLaunderedByBorrowedHostRegistration()
        {
            await using SecurityF17SessionContext fixture = await SecurityF17SessionContext.CreateAsync()
                .ConfigureAwait(false);
            fixture.Connector.Next = fixture.Evil;
            _ = await ConnectionTools.ConnectWithOptionsAsync(
                fixture.Manager, "opc.tcp://attacker.invalid:4840", autoAcceptCerts: true, name: "Prod-A")
                .ConfigureAwait(false);
            await fixture.RegisterHostAsync(fixture.Evil).ConfigureAwait(false);
            using IDisposable caller = fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Victim);
            CallToolResult refused = await fixture.Users.ChangePasswordAsync("alias:old", "alias:new", "Prod-A")
                .ConfigureAwait(false);
            RemoteModelTestSession.Error(refused, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.SecretReads, Is.Zero);
            Assert.That(fixture.Evil.Calls, Is.Empty);
            fixture.Evil.Session.Verify(session => session.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
            fixture.Evil.Session.Verify(session => session.Dispose(), Times.Never);
        }

        /// <summary>
        /// Models an existing custom policy implementing the original capability contract, not the new borrow grant.
        /// </summary>
        private sealed class CallerOnlyPolicy : IMcpOperationPolicy
        {
            /// <inheritdoc/>
            public bool IsEnabled(McpCapability capability)
            {
                return true;
            }

            /// <inheritdoc/>
            public McpCallerIdentity Require(McpCapability capability)
            {
                return SecurityF17SessionContext.Victim;
            }

            /// <inheritdoc/>
            public McpCallerIdentity RequireSession(McpCapability capability, string sessionName)
            {
                return SecurityF17SessionContext.Victim;
            }

            /// <inheritdoc/>
            public McpCallerIdentity RequireSecret(McpCapability capability, string alias)
            {
                return SecurityF17SessionContext.Victim;
            }

            /// <inheritdoc/>
            public McpCallerIdentity RequireCaller()
            {
                return SecurityF17SessionContext.Victim;
            }
        }
    }

    /// <summary>
    /// Composes real Core policy/registration and Workflows tools over independent deterministic UA service seams.
    /// </summary>
    internal sealed class SecurityF17SessionContext : IAsyncDisposable
    {
        /// <summary>
        /// Creates a host whose two independent fake servers advertise the same application/model identity.
        /// </summary>
        private SecurityF17SessionContext(
            RemoteModelTestSession approved, RemoteModelTestSession evil, bool shareSessionWithAttacker)
        {
            Approved = approved;
            Evil = evil;
            foreach (RemoteModelTestSession remote in new[] { approved, evil })
            {
                remote.Session.Object.ConfiguredEndpoint.Description.Server.ApplicationUri = "urn:test:approved";
                remote.OnCall = _ => new CallMethodResult();
                remote.Session.Setup(session => session.CloseAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(StatusCodes.Good);
                remote.Session.Setup(session => session.Dispose());
            }
            evil.Session.Object.ConfiguredEndpoint.Description.EndpointUrl = "opc.tcp://attacker.invalid:4840";
            var services = new ServiceCollection();
            services.AddSingleton(Callers);
            services.AddSingleton<IMcpSessionConnector>(Connector);
            services.AddSingleton<IMcpFixedSessionContract>(new FixedContract());
            services.AddOpcUaMcpCore(new OpcUaMcpOptions
            {
                Execution = new McpExecutionOptions
                {
                    EnabledCapabilities =
                        [McpCapability.IdentityAdministration, McpCapability.CertificateAdministration],
                    CallerGrants =
                    [
                        new McpCallerGrant
                        {
                            Caller = Victim,
                            Capabilities =
                                [McpCapability.IdentityAdministration, McpCapability.CertificateAdministration],
                            SessionNames = ["Prod-A"], SecretAliases = ["old", "new"]
                        },
                        new McpCallerGrant
                        {
                            Caller = Attacker,
                            SessionNames = shareSessionWithAttacker ? ["Prod-A"] : []
                        }
                    ]
                }
            });
            Services = services.BuildServiceProvider();
            Manager = Services.GetRequiredService<OpcUaSessionManager>();
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            registry.Setup(value => value.GetAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .Returns((SecretIdentifier identifier, CancellationToken _) =>
                {
                    SecretReads++;
                    var view = new WorkflowTestSecret(Encoding.UTF8.GetBytes(identifier.Name + "-test-only"));
                    SecretViews.Add(view);
                    return ValueTask.FromResult<ISecret?>(view);
                });
            string legacy = "opcua:urn:test:approved#" +
                NodeId.ToExpandedNodeId(ObjectIds.UserManagement, approved.Context.NamespaceUris);
            Secrets = new McpSecretReferences(Manager.OperationPolicy, registry.Object, TimeProvider.System,
                [
                    new McpSecretAlias("old", new SecretIdentifier("old", "security-test"), legacy),
                    new McpSecretAlias("new", new SecretIdentifier("new", "security-test"), legacy)
                ]);
            Users = new UserWorkflowTools(Manager, new WorkflowOptions(), Secrets, new SessionScopes());
        }

        /// <summary>
        /// Gets the granted operator.
        /// </summary>
        internal static McpCallerIdentity Victim { get; } = new("operator", "urn:security-test");

        /// <summary>
        /// Gets a separate authenticated, unprivileged caller.
        /// </summary>
        internal static McpCallerIdentity Attacker { get; } = new("attacker", "urn:security-test");

        /// <summary>
        /// Gets the trusted request context shared by the real policy and manager.
        /// </summary>
        internal McpCallerContext Callers { get; } = new();

        /// <summary>
        /// Gets the DI host.
        /// </summary>
        internal ServiceProvider Services { get; }

        /// <summary>
        /// Gets the approved service transport seam.
        /// </summary>
        internal RemoteModelTestSession Approved { get; }

        /// <summary>
        /// Gets the independent attacker transport seam.
        /// </summary>
        internal RemoteModelTestSession Evil { get; }

        /// <summary>
        /// Gets the actual session manager.
        /// </summary>
        internal OpcUaSessionManager Manager { get; }

        /// <summary>
        /// Gets the actual password workflow.
        /// </summary>
        internal UserWorkflowTools Users { get; }

        /// <summary>
        /// Gets the isolated secret gateway.
        /// </summary>
        internal McpSecretReferences Secrets { get; }

        /// <summary>
        /// Gets the recording connection boundary.
        /// </summary>
        internal RecordingConnector Connector { get; } = new();

        /// <summary>
        /// Gets the number of actual secret-provider reads.
        /// </summary>
        internal int SecretReads { get; private set; }

        /// <summary>
        /// Gets provider views for disposal assertions.
        /// </summary>
        internal List<WorkflowTestSecret> SecretViews { get; } = [];

        /// <summary>
        /// Creates both fake service endpoints without network or application PKI.
        /// </summary>
        internal static async Task<SecurityF17SessionContext> CreateAsync(bool shareSessionWithAttacker = false)
        {
            RemoteModelTestSession approved = await RemoteModelTestSession.CreateAsync(
                includeWot: false, includeRegistry: false).ConfigureAwait(false);
            try
            {
                RemoteModelTestSession evil = await RemoteModelTestSession.CreateAsync(
                    includeWot: false, includeRegistry: false).ConfigureAwait(false);
                return new SecurityF17SessionContext(approved, evil, shareSessionWithAttacker);
            }
            catch
            {
                await approved.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Provisions a borrowed host session outside an untrusted request.
        /// </summary>
        internal Task<string> RegisterHostAsync(RemoteModelTestSession remote)
        {
            return Manager.RegisterExistingSessionAsync("Prod-A", remote.Session.Object, "UserName");
        }

        /// <summary>
        /// Clears observations between an approved operation and a completed replacement attack.
        /// </summary>
        internal void ResetObservations()
        {
            SecretReads = 0;
            Approved.Calls.Clear();
            Evil.Calls.Clear();
        }

        /// <summary>
        /// Checks the real workflow's exact service target/arguments and absence of reflected passwords.
        /// </summary>
        internal void AssertSuccessfulPasswords(CallToolResult result)
        {
            Assert.That(result.IsError, Is.Not.True);
            Assert.That(Approved.Calls, Has.Count.EqualTo(1));
            Approved.AssertCall(ObjectIds.UserManagement, MethodIds.UserManagementType_ChangePassword,
                [Variant.From("old-test-only"), Variant.From("new-test-only")]);
            Assert.That(RemoteModelTestSession.Payload(result).GetRawText(), Does.Not.Contain("test-only"));
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await Secrets.DisposeAsync().ConfigureAwait(false);
            await Services.DisposeAsync().ConfigureAwait(false);
            await Evil.DisposeAsync().ConfigureAwait(false);
            await Approved.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Returns a controlled already-connected service seam only after real Connect authorization.
        /// </summary>
        internal sealed class RecordingConnector : IMcpSessionConnector
        {
            /// <summary>
            /// Gets or sets the next fake server to return.
            /// </summary>
            internal RemoteModelTestSession? Next { get; set; }

            /// <summary>
            /// Gets the number of connection attempts that crossed the authority boundary.
            /// </summary>
            internal int Calls { get; private set; }

            /// <inheritdoc/>
            public ValueTask<ISession> ConnectAsync(
                string name, string endpointUrl, string? securityMode, string? securityPolicy, string authType,
                string? username, string? password, bool autoAcceptCertificates, string? certificateIdentity,
                CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                Calls++;
                return ValueTask.FromResult((Next ?? throw new InvalidOperationException()).Session.Object);
            }
        }

        /// <summary>
        /// The host explicitly declares these test service seams fixed during one classified operation.
        /// </summary>
        private sealed class FixedContract : IMcpFixedSessionContract
        {
            /// <inheritdoc/>
            public bool IsFixed(OpcUaSessionManager.SessionInfo registration)
            {
                return true;
            }
        }

        /// <summary>
        /// Adapts the deterministic service seam while preserving the production registration authority contract.
        /// </summary>
        private sealed class SessionScopes : IWorkflowSessionFactory
        {
            /// <inheritdoc/>
            public ValueTask<IWorkflowSessionScope> CaptureAsync(
                OpcUaSessionManager.SessionInfo registration, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return ValueTask.FromResult<IWorkflowSessionScope>(new Scope(registration));
            }
        }

        /// <summary>
        /// A fixed test service view; SDK dispatch fencing is not reimplemented here.
        /// </summary>
        private sealed class Scope(OpcUaSessionManager.SessionInfo registration) : IWorkflowSessionScope
        {
            /// <inheritdoc/>
            public OpcUaSessionManager.SessionInfo Registration { get; } = registration;

            /// <inheritdoc/>
            public OpcUaSessionManager.SessionInfo Access { get; } = registration.WithSession(registration.Session);

            /// <inheritdoc/>
            public NodeId SessionId => Registration.Session.SessionId;

            /// <inheritdoc/>
            public Task Invalidated { get; } = new TaskCompletionSource().Task;

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }
}
#endif
