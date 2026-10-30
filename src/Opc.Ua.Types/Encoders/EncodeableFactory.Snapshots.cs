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
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// <para>
    /// Registry of encodeable type activators that can be retrieved
    /// using the type id or encoding ids in encoders and decoders.
    /// Used to register source generated and custom (hand crafted)
    /// types.
    /// <br/></para>
    /// <para>
    /// You can manually add types using the <see cref="Builder"/>
    /// property exposed mutator. You can also import all types from
    /// a specified assembly when you are not required to be trimmable
    /// or NativeAOT compliant.
    /// <br/></para>
    /// <para>
    /// Once the types exist within the registry they can be consumed
    /// by encoders and decoders.
    /// <br/></para>
    /// </summary>
    public sealed partial class EncodeableFactory
    {
        /// <summary>
        /// Captures independent registration maps with their source identity and revision.
        /// </summary>
        /// <param name="source">The encodeable factory.</param>
        /// <param name="revision">The captured source revision.</param>
        internal EncodeableFactory CaptureSnapshot(out EncodeableFactory source, out long revision)
        {
            lock (m_publicationGate)
            {
                source = this;
                revision = m_revision;
                return new EncodeableFactory(this)
                {
                    m_captureSource = new WeakReference<EncodeableFactory>(this),
                    m_captureRevision = m_revision
                };
            }
        }

        /// <summary>
        /// Builds compatible additive registration maps without changing either input.
        /// </summary>
        /// <remarks>
        /// The serving identity and revision must still match preparation. Concurrent changes are rejected,
        /// including compatible additions; publication reserves this same revision before the durable decision.
        /// </remarks>
        /// <param name="additions">The privately prepared registrations.</param>
        /// <param name="source">The serving factory captured before preparation.</param>
        /// <param name="revision">The captured serving revision.</param>
        /// <param name="namespaceUris">The namespaces used to compare data type definitions.</param>
        /// <returns>The independently owned merged factory.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        internal EncodeableFactory PrepareAdditiveMerge(
            EncodeableFactory additions,
            EncodeableFactory source,
            long revision,
            NamespaceTable namespaceUris)
        {
            if (additions is null)
            {
                throw new ArgumentNullException(nameof(additions));
            }
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            EncodeableFactory merged = CaptureSnapshot(out EncodeableFactory current, out long currentRevision);
            if (!ReferenceEquals(source, current) || revision != currentRevision)
            {
                throw new OptimisticPublicationConflictException(
                    "Serving factory registrations changed after preparation.");
            }
            if (additions.IsUnchangedCapture(source, revision))
            {
                return merged;
            }
            EncodeableFactory proposed = additions.CaptureSnapshot(out _, out _);
            Dictionary<ExpandedNodeId, IEncodeableType> structures = merged.m_encodeableTypes
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            Dictionary<ExpandedNodeId, IEnumeratedType> enumerations = merged.m_enumeratedTypes
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            Dictionary<XmlQualifiedName, IType> names = merged.m_xmlNameToType
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            foreach (KeyValuePair<ExpandedNodeId, IEncodeableType> entry in proposed.m_encodeableTypes)
            {
                if (enumerations.ContainsKey(entry.Key))
                {
                    throw new InvalidOperationException(
                        $"Registration '{entry.Key}' cannot redefine an enumeration as a structure.");
                }
                if (structures.TryGetValue(entry.Key, out IEncodeableType? existing))
                {
                    ValidateCompatibleRegistration(existing, entry.Value, namespaceUris);
                }
                else
                {
                    structures.Add(entry.Key, entry.Value);
                }
            }
            foreach (KeyValuePair<ExpandedNodeId, IEnumeratedType> entry in proposed.m_enumeratedTypes)
            {
                if (structures.ContainsKey(entry.Key))
                {
                    throw new InvalidOperationException(
                        $"Registration '{entry.Key}' cannot redefine a structure as an enumeration.");
                }
                if (enumerations.TryGetValue(entry.Key, out IEnumeratedType? existing))
                {
                    ValidateCompatibleRegistration(existing, entry.Value, namespaceUris);
                }
                else
                {
                    enumerations.Add(entry.Key, entry.Value);
                }
            }
            foreach (KeyValuePair<XmlQualifiedName, IType> entry in proposed.m_xmlNameToType)
            {
                if (names.TryGetValue(entry.Key, out IType? existing))
                {
                    ValidateCompatibleRegistration(existing, entry.Value, namespaceUris);
                }
                else
                {
                    names.Add(entry.Key, entry.Value);
                }
            }
            merged.m_encodeableTypes = structures.ToFrozenDictionary();
            merged.m_enumeratedTypes = enumerations.ToFrozenDictionary();
            merged.m_xmlNameToType = names.ToFrozenDictionary();
            return merged;
        }

        /// <summary>
        /// Reserves an unchanged source image until publication completes or is abandoned.
        /// </summary>
        /// <param name="source">The encodeable factory.</param>
        /// <param name="revision">The revision observed during capture.</param>
        /// <exception cref="InvalidOperationException"></exception>
        internal Publication BeginPublication(EncodeableFactory source, long revision)
        {
            lock (m_publicationGate)
            {
                if (!ReferenceEquals(source, this) || revision != m_revision)
                {
                    throw new OptimisticPublicationConflictException(
                        "Serving factory registrations changed after preparation.");
                }
                EnsureMutable();
                m_publicationPending = true;
                return new Publication(this);
            }
        }

        /// <summary>
        /// Checks whether private maps still contain exactly their captured serving registrations.
        /// </summary>
        /// <param name="source">The expected capture source.</param>
        /// <param name="revision">The expected capture revision.</param>
        /// <returns>Whether no private mutation has occurred since capture.</returns>
        private bool IsUnchangedCapture(EncodeableFactory source, long revision)
        {
            lock (m_publicationGate)
            {
                return m_captureSource is { } origin &&
                    origin.TryGetTarget(out EncodeableFactory? captured) &&
                    ReferenceEquals(captured, source) &&
                    m_captureRevision == revision &&
                    m_revision == 0;
            }
        }

        /// <summary>
        /// Rejects replacements that would change decoding of an already published registration.
        /// </summary>
        /// <param name="existing">The serving activator.</param>
        /// <param name="candidate">The proposed activator.</param>
        /// <param name="namespaceUris">The namespaces used to compare definitions.</param>
        /// <exception cref="InvalidOperationException"></exception>
        private static void ValidateCompatibleRegistration(
            IType existing,
            IType candidate,
            NamespaceTable namespaceUris)
        {
            if (ReferenceEquals(existing, candidate))
            {
                return;
            }
            if (existing.Type != candidate.Type ||
                !existing.XmlName.Equals(candidate.XmlName) ||
                (existing is IEncodeableType) != (candidate is IEncodeableType) ||
                (existing is IEnumeratedType) != (candidate is IEnumeratedType) ||
                (existing is IDataTypeDefinitionSource) != (candidate is IDataTypeDefinitionSource) ||
                (existing is IDataTypeDefinitionSource current && candidate is IDataTypeDefinitionSource next &&
                    !current.GetDataTypeDefinition(namespaceUris).IsEqual(next.GetDataTypeDefinition(namespaceUris))))
            {
                throw new InvalidOperationException(
                    $"Registration '{candidate.XmlName}' cannot redefine a serving type.");
            }
        }

        private void EnsureMutable()
        {
            if (m_publicationPending)
            {
                throw new InvalidOperationException(
                    "The factory has a pending publication and cannot be changed.");
            }
        }

        private Lock m_publicationGate => m_storage.Gate;

        /// <summary>Weak provenance avoids retaining earlier serving factories through prepared copies.</summary>
        private WeakReference<EncodeableFactory>? m_captureSource;

        /// <summary>The serving revision from which these private maps were copied.</summary>
        private long m_captureRevision;
        private long m_revision
        {
            get => m_storage.Revision;
            set => m_storage.Revision = value;
        }

        private bool m_publicationPending
        {
            get => m_storage.PublicationPending;
            set => m_storage.PublicationPending = value;
        }

        private Storage m_storage = new();

        /// <summary>
        /// Shares canonical registrations with a published staging factory, including future additions.
        /// </summary>
        /// <param name="canonical">The serving factory.</param>
        internal void BindTo(EncodeableFactory canonical)
        {
            m_storage = canonical.m_storage;
        }

        /// <summary>
        /// Detaches cached staging factories before manager cleanup.
        /// </summary>
        internal void Detach()
        {
            m_storage = CaptureSnapshot(out _, out _).m_storage;
        }

        /// <summary>
        /// Owns shared frozen registration maps and their writer reservation.
        /// </summary>
        private sealed class Storage
        {
            /// <summary>Serializes registration writers through every bound handle.</summary>
            internal readonly Lock Gate = new();

            /// <summary>Holds the published encodeable registration map.</summary>
            internal FrozenDictionary<ExpandedNodeId, IEncodeableType> EncodeableTypes =
                new Dictionary<ExpandedNodeId, IEncodeableType>().ToFrozenDictionary();

            /// <summary>Holds the published enumeration registration map.</summary>
            internal FrozenDictionary<ExpandedNodeId, IEnumeratedType> EnumeratedTypes =
                new Dictionary<ExpandedNodeId, IEnumeratedType>().ToFrozenDictionary();

            /// <summary>Holds the published XML name map.</summary>
            internal FrozenDictionary<XmlQualifiedName, IType> XmlNames =
                new Dictionary<XmlQualifiedName, IType>().ToFrozenDictionary();

            /// <summary>Identifies the current canonical registration revision.</summary>
            internal long Revision;

            /// <summary>Rejects registration writes reserved by an undecided publication.</summary>
            internal bool PublicationPending;
        }

        /// <summary>
        /// Reserves factory mutations until additive publication completes or is abandoned.
        /// </summary>
        /// <param name="owner">The encodeable factory.</param>
        internal sealed class Publication(EncodeableFactory owner) : IDisposable
        {
            /// <summary>
            /// Installs prevalidated additive registrations in the canonical storage.
            /// </summary>
            /// <exception cref="ObjectDisposedException"></exception>
            public void Complete(EncodeableFactory merged)
            {
                EncodeableFactory current = m_owner ?? throw new ObjectDisposedException(nameof(Publication));
                lock (current.m_publicationGate)
                {
                    current.m_encodeableTypes = merged.m_encodeableTypes;
                    current.m_enumeratedTypes = merged.m_enumeratedTypes;
                    current.m_xmlNameToType = merged.m_xmlNameToType;
                    current.m_revision++;
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                EncodeableFactory? current = Interlocked.Exchange(ref m_owner, null);
                if (current is not null)
                {
                    lock (current.m_publicationGate)
                    {
                        current.m_publicationPending = false;
                    }
                }
            }

            private EncodeableFactory? m_owner = owner;
        }
    }
}
