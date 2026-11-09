/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Persists registrar identity, software status, and application relations through an injected file system.
    /// One instance must exclusively own its checkpoint path. Use a transactional lifecycle provider for
    /// multiple writer processes or replicated deployments. The host protects this authorization data from writes.
    /// </summary>
    public sealed class FileSystemDeviceRegistrarLifecycle :
        IDeviceRegistrarLifecycle,
        IDeviceRegistrarIdentityLifecycle,
        IDeviceRegistrarCertificateLifecycle,
        IDeviceRegistrarApplicationScopeLifecycle,
        IDisposable
    {
        /// <summary>
        /// Creates a single-host store. Namespace URIs, rather than transient namespace indexes, identify GDS records.
        /// The file system is borrowed; its Replace operation must publish a complete checkpoint indivisibly.
        /// </summary>
        public FileSystemDeviceRegistrarLifecycle(
            string path,
            NamespaceTable namespaceUris,
            IFileSystem? fileSystem = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A lifecycle checkpoint path is required.", nameof(path));
            }
            m_path = path;
            m_namespaceUris = namespaceUris ?? throw new ArgumentNullException(nameof(namespaceUris));
            m_fileSystem = fileSystem ?? LocalFileSystem.Instance;
        }

        /// <summary>
        /// Creates a hosted store that cannot process requests until its registrar model binds the namespace table.
        /// </summary>
        internal FileSystemDeviceRegistrarLifecycle(IFileSystem? fileSystem, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A lifecycle checkpoint path is required.", nameof(path));
            }
            m_path = path;
            m_fileSystem = fileSystem ?? LocalFileSystem.Instance;
        }

        /// <summary>
        /// Binds the one model host before any checkpoint is decoded or published.
        /// </summary>
        internal void BindNamespaceTable(NamespaceTable namespaceUris)
        {
            if (namespaceUris == null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            NamespaceTable? previous = Interlocked.CompareExchange(ref m_namespaceUris, namespaceUris, null);
            if (previous != null && !ReferenceEquals(previous, namespaceUris))
            {
                throw new InvalidOperationException("The registrar lifecycle is already bound to another model host.");
            }
        }

        /// <inheritdoc/>
        public async ValueTask RecordCandidateAsync(
            string productInstanceUri,
            NodeId applicationId,
            ByteString selectedIdentity,
            bool requiresSoftwareUpdate,
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async (state, ct) =>
            {
                await state.RecordCandidateAsync(
                    productInstanceUri, applicationId, selectedIdentity, requiresSoftwareUpdate, ct)
                    .ConfigureAwait(false);
                return true;
            }, write: true, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask<DeviceRegistrarIdentityCandidate?> FindCandidateByCertificateAsync(
            ByteString channelCertificate,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                (state, ct) => state.FindCandidateByCertificateAsync(channelCertificate, ct),
                write: false, cancellationToken);
        }

        /// <summary>
        /// Records a DCA certificate only after the host has verified issuance and installation for this device.
        /// </summary>
        public async ValueTask RegisterDeviceAsync(
            string productInstanceUri,
            NodeId applicationId,
            ByteString certificate,
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async (state, ct) =>
            {
                await state.RegisterDeviceAsync(productInstanceUri, applicationId, certificate, ct)
                    .ConfigureAwait(false);
                return true;
            }, write: true, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask RecordIssuedDcaCertificateAsync(
            string productInstanceUri,
            NodeId applicationId,
            ByteString certificate,
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async (state, ct) =>
            {
                await state.RecordIssuedDcaCertificateAsync(productInstanceUri, applicationId, certificate, ct)
                    .ConfigureAwait(false);
                return true;
            }, write: true, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask<bool> ConfirmDcaCertificateAsync(
            ByteString channelCertificate,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                (state, ct) => state.ConfirmDcaCertificateAsync(channelCertificate, ct),
                write: true, cancellationToken, publish: changed => changed);
        }

        /// <summary>
        /// Returns a detached device snapshot, or null when the device is unknown.
        /// </summary>
        public ValueTask<DeviceRegistrarDevice?> FindDeviceAsync(
            string productInstanceUri,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                (state, ct) => state.FindDeviceAsync(productInstanceUri, ct), write: false, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<string?> FindDeviceByCertificateAsync(
            ByteString certificate,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                (state, ct) => state.FindDeviceByCertificateAsync(certificate, ct), write: false, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<DeviceRegistrarApplicationBinding?> FindApplicationAsync(
            NodeId applicationId,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                (state, ct) => state.FindApplicationAsync(applicationId, ct), write: false, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<bool> UpdateSoftwareStatusAsync(
            string productInstanceUri,
            bool status,
            string softwareRevision,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                (state, ct) => state.UpdateSoftwareStatusAsync(productInstanceUri, status, softwareRevision, ct),
                write: true, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask RestrictToDcaApplicationAsync(
            string productInstanceUri,
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async (state, ct) =>
            {
                await state.RestrictToDcaApplicationAsync(productInstanceUri, ct).ConfigureAwait(false);
                return true;
            }, write: true, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask RequireManagedApplicationAllowedAsync(
            string productInstanceUri,
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async (state, ct) =>
            {
                await state.RequireManagedApplicationAllowedAsync(productInstanceUri, ct).ConfigureAwait(false);
                return true;
            }, write: false, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask AssociateApplicationAsync(
            string productInstanceUri,
            NodeId applicationId,
            string protocolUri,
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async (state, ct) =>
            {
                await state.AssociateApplicationAsync(productInstanceUri, applicationId, protocolUri, ct)
                    .ConfigureAwait(false);
                return true;
            }, write: true, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Releases the serialization gate after the host stops dispatching lifecycle operations.
        /// Does not dispose the borrowed file system.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                m_gate.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Reloads and validates authorization state for each operation; failed mutations never publish local state.
        /// </summary>
        /// <typeparam name="T">The lifecycle operation's result type.</typeparam>
        private async ValueTask<T> ExecuteAsync<T>(
            Func<MemoryDeviceRegistrarLifecycle, CancellationToken, ValueTask<T>> operation,
            bool write,
            CancellationToken cancellationToken,
            Predicate<T>? publish = null)
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(FileSystemDeviceRegistrarLifecycle));
            }
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                RequireNamespaceTable();
                MemoryDeviceRegistrarLifecycle state = await LoadAsync(cancellationToken).ConfigureAwait(false);
                T result = await operation(state, cancellationToken).ConfigureAwait(false);
                if (write && (publish == null || publish(result)))
                {
                    await SaveAsync(state.CaptureState(), cancellationToken).ConfigureAwait(false);
                }
                return result;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Reads a bounded checkpoint and reconstructs it through the same ownership checks as live operations.
        /// Corrupt or incompatible data is reported; it is never replaced with an empty authorization database.
        /// </summary>
        private async ValueTask<MemoryDeviceRegistrarLifecycle> LoadAsync(CancellationToken cancellationToken)
        {
            var state = new MemoryDeviceRegistrarLifecycle();
            if (!m_fileSystem.Exists(m_path))
            {
                return state;
            }
            using Stream input = m_fileSystem.OpenRead(m_path);
            using var buffer = new MemoryStream();
            byte[] block = new byte[8192];
            int read;
#if NETSTANDARD2_0 || NETFRAMEWORK
            while ((read = await input.ReadAsync(block, 0, block.Length, cancellationToken).ConfigureAwait(false)) > 0)
#else
            while ((read = await input.ReadAsync(block.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
#endif
            {
                if (buffer.Length + read > MaximumCheckpointBytes)
                {
                    throw new InvalidDataException("The registrar lifecycle checkpoint exceeds its size limit.");
                }
#if NETSTANDARD2_0 || NETFRAMEWORK
                await buffer.WriteAsync(block, 0, read, cancellationToken).ConfigureAwait(false);
#else
                await buffer.WriteAsync(block.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
#endif
            }
            try
            {
                using JsonDocument document = JsonDocument.Parse(buffer.ToArray());
                JsonElement root = document.RootElement;
                RequireUniqueObject(root);
                int version = root.GetProperty("version").GetInt32();
                if (version is not (1 or 2))
                {
                    throw new InvalidDataException("The registrar lifecycle checkpoint version is not supported.");
                }
                var candidateProducts = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonElement candidate in root.GetProperty("candidates").EnumerateArray())
                {
                    RequireUniqueObject(candidate);
                    string product = Text(candidate, "product");
                    if (!candidateProducts.Add(product))
                    {
                        throw new InvalidDataException(
                            "The registrar lifecycle checkpoint repeats an identity candidate.");
                    }
                    await state.RecordCandidateAsync(
                        product, DecodeId(Text(candidate, "application")),
                        DecodeCertificate(candidate, "certificate"),
                        !candidate.GetProperty("issuanceAllowed").GetBoolean(), cancellationToken)
                        .ConfigureAwait(false);
                }
                var devices = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonElement device in root.GetProperty("devices").EnumerateArray())
                {
                    RequireUniqueObject(device);
                    string product = Text(device, "product");
                    if (!devices.Add(product))
                    {
                        throw new InvalidDataException("The registrar lifecycle checkpoint repeats a device.");
                    }
                    NodeId dcaId = DecodeId(Text(device, "application"));
                    ByteString certificate = DecodeCertificate(device, "certificate");
                    if (!certificate.IsEmpty)
                    {
                        await state.RegisterDeviceAsync(product, dcaId, certificate, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    DeviceRegistrarDevice? registered = await state.FindDeviceAsync(product, cancellationToken)
                        .ConfigureAwait(false);
                    if (registered == null || registered.DcaApplicationId != dcaId)
                    {
                        throw new InvalidDataException("A lifecycle device does not match an identity or DCA binding.");
                    }
                    await state.UpdateSoftwareStatusAsync(
                        product, device.GetProperty("softwareStatus").GetBoolean(),
                        Text(device, "softwareRevision"), cancellationToken).ConfigureAwait(false);
                    if (device.TryGetProperty("pendingCertificate", out _))
                    {
                        ByteString pending = DecodeCertificate(device, "pendingCertificate");
                        if (!pending.IsEmpty)
                        {
                            if (pending == certificate)
                            {
                                throw new InvalidDataException(
                                    "An active DCA credential cannot also await installation.");
                            }
                            await state.RecordIssuedDcaCertificateAsync(product, dcaId, pending, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                    var applications = new HashSet<NodeId>();
                    foreach (JsonElement application in device.GetProperty("applications").EnumerateArray())
                    {
                        RequireUniqueObject(application);
                        NodeId id = DecodeId(Text(application, "id"));
                        if (!applications.Add(id))
                        {
                            throw new InvalidDataException(
                                "The registrar lifecycle checkpoint repeats an application.");
                        }
                        await state.AssociateApplicationAsync(
                            product, id, Text(application, "protocol"), cancellationToken).ConfigureAwait(false);
                    }
                    // Legacy checkpoints lack scope evidence; absence must not imply composite privilege.
                    if (version == 1 || device.GetProperty("singletonRestricted").GetBoolean())
                    {
                        await state.RestrictToDcaApplicationAsync(product, cancellationToken).ConfigureAwait(false);
                    }
                }
                if (!candidateProducts.IsSubsetOf(devices))
                {
                    throw new InvalidDataException("An identity candidate has no corresponding lifecycle device.");
                }
                return state;
            }
            catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException or
                KeyNotFoundException or ServiceResultException or ArgumentException)
            {
                throw new InvalidDataException("The registrar lifecycle checkpoint is invalid.", error);
            }
        }

        /// <summary>
        /// Stages a complete checkpoint and publishes it only after writing, flushing, and observing cancellation.
        /// Cancellation after publication does not turn a committed operation into an uncommitted response.
        /// </summary>
        private async ValueTask SaveAsync(DeviceRegistrarLifecycleState state, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", 2);
                writer.WriteStartArray("candidates");
                foreach (DeviceRegistrarIdentityCandidate candidate in state.Candidates)
                {
                    writer.WriteStartObject();
                    writer.WriteString("product", candidate.ProductInstanceUri);
                    writer.WriteString("application", EncodeId(candidate.ApplicationId));
                    writer.WriteBase64String("certificate", candidate.SelectedIdentity.Span);
                    writer.WriteBoolean("issuanceAllowed", candidate.CertificateIssuanceAllowed);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteStartArray("devices");
                foreach (DeviceRegistrarDevice device in state.Devices)
                {
                    writer.WriteStartObject();
                    writer.WriteString("product", device.ProductInstanceUri);
                    writer.WriteString("application", EncodeId(device.DcaApplicationId));
                    writer.WriteBase64String("certificate", device.DcaCertificate.Span);
                    writer.WriteBase64String("pendingCertificate", device.PendingDcaCertificate.Span);
                    writer.WriteBoolean("softwareStatus", device.SoftwareStatus);
                    writer.WriteBoolean("singletonRestricted", device.IsSingletonRestricted);
                    writer.WriteString("softwareRevision", device.SoftwareRevision);
                    writer.WriteStartArray("applications");
                    foreach (DeviceRegistrarApplication application in device.Applications)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("id", EncodeId(application.ApplicationId));
                        writer.WriteString("protocol", application.ProtocolUri);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            if (buffer.Length > MaximumCheckpointBytes)
            {
                throw new InvalidDataException("The registrar lifecycle checkpoint exceeds its size limit.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            string staging = m_path + "." + Guid.NewGuid().ToString("N") + ".pending";
            ExceptionDispatchInfo? publicationError = null;
            try
            {
                using (Stream output = m_fileSystem.OpenWrite(staging))
                {
                    buffer.Position = 0;
                    await buffer.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                m_fileSystem.Replace(staging, m_path);
            }
            catch (Exception error)
            {
                publicationError = ExceptionDispatchInfo.Capture(error);
            }
            try
            {
                if (m_fileSystem.Exists(staging))
                {
                    m_fileSystem.Delete(staging);
                }
            }
            catch (Exception cleanupError) when (publicationError != null)
            {
                throw new AggregateException(
                    "Publishing the registrar checkpoint and removing its staging file both failed.",
                    publicationError.SourceException, cleanupError);
            }
            publicationError?.Throw();
        }

        /// <summary>
        /// Persists identifiers with namespace URIs so namespace ordering may change on restart.
        /// </summary>
        private string EncodeId(NodeId id)
        {
            NamespaceTable namespaces = RequireNamespaceTable();
            if (id.IsNull || string.IsNullOrEmpty(namespaces.GetString(id.NamespaceIndex)))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            return NodeId.ToExpandedNodeId(id, namespaces).ToString();
        }

        /// <summary>
        /// Resolves persisted namespace URIs against the current host rather than reusing prior indexes.
        /// </summary>
        private NodeId DecodeId(string encoded)
        {
            NamespaceTable namespaces = RequireNamespaceTable();
            ExpandedNodeId expanded = ExpandedNodeId.Parse(encoded);
            if (expanded.ServerIndex != 0 ||
                (expanded.NamespaceIndex != 0 && string.IsNullOrEmpty(expanded.NamespaceUri)))
            {
                throw new InvalidDataException("A lifecycle application identifier has no stable local namespace URI.");
            }
            if (!string.IsNullOrEmpty(expanded.NamespaceUri))
            {
                namespaces.GetIndexOrAppend(expanded.NamespaceUri);
            }
            NodeId id = ExpandedNodeId.ToNodeId(expanded, namespaces);
            if (id.IsNull)
            {
                throw new InvalidDataException("A lifecycle application identifier cannot be null.");
            }
            return id;
        }

        /// <summary>
        /// Returns the namespace mapping owned by the bound model host.
        /// </summary>
        private NamespaceTable RequireNamespaceTable()
        {
            return m_namespaceUris ?? throw new ServiceResultException(
                StatusCodes.BadServerHalted, "The registrar model has not bound its namespace table.");
        }

        /// <summary>
        /// Requires a string, including an explicitly empty string where the persisted contract permits it.
        /// </summary>
        private static string Text(JsonElement element, string property)
        {
            return element.GetProperty(property).GetString() ??
                throw new InvalidDataException("A lifecycle checkpoint string cannot be null.");
        }

        /// <summary>
        /// Decodes a certificate without treating its untrusted bytes as proof of device possession.
        /// </summary>
        private static ByteString DecodeCertificate(JsonElement element, string property)
        {
            return ByteString.From(Convert.FromBase64String(Text(element, property)).AsSpan());
        }

        /// <summary>
        /// Rejects duplicate JSON members instead of allowing ambiguous authorization state.
        /// </summary>
        private static void RequireUniqueObject(JsonElement element)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException("A lifecycle checkpoint repeats a JSON member.");
                }
            }
        }

        /// <summary>The supported maximum size of a complete checkpoint.</summary>
        private const int MaximumCheckpointBytes = 32 * 1024 * 1024;

        /// <summary>Serializes all reads and staged mutations through this provider instance.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>The caller-selected exclusive checkpoint path.</summary>
        private readonly string m_path;

        /// <summary>The current host's namespace URI mapping.</summary>
        private NamespaceTable? m_namespaceUris;

        /// <summary>The borrowed file system with indivisible replacement semantics.</summary>
        private readonly IFileSystem m_fileSystem;

        /// <summary>Indicates that the owning host has stopped and disposed this provider.</summary>
        private int m_disposed;
    }
}
