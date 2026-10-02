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

#nullable enable

using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Completion of a token renewal handshake on the client channel.
    /// </summary>
    [TestFixture]
    [Category("TcpTransport")]
    [Parallelizable]
    public sealed class UaSCBinaryClientChannelHandshakeTests
    {
        private const BindingFlags kPrivate = BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>
        /// The completion callback of a renewal runs on the thread pool. When
        /// the renewal timer has already started the next handshake by then,
        /// the callback must finish the operation it was raised for instead
        /// of blocking - under the channel gate - on the newer handshake,
        /// which can only complete once its response gets that gate.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task HandshakeCompletionDoesNotWaitForANewerHandshakeAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var quotas = new ChannelQuotas(ServiceMessageContext.Create(telemetry));
            var buffers = new BufferManager(nameof(UaSCBinaryClientChannelHandshakeTests), 65536, telemetry);
            var factory = new Mock<IUaSCByteTransportFactory>();
            factory.SetupGet(f => f.Implementation).Returns("UA-FAKE");
            using var channel = new UaSCUaBinaryClientChannel(
                "handshake",
                buffers,
                factory.Object,
                quotas,
                null,
                null,
                null,
                new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost:4840",
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                },
                telemetry);

            Type type = typeof(UaSCUaBinaryClientChannel);
            MethodInfo beginOperation = type.GetMethod("BeginOperation", kPrivate)!;
            FieldInfo handshakeOperation = type.GetField("m_handshakeOperation", kPrivate)!;
            object callback = type.GetField("m_handshakeComplete", kPrivate)!.GetValue(channel)!;
            var requests = (IDictionary)type.GetField("m_requests", kPrivate)!.GetValue(channel)!;

            var completed = (ChannelAsyncOperation<int>)beginOperation.Invoke(
                channel, [int.MaxValue, callback, null])!;
            var newer = (ChannelAsyncOperation<int>)beginOperation.Invoke(
                channel, [int.MaxValue, callback, null])!;
            uint completedRequestId = (uint)completed.GetType().GetProperty("RequestId")!.GetValue(completed)!;

            // the renewal timer replaced the handshake before the callback ran.
            handshakeOperation.SetValue(channel, newer);

            try
            {
                // queued to the pool, as the channel completes it under the gate.
                completed.Complete(true, 0);

                Assert.That(
                    await PollAsync(() => !requests.Contains(completedRequestId)).ConfigureAwait(false),
                    Is.True,
                    "the completion callback never finished the operation it was raised for");

                Task gate = Task.Run(() =>
                {
                    using (channel.Gate.Enter())
                    {
                    }
                });
                Assert.That(
                    await Task.WhenAny(gate, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false),
                    Is.SameAs(gate),
                    "the completion callback holds the gate");

                Assert.That(handshakeOperation.GetValue(channel), Is.SameAs(newer));
                Assert.That(newer.IsCompleted, Is.False);
            }
            finally
            {
                // releases a callback blocked on the newer handshake.
                newer.Complete(0);
            }
        }

        private static async Task<bool> PollAsync(Func<bool> condition)
        {
            for (int ii = 0; ii < 100; ii++)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(100).ConfigureAwait(false);
            }

            return condition();
        }
    }
}
