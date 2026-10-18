// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System.Runtime.CompilerServices;

namespace Opc.Ua.Native.Http3.Tests
{
    /// <summary>
    /// Executes production C exports from both JIT and NativeAOT without any QUIC runtime.
    /// Synthetic connection facts test the state boundary, not authentication or networking.
    /// </summary>
    public sealed class NativeAbiTests
    {
        /// <summary>
        /// Verifies every blittable layout before use.
        /// </summary>
        [Test]
        public async Task ProductionLayoutsMatchNativeSizesAsync()
        {
            int status = NativeMethods.Layout(out NativeLayout layout, (nuint)Unsafe.SizeOf<NativeLayout>());
            await Assert.That(status).IsEqualTo(0);
            await Assert.That(layout.Size).IsEqualTo(40u);
            await Assert.That(layout.Abi).IsEqualTo(1u);
            await Assert.That(layout.Revision).IsEqualTo(2u);
            await Assert.That(layout.PointerSize).IsEqualTo(8u);
            await Assert.That(layout.FactsSize).IsEqualTo((uint)Unsafe.SizeOf<NativeFacts>());
            await Assert.That(layout.ConfigurationSize).IsEqualTo((uint)Unsafe.SizeOf<NativeConfiguration>());
            await Assert.That(layout.EventSize).IsEqualTo((uint)Unsafe.SizeOf<NativeEvent>());
            await Assert.That(layout.ErrorSize).IsEqualTo((uint)Unsafe.SizeOf<NativeError>());
            await Assert.That(layout.ResourceSize).IsEqualTo((uint)Unsafe.SizeOf<NativeResources>());
            await Assert.That(layout.Reserved).IsEqualTo(0u);
        }

        /// <summary>
        /// Verifies policy creation fails with a typed error and no handle.
        /// </summary>
        [Test]
        public async Task InvalidApplicationPairTagFailsWithoutNativeHandleAsync()
        {
            int result = CreateRaw(0, 0, out nint handle, out NativeError error);
            await Assert.That(result).IsEqualTo(-2);
            await Assert.That(handle).IsEqualTo(nint.Zero);
            await Assert.That(error.Scope).IsEqualTo(5u);
            await Assert.That(error.Code).IsEqualTo(8u);
        }

        /// <summary>
        /// Proves insufficient buffers do not consume events or transfer ownership.
        /// </summary>
        [Test]
        public async Task PollRetryAndCompletionPreserveCallerOwnershipAsync()
        {
            using SafeHttp3Handle handle = Create(0);
            NativeMethods.Resources(handle, out NativeResources before);
            int retry = Poll(handle, [], out NativeEvent needed);
            NativeMethods.Resources(handle, out NativeResources afterRetry);
            await Assert.That(retry).IsEqualTo(2);
            await Assert.That(afterRetry.Queued).IsEqualTo(before.Queued);
            await Assert.That(afterRetry.Leased).IsEqualTo(0ul);
            byte[] data = new byte[checked((int)needed.Length)];
            int read = Poll(handle, data, out NativeEvent output);
            await Assert.That(read).IsEqualTo(1);
            await Assert.That(output.Argument).IsEqualTo(needed.Argument);
            await Assert.That(output.Length).IsEqualTo(needed.Length);
            NativeMethods.Resources(handle, out NativeResources leased);
            await Assert.That(leased.Leased).IsEqualTo(output.Length);
            int completed = Command(handle, 20, output.Argument, output.Length, 1, []);
            await Assert.That(completed).IsEqualTo(0);
            NativeMethods.Resources(handle, out NativeResources released);
            await Assert.That(released.Leased).IsEqualTo(0ul);
            await Assert.That(released.Sends).IsEqualTo(2ul);
            await Assert.That(Command(handle, 999, 0, 0, 0, [])).IsEqualTo(-2);
            NativeError error = GetError(handle);
            await Assert.That(error.Code).IsEqualTo(10u);
            await Assert.That(error.Scope).IsEqualTo(1u);
            await Assert.That(error.Operation).IsEqualTo(999u);
            DrainInitialWrites(handle);
        }

