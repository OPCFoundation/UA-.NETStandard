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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.SchemaRegistry.Server
{
    /// <summary>
    /// Owns the exact Versions and explicit default selections of one Schema Registry origin.
    /// Native and raw writes share one validated CAS commit; fingerprints are indexes, not entity keys.
    /// </summary>
    public sealed class SchemaRegistryStore : IDisposable
    {
        /// <summary>
        /// Creates a store. The application owns <paramref name="storage"/> and its disposal.
        /// The optional selector binding is a format-owner contract, never inferred from a URI fragment.
        /// </summary>
        public SchemaRegistryStore(
            ArrayOf<ISchemaFormatProvider> providers,
            IRegistryStateStore storage,
            IServiceMessageContext context,
            RegistryEntityReferenceDataType origin,
            Func<string, string, string, string>? bindSelector = null)
        {
            m_storage = storage ?? throw new ArgumentNullException(nameof(storage));
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_origin = new RegistryOriginKey(origin);
            m_bindSelector = bindSelector;
            m_logger = context.Telemetry.CreateLogger<SchemaRegistryStore>();
            foreach (ISchemaFormatProvider provider in providers)
            {
                if (provider is null || string.IsNullOrEmpty(provider.Format) ||
                    !m_providers.TryAdd(provider.Format, provider))
                {
                    throw new ArgumentException("Schema providers require distinct format identifiers.",
                        nameof(providers));
                }
            }
            if (m_providers.Count == 0)
            {
                throw new ArgumentException("At least one schema provider is required.", nameof(providers));
            }
        }

        /// <summary>
        /// Gets copies of the committed exact Versions in Xid order.
        /// </summary>
        public ArrayOf<SchemaEntry> Entries => [.. Current.Entries.Values.Select(Copy)];

        /// <summary>
        /// Gets the registry revision captured by native snapshots.
        /// </summary>
        public uint RegistryEpoch => checked((uint)Math.Max(1ul, Current.Revision));

        /// <summary>
        /// Returns the provider-owned reference for an exact Xid or a configured logical selection.
        /// No default is inferred from available Versions.
        /// </summary>
        public SchemaReferenceDataType ReferenceForXid(string xid)
        {
            return ReferenceForXid(Current, xid);
        }

        /// <summary>
        /// Reads a provider-owned Xid selection and registry epoch from one committed generation.
        /// </summary>
        public (TypedSchemaReadResultDataType Result, uint RegistryEpoch) ReadXid(string xid)
        {
            Generation current = Current;
            SchemaReferenceDataType reference = ReferenceForXid(current, xid);
            return (Read(current, reference), checked((uint)Math.Max(1ul, current.Revision)));
        }

        private static SchemaReferenceDataType ReferenceForXid(Generation current, string xid)
        {
            if (current.Entries.TryGetValue(xid, out SchemaEntry? exact))
            {
                return (SchemaReferenceDataType)exact.Reference.Clone();
            }
            if (current.Defaults.TryGetValue(xid, out DefaultSelection? selection))
            {
                var reference = (SchemaReferenceDataType)current.Entries[selection.VersionXid].Reference.Clone();
                reference.Entity.Xid = xid;
                reference.Entity.Role = "LogicalResource";
                reference.EntityUri = selection.EntityUri;
                reference.SelectedObjectUri = selection.EntityUri;
                return reference;
            }
            throw new ServiceResultException(StatusCodes.BadNotFound, "No exact Version or configured default was selected.");
        }

        /// <summary>
        /// Gets or sets activation of a durable generation. Invoked before a successful write returns.
        /// </summary>
        public Func<ArrayOf<SchemaEntry>, CancellationToken, ValueTask>? Activation { get; set; }

        /// <summary>
        /// Gets or sets additional reference admission checks, for example configured Group membership.
        /// Runs before any state commit.
        /// </summary>
        public Action<SchemaReferenceDataType>? ValidateReference { get; set; }

        /// <summary>
        /// Loads and validates stored bytes. Corrupt state fails startup without repair or defaults.
        /// </summary>
        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (m_current is not null)
                {
                    throw new InvalidOperationException("The schema store is already started.");
                }
                RegistryStoredState stored = await m_storage.ReadAsync(cancellationToken).ConfigureAwait(false);
                Generation generation = stored.Revision == 0
                    ? new Generation(0, new SortedDictionary<string, SchemaEntry>(StringComparer.Ordinal), [])
                    : Decode(stored);
                Volatile.Write(ref m_current, generation);
                if (Activation is { } activate)
                {
                    await activate(Entries, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Resolves the legacy raw fingerprint among authorized visible document candidates.
        /// Distinct bytes, formats or algorithms fail as ambiguous without candidate identifiers.
        /// </summary>
        public SchemaEntry Resolve(ByteString schemaId, Func<SchemaEntry, bool>? visible = null)
        {
            if (schemaId.IsNull || schemaId.Length == 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "A SchemaId is required.");
            }
            SchemaEntry? selected = null;
            foreach (SchemaEntry entry in Current.Entries.Values)
            {
                if (entry.Reference.SchemaId != schemaId || visible is not null && !visible(Copy(entry)))
                {
                    continue;
                }
                if (selected is not null && (selected.Format != entry.Format ||
                    selected.Reference.SchemaIdAlg != entry.Reference.SchemaIdAlg || selected.Document != entry.Document))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "The visible SchemaId lookup is ambiguous.");
                }
                selected = entry;
            }
            return selected is null
                ? throw new ServiceResultException(StatusCodes.BadNotFound, "The SchemaId was not found.")
                : Copy(selected);
        }

        /// <summary>
        /// Reads an exact Version or an explicitly configured logical default. No Version, origin,
        /// format, entity URI or selector is silently substituted.
        /// </summary>
        public TypedSchemaReadResultDataType Read(SchemaReferenceDataType reference)
        {
            return Read(Current, reference);
        }

        private TypedSchemaReadResultDataType Read(Generation generation, SchemaReferenceDataType reference)
        {
            try
            {
                ISchemaFormatProvider provider = Validate(reference);
                SchemaEntry entry = Select(generation, reference, provider);
                Claim(reference, entry.Reference.SchemaIdAlg!, entry.Reference.SchemaId);
                var result = Good(entry);
                if (string.IsNullOrEmpty(reference.Selector))
                {
                    if (reference.SelectedObjectUri != reference.EntityUri)
                    {
                        throw Input("A selected-object URI requires an explicit selector.");
                    }
                    return result;
                }
                if (m_bindSelector is null)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "No format-owned selector URI binding is configured.");
                }
                if (m_bindSelector(reference.EntityUri!, reference.Selector, provider.Format) !=
                    reference.SelectedObjectUri)
                {
                    throw Input("The selected-object URI differs from its explicit provider binding.");
                }
                result.Document.SelectedContent = new ExtensionObject(provider.Select(result.Document.Content,
                    reference.Selector));
                result.Document.Reference.Selector = reference.Selector;
                result.Document.Reference.SelectedObjectUri = m_bindSelector(result.Document.Reference.EntityUri!,
                    reference.Selector, provider.Format);
                return result;
            }
            catch (Exception error) when (ExpectedFailure(error))
            {
                return Failure(error);
            }
        }

        /// <summary>
        /// Returns the exact committed bytes of an exact Version.
        /// </summary>
        public ByteString DocumentBytes(SchemaReferenceDataType reference)
        {
            ISchemaFormatProvider provider = Validate(reference);
            Whole(reference, "ExactVersion");
            SchemaEntry entry = Select(Current, reference, provider);
            Claim(reference, entry.Reference.SchemaIdAlg!, entry.Reference.SchemaId);
            return ByteString.From(entry.Document.ToArray());
        }

        /// <summary>
        /// Registers or updates an exact Version using retained raw bytes.
        /// </summary>
        public ValueTask<TypedSchemaReadResultDataType> RegisterRawAsync(
            SchemaReferenceDataType reference,
            ByteString document,
            uint expectedEpoch = 0,
            CancellationToken cancellationToken = default)
        {
            return CommitAsync(reference, expectedEpoch, provider =>
            {
                if (document.IsNull)
                {
                    throw Input("A non-null schema document is required.");
                }
                return (ByteString.From(document.ToArray()), provider.Parse(document.Span));
            }, cancellationToken);
        }

        /// <summary>
        /// Writes complete typed content at an exact Version. A native no-op retains the original
        /// document bytes, including Arrow serialization, whitespace and exact numeric forms.
        /// </summary>
        public ValueTask<TypedSchemaReadResultDataType> WriteAsync(
            TypedSchemaWriteRequestDataType request,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            return CommitAsync(request.Reference, request.ExpectedEpoch, provider =>
            {
                if (request.Content is null || request.Content.Format != provider.Format)
                {
                    throw Input("The schema content format differs from its reference.");
                }
                Generation generation = Current;
                if (generation.Entries.TryGetValue(request.Reference.Entity.Xid!, out SchemaEntry? previous) &&
                    previous.Content.IsEqual(request.Content))
                {
                    return (previous.Document, previous.Content);
                }
                ByteString bytes = provider.Serialize(request.Content);
                SchemaContentDataType parsed = provider.Parse(bytes.Span);
                if (!parsed.IsEqual(request.Content))
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "The format owner cannot serialize this content losslessly.");
                }
                return (bytes, parsed);
            }, cancellationToken);
        }

        /// <summary>
        /// Sets a logical Resource's default to an existing exact Version of that Resource.
        /// It never infers a default from the newest Version.
        /// </summary>
        public async ValueTask ConfigureDefaultAsync(
            SchemaReferenceDataType reference,
            string versionXid,
            CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ISchemaFormatProvider provider = Validate(reference);
                Whole(reference, "LogicalResource");
                string resource = ResourceXid(versionXid, "ExactVersion");
                if (resource != reference.Entity.Xid || !string.IsNullOrEmpty(reference.SchemaIdAlg))
                {
                    throw Input("A default selects an exact Version of the same logical Resource.");
                }
                Generation current = Current;
                if (!current.Entries.TryGetValue(versionXid, out SchemaEntry? entry))
                {
                    throw new ServiceResultException(StatusCodes.BadNotFound, "The default Version is not registered.");
                }
                if (entry.Format != provider.Format)
                {
                    throw Input("The default Version has another format.");
                }
                if (current.Defaults.TryGetValue(resource, out DefaultSelection? previous) &&
                    previous.EntityUri != reference.EntityUri)
                {
                    throw Input("The logical Resource is bound to another entity URI.");
                }
                var defaults = new Dictionary<string, DefaultSelection>(current.Defaults, StringComparer.Ordinal)
                {
                    [resource] = new DefaultSelection(reference.EntityUri!, provider.Format, versionXid)
                };
                if (previous is not null && previous == defaults[resource])
                {
                    return;
                }
                StatusCode status = await PublishAsync(new Generation(current.Revision, current.Entries, defaults),
                    cancellationToken).ConfigureAwait(false);
                if (StatusCode.IsUncertain(status))
                {
                    throw new ServiceResultException(status, "The default is committed but its projection is pending.");
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Gets whether a committed Version is the provider-selected default.
        /// </summary>
        public bool IsDefault(string versionXid)
        {
            string resource = ResourceXid(versionXid, "ExactVersion");
            return Current.Defaults.TryGetValue(resource, out DefaultSelection? selection) &&
                selection.VersionXid == versionXid;
        }

        /// <inheritdoc/>
        public void Dispose() => m_gate.Dispose();

        private async ValueTask<TypedSchemaReadResultDataType> CommitAsync(
            SchemaReferenceDataType reference,
            uint expectedEpoch,
            Func<ISchemaFormatProvider, (ByteString Bytes, SchemaContentDataType Content)> prepare,
            CancellationToken cancellationToken)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ISchemaFormatProvider provider = Validate(reference);
                Whole(reference, "ExactVersion");
                Generation current = Current;
                string xid = reference.Entity.Xid!;
                current.Entries.TryGetValue(xid, out SchemaEntry? previous);
                if (expectedEpoch != 0 && (previous is null || previous.Epoch != expectedEpoch))
                {
                    return Failure(StatusCodes.BadInvalidState, "E_SCHEMA_EPOCH_CONFLICT",
                        "ExpectedEpoch differs from the selected Version.");
                }
                if (previous is not null && (previous.Format != provider.Format ||
                    previous.Reference.EntityUri != reference.EntityUri))
                {
                    throw Input("The Version is bound to another format or entity URI.");
                }
                string resource = ResourceXid(xid, "ExactVersion");
                foreach (SchemaEntry entry in current.Entries.Values)
                {
                    if (ResourceXid(entry.Reference.Entity.Xid!, "ExactVersion") == resource &&
                        entry.Format != provider.Format)
                    {
                        throw Input("All Versions of one schema Resource share one format.");
                    }
                }
                (ByteString bytes, SchemaContentDataType content) = prepare(provider);
                ByteString fingerprint = provider.ComputeSchemaId(bytes.Span);
                Claim(reference, provider.SchemaIdAlgorithm, fingerprint);
                if (previous is not null && previous.Document == bytes)
                {
                    return Good(previous);
                }
                if (previous?.Epoch == uint.MaxValue)
                {
                    return Failure(StatusCodes.BadInvalidState, "E_SCHEMA_EPOCH", "The schema epoch is exhausted.");
                }
                var canonical = (SchemaReferenceDataType)reference.Clone();
                canonical.Format = provider.Format;
                canonical.SchemaIdAlg = provider.SchemaIdAlgorithm;
                canonical.SchemaId = fingerprint;
                canonical.Selector = string.Empty;
                canonical.SelectedObjectUri = canonical.EntityUri;
                var next = new SchemaEntry(canonical, provider.ContentType, bytes,
                    (SchemaContentDataType)content.Clone(), previous is null ? 1u : previous.Epoch + 1);
                var entries = new SortedDictionary<string, SchemaEntry>(current.Entries, StringComparer.Ordinal)
                {
                    [xid] = next
                };
                StatusCode status = await PublishAsync(new Generation(current.Revision, entries, current.Defaults),
                    cancellationToken).ConfigureAwait(false);
                TypedSchemaReadResultDataType result = Good(next);
                if (StatusCode.IsUncertain(status))
                {
                    result.StatusCode = status;
                    result.Issues =
                    [
                        new RegistryDiagnosticDataType
                        {
                            StatusCode = status,
                            Code = "E_PROJECTION_PENDING",
                            Path = [],
                            Detail = "The schema Version is committed but its projection is pending."
                        }
                    ];
                }
                return result;
            }
            catch (Exception error) when (ExpectedFailure(error))
            {
                return Failure(error);
            }
            finally
            {
                m_gate.Release();
            }
        }

        private async ValueTask<StatusCode> PublishAsync(Generation next, CancellationToken cancellationToken)
        {
            if (next.Revision >= uint.MaxValue)
            {
                throw new ServiceResultException(StatusCodes.BadOutOfRange, "The registry epoch is exhausted.");
            }
            RegistryStateCommit commit = await m_storage.CommitAsync(next.Revision, Encode(next), cancellationToken)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(commit.StatusCode))
            {
                throw new ServiceResultException(commit.StatusCode, "The schema store changed concurrently.");
            }
            next = next with { Revision = commit.State.Revision };
            Volatile.Write(ref m_current, next);
            if (Activation is { } activate)
            {
                try
                {
                    await activate(Entries, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    m_logger.SchemaActivationFailed(next.Revision, error);
                    return StatusCodes.UncertainNotAllNodesAvailable;
                }
            }
            return StatusCodes.Good;
        }

        private ISchemaFormatProvider Validate(SchemaReferenceDataType reference)
        {
            if (reference?.Entity is null)
            {
                throw Input("An explicit schema reference is required.");
            }
            if (!m_origin.Equals(new RegistryOriginKey(reference.Entity)))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "The schema origin is not authorized.");
            }
            ResourceXid(reference.Entity.Xid!, reference.Entity.Role!);
            if (!Uri.TryCreate(reference.EntityUri, UriKind.Absolute, out _) ||
                reference.EntityUri.AsSpan().IndexOf('#') >= 0)
            {
                throw Input("A schema entity URI is absolute and has no fragment.");
            }
            bool claimed = !string.IsNullOrEmpty(reference.SchemaIdAlg);
            if (claimed != (!reference.SchemaId.IsNull && reference.SchemaId.Length > 0))
            {
                throw Input("SchemaIdAlg and SchemaId must be supplied together.");
            }
            if (!reference.Entity.HasNativeTarget && !reference.Entity.NativeTarget.IsNull)
            {
                throw Input("An absent native target must be null.");
            }
            ValidateReference?.Invoke(reference);
            return reference.Format is not null && m_providers.TryGetValue(reference.Format,
                out ISchemaFormatProvider? provider)
                ? provider
                : throw new ServiceResultException(StatusCodes.BadNotSupported, "The schema format is unsupported.");
        }

        private static void Whole(SchemaReferenceDataType reference, string role)
        {
            if (reference.Entity.Role != role || !string.IsNullOrEmpty(reference.Selector) ||
                reference.SelectedObjectUri != reference.EntityUri)
            {
                throw Input("This operation selects a complete " + role + " schema document.");
            }
        }

        private static SchemaEntry Select(Generation generation, SchemaReferenceDataType reference,
            ISchemaFormatProvider provider)
        {
            string xid = reference.Entity.Xid!;
            if (reference.Entity.Role == "LogicalResource")
            {
                if (!generation.Defaults.TryGetValue(xid, out DefaultSelection? selection))
                {
                    throw new SchemaSelectionException("E_SCHEMA_NO_DEFAULT");
                }
                if (selection.EntityUri != reference.EntityUri || selection.Format != provider.Format)
                {
                    throw Input("The logical schema is bound to another URI or format.");
                }
                xid = selection.VersionXid;
            }
            if (!generation.Entries.TryGetValue(xid, out SchemaEntry? entry))
            {
                throw new SchemaSelectionException("E_SCHEMA_NOT_FOUND");
            }
            if (entry.Format != provider.Format || reference.Entity.Role == "ExactVersion" &&
                entry.Reference.EntityUri != reference.EntityUri)
            {
                throw Input("The exact schema is bound to another URI or format.");
            }
            return entry;
        }

        private static void Claim(SchemaReferenceDataType reference, string algorithm, ByteString fingerprint)
        {
            if (!string.IsNullOrEmpty(reference.SchemaIdAlg) &&
                (reference.SchemaIdAlg != algorithm || reference.SchemaId != fingerprint))
            {
                throw Input("The claimed fingerprint differs from the selected document.");
            }
        }

        private static string ResourceXid(string xid, string role)
        {
            string[] parts = xid is { Length: > 0 } && xid[0] == '/' ? xid.Substring(1).Split('/') : [];
            bool exact = role == "ExactVersion";
            if (role != "LogicalResource" && !exact || parts.Length != (exact ? 6 : 4) ||
                parts[0] != "schemagroups" || parts[2] != "schemas" || exact && parts[4] != "versions")
            {
                throw Input("The schema Xid and LogicalResource/ExactVersion role disagree.");
            }
            foreach (string part in parts)
            {
                if (string.IsNullOrEmpty(part) || part is "." or ".." || part.Length > 128)
                {
                    throw Input("The schema Xid has an invalid segment.");
                }
            }
            return "/" + string.Join("/", parts, 0, 4);
        }

        private ByteString Encode(Generation generation)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, m_context, true))
            {
                encoder.WriteString(null, kStorageFormat);
                encoder.WriteStringArray(null, m_context.NamespaceUris.ToArray().ToArrayOf());
                encoder.WriteUInt32(null, (uint)generation.Entries.Count);
                foreach (SchemaEntry entry in generation.Entries.Values)
                {
                    encoder.WriteEncodeable(null, Good(entry).Document);
                    encoder.WriteByteString(null, entry.Document);
                }
                encoder.WriteUInt32(null, (uint)generation.Defaults.Count);
                foreach (KeyValuePair<string, DefaultSelection> item in generation.Defaults.OrderBy(
                    value => value.Key, StringComparer.Ordinal))
                {
                    encoder.WriteString(null, item.Key);
                    encoder.WriteString(null, item.Value.EntityUri);
                    encoder.WriteString(null, item.Value.Format);
                    encoder.WriteString(null, item.Value.VersionXid);
                }
            }
            return ByteString.From(stream.ToArray());
        }

        private Generation Decode(RegistryStoredState stored)
        {
            using var stream = new MemoryStream(stored.Document.ToArray(), writable: false);
            var context = new ServiceMessageContext(m_context, m_context.Telemetry);
            using var decoder = new BinaryDecoder(stream, context, true);
            if (decoder.ReadString(null) != kStorageFormat)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "Unsupported schema store format.");
            }
            ArrayOf<string?> namespaces = decoder.ReadStringArray(null);
            var namespaceUris = new string[namespaces.Count];
            for (int index = 0; index < namespaceUris.Length; index++)
            {
                namespaceUris[index] = namespaces[index] ??
                    throw new ServiceResultException(StatusCodes.BadDecodingError, "A namespace URI is null.");
            }
            context.NamespaceUris = new NamespaceTable(namespaceUris);
            uint count = decoder.ReadUInt32(null);
            if (count > 100000)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            var entries = new SortedDictionary<string, SchemaEntry>(StringComparer.Ordinal);
            for (uint index = 0; index < count; index++)
            {
                SchemaDocumentDataType native = decoder.ReadEncodeable<SchemaDocumentDataType>(null);
                ByteString bytes = decoder.ReadByteString(null);
                ISchemaFormatProvider provider = Validate(native.Reference);
                Whole(native.Reference, "ExactVersion");
                SchemaContentDataType content = provider.Parse(bytes.Span);
                ByteString fingerprint = provider.ComputeSchemaId(bytes.Span);
                if (native.Epoch == 0 || native.HasConfigurationVersion || !native.Content.IsEqual(content) ||
                    native.Reference.SchemaIdAlg != provider.SchemaIdAlgorithm ||
                    native.Reference.SchemaId != fingerprint)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError, "The stored schema integrity differs.");
                }
                entries.Add(native.Reference.Entity.Xid!, new SchemaEntry(native.Reference, provider.ContentType,
                    bytes, content, native.Epoch));
            }
            uint defaultsCount = decoder.ReadUInt32(null);
            if (defaultsCount > count)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "Invalid schema default count.");
            }
            var defaults = new Dictionary<string, DefaultSelection>(StringComparer.Ordinal);
            for (uint index = 0; index < defaultsCount; index++)
            {
                string resource = decoder.ReadString(null)!;
                var selection = new DefaultSelection(decoder.ReadString(null)!, decoder.ReadString(null)!,
                    decoder.ReadString(null)!);
                if (!entries.TryGetValue(selection.VersionXid, out SchemaEntry? version) ||
                    ResourceXid(selection.VersionXid, "ExactVersion") != resource || version.Format != selection.Format)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError, "Invalid stored schema default.");
                }
                defaults.Add(resource, selection);
            }
            if (stream.Position != stream.Length)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "Unexpected schema store trailing bytes.");
            }
            return new Generation(stored.Revision, entries, defaults);
        }

        private static TypedSchemaReadResultDataType Good(SchemaEntry entry)
        {
            return new TypedSchemaReadResultDataType
            {
                StatusCode = StatusCodes.Good,
                Document = new SchemaDocumentDataType
                {
                    Reference = (SchemaReferenceDataType)entry.Reference.Clone(),
                    Epoch = entry.Epoch,
                    Content = (SchemaContentDataType)entry.Content.Clone(),
                    HasConfigurationVersion = false,
                    SelectedContent = ExtensionObject.Null
                },
                Issues = []
            };
        }

        private static SchemaEntry Copy(SchemaEntry entry)
        {
            return entry with
            {
                Reference = (SchemaReferenceDataType)entry.Reference.Clone(),
                Content = (SchemaContentDataType)entry.Content.Clone(),
                Document = ByteString.From(entry.Document.ToArray())
            };
        }

        private static bool ExpectedFailure(Exception error) => error is ArgumentException or ServiceResultException;

        private static ServiceResultException Input(string detail) => new(StatusCodes.BadInvalidArgument, detail);

        private static TypedSchemaReadResultDataType Failure(Exception error)
        {
            StatusCode status = error is ServiceResultException service
                ? service.StatusCode : StatusCodes.BadInvalidArgument;
            string code = error is SchemaSelectionException selection ? selection.CodeName :
                status == StatusCodes.BadUserAccessDenied ? "E_SCHEMA_ACCESS" :
                status == StatusCodes.BadNotSupported ? "E_SCHEMA_UNSUPPORTED" : "E_SCHEMA_INPUT";
            return Failure(status, code, error.Message);
        }

        private static TypedSchemaReadResultDataType Failure(StatusCode status, string code, string detail)
        {
            return new TypedSchemaReadResultDataType
            {
                StatusCode = status,
                Document = null!,
                Issues = [new RegistryDiagnosticDataType { StatusCode = status, Code = code, Path = [], Detail = detail }]
            };
        }

        /// <summary>
        /// One committed exact schema Version and its retained bytes.
        /// </summary>
        public sealed record SchemaEntry(
            SchemaReferenceDataType Reference,
            string ContentType,
            ByteString Document,
            SchemaContentDataType Content,
            uint Epoch)
        {
            /// <summary>Gets the provider format identifier.</summary>
            public string Format => Reference.Format!;
        }

        private sealed record Generation(ulong Revision, SortedDictionary<string, SchemaEntry> Entries,
            Dictionary<string, DefaultSelection> Defaults);

        private sealed record DefaultSelection(string EntityUri, string Format, string VersionXid);

#pragma warning disable CA1032, RCS1194 // This internal failure always identifies a missing selection.
        private sealed class SchemaSelectionException(string code) : ServiceResultException(StatusCodes.BadNotFound)
        {
            public string CodeName { get; } = code;
        }
#pragma warning restore CA1032, RCS1194

        private Generation Current => Volatile.Read(ref m_current) ??
            throw new InvalidOperationException("The schema store is not started.");

        private const string kStorageFormat = "SchemaRegistryState/1.0";
        private readonly IRegistryStateStore m_storage;
        private readonly IServiceMessageContext m_context;
        private readonly RegistryOriginKey m_origin;
        private readonly Func<string, string, string, string>? m_bindSelector;
        private readonly Dictionary<string, ISchemaFormatProvider> m_providers = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly ILogger m_logger;
        private Generation? m_current;
    }

    internal static partial class SchemaRegistryStoreLog
    {
        [LoggerMessage(EventId = SchemaRegistryServerEventIds.SchemaRegistryStore + 0, Level = LogLevel.Error,
            Message = "Schema registry generation {Revision} is committed but its projection failed.")]
        public static partial void SchemaActivationFailed(this ILogger logger, ulong revision, Exception exception);
    }
}
