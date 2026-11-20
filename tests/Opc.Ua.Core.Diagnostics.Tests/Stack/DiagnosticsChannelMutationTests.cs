// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Tests.Stack
{
    /// <summary>
    /// Verifies the privileged diagnostics token mutation surface is limited
    /// to the Pcap binding and test assemblies.
    /// </summary>
    [TestFixture]
    public sealed class DiagnosticsChannelMutationTests
    {
        [Test]
        public void OfflineLoadTokensViaInterfaceFromAllowedCallerSucceeds()
        {
            using var channel = new TestChannel();

#pragma warning disable CA2000
            ChannelToken current = CreateToken(channelId: 0x1001, tokenId: 0x2001);
            ChannelToken previous = CreateToken(channelId: 0x1001, tokenId: 0x2000);
            ((IDiagnosticsChannelMutation)channel).LoadTokensForOfflineDecode(current, previous);
#pragma warning restore CA2000

            Assert.That(channel.LoadedCurrentToken, Is.SameAs(current));
            Assert.That(channel.LoadedPreviousToken, Is.SameAs(previous));
            Assert.That(channel.LoadedRenewedToken, Is.Null);
        }

        [Test]
        public void OfflineLoadTokensFromDisallowedCallerThrows()
        {
            using var channel = new TestChannel();
            ChannelToken current = CreateToken(channelId: 0x1002, tokenId: 0x2002);
            ChannelToken previous = CreateToken(channelId: 0x1002, tokenId: 0x2001);

            try
            {
                MethodInfo loadMethod = typeof(IDiagnosticsChannelMutation).GetMethod(
                    nameof(IDiagnosticsChannelMutation.LoadTokensForOfflineDecode)) ??
                    throw new AssertionException("Could not find diagnostics mutation method.");

                TargetInvocationException? exception = Assert.Throws<TargetInvocationException>(
                    () => loadMethod.Invoke(channel, [current, previous]));

                Assert.That(exception, Is.Not.Null);
                Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
                Assert.That(
                    exception.InnerException!.Message,
                    Is.EqualTo(
                        "LoadTokensForOfflineDecode may only be called from the Opc.Ua.Core.Diagnostics assembly."));
                Assert.That(channel.LoadedCurrentToken, Is.Null);
                Assert.That(channel.LoadedPreviousToken, Is.Null);
            }
            finally
            {
                current.Dispose();
                previous.Dispose();
            }
        }

        [Test]
        public void ObsoleteOfflineLoadTokensMethodForwardsToInterface()
        {
            using var channel = new TestChannel();

#pragma warning disable CA2000
            ChannelToken current = CreateToken(channelId: 0x1003, tokenId: 0x2003);
            ChannelToken previous = CreateToken(channelId: 0x1003, tokenId: 0x2002);
            channel.LoadViaObsoleteShim(current, previous);
#pragma warning restore CA2000

            Assert.That(channel.LoadedCurrentToken, Is.SameAs(current));
            Assert.That(channel.LoadedPreviousToken, Is.SameAs(previous));
            Assert.That(channel.LoadedRenewedToken, Is.Null);
        }

        private static ChannelToken CreateToken(uint channelId, uint tokenId)
        {
            return new ChannelToken
            {
                ChannelId = channelId,
                TokenId = tokenId,
                CreatedAt = DateTime.UtcNow,
                CreatedAtTimestamp = TimeProvider.System.GetTimestamp(),
                Lifetime = int.MaxValue,
                SecurityPolicy = SecurityPolicies.Default.GetInfo(SecurityPolicies.None)
            };
        }

        private sealed class TestChannel : UaSCUaBinaryChannel
        {
            public TestChannel()
                : base(
                    contextId: "diagnostics-mutation-test",
                    bufferManager: new BufferManager(
                        "diagnostics-mutation-test",
                        TcpMessageLimits.DefaultMaxBufferSize,
                        TestTelemetryContext.Instance),
                    quotas: new ChannelQuotas(ServiceMessageContext.CreateEmpty(TestTelemetryContext.Instance)),
                    serverCertificates: null,
                    endpoints: null,
                    securityMode: MessageSecurityMode.None,
                    securityPolicyUri: SecurityPolicies.None,
                    telemetry: TestTelemetryContext.Instance)
            {
            }

            public ChannelToken? LoadedCurrentToken => CurrentToken;

            public ChannelToken? LoadedPreviousToken => PreviousToken;

            public ChannelToken? LoadedRenewedToken => RenewedToken;

            public void LoadViaObsoleteShim(ChannelToken current, ChannelToken previous)
            {
#pragma warning disable CS0618
                OfflineLoadTokens(current, previous);
#pragma warning restore CS0618
            }
        }
    }
}
