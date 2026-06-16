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
using System.Reflection;
using NUnit.Framework;

using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Diagnostics.Tests.Stack
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
                        "LoadTokensForOfflineDecode may only be called from the Opc.Ua.Core.Diagnostics binding."));
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
                SecurityPolicy = SecurityPolicies.GetInfo(SecurityPolicies.None)
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
