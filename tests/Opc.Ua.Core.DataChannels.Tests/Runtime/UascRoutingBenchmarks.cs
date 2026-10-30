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

#if NET8_0_OR_GREATER
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Runtime
{
    /// <summary>
    /// Measures actual post-security DATA envelope selection without ingress copies, queues or cryptography.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [MemoryDiagnoser]
    public class UascRoutingBenchmarks
    {
        /// <summary>
        /// Gets or sets the number of active admissions alongside one retired identifier.
        /// </summary>
        [Params(1, 32, 128)]
        public int AdmittedFlows { get; set; }

        /// <summary>
        /// Gets or sets the standard PublisherId representation.
        /// </summary>
        [Params(BuiltInType.Byte, BuiltInType.UInt16, BuiltInType.UInt32, BuiltInType.UInt64, BuiltInType.String)]
        public BuiltInType PublisherIdType { get; set; } = BuiltInType.UInt32;

        /// <summary>
        /// Gets or sets the number of removed admissions preceding the active set.
        /// </summary>
        [Params(1, 1024)]
        public int RetiredFlows { get; set; } = 1;

        /// <summary>
        /// Creates real admissions and retires one before measuring the immutable routing state.
        /// </summary>
        [GlobalSetup]
        public async Task SetupAsync()
        {
            m_pair = new FlowLoopback(options: new DataChannelOptions
            {
                MaxMessageSize = 1024,
                MaxReceiveBytes = 4096,
                MaxContextBytes = 1024 * 1024,
                MaxQueueMessages = 16,
                MaxContextMessages = 4096
            });
            await m_pair.StartAsync().ConfigureAwait(false);
            for (int i = 0; i < RetiredFlows; i++)
            {
                FlowPair retired = await m_pair.OpenAsync().ConfigureAwait(false);
                m_retiredId = retired.Local.ChannelId;
                await retired.Local.ResetAsync(DataChannelResult.Closed).ConfigureAwait(false);
                await retired.Remote.StateData.Released.Task.ConfigureAwait(false);
            }
            for (int i = 0; i < AdmittedFlows; i++)
            {
                FlowPair flow = await m_pair.OpenAsync().ConfigureAwait(false);
                m_activeId = flow.Local.ChannelId;
            }
            await m_pair.FlushAsync().ConfigureAwait(false);
            Variant publisherId = PublisherIdType switch
            {
                BuiltInType.Byte => new Variant((byte)42),
                BuiltInType.UInt16 => new Variant((ushort)42),
                BuiltInType.UInt32 => new Variant(42U),
                BuiltInType.UInt64 => new Variant(42UL),
                BuiltInType.String => new Variant("publisher-with-string-identity"),
                _ => throw new InvalidOperationException("Unsupported benchmark PublisherId type.")
            };
            byte[] networkMessage = UadpApplicationPayloadHeaderCodec.Encode(
                publisherId, ByteString.FromHexString("010203"), 1024).ToArray();
            networkMessage[2] = 0;
            m_payload = new ByteString(networkMessage);
        }

        /// <summary>
        /// Releases the real context pair after measurement.
        /// </summary>
        [TearDown]
        [GlobalCleanup]
        public async Task CleanupAsync()
        {
            if (m_pair != null)
            {
                await m_pair.DisposeAsync().ConfigureAwait(false);
                m_pair = null;
            }
        }

        /// <summary>
        /// Selects and validates the last admitted flow without a PublisherId route table.
        /// </summary>
        [Benchmark]
        public DataChannelResult ActiveAdmission()
        {
            return CreateEnvelope(m_pair!.Acceptor, m_activeId, m_payload, out _, out _);
        }

        /// <summary>
        /// Classifies a removed admission using the peer high-water mark.
        /// </summary>
        [Benchmark]
        public bool RetiredAdmission()
        {
            CreateEnvelope(m_pair!.Acceptor, m_retiredId, m_payload, out _, out bool silent);
            return silent;
        }

        /// <summary>
        /// Classifies an identifier beyond the peer high-water mark without constructing a STATUS.
        /// </summary>
        [Benchmark]
        public bool UnknownAdmission()
        {
            CreateEnvelope(m_pair!.Acceptor, 0x7FFFFFFF, m_payload, out _, out bool silent);
            return silent;
        }

        /// <summary>
        /// Proves the benchmark fixtures reach distinct active, retired and never-admitted paths.
        /// </summary>
        [Test]
        public async Task RoutingBenchmarksUseActualAdmissionStatesAsync(
            [Values(1, 32, 128)] int admittedFlows,
            [Values(BuiltInType.Byte, BuiltInType.UInt16, BuiltInType.UInt32, BuiltInType.UInt64, BuiltInType.String)]
            BuiltInType publisherIdType,
            [Values(1, 1024)] int retiredFlows)
        {
            AdmittedFlows = admittedFlows;
            PublisherIdType = publisherIdType;
            RetiredFlows = retiredFlows;
            try
            {
                await SetupAsync().ConfigureAwait(false);
                DataChannelResult retired = CreateEnvelope(
                    m_pair!.Acceptor, m_retiredId, m_payload, out _, out bool retiredSilent);
                DataChannelResult unknown = CreateEnvelope(
                    m_pair.Acceptor, 0x7FFFFFFF, m_payload, out _, out bool unknownSilent);
                Assert.Multiple(() =>
                {
                    Assert.That(ActiveAdmission(), Is.EqualTo(DataChannelResult.Good));
                    Assert.That(retired, Is.EqualTo(DataChannelResult.UnknownChannel));
                    Assert.That(retiredSilent, Is.True);
                    Assert.That(unknown, Is.EqualTo(DataChannelResult.UnknownChannel));
                    Assert.That(unknownSilent, Is.False);
                    Assert.That(m_pair.Acceptor.Channels.Count, Is.EqualTo(admittedFlows));
                });
            }
            finally
            {
                await CleanupAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Calls the production selector without adding a public API or runtime reflection.
        /// The fixture stops mutation before measurement; callers do not own a live ingress lock.
        /// </summary>
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "CreateUascDataEnvelopeLocked")]
        private static extern DataChannelResult CreateEnvelope(
            DataChannelContext context,
            uint channelId,
            ByteString applicationBody,
            out FlowWireEnvelope envelope,
            out bool silent);

        /// <summary>The initialized context pair.</summary>
        private FlowLoopback? m_pair;
        /// <summary>The unchanged valid UADP payload.</summary>
        private ByteString m_payload;
        /// <summary>The last active peer-allocated identifier.</summary>
        private uint m_activeId;
        /// <summary>The removed peer-allocated identifier.</summary>
        private uint m_retiredId;
    }
}
#endif
