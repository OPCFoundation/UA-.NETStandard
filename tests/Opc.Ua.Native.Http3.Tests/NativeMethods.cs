// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]

namespace Opc.Ua.Native.Http3.Tests
{
    /// <summary>
    /// Test-only blittable declarations for the production C interface; no runtime reflection.
    /// </summary>
    internal static partial class NativeMethods
    {
        /// <summary>
        /// Loads only the explicitly copied test asset by absolute path for process lifetime.
        /// </summary>
        static NativeMethods()
        {
            string file = OperatingSystem.IsWindows() ? "opcua_http3.dll" : "libopcua_http3.so";
            nint library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, file));
            NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly,
                (name, _, _) => name == "opcua_http3"
                    ? library
                    : throw new DllNotFoundException("Only the HTTP3 test asset can be resolved."));
        }

        /// <summary>
        /// Reads the native ABI layout.
        /// </summary>
        [LibraryImport("opcua_http3", EntryPoint = "opcua_http3_layout")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial int Layout(out NativeLayout result, nuint size);

        /// <summary>
        /// Creates native state from caller-owned UTF8 views and externally verified facts.
        /// </summary>
        [LibraryImport("opcua_http3", EntryPoint = "opcua_http3_create")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial int Create(
            in NativeFacts facts,
            in NativeConfiguration configuration,
            out nint handle,
            out NativeError error);

        /// <summary>
        /// Performs one synchronous, nonblocking state transition.
        /// </summary>
        [LibraryImport("opcua_http3", EntryPoint = "opcua_http3_command")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int Command(
            SafeHttp3Handle handle, uint operation, ulong id, ulong arg, ulong aux, byte* data, nuint length);

        /// <summary>
        /// Copies one event into caller-owned memory and leases transport writes.
        /// </summary>
        [LibraryImport("opcua_http3", EntryPoint = "opcua_http3_poll")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int Poll(
            SafeHttp3Handle handle, out NativeEvent result, byte* buffer, nuint capacity);

        /// <summary>
        /// Reads typed failure information without parsing diagnostic strings.
        /// </summary>
        [LibraryImport("opcua_http3", EntryPoint = "opcua_http3_last_error")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int Error(
            SafeHttp3Handle handle, out NativeError error, byte* buffer, nuint capacity);

        /// <summary>
        /// Observes retained byte and lease accounting.
        /// </summary>
        [LibraryImport("opcua_http3", EntryPoint = "opcua_http3_resource_snapshot")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial int Resources(SafeHttp3Handle handle, out NativeResources result);

        /// <summary>
        /// Destroys a handle exactly once.
        /// </summary>
        [LibraryImport("opcua_http3", EntryPoint = "opcua_http3_destroy")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial void Destroy(nint handle);
    }

    /// <summary>
    /// Test-owned handle; transport buffers must be released before normal disposal.
    /// </summary>
    internal sealed class SafeHttp3Handle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>
        /// Takes ownership of a successfully created native handle.
        /// </summary>
        internal SafeHttp3Handle(nint value)
            : base(ownsHandle: true)
        {
            SetHandle(value);
        }

        /// <inheritdoc />
        protected override bool ReleaseHandle()
        {
            NativeMethods.Destroy(handle);
            return true;
        }
    }

    /// <summary>
    /// Native ABI size discovery.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeLayout
    {
        /// <summary>Size of this structure.</summary>
        internal uint Size;

        /// <summary>ABI version.</summary>
        internal uint Abi;

        /// <summary>Send completion contract revision.</summary>
        internal uint Revision;

        /// <summary>Native pointer width.</summary>
        internal uint PointerSize;

        /// <summary>Transport fact structure size.</summary>
        internal uint FactsSize;

        /// <summary>Configuration structure size.</summary>
        internal uint ConfigurationSize;

        /// <summary>Event metadata size.</summary>
        internal uint EventSize;

        /// <summary>Error information size.</summary>
        internal uint ErrorSize;

        /// <summary>Resource snapshot size.</summary>
        internal uint ResourceSize;

        /// <summary>Reserved, zero.</summary>
        internal uint Reserved;
    }

    /// <summary>
    /// External connection facts; synthetic values here are only state-test fixtures.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeFacts
    {
        /// <summary>Layout version.</summary>
        internal uint Abi;

        /// <summary>Client zero or server one.</summary>
        internal uint Role;

        /// <summary>Local native datagram transport parameter.</summary>
        internal ulong LocalDatagram;

        /// <summary>Peer native datagram transport parameter.</summary>
        internal ulong PeerDatagram;

        /// <summary>Reliable reset transport parameter.</summary>
        internal ulong ResetParameter;

        /// <summary>Reliable reset frame type.</summary>
        internal ulong ResetFrame;

        /// <summary>Initial local WT bidirectional stream credit.</summary>
        internal ulong InitialBidi;

        /// <summary>Initial local WT payload credit.</summary>
        internal ulong InitialData;

        /// <summary>Local exact09 empty parameter evidence.</summary>
        internal uint LocalResetEmpty;

        /// <summary>Remote exact09 empty parameter evidence.</summary>
        internal uint PeerResetEmpty;

        /// <summary>Handshake confirmation evidence.</summary>
        internal uint Confirmed;

        /// <summary>Validated expected application pair evidence.</summary>
        internal uint MutualAuthentication;

        /// <summary>Negotiated h3 evidence.</summary>
        internal uint H3;

        /// <summary>Reserved, zero.</summary>
        internal uint Reserved;
    }

    /// <summary>
    /// Borrowed UTF8 memory, retained only for the create call.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct NativeUtf8
    {
        /// <summary>Pointer to bytes.</summary>
        internal byte* Data;

        /// <summary>Byte count.</summary>
        internal nuint Length;
    }

    /// <summary>
    /// Local resource admission limits.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeLimits
    {
        /// <summary>Maximum input bytes per call.</summary>
        internal ulong Input;

        /// <summary>Queued and leased byte budget.</summary>
        internal ulong Output;

        /// <summary>Send descriptor count.</summary>
        internal ulong Sends;

        /// <summary>Queued event count.</summary>
        internal ulong Events;

        /// <summary>Stream lifetime and tombstone count.</summary>
        internal ulong Streams;

        /// <summary>Header count.</summary>
        internal ulong Headers;

        /// <summary>Decoded field-section byte budget.</summary>
        internal ulong HeaderBytes;

        /// <summary>Per-control-stream lifetime ingress budget.</summary>
        internal ulong Control;

        /// <summary>Request buffering budget.</summary>
        internal ulong Request;

        /// <summary>Capsule buffering budget.</summary>
        internal ulong Capsule;

        /// <summary>Pending-session ingress budget.</summary>
        internal ulong Pending;

        /// <summary>Completion receipt history.</summary>
        internal ulong Receipts;
    }

    /// <summary>
    /// Immutable caller policy, allocator reservations and external correlation tags.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeConfiguration
    {
        /// <summary>Configuration byte size.</summary>
        internal uint Size;

        /// <summary>Configuration version.</summary>
        internal uint Abi;

        /// <summary>Origin discriminator.</summary>
        internal uint OriginPolicy;

        /// <summary>Exactly one session lifetime.</summary>
        internal uint Sessions;

        /// <summary>Exact authority.</summary>
        internal NativeUtf8 Authority;

        /// <summary>Exact URI resource.</summary>
        internal NativeUtf8 Path;

        /// <summary>Optional exact Origin.</summary>
        internal NativeUtf8 Origin;

        /// <summary>Required application protocol.</summary>
        internal NativeUtf8 Protocol;

        /// <summary>Allocated local control stream.</summary>
        internal ulong Control;

        /// <summary>Allocated local encoder stream.</summary>
        internal ulong Encoder;

        /// <summary>Allocated local decoder stream.</summary>
        internal ulong Decoder;

        /// <summary>Reserved CONNECT stream zero.</summary>
        internal ulong Connect;

        /// <summary>External connection correlation, not authentication.</summary>
        internal ulong ConnectionTag;

        /// <summary>External identity-pair correlation, not authentication.</summary>
        internal ulong PairTag;

        /// <summary>Local limits.</summary>
        internal NativeLimits Limits;
    }

    /// <summary>
    /// Native event metadata without owned pointers.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeEvent
    {
        /// <summary>Event discriminator.</summary>
        internal uint Kind;

        /// <summary>FIN and association-prefix flags.</summary>
        internal uint Flags;

        /// <summary>Native stream identifier.</summary>
        internal ulong Stream;

        /// <summary>Session identifier, including valid zero.</summary>
        internal ulong Session;

        /// <summary>Lease token or event-specific argument.</summary>
        internal ulong Argument;

        /// <summary>Absolute stream offset or event-specific argument.</summary>
        internal ulong Auxiliary;

        /// <summary>Payload byte length.</summary>
        internal ulong Length;
    }

    /// <summary>
    /// Typed scope, code and wire error information.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeError
    {
        /// <summary>Operation, stream, session, connection or configuration.</summary>
        internal uint Scope;

        /// <summary>Stable adapter code.</summary>
        internal uint Code;

        /// <summary>HTTP3/WT wire code, zero for local errors.</summary>
        internal ulong Protocol;

        /// <summary>Affected stream.</summary>
        internal ulong Stream;

        /// <summary>Affected session.</summary>
        internal ulong Session;

        /// <summary>Diagnostic UTF8 byte count.</summary>
        internal ulong Length;

        /// <summary>Failed operation.</summary>
        internal uint Operation;

        /// <summary>Retryability or fatality flags.</summary>
        internal uint Flags;
    }

    /// <summary>
    /// Read-only native resource counters.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeResources
    {
        /// <summary>Capsule allocation.</summary>
        internal ulong Capsule;

        /// <summary>Request allocation.</summary>
        internal ulong Request;

        /// <summary>Queued payload allocation.</summary>
        internal ulong Queued;

        /// <summary>Caller-leased payload bytes.</summary>
        internal ulong Leased;

        /// <summary>Pending send descriptors.</summary>
        internal ulong Sends;

        /// <summary>Reserved session payload credit.</summary>
        internal ulong Payload;

        /// <summary>Configured output budget.</summary>
        internal ulong Maximum;

        /// <summary>Completion contract revision.</summary>
        internal ulong Revision;
    }
}
