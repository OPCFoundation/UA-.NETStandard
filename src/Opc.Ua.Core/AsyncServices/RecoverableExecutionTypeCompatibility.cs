/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Establishes qualified binary interpretation compatibility for persisted recoverable payloads.
    /// </summary>
    /// <remarks>
    /// Hashes complete ordered definition metadata, registration/encoding identities, exact URI mappings,
    /// and explicit codec revisions. All registrations are covered, not only a top-level request type.
    /// A changed or unqualified image fails closed before payload decoding. This is not a proof of arbitrary
    /// handwritten codec semantics: custom qualification is an explicit application authority's contract.
    /// </remarks>
    public sealed class RecoverableExecutionTypeCompatibility
    {
        /// <summary>
        /// Creates a directly constructible compatibility authority with optional custom codec qualification.
        /// Built-ins are recognized only by exact generated singleton registrations in a fresh trusted set.
        /// </summary>
        public RecoverableExecutionTypeCompatibility(IRecoverableExecutionCodecQualifier? qualifier = null)
        {
            m_qualifier = qualifier;
        }

        /// <summary>
        /// Captures and qualifies an independent registration image and its exact namespace/Server mappings.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<RecoverableExecutionTypeContext> CaptureAsync(
            IServiceMessageContext context, CancellationToken ct = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            ct.ThrowIfCancellationRequested();
            if (context.Factory is not EncodeableFactory current)
            {
                throw Unsupported("The factory cannot provide an atomic registration snapshot.");
            }
            EncodeableFactory captured = current.Fork();
            ServiceMessageContext original = RecoverableExecutionTypeContext.CloneContext(context, captured);
            ValidateTables(original);
            EncodeableFactory trusted = CreateTrustedRegistrations();
            var definitions = new ServiceMessageContext(original, original.Telemetry)
            {
                MaxMessageSize = kMaximumMetadataBytes,
                MaxByteStringLength = kMaximumMetadataBytes,
                MaxArrayLength = kMaximumRegistrations,
                MaxStringLength = kMaximumMetadataBytes
            };
            var entries = new List<Registration>();
            AddRegistrations(trusted, entries, authority: true);
            AddRegistrations(captured, entries, authority: false);
            entries.Sort(static (left, right) =>
            {
                int image = left.Authority.CompareTo(right.Authority);
                if (image != 0)
                {
                    return image;
                }
                int id = string.CompareOrdinal(left.Id.ToString(), right.Id.ToString());
                return id != 0 ? id : left.Enumeration.CompareTo(right.Enumeration);
            });
            var metadata = new Dictionary<IType, ByteString>(CodecReferenceComparer.Instance);
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, definitions, true))
            {
                encoder.WriteString(null, kWitnessContract);
                encoder.WriteStringArray(null, original.NamespaceUris.ToArrayOf());
                encoder.WriteStringArray(null, original.ServerUris.ToArrayOf());
                encoder.WriteInt32(null, entries.Count);
                foreach (Registration registration in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (registration.Codec is not IDataTypeDefinitionSource source)
                    {
                        throw Unsupported("A registration has no complete data type definition metadata.");
                    }
                    ByteString revision = IsTrusted(trusted, registration)
                        ? s_builtinRevision
                        : m_qualifier == null ? default :
                            await m_qualifier.GetRevisionAsync(registration.Id, registration.Codec, ct)
                                .ConfigureAwait(false);
                    if (revision.Length is <= 0 or > 4096)
                    {
                        throw Unsupported("A custom codec has no qualified immutable revision.");
                    }
                    if (!metadata.TryGetValue(registration.Codec, out ByteString description))
                    {
                        description = Describe(registration.Codec, source, definitions, captured, trusted);
                        metadata.Add(registration.Codec, description);
                    }
                    encoder.WriteBoolean(null, registration.Authority);
                    encoder.WriteBoolean(null, registration.Enumeration);
                    WriteIdentity(encoder, registration.Id, original.NamespaceUris);
                    encoder.WriteString(null, registration.Codec.XmlName.Name);
                    encoder.WriteString(null, registration.Codec.XmlName.Namespace);
                    encoder.WriteByteString(null, revision);
                    encoder.WriteByteString(null, description);
                    if (stream.Length > kMaximumMetadataBytes)
                    {
                        throw Unsupported("The registration metadata exceeds the persistence witness limit.");
                    }
                }
            }
            ByteString hash = Hash(stream.ToArray());
            byte[] witness = new byte[kWitnessLength];
            "RSEW"u8.CopyTo(witness);
            witness[4] = 1;
            hash.Span.CopyTo(witness.AsSpan(5));
            return new RecoverableExecutionTypeContext(original, captured, ByteString.From(witness));
        }

        /// <summary>
        /// Verifies saved compatibility before returning any context that may decode persisted payloads.
        /// Old, malformed, changed, missing-metadata and unqualified images are rejected without fallback.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<RecoverableExecutionTypeContext> RestoreAsync(
            IServiceMessageContext context, ByteString witness, CancellationToken ct = default)
        {
            if (witness.Length != kWitnessLength ||
                !witness.Span[..4].SequenceEqual("RSEW"u8) ||
                witness.Span[4] != 1)
            {
                throw Mismatch();
            }
            RecoverableExecutionTypeContext captured = await CaptureAsync(context, ct).ConfigureAwait(false);
            if (captured.Witness != witness)
            {
                throw Mismatch();
            }
            return captured;
        }

        /// <summary>
        /// Builds authority from generated registrations, never from the mutable global factory.
        /// </summary>
        private static EncodeableFactory CreateTrustedRegistrations()
        {
            var factory = new EncodeableFactory();
            factory.Builder.AddOpcUa().AddOpcUaRse().Commit();
            return factory;
        }

        /// <summary>
        /// Covers both registry maps, including aliases and encoding identifiers.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void AddRegistrations(
            EncodeableFactory factory, List<Registration> registrations, bool authority)
        {
            foreach (ExpandedNodeId id in factory.KnownTypeIds.Distinct())
            {
                if (id.IsNull)
                {
                    throw Unsupported("A null registration identifier cannot qualify persistence.");
                }
                if (factory.TryGetEncodeableType(id, out IEncodeableType? structure))
                {
                    ValidateXmlAssociation(factory, structure);
                    registrations.Add(new Registration(id, structure, false, authority));
                }
                if (factory.TryGetEnumeratedType(id, out IEnumeratedType? enumeration))
                {
                    ValidateXmlAssociation(factory, enumeration);
                    registrations.Add(new Registration(id, enumeration, true, authority));
                }
                if (registrations.Count > kMaximumRegistrations)
                {
                    throw Unsupported("Too many registrations for a bounded persistence witness.");
                }
            }
        }

        /// <summary>
        /// Refuses ambiguous XML aliases instead of hashing identities while ignoring a different lookup winner.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void ValidateXmlAssociation(EncodeableFactory factory, IType codec)
        {
            if (codec.XmlName == null ||
                string.IsNullOrEmpty(codec.XmlName.Name) ||
                !factory.TryGetType(codec.XmlName, out IType? selected) ||
                !ReferenceEquals(selected, codec))
            {
                throw Unsupported("A registration has an ambiguous or missing qualified XML identity.");
            }
        }

        /// <summary>
        /// Recognizes an exact generated singleton at its authoritative registration key without reflection or names.
        /// </summary>
        private static bool IsTrusted(EncodeableFactory trusted, Registration registration)
        {
            return registration.Enumeration
                ? trusted.TryGetEnumeratedType(registration.Id, out IEnumeratedType? enumeration) &&
                    ReferenceEquals(enumeration, registration.Codec)
                : trusted.TryGetEncodeableType(registration.Id, out IEncodeableType? structure) &&
                    ReferenceEquals(structure, registration.Codec);
        }

        /// <summary>
        /// Hashes every ordered definition field, including metadata omitted from its standard wire encoding.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static ByteString Describe(
            IType codec, IDataTypeDefinitionSource source, ServiceMessageContext context,
            EncodeableFactory actual, EncodeableFactory trusted)
        {
            DataTypeDefinition definition = source.GetDataTypeDefinition(context.NamespaceUris) ??
                throw Unsupported("A registration returned no definition.");
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                if (codec is IEncodeableType structureCodec)
                {
                    IEncodeable instance = structureCodec.CreateInstance() ??
                        throw Unsupported("A registration cannot create its declared type.");
                    try
                    {
                        WriteIdentity(encoder, instance.TypeId, context.NamespaceUris);
                        WriteIdentity(encoder, instance.BinaryEncodingId, context.NamespaceUris);
                        WriteIdentity(encoder, instance.XmlEncodingId, context.NamespaceUris);
                    }
                    finally
                    {
                        if (instance is IPooledEncodeable pooled)
                        {
                            pooled.Reuse();
                        }
                    }
                }
                switch (definition)
                {
                    case StructureDefinition structure:
                        encoder.WriteByte(null, 1);
                        WriteNodeIdentity(encoder, structure.DefaultEncodingId, context.NamespaceUris);
                        WriteNodeIdentity(encoder, structure.BaseDataType, context.NamespaceUris);
                        ValidateDependency(structure.BaseDataType, context.NamespaceUris, actual, trusted);
                        encoder.WriteInt32(null, (int)structure.StructureType);
                        encoder.WriteInt32(null, structure.FirstExplicitFieldIndex);
                        encoder.WriteInt32(null, structure.Fields.Count);
                        foreach (StructureField field in structure.Fields)
                        {
                            if (field == null || string.IsNullOrEmpty(field.Name) || field.DataType.IsNull)
                            {
                                throw Unsupported("A structure field lacks required definition metadata.");
                            }
                            ValidateDependency(field.DataType, context.NamespaceUris, actual, trusted);
                            encoder.WriteString(null, field.Name);
                            encoder.WriteLocalizedText(null, field.Description);
                            WriteNodeIdentity(encoder, field.DataType, context.NamespaceUris);
                            encoder.WriteInt32(null, field.ValueRank);
                            encoder.WriteUInt32Array(null, field.ArrayDimensions);
                            encoder.WriteUInt32(null, field.MaxStringLength);
                            encoder.WriteBoolean(null, field.IsOptional);
                        }
                        break;
                    case EnumDefinition enumeration:
                        encoder.WriteByte(null, 2);
                        encoder.WriteBoolean(null, enumeration.IsOptionSet);
                        encoder.WriteInt32(null, enumeration.Fields.Count);
                        foreach (EnumField field in enumeration.Fields)
                        {
                            if (field == null || string.IsNullOrEmpty(field.Name))
                            {
                                throw Unsupported("An enumeration field lacks required definition metadata.");
                            }
                            encoder.WriteString(null, field.Name);
                            encoder.WriteInt64(null, field.Value);
                            encoder.WriteLocalizedText(null, field.DisplayName);
                            encoder.WriteLocalizedText(null, field.Description);
                        }
                        break;
                    default:
                        throw Unsupported("The codec definition is not a complete structure or enumeration.");
                }
            }
            if (stream.Length > kMaximumMetadataBytes)
            {
                throw Unsupported("A definition exceeds the persistence witness limit.");
            }
            return Hash(stream.ToArray());
        }

        /// <summary>
        /// Requires custom dependencies to be registered; intrinsic Core types are bound by the trusted Core contract.
        /// The complete trusted Core/RSE image is included in the witness even for a smaller input factory.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void ValidateDependency(
            NodeId id, NamespaceTable namespaces, EncodeableFactory actual, EncodeableFactory trusted)
        {
            if (id.IsNull)
            {
                return;
            }
            var absolute = NodeId.ToExpandedNodeId(id, namespaces);
            if (absolute.IsNull)
            {
                throw Unsupported("A definition references an unmapped namespace.");
            }
            if (actual.TryGetEncodeableType(absolute, out _) ||
                actual.TryGetEnumeratedType(absolute, out _) ||
                actual.TryGetEncodeableType(new ExpandedNodeId(id), out _) ||
                actual.TryGetEnumeratedType(new ExpandedNodeId(id), out _) ||
                trusted.TryGetEncodeableType(absolute, out _) ||
                trusted.TryGetEnumeratedType(absolute, out _))
            {
                return;
            }
            if (id.NamespaceIndex == 0 &&
                (TypeInfo.GetBuiltInType(id) != BuiltInType.Null ||
                    (id.TryGetValue(out uint numeric) && DataTypes.TryGetBrowseName(numeric, out _))))
            {
                return;
            }
            throw Unsupported("A custom definition dependency has no registered, qualified metadata.");
        }

        /// <summary>
        /// Writes namespace-qualified identities independently of temporary metadata-table indices.
        /// </summary>
        private static void WriteNodeIdentity(BinaryEncoder encoder, NodeId id, NamespaceTable namespaces)
        {
            WriteIdentity(encoder, new ExpandedNodeId(id), namespaces);
        }

        /// <summary>
        /// Includes full identifiers, namespace URIs and Server indices without ambiguous string concatenation.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void WriteIdentity(BinaryEncoder encoder, ExpandedNodeId id, NamespaceTable namespaces)
        {
            encoder.WriteBoolean(null, id.IsNull);
            if (id.IsNull)
            {
                return;
            }
            string? uri = id.NamespaceUri;
            if (string.IsNullOrEmpty(uri))
            {
                uri = namespaces.GetString(id.InnerNodeId.NamespaceIndex);
            }
            if (string.IsNullOrEmpty(uri))
            {
                throw Unsupported("A registration identifier references an unmapped namespace.");
            }
            encoder.WriteString(null, uri);
            encoder.WriteUInt32(null, id.ServerIndex);
            encoder.WriteNodeId(null, id.InnerNodeId.WithNamespaceIndex(0));
        }

        /// <summary>
        /// Rejects invalid tables rather than normalizing a saved interpretation.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void ValidateTables(ServiceMessageContext context)
        {
            if (context.NamespaceUris.Count == 0 ||
                context.NamespaceUris.GetString(0) != Namespaces.OpcUa ||
                context.NamespaceUris.Count > ushort.MaxValue + 1)
            {
                throw Unsupported("The namespace table cannot qualify persistence.");
            }
            for (uint i = 0; i < context.NamespaceUris.Count; i++)
            {
                if (string.IsNullOrEmpty(context.NamespaceUris.GetString(i)))
                {
                    throw Unsupported("An empty namespace URI cannot qualify persistence.");
                }
            }
        }

        /// <summary>
        /// Uses the same SHA256 operation on every supported framework.
        /// </summary>
        private static ByteString Hash(byte[] input)
        {
#if NET8_0_OR_GREATER
            return ByteString.From(SHA256.HashData(input));
#else
            using SHA256 algorithm = SHA256.Create();
            return ByteString.From(algorithm.ComputeHash(input));
#endif
        }

        /// <summary>
        /// Reports inability to qualify an interpretation without disclosing payloads or identity material.
        /// </summary>
        private static ServiceResultException Unsupported(string message)
        {
            return new ServiceResultException(StatusCodes.BadNotSupported, message);
        }

        /// <summary>
        /// Reports a changed or unversioned persisted interpretation before payload decoding.
        /// </summary>
        private static ServiceResultException Mismatch()
        {
            return new ServiceResultException(
                StatusCodes.BadDecodingError, "The persisted type interpretation is not compatible with this context.");
        }

        /// <summary>
        /// Captures one actual registry association.
        /// </summary>
        private sealed record Registration(ExpandedNodeId Id, IType Codec, bool Enumeration, bool Authority);

        /// <summary>
        /// Keeps custom Equals implementations out of codec identity and metadata capture.
        /// </summary>
        private sealed class CodecReferenceComparer : IEqualityComparer<IType>
        {
            /// <summary>
            /// Gets the reference-only comparer.
            /// </summary>
            internal static CodecReferenceComparer Instance { get; } = new();

            /// <inheritdoc/>
            public bool Equals(IType? x, IType? y)
            {
                return ReferenceEquals(x, y);
            }

            /// <inheritdoc/>
            public int GetHashCode(IType obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        /// <summary>
        /// This explicit authority revision must change if intrinsic/generated codec semantics change
        /// without a corresponding definition change. Metadata cannot prove arbitrary codec implementation semantics.
        /// </summary>
        private static readonly ByteString s_builtinRevision = ByteString.From("opcua-1.05.07-rse-0.4-codec/v1"u8);
        private const string kWitnessContract = "OpcUa.RSE.TypeWitness/SHA256/v1";
        private const int kWitnessLength = 37;
        private const int kMaximumRegistrations = 20000;
        private const int kMaximumMetadataBytes = 64 * 1024 * 1024;
        private readonly IRecoverableExecutionCodecQualifier? m_qualifier;
    }
}
