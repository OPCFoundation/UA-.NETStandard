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

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// The server-owned caller identity that binds native continuation points.
    /// </summary>
    /// <param name="Session">The calling Session.</param>
    /// <param name="AuthorizationView">An identity that changes with the caller's effective access.</param>
    public readonly record struct RegistryCaller(string Session, string AuthorizationView);

    /// <summary>
    /// A provider-owned model or capabilities document and its named native record type.
    /// </summary>
    public sealed record RegistryAuxiliaryDocument(RegistryObjectValueDataType Value, string RecordType);

    /// <summary>
    /// One immutable committed generation of a native registry instance.
    /// </summary>
    public sealed class RegistryCommittedState
    {
        internal RegistryCommittedState(
            ulong revision,
            ByteString document,
            RegistryObjectValueDataType root,
            uint epoch)
        {
            Revision = revision;
            Document = document;
            m_root = root;
            Epoch = epoch;
        }

        /// <summary>
        /// Gets the store revision of this generation.
        /// </summary>
        public ulong Revision { get; }

        /// <summary>
        /// Gets the exact committed document bytes.
        /// </summary>
        public ByteString Document { get; }

        /// <summary>
        /// Gets the committed registry epoch.
        /// </summary>
        public uint Epoch { get; }

        /// <summary>
        /// Returns a private copy of the committed generic document.
        /// </summary>
        public RegistryObjectValueDataType CloneDocument()
        {
            return (RegistryObjectValueDataType)m_root.Clone();
        }

        internal RegistryObjectValueDataType Root => m_root;

        private readonly RegistryObjectValueDataType m_root;
    }

    /// <summary>
    /// Configures a <see cref="RegistryNativeHost"/>.
    /// </summary>
    public sealed class RegistryNativeHostOptions
    {
        /// <summary>
        /// Gets or sets the atomic complete-state store that owns the committed generations.
        /// </summary>
        public IRegistryStateStore? Store { get; set; }

        /// <summary>
        /// Gets or sets the mapper between generic documents and named native records.
        /// </summary>
        public RegistryRecordMapper? Mapper { get; set; }

        /// <summary>
        /// Gets or sets the Group collections that this registry instance exposes.
        /// </summary>
        public ArrayOf<string> Collections { get; set; }

        /// <summary>
        /// Gets or sets the named record DataType of the complete registry document.
        /// </summary>
        public string? RegistryRecordType { get; set; }

        /// <summary>
        /// Gets or sets the named record DataType of a Group in a collection.
        /// </summary>
        public Func<string, string>? GroupRecordType { get; set; }

        /// <summary>
        /// Gets or sets the named record DataType of a metadata Resource and its Versions.
        /// </summary>
        public string? ResourceRecordType { get; set; }

        /// <summary>
        /// Gets or sets the document committed when the store is uninitialized.
        /// It carries the registry epoch and identity; the host never invents them.
        /// </summary>
        public RegistryObjectValueDataType? InitialDocument { get; set; }

        /// <summary>
        /// Gets or sets domain validation of a complete resulting registry document.
        /// Throw a <see cref="ServiceResultException"/>, ideally implementing
        /// <see cref="IRegistryDiagnosticSource"/>, to reject it.
        /// </summary>
        public Action<RegistryObjectValueDataType>? Validate { get; set; }

        /// <summary>
        /// Gets additional provider-owned documents, such as model and capabilities.
        /// They are selected only at the registry root and use the registry epoch.
        /// </summary>
        public Dictionary<string, RegistryAuxiliaryDocument> Documents { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets or sets the Xids that no ordinary mutation may change, for example surfaced entries.
        /// </summary>
        public Func<ArrayOf<string>>? ProtectedPaths { get; set; }

        /// <summary>
        /// Gets or sets the NodeId returned for a committed target Xid.
        /// </summary>
        public Func<string, ExpandedNodeId>? TargetNodeId { get; set; }

        /// <summary>
        /// Gets or sets the message context used to measure Binary-encoded results.
        /// </summary>
        public IServiceMessageContext? MessageContext { get; set; }

        /// <summary>
        /// Gets or sets the maximum Binary-encoded size of one ReadDocument result.
        /// Larger documents are read through the native snapshot Methods.
        /// </summary>
        public long MaxResultBytes { get; set; } = 4 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the upper bound accepted for MaxItems.
        /// </summary>
        public uint MaxReadItems { get; set; } = 100000;

        /// <summary>
        /// Gets or sets the number of outstanding ReadDocument continuation points.
        /// </summary>
        public int MaxContinuationPoints { get; set; } = 64;

        /// <summary>
        /// Gets or sets the telemetry context used for logging.
        /// </summary>
        public ITelemetryContext? Telemetry { get; set; }
    }

    /// <summary>
    /// Owns the committed native state of one registry instance and implements the shared
    /// TypedAccess operations: bounded reads, typed replacement, typed Set/Remove changes,
    /// the RFC 7396 compatibility patch and deletion.
    /// </summary>
    /// <remarks>
    /// Every mutation reads one committed generation, validates the complete resulting document,
    /// commits it once by compare-and-swap and then activates it through <see cref="Activation"/>
    /// before the operation reports success. A failure before the store commit publishes nothing;
    /// an activation failure after it is reported as an uncertain result, never as a no-op.
    /// Authorization is the caller's responsibility; the host binds continuation points to the
    /// supplied <see cref="RegistryCaller"/>.
    /// </remarks>
    public sealed partial class RegistryNativeHost : IDisposable
    {
        /// <summary>
        /// Creates a host. <see cref="StartAsync"/> loads or initializes the committed state.
        /// </summary>
        /// <exception cref="ArgumentException">The options are incomplete.</exception>
        public RegistryNativeHost(RegistryNativeHostOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            m_store = options.Store ?? throw new ArgumentException("A state store is required.", nameof(options));
            m_mapper = options.Mapper ?? throw new ArgumentException("A record mapper is required.", nameof(options));
            if (options.Collections.Count == 0 || string.IsNullOrEmpty(options.RegistryRecordType) ||
                options.GroupRecordType is null || string.IsNullOrEmpty(options.ResourceRecordType) ||
                options.InitialDocument is null || options.MessageContext is null ||
                options.MaxResultBytes < 1 || options.MaxReadItems == 0 || options.MaxContinuationPoints < 1)
            {
                throw new ArgumentException("The native host options are incomplete or out of range.",
                    nameof(options));
            }
            foreach (string collection in options.Collections)
            {
                if (string.IsNullOrEmpty(collection) ||
                    !m_mapper.Catalog.TryGetType(options.GroupRecordType(collection), out _))
                {
                    throw new ArgumentException("Every collection needs a published Group record type.",
                        nameof(options));
                }
            }
            m_collections = options.Collections;
            m_registryRecordType = options.RegistryRecordType!;
            m_groupRecordType = options.GroupRecordType;
            m_resourceRecordType = options.ResourceRecordType!;
            m_initialDocument = (RegistryObjectValueDataType)options.InitialDocument.Clone();
            m_validate = options.Validate;
            foreach (KeyValuePair<string, RegistryAuxiliaryDocument> item in options.Documents)
            {
                var value = (RegistryObjectValueDataType)item.Value.Value.Clone();
                m_mapper.Project(value, item.Value.RecordType);
                m_documents.Add(item.Key, new RegistryAuxiliaryDocument(value, item.Value.RecordType));
            }
            m_protectedPaths = options.ProtectedPaths;
            m_targetNodeId = options.TargetNodeId;
            m_messageContext = options.MessageContext;
            m_maxResultBytes = options.MaxResultBytes;
            m_maxReadItems = options.MaxReadItems;
            m_maxContinuationPoints = options.MaxContinuationPoints;
            m_logger = options.Telemetry is null
                ? NullLogger.Instance
                : options.Telemetry.CreateLogger<RegistryNativeHost>();
        }

        /// <summary>
        /// Gets or sets the activation of a committed generation, for example a projection
        /// refresh. It is awaited under the commit gate before a mutation reports success.
        /// </summary>
        public Func<RegistryCommittedState, CancellationToken, ValueTask>? Activation { get; set; }

        /// <summary>
        /// Gets the collections exposed by this registry instance.
        /// </summary>
        public ArrayOf<string> Collections => m_collections;

        /// <summary>
        /// Gets the mapper used for named records.
        /// </summary>
        public RegistryRecordMapper Mapper => m_mapper;

        /// <summary>
        /// Gets the currently committed generation.
        /// </summary>
        /// <exception cref="InvalidOperationException">The host has not been started.</exception>
        public RegistryCommittedState Current => Volatile.Read(ref m_current) ??
            throw new InvalidOperationException("The native registry host has not been started.");

        /// <summary>
        /// Loads the committed generation, or commits the initial document into an uninitialized
        /// store, validates it and activates it. Corrupt committed state is rejected, not repaired.
        /// </summary>
        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            await m_commitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref m_current) is not null)
                {
                    throw new InvalidOperationException("The native registry host is already started.");
                }
                RegistryStoredState stored = await m_store.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (stored.Revision == 0)
                {
                    ValidateDocument(m_initialDocument);
                    ByteString initial = RegistryValues.ToJson(m_initialDocument);
                    RegistryStateCommit commit = await m_store.CommitAsync(0, initial, cancellationToken)
                        .ConfigureAwait(false);
                    if (StatusCode.IsBad(commit.StatusCode))
                    {
                        throw new ServiceResultException(commit.StatusCode,
                            "The registry store was initialized concurrently.");
                    }
                    stored = commit.State;
                }
                RegistryCommittedState state = Load(stored);
                Volatile.Write(ref m_current, state);
                Func<RegistryCommittedState, CancellationToken, ValueTask>? activate = Activation;
                m_projectionPending = true;
                if (activate is not null)
                {
                    await activate(state, cancellationToken).ConfigureAwait(false);
                }
                m_projectionPending = false;
            }
            finally
            {
                m_commitGate.Release();
            }
        }

        /// <summary>
        /// Reads a committed document atomically. The named view returns the appropriate record
        /// subtype; the generic view pages the top-level members or items of a container.
        /// Failures are returned as typed diagnostics in the result.
        /// </summary>
        public RegistryReadResultDataType Read(RegistryCaller caller, RegistryReadRequestDataType request)
        {
            try
            {
                return ReadCore(caller, request);
            }
            catch (Exception error) when (IsOperationFailure(error))
            {
                RegistryDiagnosticDataType diagnostic = Diagnostic(error);
                return new RegistryReadResultDataType
                {
                    StatusCode = diagnostic.StatusCode,
                    Epoch = 0,
                    ContinuationPoint = ByteString.Empty,
                    Issues = [diagnostic]
                };
            }
        }

        /// <summary>
        /// Registers or replaces one complete Group or metadata Resource from its named record.
        /// </summary>
        public ValueTask<RegistryMutationResultDataType> WriteAsync(
            RegistryWriteRequestDataType request,
            CancellationToken cancellationToken = default)
        {
            return CommitAsync(request?.TargetXid, state =>
            {
                if (request?.Definition is null || request.TargetXid is null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                        "A target Xid and a native record are required.");
                }
                string expected = RecordTypeOf(request.TargetXid, entitiesOnly: true);
                if (!m_mapper.Catalog.TryGetType(request.Definition.TypeId,
                        out RegistryNativeTypeDescriptor? type) ||
                    !m_mapper.Catalog.IsSubtype(type.Name, expected))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                        "The native record subtype does not match its target role.");
                }
                if (m_mapper.Restore(request.Definition) is not RegistryObjectValueDataType definition)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                        "An entity record must restore to an object.");
                }
                return RegistryMetadataMutation.Replace(state.Root, request.TargetXid, definition,
                    request.ExpectedEpoch, m_collections, ProtectedPaths(request.TargetXid), ValidateOrdinaryDocument);
            }, cancellationToken);
        }

        /// <summary>
        /// Applies typed Set and Remove operations to one existing Group or metadata Resource.
        /// </summary>
        public ValueTask<RegistryMutationResultDataType> ApplyChangesAsync(
            RegistryChangeRequestDataType request,
            CancellationToken cancellationToken = default)
        {
            return CommitAsync(request?.TargetXid, state =>
            {
                if (request?.TargetXid is null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument, "A target Xid is required.");
                }
                RecordTypeOf(request.TargetXid, entitiesOnly: true);
                return RegistryMetadataMutation.Apply(state.Root, request, m_collections, ProtectedPaths(request.TargetXid),
                    ValidateOrdinaryDocument);
            }, cancellationToken);
        }

        /// <summary>
        /// Applies the optional RFC 7396 compatibility patch. A JSON null member removes it.
        /// </summary>
        public ValueTask<RegistryMutationResultDataType> PatchAsync(
            string targetXid,
            ByteString patch,
            uint expectedEpoch,
            CancellationToken cancellationToken = default)
        {
            return CommitAsync(targetXid, state =>
            {
                RecordTypeOf(targetXid, entitiesOnly: true);
                if (patch.IsNull || RegistryValues.Parse(patch.Span) is not RegistryObjectValueDataType changes)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                        "A merge patch must be a JSON object.");
                }
                return RegistryMetadataMutation.Patch(state.Root, targetXid, changes, expectedEpoch,
                    m_collections, ProtectedPaths(targetXid), ValidateOrdinaryDocument);
            }, cancellationToken);
        }

        /// <summary>
        /// Deletes one Group, with its Resources, or one metadata Resource.
        /// </summary>
        public ValueTask<RegistryMutationResultDataType> DeleteAsync(
            string targetXid,
            uint expectedEpoch,
            CancellationToken cancellationToken = default)
        {
            return CommitAsync(targetXid, state =>
            {
                RecordTypeOf(targetXid, entitiesOnly: true);
                return RegistryMetadataMutation.Delete(state.Root, targetXid, expectedEpoch, m_collections,
                    ProtectedPaths(targetXid), ValidateOrdinaryDocument);
            }, cancellationToken);
        }

        /// <summary>
        /// Adds or removes a String label through the registry's shared validation/CAS activation path.
        /// </summary>
        public ValueTask<RegistryMutationResultDataType> LabelAsync(
            string targetXid, string name, string? value, uint expectedEpoch,
            CancellationToken cancellationToken = default)
        {
            return CommitAsync(targetXid, state => RegistryMetadataMutation.Label(state.Root, targetXid, name, value,
                expectedEpoch, m_collections, ProtectedPaths(targetXid), ValidateOrdinaryDocument), cancellationToken);
        }

        /// <summary>
        /// Selects the document pinned by a native snapshot from the current committed generation.
        /// </summary>
        /// <exception cref="ServiceResultException">The selection or expected epoch is invalid.</exception>
        public RegistrySnapshotSource SelectSnapshot(RegistrySnapshotOpenRequestDataType request)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (request.View > 1)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Unknown native view.");
            }
            RegistryCommittedState state = Current;
            (RegistryValueDataType value, string recordType, uint epoch) =
                SelectDocument(state, request.TargetXid, request.DocumentKind);
            if (request.ExpectedEpoch != 0 && request.ExpectedEpoch != epoch)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "ExpectedEpoch differs from the target.");
            }
            IEncodeable document = request.View == 1
                ? m_mapper.Project(value, recordType)
                : (RegistryValueDataType)value.Clone();
            return new RegistrySnapshotSource(document, epoch, state.Epoch);
        }

        /// <summary>
        /// Returns the named record and epoch of a committed entity, or of the registry for "/".
        /// </summary>
        /// <exception cref="ServiceResultException">The entity does not exist.</exception>
        public (RegistryRecordDataType Record, uint Epoch) ReadRecord(RegistryCommittedState state, string xid)
        {
            if (state is null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            (RegistryValueDataType value, string recordType, uint epoch) = Select(state, xid);
            return (m_mapper.Project(value, recordType), epoch);
        }

        /// <summary>
        /// Returns a copy of the committed generic value and epoch of an entity, or of the registry for "/".
        /// </summary>
        /// <exception cref="ServiceResultException">The entity does not exist.</exception>
        public (RegistryValueDataType Value, uint Epoch) ReadValue(RegistryCommittedState state, string xid)
        {
            if (state is null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            (RegistryValueDataType value, _, uint epoch) = Select(state, xid);
            return ((RegistryValueDataType)value.Clone(), epoch);
        }

        /// <summary>
        /// Releases the continuation points of a Session.
        /// </summary>
        public void ReleaseSession(string session)
        {
            lock (m_pagesGate)
            {
                var released = new List<ByteString>();
                foreach (KeyValuePair<ByteString, Page> page in m_pages)
                {
                    if (page.Value.Caller.Session == session)
                    {
                        released.Add(page.Key);
                    }
                }
                foreach (ByteString token in released)
                {
                    m_pages.Remove(token);
                }
            }
        }

        /// <summary>
        /// Releases continuation points and the commit gate. The host does not own its store.
        /// </summary>
        public void Dispose()
        {
            lock (m_pagesGate)
            {
                m_pages.Clear();
            }
            m_commitGate.Dispose();
        }

        private RegistryReadResultDataType ReadCore(RegistryCaller caller, RegistryReadRequestDataType request)
        {
            if (request is null)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "A read request is required.");
            }
            if (request.View > 1)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Unknown native view.");
            }
            if (request.MaxItems == 0 || request.MaxItems > m_maxReadItems)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                    "MaxItems must be positive and within the advertised limit.");
            }
            if (!request.ContinuationPoint.IsNull && request.ContinuationPoint.Length > 0)
            {
                Page? page;
                lock (m_pagesGate)
                {
                    if (!m_pages.TryGetValue(request.ContinuationPoint, out page) || page.Caller != caller)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState,
                            "The continuation point is invalid, consumed or bound to another caller.");
                    }
                    if (page.Target != request.TargetXid || page.Kind != request.DocumentKind ||
                        page.Maximum != request.MaxItems || request.View != 0)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState,
                            "The continuation context differs from the first read.");
                    }
                    m_pages.Remove(request.ContinuationPoint);
                }
                return NextPage(page);
            }
            RegistryCommittedState state = Current;
            (RegistryValueDataType value, string recordType, uint epoch) =
                SelectDocument(state, request.TargetXid, request.DocumentKind);
            if (request.View == 1)
            {
                return Checked(new RegistryReadResultDataType
                {
                    StatusCode = StatusCodes.Good,
                    Epoch = epoch,
                    Document = new ExtensionObject(m_mapper.Project(value, recordType)),
                    ContinuationPoint = ByteString.Empty,
                    Issues = []
                });
            }
            string target = request.TargetXid ?? string.Empty;
            string kind = request.DocumentKind ??
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "A document kind is required.");
            if (value is RegistryObjectValueDataType map)
            {
                var names = new string?[map.Members.Count];
                var values = new RegistryValueDataType[map.Members.Count];
                for (int index = 0; index < names.Length; index++)
                {
                    names[index] = map.Members[index].Name;
                    values[index] = map.Members[index].Value;
                }
                return NextPage(new Page(caller, epoch, target, kind, names, values, 0, request.MaxItems));
            }
            if (value is RegistryArrayValueDataType array)
            {
                var items = new RegistryValueDataType[array.Items.Count];
                for (int index = 0; index < items.Length; index++)
                {
                    items[index] = array.Items[index];
                }
                return NextPage(new Page(caller, epoch, target, kind, null, items, 0, request.MaxItems));
            }
            return Checked(new RegistryReadResultDataType
            {
                StatusCode = StatusCodes.Good,
                Epoch = epoch,
                Document = new ExtensionObject((RegistryValueDataType)value.Clone()),
                ContinuationPoint = ByteString.Empty,
                Issues = []
            });
        }

        private RegistryReadResultDataType NextPage(Page page)
        {
            int count = (int)Math.Min(page.Maximum, (uint)(page.Values.Length - page.Offset));
            RegistryValueDataType value;
            if (page.Names is null)
            {
                var items = new RegistryValueDataType[count];
                for (int index = 0; index < count; index++)
                {
                    items[index] = (RegistryValueDataType)page.Values[page.Offset + index].Clone();
                }
                value = new RegistryArrayValueDataType { Kind = 4, Items = items };
            }
            else
            {
                var members = new RegistryMemberDataType[count];
                for (int index = 0; index < count; index++)
                {
                    members[index] = new RegistryMemberDataType
                    {
                        Name = page.Names[page.Offset + index],
                        Value = (RegistryValueDataType)page.Values[page.Offset + index].Clone()
                    };
                }
                value = new RegistryObjectValueDataType { Kind = 5, Members = members };
            }
            int next = page.Offset + count;
            ByteString token = next < page.Values.Length ? NewToken() : ByteString.Empty;
            RegistryReadResultDataType result = Checked(new RegistryReadResultDataType
            {
                StatusCode = StatusCodes.Good,
                Epoch = page.Epoch,
                Document = new ExtensionObject(value),
                ContinuationPoint = token,
                Issues = []
            });
            if (token.Length > 0)
            {
                lock (m_pagesGate)
                {
                    if (m_pages.Count >= m_maxContinuationPoints)
                    {
                        throw new ServiceResultException(StatusCodes.BadNoContinuationPoints,
                            "The native read continuation capacity is exhausted.");
                    }
                    m_pages.Add(token, page with { Offset = next });
                }
            }
            return result;
        }

        private RegistryReadResultDataType Checked(RegistryReadResultDataType result)
        {
            try
            {
                RegistryEncodedSize.Measure(result, m_messageContext, m_maxResultBytes);
            }
            catch (ServiceResultException error) when (error.StatusCode == StatusCodes.BadEncodingLimitsExceeded)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The result exceeds the response limit; read it through a native snapshot.");
            }
            return result;
        }

        private async ValueTask<RegistryMutationResultDataType> CommitAsync(
            string? targetXid,
            Func<RegistryCommittedState, RegistryMetadataCommit> prepare,
            CancellationToken cancellationToken)
        {
            await m_commitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                RegistryCommittedState state = Current;
                RegistryMetadataCommit commit;
                try
                {
                    commit = prepare(state);
                }
                catch (Exception error) when (IsOperationFailure(error))
                {
                    return MutationFailure(error);
                }
                if (!commit.Changed)
                {
                    if (m_projectionPending)
                    {
                        return MutationResult(StatusCodes.UncertainNotAllNodesAvailable, commit.TargetEpoch,
                            targetXid, [ProjectionPending()]);
                    }
                    return MutationResult(StatusCodes.Good, commit.TargetEpoch, targetXid, []);
                }
                ByteString document = RegistryValues.ToJson(commit.Document);
                RegistryStateCommit stored;
                try
                {
                    stored = await m_store.CommitAsync(state.Revision, document, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException error)
                {
                    return MutationFailure(error);
                }
                if (StatusCode.IsBad(stored.StatusCode))
                {
                    return MutationFailure(new ServiceResultException(stored.StatusCode,
                        "The committed registry state changed concurrently."));
                }
                var next = new RegistryCommittedState(stored.State.Revision, document, commit.Document,
                    RegistryMetadataMutation.Epoch(commit.Document));
                try
                {
                    m_projectionPending = true;
                    await NotifyActivatingAsync(next).ConfigureAwait(false);
                    Volatile.Write(ref m_current, next);
                    Func<RegistryCommittedState, CancellationToken, ValueTask>? activate = Activation;
                    if (activate is not null)
                    {
                        // The generation is durable; its activation is not abandoned on caller cancellation.
                        await activate(next, CancellationToken.None).ConfigureAwait(false);
                    }
                    await NotifyActivatedAsync(next).ConfigureAwait(false);
                    m_projectionPending = false;
                }
                catch (Exception error)
                {
                    Volatile.Write(ref m_current, next);
                    m_logger.ActivationFailed(next.Revision, error);
                    return MutationResult(StatusCodes.UncertainNotAllNodesAvailable, commit.TargetEpoch,
                        targetXid, [ProjectionPending()]);
                }
                return MutationResult(StatusCodes.Good, commit.TargetEpoch, targetXid, []);
            }
            finally
            {
                m_commitGate.Release();
            }
        }

        private RegistryMutationResultDataType MutationResult(
            StatusCode status,
            uint epoch,
            string? targetXid,
            ArrayOf<RegistryDiagnosticDataType> issues)
        {
            return new RegistryMutationResultDataType
            {
                StatusCode = status,
                Epoch = epoch,
                Target = targetXid is not null && m_targetNodeId is not null && epoch != 0
                    ? m_targetNodeId(targetXid)
                    : ExpandedNodeId.Null,
                Issues = issues
            };
        }

        private static RegistryDiagnosticDataType ProjectionPending() => new()
        {
            StatusCode = StatusCodes.UncertainNotAllNodesAvailable,
            Code = "E_PROJECTION_PENDING",
            Path = [],
            Detail = "The change is committed; its address-space projection is not active."
        };

        private static RegistryMutationResultDataType MutationFailure(Exception error)
        {
            RegistryDiagnosticDataType diagnostic = Diagnostic(error);
            return new RegistryMutationResultDataType
            {
                StatusCode = diagnostic.StatusCode,
                Epoch = 0,
                Target = ExpandedNodeId.Null,
                Issues = [diagnostic]
            };
        }

        private RegistryCommittedState Load(RegistryStoredState stored)
        {
            if (stored.Document.IsNull ||
                RegistryValues.Parse(stored.Document.Span) is not RegistryObjectValueDataType root)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError,
                    "The committed registry document is not an object.");
            }
            ValidateDocument(root);
            return new RegistryCommittedState(stored.Revision, stored.Document, root,
                RegistryMetadataMutation.Epoch(root));
        }

        private void ValidateDocument(RegistryObjectValueDataType document)
        {
            m_mapper.Project(document, m_registryRecordType);
            m_validate?.Invoke(document);
        }

        private ArrayOf<string> ProtectedPaths(string? target = null, RegistryNativeProvider? provider = null)
        {
            var paths = new List<string>();
            if (m_protectedPaths is not null)
            {
                foreach (string path in m_protectedPaths())
                {
                    paths.Add(path);
                }
            }
            lock (m_providerGate)
            {
                foreach (RegistryNativeProvider owner in m_providers)
                {
                    if (owner == provider)
                    {
                        continue;
                    }
                    foreach (RegistryMemberDataType group in ProviderGroups(Current.Root, owner.Collection))
                    {
                        string path = "/" + owner.Collection + "/" + group.Name;
                        if (owner.Owns(path))
                        {
                            paths.Add(path);
                        }
                    }
                    if (target is not null && owner.Owns(target))
                    {
                        paths.Add(target);
                    }
                }
            }
            return paths.ToArray();
        }

        private (RegistryValueDataType Value, string RecordType, uint Epoch) SelectDocument(
            RegistryCommittedState state, string? xid, string? kind)
        {
            if (kind == "metadata")
            {
                return Select(state, xid);
            }
            if (xid is not (null or "" or "/"))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                    "A model or capabilities document is selected at the registry root.");
            }
            return kind is not null && m_documents.TryGetValue(kind, out RegistryAuxiliaryDocument? document)
                ? (document.Value, document.RecordType, state.Epoch)
                : throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The requested document kind is not provided by this registry.");
        }

        private (RegistryValueDataType Value, string RecordType, uint Epoch) Select(
            RegistryCommittedState state,
            string? xid)
        {
            string recordType = RecordTypeOf(xid, entitiesOnly: false);
            if (xid is null || xid.Length == 0 || xid == "/")
            {
                return (state.Root, recordType, state.Epoch);
            }
            RegistryValueDataType current = state.Root;
            string[] path = xid.Substring(1).Split('/');
            if (path.Length == 6)
            {
                string logical = "/" + string.Join("/", path, 0, 4);
                (RegistryValueDataType value, _, uint epoch) = Select(state, logical);
                string? retained = value is RegistryObjectValueDataType message &&
                    Member(message, "versionid") is RegistryStringValueDataType id ? id.Value : "1";
                if (path[5] != retained)
                {
                    throw new ServiceResultException(StatusCodes.BadNotFound,
                        "The selected sole metadata Version does not exist.");
                }
                return (value, recordType, epoch);
            }
            foreach (string part in path)
            {
                current = Member(current, part) ?? throw new ServiceResultException(StatusCodes.BadNotFound,
                    "The selected entity does not exist.");
            }
            if (current is not RegistryObjectValueDataType entity)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "The selected entity is not an object.");
            }
            return (entity, recordType, RegistryMetadataMutation.Epoch(entity));
        }

        private string RecordTypeOf(string? xid, bool entitiesOnly)
        {
            if (xid is null || xid.Length == 0 || xid == "/")
            {
                return entitiesOnly
                    ? throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                        "A Group or metadata Resource Xid is required.")
                    : m_registryRecordType;
            }
            string[] parts = xid[0] == '/' ? xid.Substring(1).Split('/') : [];
            bool known = false;
            foreach (string collection in m_collections)
            {
                known |= parts.Length > 0 && parts[0] == collection;
            }
            if (!known || (parts.Length != 2 && parts.Length != 4 && parts.Length != 6) ||
                (parts.Length >= 4 && parts[2] != "messages") ||
                (parts.Length == 6 && parts[4] != "versions") ||
                (entitiesOnly && parts.Length == 6))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                    "The Xid does not select a Group or metadata Resource of this registry.");
            }
            foreach (string part in parts)
            {
                if (part.Length == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Invalid Xid segment.");
                }
            }
            return parts.Length == 2 ? m_groupRecordType(parts[0]) : m_resourceRecordType;
        }

        private static RegistryValueDataType? Member(RegistryValueDataType value, string name)
        {
            if (value is RegistryObjectValueDataType map)
            {
                foreach (RegistryMemberDataType member in map.Members)
                {
                    if (string.Equals(member.Name, name, StringComparison.Ordinal))
                    {
                        return member.Value;
                    }
                }
            }
            return null;
        }

        private static ByteString NewToken()
        {
            var bytes = new byte[24];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }
            return ByteString.From(bytes);
        }

        private static bool IsOperationFailure(Exception error)
        {
            return error is ServiceResultException or ArgumentException or InvalidOperationException;
        }

        private static RegistryDiagnosticDataType Diagnostic(Exception error)
        {
            if (error is IRegistryDiagnosticSource source)
            {
                return source.ToDiagnostic();
            }
            StatusCode status = error is ServiceResultException result
                ? result.StatusCode
                : StatusCodes.BadInvalidArgument;
            string code = "E_NATIVE_INPUT";
            foreach ((StatusCode Status, string Code) known in s_codes)
            {
                if (known.Status == status)
                {
                    code = known.Code;
                    break;
                }
            }
            return new RegistryDiagnosticDataType
            {
                StatusCode = status,
                Code = code,
                Path = [],
                Detail = error.Message
            };
        }

        private static readonly (StatusCode Status, string Code)[] s_codes =
        [
            (StatusCodes.BadInvalidState, "E_EPOCH_CONFLICT"),
            (StatusCodes.BadNotFound, "E_NOT_FOUND"),
            (StatusCodes.BadNotWritable, "E_NOT_WRITABLE"),
            (StatusCodes.BadOutOfRange, "E_EPOCH_EXHAUSTED"),
            (StatusCodes.BadEncodingLimitsExceeded, "E_LIMIT"),
            (StatusCodes.BadNoContinuationPoints, "E_LIMIT"),
            (StatusCodes.BadNotSupported, "E_NOT_SUPPORTED")
        ];

        private sealed record Page(
            RegistryCaller Caller,
            uint Epoch,
            string Target,
            string Kind,
            string?[]? Names,
            RegistryValueDataType[] Values,
            int Offset,
            uint Maximum);

        private readonly IRegistryStateStore m_store;
        private readonly RegistryRecordMapper m_mapper;
        private readonly ArrayOf<string> m_collections;
        private readonly string m_registryRecordType;
        private readonly Func<string, string> m_groupRecordType;
        private readonly string m_resourceRecordType;
        private readonly RegistryObjectValueDataType m_initialDocument;
        private readonly Action<RegistryObjectValueDataType>? m_validate;
        private readonly Dictionary<string, RegistryAuxiliaryDocument> m_documents = new(StringComparer.Ordinal);
        private readonly Func<ArrayOf<string>>? m_protectedPaths;
        private readonly Func<string, ExpandedNodeId>? m_targetNodeId;
        private readonly IServiceMessageContext m_messageContext;
        private readonly long m_maxResultBytes;
        private readonly uint m_maxReadItems;
        private readonly int m_maxContinuationPoints;
        private readonly ILogger m_logger;
        private readonly SemaphoreSlim m_commitGate = new(1, 1);
        private readonly Lock m_pagesGate = new();
        private readonly Dictionary<ByteString, Page> m_pages = [];
        private RegistryCommittedState? m_current;
        private bool m_projectionPending;
    }

    internal static partial class RegistryNativeHostLog
    {
        [LoggerMessage(EventId = XRegistryServerEventIds.RegistryNativeHost + 0, Level = LogLevel.Error,
            Message = "Registry generation {Revision} is committed but its activation failed.")]
        public static partial void ActivationFailed(this ILogger logger, ulong revision, Exception exception);
    }
}
