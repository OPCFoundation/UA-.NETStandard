// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Opc.Ua.Native.QuicGo
{
    /// <summary>
    /// Blittable ABI3 declarations; no marshalled delegates, Go pointers or reflection-generated stubs.
    /// </summary>
    internal sealed unsafe class NativeApi
    {
        /// <summary>
        /// Loads one verified image. Its module handle is deliberately never freed, even on ABI failure.
        /// </summary>
        internal NativeApi(string path)
        {
            nint library = NativeLibrary.Load(path);
            m_abi = (delegate* unmanaged[Cdecl]<Abi*, int>)NativeLibrary.GetExport(library, "opcua_quic_abi");
            m_listen = (delegate* unmanaged[Cdecl]<Config*, Handle*, int>)
                NativeLibrary.GetExport(library, "opcua_quic_listen_begin");
            m_connect = (delegate* unmanaged[Cdecl]<Config*, Handle*, int>)
                NativeLibrary.GetExport(library, "opcua_quic_connect_begin");
            m_listener = (delegate* unmanaged[Cdecl]<Handle, Request*, Handle*, int>)
                NativeLibrary.GetExport(library, "opcua_quic_listener_begin");
            m_connection = (delegate* unmanaged[Cdecl]<Handle, Request*, Handle*, int>)
                NativeLibrary.GetExport(library, "opcua_quic_connection_begin");
            m_stream = (delegate* unmanaged[Cdecl]<Handle, Request*, Handle*, int>)
                NativeLibrary.GetExport(library, "opcua_quic_stream_begin");
            m_poll = (delegate* unmanaged[Cdecl]<Handle, Result*, byte*, uint, int>)
                NativeLibrary.GetExport(library, "opcua_quic_poll");
            m_cancel = (delegate* unmanaged[Cdecl]<Handle, ulong, int>)
                NativeLibrary.GetExport(library, "opcua_quic_cancel");
            m_release = (delegate* unmanaged[Cdecl]<Handle, int>)NativeLibrary.GetExport(library, "opcua_quic_release");
            m_counts = (delegate* unmanaged[Cdecl]<Counts*, int>)NativeLibrary.GetExport(library, "opcua_quic_counts");
            var abi = new Abi { Size = (uint)sizeof(Abi) };
            if (m_abi(&abi) != 0 ||
                abi.Version != 4 ||
                abi.PointerSize != 8 ||
                abi.ConfigSize != sizeof(Config) ||
                abi.RequestSize != sizeof(Request) ||
                abi.ResultSize != sizeof(Result) ||
                abi.InfoSize != 920 ||
                abi.CountsSize != sizeof(Counts))
            {
                throw new InvalidDataException(
                    "Incompatible native QUIC ABI. Restart with the reviewed native assets.");
            }
        }

        /// <summary>
        /// Copies bounded caller credentials into native ownership before returning.
        /// </summary>
        internal NativeQuicStatus Create(bool listen, NativeQuicOptions options, out Handle operation)
        {
            NativeQuicCredentials identity = options.Credentials;
            byte[] key = identity.PrivateKey.Copy();
            byte[]? storage = null;
            try
            {
                byte[][] blobs = new byte[8 + (2 * options.AllowedPeers.Count)][];
                blobs[0] = s_utf8.GetBytes(options.Address);
                blobs[1] = s_utf8.GetBytes(options.ApplicationProtocol);
                blobs[2] = identity.CertificateDer.Span.ToArray();
                blobs[3] = identity.IssuerChainDer.Span.ToArray();
                blobs[4] = key;
                blobs[5] = identity.TrustRootsDer.Span.ToArray();
                blobs[6] = s_utf8.GetBytes(identity.ApplicationUri);
                blobs[7] = s_utf8.GetBytes(options.ServerName);
                int length = 0;
                for (int i = 0; i < options.AllowedPeers.Count; i++)
                {
                    blobs[8 + (2 * i)] = s_utf8.GetBytes(options.AllowedPeers[i].ApplicationUri);
                    blobs[9 + (2 * i)] = options.AllowedPeers[i].SpkiSha256.Span.ToArray();
                }
                foreach (byte[] blob in blobs)
                {
                    length = checked(length + blob.Length);
                }
                storage = new byte[length];
                fixed (byte* start = storage)
                {
                    Span<Bytes> spans = stackalloc Bytes[blobs.Length];
                    int offset = 0;
                    for (int i = 0; i < blobs.Length; i++)
                    {
                        blobs[i].CopyTo(storage.AsSpan(offset));
                        spans[i] = new Bytes { Data = start + offset, Length = (uint)blobs[i].Length };
                        offset += blobs[i].Length;
                    }
                    Peer* peers = stackalloc Peer[options.AllowedPeers.Count];
                    for (int i = 0; i < options.AllowedPeers.Count; i++)
                    {
                        peers[i] = new Peer { Uri = spans[8 + (2 * i)], Pin = spans[9 + (2 * i)] };
                    }
                    var config = new Config
                    {
                        Size = (uint)sizeof(Config),
                        Timeout = options.TimeoutMilliseconds,
                        Address = spans[0],
                        Alpn = spans[1],
                        Identity = new Identity
                        {
                            Certificate = spans[2],
                            Issuers = spans[3],
                            Key = spans[4],
                            Roots = spans[5],
                            Uri = spans[6],
                            ServerName = spans[7]
                        },
                        Peers = peers,
                        PeerCount = (uint)options.AllowedPeers.Count,
                        StreamLimit = options.StreamLimit,
                        IdleTimeout = options.IdleTimeoutMilliseconds
                    };
                    Handle handle = default;
                    int status = listen ? m_listen(&config, &handle) : m_connect(&config, &handle);
                    operation = handle;
                    return (NativeQuicStatus)status;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                if (storage is not null)
                {
                    CryptographicOperations.ZeroMemory(storage);
                }
            }
        }

        /// <summary>
        /// Starts a typed resource operation without waiting for transport completion.
        /// </summary>
        internal NativeQuicStatus Begin(
            uint category, ulong owner, uint kind, in NativeQuicRequest request, out Handle operation)
        {
            fixed (byte* data = request.Payload.Span)
            {
                var native = new Request
                {
                    Size = (uint)sizeof(Request),
                    Kind = kind,
                    Timeout = checked((uint)request.Timeout.TotalMilliseconds),
                    Flags = request.Flags,
                    ErrorCode = request.ErrorCode,
                    Payload = new Bytes { Data = data, Length = (uint)request.Payload.Length },
                    Capacity = request.Capacity,
                    Index = request.CertificateIndex
                };
                var ownerHandle = new Handle { Value = owner };
                Handle handle = default;
                int status = category switch
                {
                    1 => m_listener(ownerHandle, &native, &handle),
                    2 => m_connection(ownerHandle, &native, &handle),
                    3 => m_stream(ownerHandle, &native, &handle),
                    _ => (int)NativeQuicStatus.Invalid
                };
                operation = handle;
                return (NativeQuicStatus)status;
            }
        }

        /// <summary>
        /// Observes a terminal result without consuming it on a short buffer.
        /// </summary>
        internal NativeQuicStatus Poll(Handle operation, Span<byte> payload, out Result result)
        {
            var native = new Result { Size = (uint)sizeof(Result) };
            fixed (byte* data = payload)
            {
                var status = (NativeQuicStatus)m_poll(operation, &native, data, (uint)payload.Length);
                result = native;
                return status;
            }
        }

        /// <summary>
        /// Requests cancellation without releasing worker-owned buffers.
        /// </summary>
        internal void Cancel(Handle operation, ulong errorCode)
        {
            _ = m_cancel(operation, errorCode);
        }

        /// <summary>
        /// Releases an already observed result.
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        internal void Release(Handle operation)
        {
            if (m_release(operation) != 0)
            {
                throw new InvalidDataException("Native result ownership was not observed before release.");
            }
        }

        /// <summary>
        /// Gets bounded ownership counters without network activity.
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        internal NativeQuicCounts GetCounts()
        {
            var counts = new Counts { Size = (uint)sizeof(Counts) };
            if (m_counts(&counts) != 0)
            {
                throw new InvalidDataException("Native ownership counters could not be read.");
            }
            return new NativeQuicCounts(counts.Operations, counts.Terminal, counts.Listeners,
                counts.Connections, counts.Streams, counts.NativeConnections, counts.Transports, counts.Retained);
        }

        /// <summary>
        /// ABI handle; not a native address.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct Handle
        {
            /// <summary>Monotonic native identifier.</summary>
            internal ulong Value;
        }

        /// <summary>
        /// ABI copied byte span.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct Bytes
        {
            /// <summary>Caller memory, retained only for the duration of begin.</summary>
            internal byte* Data;

            /// <summary>Byte count.</summary>
            internal uint Length;

            /// <summary>Must be zero.</summary>
            internal uint Reserved;
        }

        /// <summary>
        /// ABI explicit credentials.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct Identity
        {
            /// <summary>Leaf DER.</summary>
            internal Bytes Certificate;

            /// <summary>Concatenated issuer DER.</summary>
            internal Bytes Issuers;

            /// <summary>Unencrypted PKCS8.</summary>
            internal Bytes Key;

            /// <summary>Explicit trust DER.</summary>
            internal Bytes Roots;

            /// <summary>Local URI UTF8.</summary>
            internal Bytes Uri;

            /// <summary>Expected TLS server name UTF8.</summary>
            internal Bytes ServerName;
        }

        /// <summary>
        /// ABI authorization rule.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct Peer
        {
            /// <summary>Expected URI SAN.</summary>
            internal Bytes Uri;

            /// <summary>Expected SHA256 SPKI.</summary>
            internal Bytes Pin;
        }

        /// <summary>
        /// ABI listener/connector configuration.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct Config
        {
            /// <summary>Structure size.</summary>
            internal uint Size;

            /// <summary>Finite timeout.</summary>
            internal uint Timeout;

            /// <summary>Endpoint UTF8.</summary>
            internal Bytes Address;

            /// <summary>Exact ALPN.</summary>
            internal Bytes Alpn;

            /// <summary>Supplied identity.</summary>
            internal Identity Identity;

            /// <summary>Bounded copied rules.</summary>
            internal Peer* Peers;

            /// <summary>Rule count.</summary>
            internal uint PeerCount;

            /// <summary>Incoming streams per direction.</summary>
            internal uint StreamLimit;

            /// <summary>Idle timeout.</summary>
            internal uint IdleTimeout;

            /// <summary>Must be zero.</summary>
            internal uint Reserved;
        }

        /// <summary>
        /// ABI operation request.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct Request
        {
            /// <summary>Structure size.</summary>
            internal uint Size;

            /// <summary>Operation discriminator.</summary>
            internal uint Kind;

            /// <summary>Finite timeout.</summary>
            internal uint Timeout;

            /// <summary>Operation flags.</summary>
            internal uint Flags;

            /// <summary>Application error.</summary>
            internal ulong ErrorCode;

            /// <summary>Input payload.</summary>
            internal Bytes Payload;

            /// <summary>Read capacity.</summary>
            internal uint Capacity;

            /// <summary>Verified chain index.</summary>
            internal uint Index;
        }

        /// <summary>
        /// ABI immutable completion snapshot.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct Result
        {
            /// <summary>Structure size.</summary>
            internal uint Size;

            /// <summary>Pending or terminal.</summary>
            internal uint State;

            /// <summary>Operation discriminator.</summary>
            internal uint Kind;

            /// <summary>Cancellation observation.</summary>
            internal uint CancelRequested;

            /// <summary>Operation status.</summary>
            internal int Status;

            /// <summary>Native status before cancellation normalization.</summary>
            internal int NativeStatus;

            /// <summary>Accepted byte count.</summary>
            internal uint Written;

            /// <summary>Retained output length.</summary>
            internal uint PayloadLength;

            /// <summary>Created listener.</summary>
            internal Handle Listener;

            /// <summary>Created connection.</summary>
            internal Handle Connection;

            /// <summary>Created stream.</summary>
            internal Handle Stream;

            /// <summary>Native stream ID.</summary>
            internal ulong StreamId;

            /// <summary>Application error.</summary>
            internal ulong ErrorCode;

            /// <summary>Native final size or sent offset.</summary>
            internal ulong FinalSize;

            /// <summary>Native reliable size.</summary>
            internal ulong ReliableSize;

            /// <summary>Actual encoded reset codepoint.</summary>
            internal ulong Frame;

            /// <summary>Native datagram payload limit.</summary>
            internal uint MaximumDatagramPayload;

            /// <summary>Peer-originated result.</summary>
            internal uint Remote;

            /// <summary>Read outcome.</summary>
            internal uint Outcome;

            /// <summary>Must be zero.</summary>
            internal uint Reserved;
        }

        /// <summary>
        /// ABI ownership counters.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct Counts
        {
            /// <summary>Structure size.</summary>
            internal uint Size;

            /// <summary>Operation count.</summary>
            internal uint Operations;

            /// <summary>Terminal operation count.</summary>
            internal uint Terminal;

            /// <summary>Listener handles.</summary>
            internal uint Listeners;

            /// <summary>Connection handles.</summary>
            internal uint Connections;

            /// <summary>Stream handles.</summary>
            internal uint Streams;

            /// <summary>Native peers including handshakes.</summary>
            internal uint NativeConnections;

            /// <summary>Owned sockets.</summary>
            internal uint Transports;

            /// <summary>Reserved operation bytes.</summary>
            internal ulong Retained;
        }

        /// <summary>
        /// ABI version/layout descriptor.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct Abi
        {
            /// <summary>Structure size.</summary>
            internal uint Size;

            /// <summary>ABI version.</summary>
            internal uint Version;

            /// <summary>Native pointer width.</summary>
            internal uint PointerSize;

            /// <summary>Config width.</summary>
            internal uint ConfigSize;

            /// <summary>Request width.</summary>
            internal uint RequestSize;

            /// <summary>Result width.</summary>
            internal uint ResultSize;

            /// <summary>Info width.</summary>
            internal uint InfoSize;

            /// <summary>Counts width.</summary>
            internal uint CountsSize;
        }

        private static readonly UTF8Encoding s_utf8 = new(false, true);
        private readonly delegate* unmanaged[Cdecl]<Abi*, int> m_abi;
        private readonly delegate* unmanaged[Cdecl]<Config*, Handle*, int> m_listen;
        private readonly delegate* unmanaged[Cdecl]<Config*, Handle*, int> m_connect;
        private readonly delegate* unmanaged[Cdecl]<Handle, Request*, Handle*, int> m_listener;
        private readonly delegate* unmanaged[Cdecl]<Handle, Request*, Handle*, int> m_connection;
        private readonly delegate* unmanaged[Cdecl]<Handle, Request*, Handle*, int> m_stream;
        private readonly delegate* unmanaged[Cdecl]<Handle, Result*, byte*, uint, int> m_poll;
        private readonly delegate* unmanaged[Cdecl]<Handle, ulong, int> m_cancel;
        private readonly delegate* unmanaged[Cdecl]<Handle, int> m_release;
        private readonly delegate* unmanaged[Cdecl]<Counts*, int> m_counts;
    }
}
