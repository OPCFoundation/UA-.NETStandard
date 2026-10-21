/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the OPC Foundation MIT License 1.00.
 * See LICENSE.txt in the project root for license information.
 * ======================================================================*/

using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Opc.Ua;

namespace RecoverableServiceDemo
{
    /// <summary>
    /// Private TEST control plumbing, not an OPC UA transport or a simulated Service reply.
    /// The two directions use separate pipes so a blocked business handler never blocks time reads.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class RestartPipe(PipeStream stream) : IAsyncDisposable
    {
        internal static NamedPipeServerStream CreateServer(string name)
        {
            return new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        }

        internal static async Task<NamedPipeClientStream> ConnectAsync(
            string name, int supervisorPid, CancellationToken ct)
        {
            var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, TokenImpersonationLevel.Identification);
            try
            {
                await pipe.ConnectAsync(ct).ConfigureAwait(false);
                if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint actual))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
                Require(actual == checked((uint)supervisorPid), "The IPC server is not the launching supervisor.");
                return pipe;
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        internal static async Task AcceptAsync(NamedPipeServerStream pipe, int childPid, CancellationToken ct)
        {
            await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint actual))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            Require(actual == checked((uint)childPid), "The IPC client is not the explicitly launched Server child.");
        }

        internal async ValueTask<byte[]> ExchangeAsync(
            RestartCommand command, Action<BinaryWriter>? arguments, CancellationToken ct)
        {
            await m_exchange.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(m_faulted, this);
                await WriteAsync(stream, writer =>
                {
                    writer.Write((byte)command);
                    arguments?.Invoke(writer);
                }, ct).ConfigureAwait(false);
                byte[] reply = await ReadAsync(stream, ct).ConfigureAwait(false);
                if (reply.Length == 0 || reply[0] != 1)
                {
                    CryptographicOperations.ZeroMemory(reply);
                    throw new ServiceResultException(StatusCodes.BadResourceUnavailable,
                        "The private TEST authority/control exchange failed closed.");
                }
                return reply;
            }
            catch
            {
                // Cancellation cannot leave a reply queued for a different request.
                m_faulted = true;
                await stream.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                m_exchange.Release();
            }
        }

        internal async ValueTask SendAsync(
            RestartCommand command, Action<BinaryWriter>? arguments, CancellationToken ct)
        {
            byte[] reply = await ExchangeAsync(command, arguments, ct).ConfigureAwait(false);
            try
            {
                Require(reply.Length == 1, "An empty IPC acknowledgement was expected.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(reply);
            }
        }

        internal static byte[] Encode(Action<BinaryWriter> encode)
        {
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
            {
                encode(writer);
            }
            Require(buffer.Length is > 0 and <= kMaximumFrameBytes, "The private IPC frame exceeded its bound.");
            return buffer.ToArray();
        }

        internal static async ValueTask WriteAsync(
            Stream destination, Action<BinaryWriter> encode, CancellationToken ct)
        {
            byte[] payload = Encode(encode);
            try
            {
                byte[] length = new byte[sizeof(int)];
                BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
                await destination.WriteAsync(length, ct).ConfigureAwait(false);
                await destination.WriteAsync(payload, ct).ConfigureAwait(false);
                await destination.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                // Bootstrap frames contain secret-store material, never command-line credentials.
                CryptographicOperations.ZeroMemory(payload);
            }
        }

        internal static async ValueTask<byte[]> ReadAsync(Stream source, CancellationToken ct)
        {
            byte[] prefix = new byte[sizeof(int)];
            await source.ReadExactlyAsync(prefix, ct).ConfigureAwait(false);
            int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
            Require(length is > 0 and <= kMaximumFrameBytes, "An invalid private IPC frame was received.");
            byte[] payload = new byte[length];
            try
            {
                await source.ReadExactlyAsync(payload, ct).ConfigureAwait(false);
                return payload;
            }
            catch
            {
                CryptographicOperations.ZeroMemory(payload);
                throw;
            }
        }

        internal static BinaryReader Reader(byte[] payload, bool reply = false)
        {
            var buffer = new MemoryStream(payload, writable: false);
            if (reply)
            {
                buffer.Position = 1;
            }
            return new BinaryReader(buffer, Encoding.UTF8);
        }

        internal static void End(BinaryReader reader)
        {
            Require(reader.BaseStream.Position == reader.BaseStream.Length, "Unexpected trailing IPC data.");
        }

        internal static void WriteBytes(BinaryWriter writer, ReadOnlySpan<byte> bytes)
        {
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        internal static byte[] ReadBytes(BinaryReader reader, int maximum = kMaximumFrameBytes)
        {
            int length = reader.ReadInt32();
            Require(length >= 0 && length <= maximum, "An IPC field exceeded its bound.");
            byte[] bytes = reader.ReadBytes(length);
            Require(bytes.Length == length, "An IPC field was truncated.");
            return bytes;
        }

        internal static void WriteId(BinaryWriter writer, Uuid value)
        {
            writer.Write(value.ToByteArray());
        }

        internal static Uuid ReadId(BinaryReader reader)
        {
            byte[] value = reader.ReadBytes(16);
            Require(value.Length == 16, "An IPC identity was truncated.");
            return new Uuid(value);
        }

        internal static void Require([DoesNotReturnIf(false)] bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        public async ValueTask DisposeAsync()
        {
            m_faulted = true;
            await stream.DisposeAsync().ConfigureAwait(false);
            m_exchange.Dispose();
        }

        // Preserve the verified Windows SafePipeHandle/BOOL ABI and System32-only loading.
        // LibraryImport requires AllowUnsafeBlocks, which this project does not enable.
        // TODO: Migrate after an approved unsafe/PInvoke policy change and rerun peer-PID denial checks.
#pragma warning disable SYSLIB1054
        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
#pragma warning restore SYSLIB1054

        private const int kMaximumFrameBytes = 65_536;
        private readonly SemaphoreSlim m_exchange = new(1, 1);
        private bool m_faulted;
    }

    /// <summary>Local harness controls only. RSE always uses the generated OPC UA contract.</summary>
    internal enum RestartCommand : byte
    {
        Bootstrap = 1,
        Time,
        Acquire,
        Recover,
        Ready,
        EffectCommitted,
        Arm,
        Release,
        Snapshot,
        Stop,
        RequireUnavailableTime
    }
}
