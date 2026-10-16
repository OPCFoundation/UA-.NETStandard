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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Versioned SDK-owned classic persistence. Raw IEncodeable bodies remain
    /// schema-coupled and must not be passed off as legacy saved files.
    /// </summary>
    internal static class SnapshotCodec
    {
        /// <summary>
        /// Saves a complete classic subscription collection.
        /// </summary>
        internal static void SaveSubscriptions(
            Stream stream,
            IServiceMessageContext context,
            ArrayOf<SubscriptionState> subscriptions)
        {
            using var encoder = new BinaryEncoder(stream, context, true);
            WriteHeader(encoder, context, kSubscriptions);
            encoder.WriteInt32(null, subscriptions.Count);
            foreach (SubscriptionState state in subscriptions)
            {
                state.Encode(encoder);
            }
        }

        /// <summary>
        /// Decodes and validates every subscription before any can be attached.
        /// </summary>
        internal static ArrayOf<SubscriptionState> LoadSubscriptions(
            Stream stream,
            IServiceMessageContext context)
        {
            byte[] prefix = ReadPrefix(stream);
            using var input = new PrefixReplayStream(stream, prefix);
            using var decoder = new BinaryDecoder(input, context, true);
            bool legacy = ReadHeader(decoder, prefix, kSubscriptions);
            (NamespaceTable namespaces, StringTable servers) = ReadTables(decoder);
            decoder.SetMappingTables(namespaces, servers);

            int count = decoder.ReadInt32(null);
            if (count < 0)
            {
                throw InvalidData("A subscription count cannot be negative.");
            }
            if (context.MaxArrayLength > 0 && count > context.MaxArrayLength)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "The subscription count exceeds MaxArrayLength.");
            }
            // Do not allocate directly from an untrusted collection count.
            // Nested arrays and strings use BinaryDecoder's own limits.
            var states = new List<SubscriptionState>();
            for (int i = 0; i < count; i++)
            {
                SubscriptionState state;
                if (legacy)
                {
                    var old = new SubscriptionStateV0();
                    old.Decode(decoder);
                    state = old.ToCurrent();
                }
                else
                {
                    state = new SubscriptionState();
                    state.Decode(decoder);
                }
                ValidateSubscription(state, context.NamespaceUris);
                states.Add(state);
            }
            EnsureEnd(input);
            return states.ToArray();
        }

        /// <summary>
        /// Saves a session configuration using its own payload discriminator.
        /// </summary>
        internal static void SaveConfiguration(
            Stream stream,
            IServiceMessageContext context,
            SessionConfiguration configuration)
        {
            using var encoder = new BinaryEncoder(stream, context, true);
            WriteHeader(encoder, context, kConfiguration);
            configuration.Encode(encoder);
        }

        /// <summary>
        /// Reads either the frozen unversioned or current session configuration.
        /// </summary>
        internal static SessionConfiguration LoadConfiguration(Stream stream, ITelemetryContext telemetry)
        {
            var context = ServiceMessageContext.Create(telemetry);
            context.Factory.Builder.AddOpcUaClientDataTypes();
            byte[] prefix = ReadPrefix(stream);
            using var input = new PrefixReplayStream(stream, prefix);
            using var decoder = new BinaryDecoder(input, context, true);
            bool legacy = ReadHeader(decoder, prefix, kConfiguration);
            (NamespaceTable namespaces, StringTable servers) = ReadTables(decoder);
            context.NamespaceUris = namespaces;
            context.ServerUris = servers;

            SessionConfiguration configuration;
            if (legacy)
            {
                var old = new SessionConfigurationV0();
                old.Decode(decoder);
                configuration = old.ToCurrent();
            }
            else
            {
                configuration = new SessionConfiguration();
                configuration.Decode(decoder);
            }
            ValidateNodeId(configuration.SessionId, namespaces);
            ValidateNodeId(configuration.AuthenticationToken, namespaces);
            foreach (SubscriptionState state in configuration.Subscriptions)
            {
                ValidateSubscription(state, namespaces);
            }
            EnsureEnd(input);
            return configuration;
        }

        private static void WriteHeader(BinaryEncoder encoder, IServiceMessageContext context, int kind)
        {
            encoder.WriteInt32(null, kDiscriminator);
            encoder.WriteInt32(null, kVersion);
            encoder.WriteInt32(null, kind);
            encoder.WriteStringArray(null, context.NamespaceUris.ToArrayOf());
            encoder.WriteStringArray(null, context.ServerUris.ToArrayOf());
        }

        private static bool ReadHeader(BinaryDecoder decoder, byte[] prefix, int expectedKind)
        {
            int discriminator = BinaryPrimitives.ReadInt32LittleEndian(prefix);
            if (discriminator >= -1)
            {
                // The replay stream gives ReadStringArray its original length
                // prefix so all allocation/string checks remain in BinaryDecoder.
                return true;
            }
            if (discriminator != kDiscriminator)
            {
                throw InvalidData("Unknown classic snapshot discriminator.");
            }
            decoder.ReadInt32(null);
            if (decoder.ReadInt32(null) != kVersion)
            {
                throw InvalidData("Unsupported classic snapshot version.");
            }
            if (decoder.ReadInt32(null) != expectedKind)
            {
                throw InvalidData("The classic snapshot payload kind does not match the loading entry point.");
            }
            return false;
        }

        private static (NamespaceTable Namespaces, StringTable Servers) ReadTables(BinaryDecoder decoder)
        {
            ArrayOf<string?> namespaces = ReadTable(decoder);
            ArrayOf<string?> servers = ReadTable(decoder);
            if ((!namespaces.IsNull && (namespaces.Count == 0 || namespaces[0] != Namespaces.OpcUa)) ||
                namespaces.Count > ushort.MaxValue + 1)
            {
                throw InvalidData("Invalid classic snapshot namespace table.");
            }
            foreach (string? value in namespaces)
            {
                if (string.IsNullOrEmpty(value))
                {
                    throw InvalidData("A namespace table entry cannot be null or empty.");
                }
            }
            foreach (string? value in servers)
            {
                if (value == null)
                {
                    throw InvalidData("A server URI table entry cannot be null.");
                }
            }
            return (
                namespaces.IsNull ? new NamespaceTable() : new NamespaceTable(namespaces.Memory.ToArray()!),
                servers.IsNull ? new StringTable() : new StringTable(servers.Memory.ToArray()!));
        }

        private static ArrayOf<string?> ReadTable(BinaryDecoder decoder)
        {
            byte[] prefix = ReadPrefix(decoder.BaseStream);
            if (BinaryPrimitives.ReadInt32LittleEndian(prefix) < -1)
            {
                throw InvalidData("An invalid negative URI table length was encountered.");
            }
            using var input = new PrefixReplayStream(decoder.BaseStream, prefix);
            using var tableDecoder = new BinaryDecoder(input, decoder.Context, true);
            return tableDecoder.ReadStringArray(null);
        }

        private static void ValidateSubscription(SubscriptionState state, NamespaceTable namespaces)
        {
            foreach (MonitoredItemState item in state.MonitoredItems)
            {
                ValidateNodeId(item.StartNodeId, namespaces);
                ValidateNodeId(item.ResolvedNodeId, namespaces);
                if (!item.Encoding.IsNull && item.Encoding.NamespaceIndex >= namespaces.Count)
                {
                    throw InvalidData("An encoding namespace index is not present in the snapshot namespace table.");
                }
            }
        }

        private static void ValidateNodeId(NodeId nodeId, NamespaceTable namespaces)
        {
            if (!nodeId.IsNull && nodeId.NamespaceIndex >= namespaces.Count)
            {
                throw InvalidData("A node namespace index is not present in the snapshot namespace table.");
            }
        }

        private static void EnsureEnd(Stream stream)
        {
            if (stream.ReadByte() != -1)
            {
                throw InvalidData("Unexpected trailing bytes in a classic snapshot.");
            }
        }

        private static byte[] ReadPrefix(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            byte[] prefix = new byte[sizeof(int)];
            int offset = 0;
            while (offset < prefix.Length)
            {
                int read = stream.Read(prefix, offset, prefix.Length - offset);
                if (read == 0)
                {
                    throw InvalidData("Truncated classic snapshot header.");
                }
                offset += read;
            }
            return prefix;
        }

        private static ServiceResultException InvalidData(string message)
        {
            return new ServiceResultException(StatusCodes.BadDecodingError, message);
        }

        /// <summary>
        /// Replays only the inspected prefix without owning the caller's stream.
        /// Position is tracked for decoder body accounting on forward-only input;
        /// length and seek operations remain unavailable when the source forbids them.
        /// </summary>
        private sealed class PrefixReplayStream : Stream
        {
            public PrefixReplayStream(Stream source, byte[] prefix)
            {
                m_source = source;
                m_prefix = prefix;
                m_origin = source.CanSeek ? source.Position - prefix.Length : 0;
            }

            public override bool CanRead => m_source.CanRead;
            public override bool CanSeek => m_source.CanSeek;
            public override bool CanWrite => false;
            public override long Length => CanSeek ? m_source.Length - m_origin : throw new NotSupportedException();
            public override long Position
            {
                get => m_position;
                set => Seek(value, SeekOrigin.Begin);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (buffer == null)
                {
                    throw new ArgumentNullException(nameof(buffer));
                }
                if (offset < 0 || count < 0 || offset > buffer.Length - count)
                {
                    throw new ArgumentOutOfRangeException(nameof(offset));
                }
                int copied = 0;
                if (m_position < m_prefix.Length)
                {
                    copied = Math.Min(count, m_prefix.Length - (int)m_position);
                    Array.Copy(m_prefix, (int)m_position, buffer, offset, copied);
                    m_position += copied;
                }
                if (copied < count)
                {
                    int read = m_source.Read(buffer, offset + copied, count - copied);
                    m_position += read;
                    copied += read;
                }
                return copied;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                if (!CanSeek)
                {
                    throw new NotSupportedException();
                }
                long position = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => checked(m_position + offset),
                    SeekOrigin.End => checked(Length + offset),
                    _ => throw new ArgumentOutOfRangeException(nameof(origin))
                };
                if (position < 0)
                {
                    throw new IOException("Cannot seek before the start of a classic snapshot.");
                }
                m_source.Position = checked(m_origin + Math.Max(m_prefix.Length, position));
                m_position = position;
                return position;
            }

            public override void Flush() => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

#pragma warning disable CA2213 // Caller owns the stream. TODO: express borrowed ownership when analyzers support it.
            private readonly Stream m_source;
#pragma warning restore CA2213
            private readonly byte[] m_prefix;
            private readonly long m_origin;
            private long m_position;
        }

        /// <summary>
        /// Negative namespace-array lengths other than -1 are reserved.
        /// </summary>
        private const int kDiscriminator = -2;
        private const int kVersion = 1;
        private const int kSubscriptions = 1;
        private const int kConfiguration = 2;
    }
}
