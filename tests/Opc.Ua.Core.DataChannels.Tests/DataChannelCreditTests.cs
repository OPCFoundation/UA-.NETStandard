/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.DataChannels.Tests
{
    [TestFixture]
    [Category("DataChannels")]
    [Parallelizable(ParallelScope.All)]
    public class DataChannelCreditTests
    {
        [Test]
        public void GrantThenConsumeDecrementsAvailableByPayloadLength()
        {
            var window = new DataChannelSendWindow();

            Assert.That(window.TryGrant(1024), Is.True);
            Assert.That(window.TryConsume(300), Is.True);

            Assert.That(window.Available, Is.EqualTo(724u));
        }

        [Test]
        public void BlockedPayloadFailsAndCountsOneRealStall()
        {
            var window = new DataChannelSendWindow(100);

            Assert.Multiple(() =>
            {
                Assert.That(window.IsBlockedBy(100), Is.False);
                Assert.That(window.IsBlockedBy(101), Is.True);
                Assert.That(window.TryConsume(101), Is.False);
                Assert.That(window.Available, Is.EqualTo(100u));
                Assert.That(window.Stalls, Is.EqualTo(1u));
            });
        }

        [Test]
        public void StallsCountsOnlyFailedPositiveConsumes()
        {
            var window = new DataChannelSendWindow(10);

            Assert.Multiple(() =>
            {
                Assert.That(window.TryConsume(0), Is.True);
                Assert.That(window.TryConsume(-1), Is.True);
                Assert.That(window.TryConsume(4), Is.True);
                Assert.That(window.Stalls, Is.Zero);
                Assert.That(window.TryConsume(7), Is.False);
                Assert.That(window.TryGrant(10), Is.True);
                Assert.That(window.TryConsume(7), Is.True);
                Assert.That(window.Stalls, Is.EqualTo(1u));
            });
        }

        [Test]
        public void GrantThatWouldOverflowIsRefusedAndDoesNotWrap()
        {
            var window = new DataChannelSendWindow(uint.MaxValue - 1);

            Assert.That(window.TryGrant(1), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(window.Available, Is.EqualTo(uint.MaxValue));
                Assert.That(window.TryGrant(1), Is.False);
                Assert.That(window.Available, Is.EqualTo(uint.MaxValue));
                Assert.That(window.IsBlockedBy(int.MaxValue), Is.False);
            });
        }

        [Test]
        public void TransportFlowControlDoesNotSpendCreditOrCountStalls()
        {
            const int payloadLength = 8 * 1024 * 1024;
            const uint initialCredit = 8;
            int iterations = (int)(uint.MaxValue / (uint)payloadLength) + 2;
            var bufferManager = new ReusingBufferManager(payloadLength);
            var transport = new CreditProbeTransport(
                new BufferManager(bufferManager),
                hasTransportFlowControl: true);
            using DataChannel channel = CreateOpenChannel(transport, initialCredit, payloadLength);

            for (int ii = 0; ii < iterations; ii++)
            {
                channel.Write(bufferManager.Buffer, CompleteMessageFlags);
                DequeueAndRelease(channel);
            }

            Assert.That(channel.GetDiagnostics().CreditStalls, Is.Zero);
        }

        [Test]
        public void InlineTransportSpendsCreditAndCountsRealStalls()
        {
            const int payloadLength = 5;
            const uint initialCredit = 8;
            var bufferManager = new ReusingBufferManager(payloadLength);
            var transport = new CreditProbeTransport(
                new BufferManager(bufferManager),
                hasTransportFlowControl: false);
            using DataChannel channel = CreateOpenChannel(transport, initialCredit, payloadLength);

            channel.Write(bufferManager.Buffer, CompleteMessageFlags);
            DequeueAndRelease(channel);

            channel.Write(bufferManager.Buffer, CompleteMessageFlags);
            DequeueAndRelease(channel);

            Assert.That(channel.GetDiagnostics().CreditStalls, Is.EqualTo(1u));
        }

        [Test]
        public void ResetReturnsFreshWindowToInitialCreditState()
        {
            var window = new DataChannelSendWindow(256);

            Assert.That(window.TryConsume(128), Is.True);
            window.Reset();

            Assert.Multiple(() =>
            {
                Assert.That(window.Available, Is.Zero);
                Assert.That(window.Stalls, Is.Zero);
                Assert.That(window.IsBlockedBy(1), Is.True);
            });
        }

        [Test]
        public void ReplenishmentIsHeldUntilThresholdThenGrantsReleasedBytes()
        {
            var credit = new DataChannelReceiveCredit(1024);

            Assert.That(credit.TryAccount(400), Is.True);
            credit.Release(400);

            Assert.That(credit.TryTakeReplenishment(100, out uint amount), Is.False);
            Assert.That(amount, Is.Zero);

            Assert.That(credit.TryAccount(200), Is.True);
            credit.Release(200);

            Assert.That(credit.TryTakeReplenishment(100, out amount), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(amount, Is.EqualTo(600u));
                Assert.That(amount % 100, Is.Zero);
                Assert.That(credit.Outstanding, Is.EqualTo(1024u));
                Assert.That(credit.Released, Is.Zero);
                Assert.That(credit.LastGrant, Is.EqualTo(600u));
            });
        }

        [Test]
        public void ReceiveAccountingRejectsOverrunAndReleaseSaturatesSafely()
        {
            var credit = new DataChannelReceiveCredit(100);

            Assert.That(credit.TryAccount(30), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(credit.Outstanding, Is.EqualTo(70u));
                Assert.That(credit.TryAccount(80), Is.False);
                Assert.That(credit.Outstanding, Is.EqualTo(70u));
            });

            credit.Release(30);
            credit.Release(int.MaxValue);
            credit.Release(int.MaxValue);

            Assert.Multiple(() =>
            {
                Assert.That(credit.Outstanding, Is.EqualTo(70u));
                Assert.That(credit.Released, Is.EqualTo(uint.MaxValue));
            });
        }

        [Test]
        public void GrantSaturatesReceiveOutstandingRatherThanWrapping()
        {
            var credit = new DataChannelReceiveCredit(uint.MaxValue - 10);

            credit.Grant(20);

            Assert.Multiple(() =>
            {
                Assert.That(credit.Outstanding, Is.EqualTo(uint.MaxValue));
                Assert.That(credit.LastGrant, Is.EqualTo(20u));
            });
        }

        private static DataChannel CreateOpenChannel(
            IDataChannelTransport transport,
            uint initialCredit,
            int maxFrameSize)
        {
            var settings = new DataChannelSettings
            {
                InitialCredit = initialCredit,
                MaxFrameSize = (uint)maxFrameSize
            };
            var args = new object[]
            {
                1u,
                new NodeId(1u),
                settings,
                transport,
                true,
                0UL
            };
            var channel = (DataChannel)s_dataChannelConstructor.Invoke(args);
            s_markOpen.Invoke(channel, null);
            return channel;
        }

        private static void DequeueAndRelease(DataChannel channel)
        {
            object?[] args = new object?[] { null, null };

            Assert.That(s_tryDequeuePayload.Invoke(channel, args), Is.True);
            s_releaseSendBuffer.Invoke(channel, new[] { args[1] });
        }

        private static ConstructorInfo GetDataChannelConstructor()
        {
            ConstructorInfo? constructor = typeof(DataChannel).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[]
                {
                    typeof(uint),
                    typeof(NodeId),
                    typeof(DataChannelSettings),
                    typeof(IDataChannelTransport),
                    typeof(bool),
                    typeof(ulong)
                },
                null);

            return constructor ??
                throw new MissingMethodException(nameof(DataChannel), ".ctor");
        }

        private sealed class CreditProbeTransport : IDataChannelTransport
        {
            public CreditProbeTransport(BufferManager bufferManager, bool hasTransportFlowControl)
            {
                BufferManager = bufferManager;
                HasTransportFlowControl = hasTransportFlowControl;
            }

            public DataChannelFramingMode FramingMode => DataChannelFramingMode.Inline;

            public int MaxFrameBodySize => int.MaxValue;

            public bool HasTransportFlowControl { get; }

            public BufferManager BufferManager { get; }

            public TimeProvider TimeProvider => TimeProvider.System;

            public ValueTask SendFrameAsync(DataChannelFrame frame, CancellationToken ct)
            {
                return default;
            }

            public void OnProtocolFault(DataChannelFrameError error)
            {
            }
        }

        private sealed class ReusingBufferManager : IBufferManager
        {
            public ReusingBufferManager(int size)
            {
                Buffer = new byte[size];
            }

            public byte[] Buffer { get; }

            public string Name => nameof(ReusingBufferManager);

            public int MaxSuggestedBufferSize => Buffer.Length;

            public int GetSuggestedBufferSize(int size)
            {
                return size;
            }

            public int GetExpectedBufferSize(int size)
            {
                return size;
            }

            public byte[] TakeBuffer(int size, string owner)
            {
                Assert.That(size, Is.LessThanOrEqualTo(Buffer.Length));
                return Buffer;
            }

            public byte[] TakeBuffer(int size, string owner, CancellationToken ct)
            {
                return TakeBuffer(size, owner);
            }

            public void TransferBuffer(byte[]? buffer, string owner)
            {
            }

            public void Lock(byte[] buffer)
            {
            }

            public void Unlock(byte[] buffer)
            {
            }

            public void ReturnBuffer(byte[]? buffer, string owner)
            {
            }
        }

        private const DataChannelFrameFlags CompleteMessageFlags =
            DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.MessageEnd;

        private static readonly MethodInfo s_tryDequeuePayload =
            typeof(DataChannel).GetMethod(
                "TryDequeuePayload",
                BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingMethodException(nameof(DataChannel), "TryDequeuePayload");

        private static readonly MethodInfo s_releaseSendBuffer =
            typeof(DataChannel).GetMethod(
                "ReleaseSendBuffer",
                BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingMethodException(nameof(DataChannel), "ReleaseSendBuffer");

        private static readonly MethodInfo s_markOpen =
            typeof(DataChannel).GetMethod(
                "MarkOpen",
                BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingMethodException(nameof(DataChannel), "MarkOpen");

        private static readonly ConstructorInfo s_dataChannelConstructor =
            GetDataChannelConstructor();
    }
}
