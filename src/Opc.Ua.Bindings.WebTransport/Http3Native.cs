// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Blittable production ABI1/revision3 declarations. All handle calls are serialized by their owner.
    /// </summary>
    internal static partial class Http3Native
    {
        /// <summary>
        /// Checks both exported versions and every structure size before creating a native peer.
        /// </summary>
        internal static void ValidateLayout()
        {
            if (AbiVersion() != 1 || ApiRevision() != 3 ||
                Layout(out Http3Layout layout, 40) != 0 ||
                layout.Size != 40 || layout.Abi != 1 || layout.Revision != 3 || layout.PointerSize != 8 ||
                layout.FactsSize != 80 || layout.ConfigurationSize != 224 || layout.EventSize != 48 ||
                layout.ErrorSize != 48 || layout.ResourceSize != 64 || layout.Reserved != 0 ||
                Marshal.SizeOf<Http3Facts>() != 80 || Marshal.SizeOf<Http3Configuration>() != 224 ||
                Marshal.SizeOf<Http3Event>() != 48 || Marshal.SizeOf<Http3Error>() != 48 ||
                Marshal.SizeOf<Http3Resources>() != 64 || Marshal.SizeOf<Http3Layout>() != 40)
            {
                throw new InvalidDataException("The native HTTP/3 ABI or ownership revision is incompatible.");
            }
        }

        /// <summary>
        /// Reads the native ABI version.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "opcua_http3_abi_version")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial uint AbiVersion();

        /// <summary>
        /// Reads the operation/ownership revision.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "opcua_http3_api_revision")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial uint ApiRevision();

        /// <summary>
        /// Reads the exact native layout.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "opcua_http3_layout")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial int Layout(out Http3Layout result, nuint size);

        /// <summary>
        /// Creates state, copying the pinned policy views synchronously.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "opcua_http3_create")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial int Create(
            in Http3Facts facts,
            in Http3Configuration configuration,
            out nint handle,
            out Http3Error error);

        /// <summary>
        /// Performs one synchronous state operation; input views are not retained.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "opcua_http3_command")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int Command(
            Http3Handle handle, uint operation, ulong id, ulong arg, ulong aux, byte* data, nuint length);

        /// <summary>
        /// Leases an output or returns required capacity without consumption.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "opcua_http3_poll")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int Poll(
            Http3Handle handle, out Http3Event result, byte* buffer, nuint capacity);

        /// <summary>
        /// Reads typed failure information; diagnostics are not protocol discriminators.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "opcua_http3_last_error")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int Error(
            Http3Handle handle, out Http3Error error, byte* buffer, nuint capacity);

        /// <summary>
        /// Reads bounded native storage and lease counters.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "opcua_http3_resource_snapshot")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial int Resources(Http3Handle handle, out Http3Resources result);

        /// <summary>
        /// Destroys exactly one peer, after the host has settled all transport leases.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "opcua_http3_destroy")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial void Destroy(nint handle);

        /// <summary>The selected import identity, resolved only by NativeHttp3Library.</summary>
        internal const string LibraryName = "opcua_http3";
    }

    /// <summary>
    /// Owns one native H3 state allocation; no TLS or transport lifetime is hidden in this handle.
    /// </summary>
    internal sealed class Http3Handle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>
        /// Takes ownership after a successful native creation.
        /// </summary>
        internal Http3Handle(nint value)
            : base(ownsHandle: true)
        {
            SetHandle(value);
        }

        /// <inheritdoc/>
        protected override bool ReleaseHandle()
        {
            Http3Native.Destroy(handle);
            return true;
        }
    }

    /// <summary>
    /// Native layout discovery.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Http3Layout
    {
        /// <summary>Structure size.</summary>
        internal uint Size;

        /// <summary>ABI version.</summary>
        internal uint Abi;

        /// <summary>Operation revision.</summary>
        internal uint Revision;

        /// <summary>Pointer width.</summary>
        internal uint PointerSize;

        /// <summary>Transport facts size.</summary>
        internal uint FactsSize;

        /// <summary>Configuration size.</summary>
        internal uint ConfigurationSize;

        /// <summary>Event size.</summary>
        internal uint EventSize;

        /// <summary>Error size.</summary>
        internal uint ErrorSize;

        /// <summary>Resource size.</summary>
        internal uint ResourceSize;

        /// <summary>Reserved, zero.</summary>
        internal uint Reserved;
    }

    /// <summary>
    /// Observed transport evidence plus explicit local WT credit policy.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Http3Facts
    {
        /// <summary>ABI version.</summary>
        internal uint Abi;

        /// <summary>Actual transport role.</summary>
        internal uint Role;

        /// <summary>Actual local datagram transport parameter.</summary>
        internal ulong LocalDatagram;

        /// <summary>Actual peer datagram transport parameter.</summary>
        internal ulong PeerDatagram;

        /// <summary>Verified reset09 transport parameter mapping.</summary>
        internal ulong ResetParameter;

        /// <summary>Verified reset09 frame mapping.</summary>
        internal ulong ResetFrame;

        /// <summary>Explicit initial local WT stream credit.</summary>
        internal ulong InitialBidi;

        /// <summary>Explicit initial local WT receive credit.</summary>
        internal ulong InitialData;

        /// <summary>Local exact09 empty parameter evidence.</summary>
        internal uint LocalResetEmpty;

        /// <summary>Peer exact09 empty parameter evidence.</summary>
        internal uint PeerResetEmpty;

        /// <summary>Actual QUIC handshake confirmation.</summary>
        internal uint Confirmed;

        /// <summary>Actual mutually authenticated application-pair evidence.</summary>
        internal uint MutualAuthentication;

        /// <summary>Actually negotiated h3.</summary>
        internal uint H3;

        /// <summary>Facts1: application-mTLS zero; facts2: server-authenticated TLS one.</summary>
        internal uint AuthenticationPolicy;
    }

    /// <summary>
    /// Borrowed, pinned UTF8 view valid only during create.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct Http3Utf8
    {
        /// <summary>Byte pointer.</summary>
        internal byte* Data;

        /// <summary>Exact byte count.</summary>
        internal nuint Length;
    }

    /// <summary>
    /// Exact native resource limits.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Http3Limits
    {
        /// <summary>Input capacity.</summary>
        internal ulong Input;

        /// <summary>Queued and leased output capacity.</summary>
        internal ulong Output;

        /// <summary>Send descriptors.</summary>
        internal ulong Sends;

        /// <summary>Event descriptors.</summary>
        internal ulong Events;

        /// <summary>Lifetime stream records.</summary>
        internal ulong Streams;

        /// <summary>Header fields.</summary>
        internal ulong Headers;

        /// <summary>Header storage.</summary>
        internal ulong HeaderBytes;

        /// <summary>Control-stream lifetime input.</summary>
        internal ulong Control;

        /// <summary>Request buffering.</summary>
        internal ulong Request;

        /// <summary>Capsule buffering.</summary>
        internal ulong Capsule;

        /// <summary>Pre-session buffering.</summary>
        internal ulong Pending;

        /// <summary>Terminal receipt history.</summary>
        internal ulong Receipts;
    }

    /// <summary>
    /// Exact caller policy and actual stream allocations.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Http3Configuration
    {
        /// <summary>Structure size.</summary>
        internal uint Size;

        /// <summary>ABI version.</summary>
        internal uint Abi;

        /// <summary>Explicit Origin discriminator.</summary>
        internal uint OriginPolicy;

        /// <summary>One session per connection lifetime.</summary>
        internal uint Sessions;

        /// <summary>Exact authority.</summary>
        internal Http3Utf8 Authority;

        /// <summary>Exact resource path/query.</summary>
        internal Http3Utf8 Path;

        /// <summary>Exact Origin or empty.</summary>
        internal Http3Utf8 Origin;

        /// <summary>Exact application protocol.</summary>
        internal Http3Utf8 Protocol;

        /// <summary>Actual allocated local control stream.</summary>
        internal ulong Control;

        /// <summary>Actual allocated local QPACK encoder stream.</summary>
        internal ulong Encoder;

        /// <summary>Actual allocated local QPACK decoder stream.</summary>
        internal ulong Decoder;

        /// <summary>Actual reserved client CONNECT0.</summary>
        internal ulong Connect;

        /// <summary>External nonzero connection correlation.</summary>
        internal ulong ConnectionTag;

        /// <summary>Config1 nonzero application-pair correlation; config2 may be zero before UASC authentication.</summary>
        internal ulong PairTag;

        /// <summary>Finite local limits.</summary>
        internal Http3Limits Limits;
    }

    /// <summary>
    /// Native event metadata. Data is copied into a bounded host owner when polled.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Http3Event
    {
        /// <summary>Event discriminator.</summary>
        internal uint Kind;

        /// <summary>Event flags.</summary>
        internal uint Flags;

        /// <summary>Actual stream ID.</summary>
        internal ulong Stream;

        /// <summary>Actual session ID, or UInt64.MaxValue for absent.</summary>
        internal ulong Session;

        /// <summary>Token or event argument.</summary>
        internal ulong Arg;

        /// <summary>Offset, reliable size or event auxiliary argument.</summary>
        internal ulong Aux;

        /// <summary>Required payload bytes.</summary>
        internal ulong Length;
    }

    /// <summary>
    /// Native typed failure, independent of diagnostic text.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Http3Error
    {
        /// <summary>Operation/stream/session/connection/configuration scope.</summary>
        internal uint Scope;

        /// <summary>Local code.</summary>
        internal uint Code;

        /// <summary>HTTP3 protocol error, or zero.</summary>
        internal ulong ProtocolCode;

        /// <summary>Stream ID, or UInt64.MaxValue.</summary>
        internal ulong Stream;

        /// <summary>Session ID, or UInt64.MaxValue.</summary>
        internal ulong Session;

        /// <summary>Diagnostic byte count.</summary>
        internal ulong MessageLength;

        /// <summary>Failed operation.</summary>
        internal uint Operation;

        /// <summary>Retryable/fatal flags.</summary>
        internal uint Flags;

        /// <summary>
        /// Gets whether replay is explicitly nonconsuming and operation-scoped.
        /// </summary>
        internal readonly bool Retryable => Scope == 1 && Code is 15 or 16 or 17 && (Flags & 1) != 0;
    }

    /// <summary>
    /// Native storage counters; host-owned copies are accounted separately.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Http3Resources
    {
        /// <summary>Retained capsules.</summary>
        internal ulong Capsules;

        /// <summary>Retained request state.</summary>
        internal ulong Requests;

        /// <summary>Queued output.</summary>
        internal ulong Queued;

        /// <summary>Leased output.</summary>
        internal ulong Leased;

        /// <summary>Pending sends.</summary>
        internal ulong Sends;

        /// <summary>Reserved WT data credit.</summary>
        internal ulong ReservedPayload;

        /// <summary>Maximum output capacity.</summary>
        internal ulong MaximumOutput;

        /// <summary>Ownership revision.</summary>
        internal ulong Revision;
    }
}
