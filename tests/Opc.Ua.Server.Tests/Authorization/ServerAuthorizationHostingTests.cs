// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.Authorization
{
    /// <summary>
    /// A hosted server takes its <see cref="IServerAuthorizationPolicy"/> and additional
    /// <see cref="IAuditEventSink"/>s from the service container.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("Authorization")]
    [NonParallelizable]
    public class ServerAuthorizationHostingTests
    {
        [Test]
        public async Task DependencyInjectionServerUsesTheRegisteredPolicyAndAuditSinkAsync()
        {
            var policy = new DenyKeyCredentialPushPolicy();
            var sink = new RecordingAuditEventSink();
            var services = new ServiceCollection();
            services.AddSingleton<IServerAuthorizationPolicy>(policy);
            services.AddSingleton<IAuditEventSink>(sink);
            using ServiceProvider provider = services.BuildServiceProvider();
            var fixture = new ServerFixture<DependencyInjectionStandardServer>(
                telemetry => new DependencyInjectionStandardServer(provider, telemetry, TimeProvider.System))
            {
                SecurityNone = true
            };
            await fixture.LoadConfigurationAsync().ConfigureAwait(false);
            await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                var server = (ServerInternalData)fixture.Server.CurrentInstance;
                Assert.That(server.AuthorizationPolicy, Is.SameAs(policy));
                Assert.That(server.AuditEventSink, Is.InstanceOf<CompositeAuditEventSink>());

                // A context bound to the running server resolves the host policy.
                var identity = new Mock<IUserIdentity>();
                identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
                identity.Setup(i => i.GrantedRoleIds)
                    .Returns(ArrayOf.Wrapped(new[] { ObjectIds.WellKnownRole_SecurityAdmin }));
                var operationContext = new OperationContext(
                    new RequestHeader(),
                    new SecureChannelContext(
                        "test",
                        new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                        RequestEncoding.Binary),
                    RequestType.Call,
                    RequestLifetime.None,
                    identity.Object);
                var context = new ServerSystemContext(server, operationContext);
                Assert.Multiple(() =>
                {
                    Assert.That(
                        RoleAuthorizationGate.Check(context, ServerAuthorizationDecision.KeyCredentialPush).StatusCode,
                        Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    Assert.That(
                        RoleAuthorizationGate.Check(context, ServerAuthorizationDecision.RoleManagement).StatusCode,
                        Is.EqualTo(StatusCodes.Good));
                });

                // An audit event reaches the registered sink while auditing is enabled.
                Assert.That(server.Auditing, Is.True, "The test fixture enables auditing.");
                var e = new AuditEventState(null);
                server.ReportAuditEvent(server.DefaultAuditContext, e);
                Assert.That(sink.Events, Does.Contain(e));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private sealed class DenyKeyCredentialPushPolicy : ServerAuthorizationPolicy
        {
            public override ServiceResult Authorize(ISystemContext context, ServerAuthorizationDecision decision)
            {
                return decision == ServerAuthorizationDecision.KeyCredentialPush
                    ? new ServiceResult(StatusCodes.BadUserAccessDenied)
                    : base.Authorize(context, decision);
            }
        }

        private sealed class RecordingAuditEventSink : IAuditEventSink
        {
            public List<AuditEventState> Events { get; } = [];

            public void ReportAuditEvent(ISystemContext context, AuditEventState e)
            {
                lock (Events)
                {
                    Events.Add(e);
                }
            }
        }
    }
}