        /// <summary>
        /// Exercises actual production state exports, configuration and byte identity from managed code.
        /// </summary>
        [Test]
        public async Task ManagedPumpNegotiatesConfiguredSessionAndPreservesPayloadAsync()
        {
            using SafeHttp3Handle client = Create(0);
            using SafeHttp3Handle server = Create(1);
            Pump(client, server);
            Require(Command(client, 10, 0, 0, 0, []), client);
            List<Observation> pending = Pump(client, server);
            await Assert.That(pending.Count(item => item.Kind == 12)).IsEqualTo(1);
            await Assert.That(pending.Count(item => item.Kind == 13)).IsEqualTo(0);
            Require(Command(server, 11, 0, 200, 0, []), server);
            List<Observation> established = Pump(client, server);
            await Assert.That(established.Count(item => item.Kind == 13)).IsEqualTo(2);
            Require(Command(client, 12, 0, 4, 0, []), client);
            Require(Command(client, 13, 4, 1, 0, "complete UA message"u8), client);
            Require(Command(client, 14, 0, 0, 0, "real datagram payload"u8), client);
            List<Observation> received = Pump(client, server);
            Observation stream = received.Single(item => item.Kind == 15);
            Observation datagram = received.Single(item => item.Kind == 17);
            await Assert.That(stream.Data.AsSpan().SequenceEqual("complete UA message"u8)).IsTrue();
            await Assert.That(datagram.Data.AsSpan().SequenceEqual("real datagram payload"u8)).IsTrue();
            await Assert.That(received.Count(item => item.Kind == 16)).IsEqualTo(1);
            NativeMethods.Resources(client, out NativeResources resources);
            await Assert.That(resources.Leased).IsEqualTo(0ul);
            await Assert.That(resources.Sends).IsEqualTo(0ul);
        }

        /// <summary>
        /// Creates a handle with synthetic facts and explicit test policy.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static SafeHttp3Handle Create(uint role)
        {
            int result = CreateRaw(role, 29, out nint handle, out NativeError error);
            if (result != 0)
            {
                throw new InvalidOperationException($"Creation failed: {error.Scope}/{error.Code}.");
            }
            return new SafeHttp3Handle(handle);
        }

        /// <summary>
        /// Pins UTF8 strings only for the synchronous native creation call.
        /// </summary>
        private static unsafe int CreateRaw(uint role, ulong pair, out nint handle, out NativeError error)
        {
            ReadOnlySpan<byte> authority = "plant.example:8443"u8;
            ReadOnlySpan<byte> path = "/ua/wt?resource=line-2"u8;
            ReadOnlySpan<byte> protocol = "opcua-flow/1"u8;
            var facts = new NativeFacts
            {
                Abi = 1,
                Role = role,
                LocalDatagram = 65535,
                PeerDatagram = 65535,
                ResetParameter = 0x1d,
                ResetFrame = 0x24,
                InitialBidi = 8,
                InitialData = 4096,
                LocalResetEmpty = 1,
                PeerResetEmpty = 1,
                Confirmed = 1,
                MutualAuthentication = 1,
                H3 = 1
            };
            fixed (byte* authorityBytes = authority, pathBytes = path, protocolBytes = protocol)
            {
                var configuration = new NativeConfiguration
                {
                    Size = (uint)sizeof(NativeConfiguration),
                    Abi = 1,
                    Sessions = 1,
                    Authority = new NativeUtf8 { Data = authorityBytes, Length = (nuint)authority.Length },
                    Path = new NativeUtf8 { Data = pathBytes, Length = (nuint)path.Length },
                    Protocol = new NativeUtf8 { Data = protocolBytes, Length = (nuint)protocol.Length },
                    Control = role + 2ul,
                    Encoder = role + 6ul,
                    Decoder = role + 10ul,
                    ConnectionTag = 23,
                    PairTag = pair,
                    Limits = new NativeLimits
                    {
                        Input = 1048576,
                        Output = 1048576,
                        Sends = 512,
                        Events = 4096,
                        Streams = 4096,
                        Headers = 32,
                        HeaderBytes = 16384,
                        Control = 65536,
                        Request = 65536,
                        Capsule = 65536,
                        Pending = 131072,
                        Receipts = 256
                    }
                };
                return NativeMethods.Create(in facts, in configuration, out handle, out error);
            }
        }

