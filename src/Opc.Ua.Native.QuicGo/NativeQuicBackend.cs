// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Opc.Ua.Native.QuicGo
{
    /// <summary>
    /// Nonblocking TAP bridge to the production Go ABI. Admitted operations always drain to an observed
    /// terminal result; the caller must inspect status and release returned resource handles explicitly.
    /// </summary>
    public sealed class NativeQuicBackend : INativeQuicBackend
    {
        /// <summary>
        /// Creates the backend without loading native code. A direct construction path accompanies DI.
        /// </summary>
        public NativeQuicBackend(NativeQuicLibrary library, TimeProvider? timeProvider = null)
        {
            m_library = library ?? throw new ArgumentNullException(nameof(library));
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public ValueTask<NativeQuicCompletion> ListenAsync(
            NativeQuicOptions options, CancellationToken cancellationToken = default)
        {
            return CreateAsync(true, options, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<NativeQuicCompletion> ConnectAsync(
            NativeQuicOptions options, CancellationToken cancellationToken = default)
        {
            return CreateAsync(false, options, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<NativeQuicCompletion> ListenerAsync(
            NativeQuicListenerHandle listener, NativeQuicListenerOperation operation,
            NativeQuicRequest request, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(1, listener.Value, (uint)operation, request, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<NativeQuicCompletion> ConnectionAsync(
            NativeQuicConnectionHandle connection, NativeQuicConnectionOperation operation,
            NativeQuicRequest request, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(2, connection.Value, (uint)operation, request, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<NativeQuicCompletion> StreamAsync(
            NativeQuicStreamHandle stream, NativeQuicStreamOperation operation,
            NativeQuicRequest request, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(3, stream.Value, (uint)operation, request, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask<NativeQuicCounts> GetCountsAsync(CancellationToken cancellationToken = default)
        {
            NativeApi api = await m_library.LoadAsync(cancellationToken).ConfigureAwait(false);
            return api.GetCounts();
        }

        /// <inheritdoc/>
        public async ValueTask<NativeQuicPeerEvidence> GetPeerEvidenceAsync(
            NativeQuicConnectionHandle connection, CancellationToken cancellationToken = default)
        {
            NativeQuicCompletion result = await ConnectionAsync(connection,
                NativeQuicConnectionOperation.Information, NativeQuicRequest.Default, cancellationToken)
                .ConfigureAwait(false);
            if (result.Status != NativeQuicStatus.Success)
            {
                throw new IOException($"Native QUIC peer evidence failed: {result.Status}.");
            }
            return new NativeQuicPeerEvidence(result.Payload);
        }

        /// <summary>
        /// Creates one independently owned native endpoint.
        /// </summary>
        private async ValueTask<NativeQuicCompletion> CreateAsync(
            bool listen, NativeQuicOptions options, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(options);
            NativeApi api = await m_library.LoadAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            NativeQuicStatus status = api.Create(listen, options, out NativeApi.Handle operation);
            return status == NativeQuicStatus.Success ?
                await ObserveAsync(api, operation, 0, null, cancellationToken).ConfigureAwait(false) :
                AdmissionFailure(status);
        }

        /// <summary>
        /// Begins an operation without a thread-pool wrapper around blocking I/O.
        /// </summary>
        private async ValueTask<NativeQuicCompletion> ExecuteAsync(
            uint category, ulong handle, uint kind, NativeQuicRequest request, CancellationToken cancellationToken)
        {
            request.Cancellation?.ThrowIfDisposed();
            CancellationToken requestToken = request.Cancellation?.Token ?? default;
            NativeApi api = await m_library.LoadAsync(cancellationToken).ConfigureAwait(false);
            request.Cancellation?.ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            requestToken.ThrowIfCancellationRequested();
            NativeQuicStatus status = api.Begin(category, handle, kind, request, out NativeApi.Handle operation);
            return status == NativeQuicStatus.Success ?
                await ObserveAsync(api, operation, request.CancellationErrorCode, request.Cancellation,
                    cancellationToken)
                    .ConfigureAwait(false) :
                AdmissionFailure(status);
        }

        /// <summary>
        /// Drains even after cancellation: no early release, lost created handle or discarded partial write.
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        private async ValueTask<NativeQuicCompletion> ObserveAsync(
            NativeApi api, NativeApi.Handle operation, ulong errorCode, NativeQuicCancellation? cancellation,
            CancellationToken cancellationToken)
        {
            CancellationToken requestToken = cancellation?.Token ?? default;
            var state = new CancellationState(api, operation, errorCode, cancellation);
            using CancellationTokenRegistration registration = cancellationToken.Register(
                static state => ((CancellationState)state!).Cancel(), state);
            using CancellationTokenRegistration requestRegistration = requestToken != cancellationToken ?
                requestToken.Register(static state => ((CancellationState)state!).Cancel(), state) : default;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
            bool observed = false;
            try
            {
                while (true)
                {
                    NativeQuicStatus status = api.Poll(operation, buffer.AsSpan(0, 65536), out NativeApi.Result result);
                    if (status == NativeQuicStatus.Pending)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(1), m_timeProvider, CancellationToken.None)
                            .ConfigureAwait(false);
                        continue;
                    }
                    if (status != NativeQuicStatus.Success || result.State != 2 || result.PayloadLength > 65536)
                    {
                        throw new InvalidDataException("Native QUIC result violated its checked ABI contract.");
                    }
                    observed = true;
                    return new NativeQuicCompletion
                    {
                        Status = (NativeQuicStatus)result.Status,
                        NativeStatus = (NativeQuicStatus)result.NativeStatus,
                        CancellationRequested = result.CancelRequested != 0,
                        BytesTransferred = result.Written,
                        Payload = new ByteString(buffer.AsSpan(0, (int)result.PayloadLength).ToArray()),
                        Listener = new NativeQuicListenerHandle(result.Listener.Value),
                        Connection = new NativeQuicConnectionHandle(result.Connection.Value),
                        Stream = new NativeQuicStreamHandle(result.Stream.Value),
                        StreamId = result.StreamId,
                        ErrorCode = result.ErrorCode,
                        FinalSize = result.FinalSize,
                        ReliableSize = result.ReliableSize,
                        ResetFrameType = result.Frame,
                        MaximumDatagramPayload = result.MaximumDatagramPayload,
                        Remote = result.Remote != 0,
                        ReadOutcome = result.Outcome
                    };
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                if (observed)
                {
                    api.Release(operation);
                }
            }
        }

        /// <summary>
        /// Represents immediate refusal without inventing an operation handle.
        /// </summary>
        private static NativeQuicCompletion AdmissionFailure(NativeQuicStatus status)
        {
            return new NativeQuicCompletion { Status = status, NativeStatus = status };
        }

        /// <summary>
        /// Keeps native cancellation strongly typed while using the BCL callback registration.
        /// </summary>
        private sealed class CancellationState(
            NativeApi api, NativeApi.Handle operation, ulong errorCode, NativeQuicCancellation? cancellation)
        {
            /// <summary>
            /// Signals, but never waits for or releases, the native operation.
            /// </summary>
            internal void Cancel()
            {
                ulong requestedCode = cancellation is { IsCancellationRequested: true } ?
                    cancellation.ErrorCode : errorCode;
                api.Cancel(operation, requestedCode);
            }
        }

        private readonly NativeQuicLibrary m_library;
        private readonly TimeProvider m_timeProvider;
    }

    /// <summary>
    /// Optional DI registration. No Core/client/server/PubSub assembly depends on this package.
    /// </summary>
    public static class NativeQuicServiceCollectionExtensions
    {
        /// <summary>
        /// Adds one injectable backend using a reviewed trusted native asset.
        /// </summary>
        public static IServiceCollection AddNativeQuicGo(
            this IServiceCollection services, NativeQuicLibrary library)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(library);
            services.TryAddSingleton<INativeQuicBackend>(provider =>
                new NativeQuicBackend(library, provider.GetService<TimeProvider>()));
            return services;
        }
    }
}
