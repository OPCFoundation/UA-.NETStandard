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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Couples a versioned interpretation witness with captured registrations and strict binary payload operations.
    /// </summary>
    public sealed class RecoverableExecutionTypeContext
    {
        /// <summary>
        /// Captures independent URI tables and a non-mutable view of an already qualified registration image.
        /// </summary>
        internal RecoverableExecutionTypeContext(
            IServiceMessageContext source, EncodeableFactory registrations, ByteString witness)
        {
            m_witness = witness.Copy();
            m_context = CloneContext(source, new QualifiedFactory(registrations));
            m_context.MaxDecoderRecoveries = 0;
        }

        /// <summary>
        /// Gets the versioned SHA256 witness. It is compatibility evidence, not authentication or bearer authority.
        /// </summary>
        public ByteString Witness => m_witness.Copy();

        /// <summary>
        /// Gets an independently configurable message context over the captured, non-mutable registrations.
        /// Unknown type lookups throw; decoding cannot silently recover an incompatible custom body as opaque bytes.
        /// </summary>
        public IServiceMessageContext MessageContext => new ServiceMessageContext(m_context, m_context.Telemetry);

        /// <summary>
        /// Encodes a payload and validates its entire encoded tree through the qualified decoder before returning it.
        /// This rejects unregistered or otherwise undecodable nested opaque ExtensionObjects.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <c>null</c>.</exception>
        public ByteString Encode(IEncodeable payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }
            var encoded = ByteString.From(BinaryEncoder.EncodeMessage(payload, m_context));
            IEncodeable validated = Decode<IEncodeable>(encoded);
            if (validated is IPooledEncodeable pooled)
            {
                pooled.Reuse();
            }
            return encoded;
        }

        /// <summary>
        /// Decodes a complete payload with the captured interpretation.
        /// Rejects unknown nested types, malformed bodies and trailing data.
        /// The caller must obtain this context through RestoreAsync before passing persisted payloads.
        /// </summary>
        /// <typeparam name="T">The expected generated payload interface or type.</typeparam>
        /// <exception cref="ServiceResultException"></exception>
        public T Decode<T>(ByteString payload) where T : IEncodeable
        {
            if (payload.IsEmpty || (m_context.MaxMessageSize > 0 && payload.Length > m_context.MaxMessageSize))
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "Invalid qualified payload length.");
            }
            using var stream = new MemoryStream(payload.ToArray(), writable: false);
            using var decoder = new BinaryDecoder(stream, m_context, true);
            T value = decoder.DecodeMessage<T>();
            if (stream.Position != stream.Length)
            {
                if (value is IPooledEncodeable pooled)
                {
                    pooled.Reuse();
                }
                throw new ServiceResultException(StatusCodes.BadDecodingError, "Trailing qualified payload data.");
            }
            return value;
        }

        /// <summary>
        /// Copies all interpretation limits and mappings while selecting an explicit captured factory.
        /// </summary>
        internal static ServiceMessageContext CloneContext(
            IServiceMessageContext source, IEncodeableFactory factory)
        {
            return new ServiceMessageContext(source.Telemetry, factory)
            {
                NamespaceUris = new NamespaceTable(source.NamespaceUris),
                ServerUris = new StringTable(source.ServerUris),
                MaxMessageSize = source.MaxMessageSize,
                MaxArrayLength = source.MaxArrayLength,
                MaxByteStringLength = source.MaxByteStringLength,
                MaxStringLength = source.MaxStringLength,
                MaxEncodingNestingLevels = source.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = source.MaxDecoderRecoveries
            };
        }

        /// <summary>
        /// Exposes captured lookups without permitting registration changes or an opaque-type decoder fallback.
        /// </summary>
        private sealed class QualifiedFactory(EncodeableFactory factory) : IEncodeableFactory
        {
            /// <inheritdoc/>
            public IEnumerable<ExpandedNodeId> KnownTypeIds => factory.KnownTypeIds;

            /// <inheritdoc/>
            public IEncodeableFactoryBuilder Builder => throw new ServiceResultException(
                StatusCodes.BadNotSupported, "A qualified persistence context cannot change registrations.");

            /// <inheritdoc/>
            public bool TryGetEncodeableType(
                ExpandedNodeId typeId, [NotNullWhen(true)] out IEncodeableType? encodeableType)
            {
                if (!factory.TryGetEncodeableType(typeId, out encodeableType))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError, "A payload contains an unqualified encodeable type.");
                }
                return true;
            }

            /// <inheritdoc/>
            public bool TryGetEnumeratedType(
                ExpandedNodeId typeId, [NotNullWhen(true)] out IEnumeratedType? enumeratedType)
            {
                if (!factory.TryGetEnumeratedType(typeId, out enumeratedType))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError, "A payload contains an unqualified enumeration.");
                }
                return true;
            }

            /// <inheritdoc/>
            public bool TryGetType(XmlQualifiedName xmlName, [NotNullWhen(true)] out IType? type)
            {
                if (!factory.TryGetType(xmlName, out type))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError, "A payload contains an unqualified XML type.");
                }
                return true;
            }
        }

        private readonly ServiceMessageContext m_context;
        private readonly ByteString m_witness;
    }
}