        /// <summary>
        /// Pins a command payload only while the state operation executes.
        /// </summary>
        private static unsafe int Command(
            SafeHttp3Handle handle, uint operation, ulong id, ulong arg, ulong aux, ReadOnlySpan<byte> data)
        {
            fixed (byte* pointer = data)
            {
                return NativeMethods.Command(handle, operation, id, arg, aux, pointer, (nuint)data.Length);
            }
        }

        /// <summary>
        /// Copies one output into caller-owned storage.
        /// </summary>
        private static unsafe int Poll(SafeHttp3Handle handle, Span<byte> buffer, out NativeEvent output)
        {
            fixed (byte* pointer = buffer)
            {
                return NativeMethods.Poll(handle, out output, pointer, (nuint)buffer.Length);
            }
        }

        /// <summary>
        /// Reads only typed error fields; diagnostic text is not a discriminator.
        /// </summary>
        private static unsafe NativeError GetError(SafeHttp3Handle handle)
        {
            NativeMethods.Error(handle, out NativeError error, null, 0);
            return error;
        }

        /// <summary>
        /// Converts a failed state operation into a failed test.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static void Require(int result, SafeHttp3Handle handle)
        {
            if (result != 0)
            {
                NativeError error = GetError(handle);
                throw new InvalidOperationException($"Native operation failed: {error.Scope}/{error.Code}/{error.Protocol}.");
            }
        }

        /// <summary>
        /// Releases unused initial control-stream leases in the ownership-only test.
        /// </summary>
        private static void DrainInitialWrites(SafeHttp3Handle handle)
        {
            Span<byte> buffer = stackalloc byte[4096];
            while (Poll(handle, buffer, out NativeEvent output) == 1)
            {
                if (output.Kind == 1)
                {
                    Require(Command(handle, 20, output.Argument, output.Length, 1, []), handle);
                }
            }
        }

        /// <summary>
        /// Bounded deterministic state pump. This is not a network or authentication test.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static List<Observation> Pump(SafeHttp3Handle client, SafeHttp3Handle server)
        {
            var events = new List<Observation>();
            Span<byte> buffer = stackalloc byte[4096];
            for (int iteration = 0; iteration < 64; iteration++)
            {
                bool progress = false;
                for (int side = 0; side < 2; side++)
                {
                    SafeHttp3Handle source = side == 0 ? client : server;
                    SafeHttp3Handle target = side == 0 ? server : client;
                    while (true)
                    {
                        int result = Poll(source, buffer, out NativeEvent output);
                        if (result == 0)
                        {
                            break;
                        }
                        if (result != 1)
                        {
                            throw new InvalidOperationException($"Unexpected poll result: {result}.");
                        }
                        ReadOnlySpan<byte> data = buffer[..checked((int)output.Length)];
                        if (output.Kind is 1 or 2)
                        {
                            Require(output.Kind == 1
                                ? Command(target, 1, output.Stream, output.Flags & 1u, 0, data)
                                : Command(target, 2, 0, 0, 0, data), target);
                            Require(Command(source, 20, output.Argument, output.Length,
                                (output.Flags & 1u) == 0 ? 1ul : 3ul, []), source);
                            progress = true;
                        }
                        else
                        {
                            events.Add(new Observation(output.Kind, data.ToArray()));
                        }
                    }
                }
                if (!progress)
                {
                    return events;
                }
            }
            throw new InvalidOperationException("The bounded native state pump did not quiesce.");
        }

        /// <summary>
        /// Captures one application observation for exact assertion.
        /// </summary>
        private sealed record Observation(uint Kind, byte[] Data);
    }
}
