/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections;
using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// A reference to a node.
    /// </summary>
    public interface IReference
    {
        /// <summary>
        /// The type of reference.
        /// </summary>
        /// <value>The reference type identifier.</value>
        NodeId ReferenceTypeId { get; }

        /// <summary>
        /// True if the reference is an inverse reference.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance is inverse; otherwise, <c>false</c>.
        /// </value>
        bool IsInverse { get; }

        /// <summary>
        /// The identifier for the target node.
        /// </summary>
        /// <value>The target identifier.</value>
        ExpandedNodeId TargetId { get; }
    }

    /// <summary>
    /// A reference to a node.
    /// </summary>
    public interface IReferenceCollection : ICollection<IReference>
    {
        /// <summary>
        /// Adds the reference to the node.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="targetId">The target identifier.</param>
        void Add(NodeId referenceTypeId, bool isInverse, ExpandedNodeId targetId);

        /// <summary>
        /// Removes the reference from the node.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="targetId">The target identifier.</param>
        /// <returns>The result of removal.</returns>
        bool Remove(NodeId referenceTypeId, bool isInverse, ExpandedNodeId targetId);

        /// <summary>
        /// Removes all of the specified references.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <returns>The result of removal.</returns>
        bool RemoveAll(NodeId referenceTypeId, bool isInverse);

        /// <summary>
        /// Checks whether any references which meet the specified critia exist.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="targetId">The target identifier.</param>
        /// <param name="includeSubtypes">if set to <c>true</c> subtypes are included.</param>
        /// <param name="typeTree">The type tree.</param>
        /// <returns>True if reference exists.</returns>
        bool Exists(
            NodeId referenceTypeId,
            bool isInverse,
            ExpandedNodeId targetId,
            bool includeSubtypes,
            ITypeTable typeTree);

        /// <summary>
        /// Returns a list of references which match the specified criteria.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="includeSubtypes">if set to <c>true</c> subtypes are included.</param>
        /// <param name="typeTree">The type tree.</param>
        /// <returns>A list of references which match the specified criteria.</returns>
        IList<IReference> Find(
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            ITypeTable typeTree);

        /// <summary>
        /// Returns a single target that meets the specified criteria.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="includeSubtypes">if set to <c>true</c> subtypes are included.</param>
        /// <param name="typeTree">The type tree.</param>
        /// <param name="index">The index.</param>
        /// <returns>A single target that meets the specified criteria.</returns>
        ExpandedNodeId FindTarget(
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            ITypeTable typeTree,
            int index);

        /// <summary>
        /// Returns a list of references to the specified target.
        /// </summary>
        /// <param name="targetId">The target identifier.</param>
        /// <returns>A list of references to the specified target.</returns>
        IList<IReference> FindReferencesToTarget(ExpandedNodeId targetId);
    }

    /// <summary>
    /// A table of references for a node.
    /// </summary>
    public class ReferenceCollection : IReferenceCollection, IFormattable
    {
        /// <summary>
        /// Initializes the object.
        /// </summary>
        public ReferenceCollection()
        {
            m_references = [];
        }

        /// <summary>
        /// Returns a string representation of the ReferenceCollection.
        /// </summary>
        /// <returns>
        /// A <see cref="string"/> that represents the current <see cref="object"/>.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Returns a string representation of the ReferenceCollection.
        /// </summary>
        /// <param name="format">The <see cref="string"/> specifying the format to use.
        /// -or-
        /// null to use the default format defined for the type of the <see cref="IFormattable"/> implementation.</param>
        /// <param name="formatProvider">The <see cref="IFormatProvider"/> to use to format the value.
        /// -or-
        /// null to obtain the numeric format information from the current locale setting of the operating system.</param>
        /// <returns>
        /// A <see cref="string"/> containing the value of the current instance in the specified format.
        /// </returns>
        /// <exception cref="FormatException"></exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format != null)
            {
                throw new FormatException(CoreUtils.Format("Invalid format string: '{0}'.", format));
            }

            return string.Format(formatProvider, "References {0}", m_references.Count);
        }

        /// <summary>
        /// Adds the reference to the node.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="targetId">The target identifier.</param>
        public void Add(NodeId referenceTypeId, bool isInverse, ExpandedNodeId targetId)
        {
            m_references[new ReferenceNode(referenceTypeId, isInverse, targetId)] = null;
        }

        /// <summary>
        /// Removes the reference from the node.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="targetId">The target identifier.</param>
        /// <returns>The result of removal.</returns>
        public bool Remove(NodeId referenceTypeId, bool isInverse, ExpandedNodeId targetId)
        {
            return m_references.Remove(new ReferenceNode(referenceTypeId, isInverse, targetId));
        }

        /// <summary>
        /// Removes all of the specified references.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <returns>The result of removal.</returns>
        public bool RemoveAll(NodeId referenceTypeId, bool isInverse)
        {
            return m_references.RemoveAll(referenceTypeId, isInverse);
        }

        /// <summary>
        /// Checks whether any references which meet the specified critia exist.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="targetId">The target identifier.</param>
        /// <param name="includeSubtypes">if set to <c>true</c> subtypes are included.</param>
        /// <param name="typeTree">The type tree.</param>
        /// <returns>True if reference exists.</returns>
        public bool Exists(
            NodeId referenceTypeId,
            bool isInverse,
            ExpandedNodeId targetId,
            bool includeSubtypes,
            ITypeTable typeTree)
        {
            var reference = new ReferenceNode(referenceTypeId, isInverse, targetId);

            // check for trivial case.
            if (m_references.ContainsKey(reference))
            {
                return true;
            }

            // can't search subtypes without a type tree.
            if (!includeSubtypes || typeTree == null)
            {
                return false;
            }

            // check for subtypes.
            return m_references.ContainsKey(reference, typeTree);
        }

        /// <summary>
        /// Returns a list of references which match the specified criteria.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="includeSubtypes">if set to <c>true</c> subtypes are included.</param>
        /// <param name="typeTree">The type tree.</param>
        /// <returns>A list of references which match the specified criteria.</returns>
        public IList<IReference> Find(
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            ITypeTable typeTree)
        {
            // can't search subtypes without a type tree.
            if (!includeSubtypes || typeTree == null)
            {
                return m_references.Find(referenceTypeId, isInverse);
            }

            // check for subtypes.
            return m_references.Find(referenceTypeId, isInverse, typeTree);
        }

        /// <summary>
        /// Returns a single target that meets the specified criteria.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="includeSubtypes">if set to <c>true</c> subtypes are included.</param>
        /// <param name="typeTree">The type tree.</param>
        /// <param name="index">The index.</param>
        /// <returns>A single target that meets the specified criteria.</returns>
        public ExpandedNodeId FindTarget(
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            ITypeTable typeTree,
            int index)
        {
            // get the list of matching references.
            IList<IReference> references;
            if (!includeSubtypes || typeTree == null)
            {
                references = m_references.Find(referenceTypeId, isInverse);
            }
            else
            {
                references = m_references.Find(referenceTypeId, isInverse, typeTree);
            }

            // return the target id.
            if (index >= 0 && index < references.Count)
            {
                return references[index].TargetId;
            }

            // not found.
            return null;
        }

        /// <summary>
        /// Returns a list of references to the specified target.
        /// </summary>
        /// <param name="targetId">The target identifier.</param>
        /// <returns>A list of references to the specified target.</returns>
        public IList<IReference> FindReferencesToTarget(ExpandedNodeId targetId)
        {
            return m_references.FindReferencesToTarget(targetId);
        }

        /// <inheritdoc/>
        public int Count => m_references.Count;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public void Add(IReference item)
        {
            m_references.Add(item, null);
        }

        /// <inheritdoc/>
        public bool Remove(IReference item)
        {
            return m_references.Remove(item);
        }

        /// <inheritdoc/>
        public void Clear()
        {
            m_references.Clear();
        }

        /// <inheritdoc/>
        public bool Contains(IReference item)
        {
            return m_references.ContainsKey(item);
        }

        /// <inheritdoc/>
        public void CopyTo(IReference[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            if (arrayIndex < 0 || arrayIndex >= array.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(arrayIndex),
                    "arrayIndex < 0 || arrayIndex >= array.Length");
            }

            var elements = new KeyValuePair<IReference, object>[array.Length - arrayIndex];
            m_references.CopyTo(elements, 0);

            for (int ii = 0; ii < elements.Length; ii++)
            {
                array[arrayIndex + ii] = elements[ii].Key;
            }
        }

        /// <inheritdoc/>
        public IEnumerator<IReference> GetEnumerator()
        {
            return m_references.Keys.GetEnumerator();
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private readonly ReferenceDictionary<object> m_references;
    }

    /// <summary>
    /// A dictionary designed to provide efficient lookups for references.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ReferenceDictionary<T> : IDictionary<IReference, T>
    {
        /// <summary>
        /// Current version
        /// </summary>
        public ulong Version { get; private set; }

        /// <summary>
        /// Creates an empty dictionary.
        /// </summary>
        public ReferenceDictionary()
        {
            Version = 0;
            m_references = [];
            m_list = new LinkedList<KeyValuePair<IReference, T>>();
        }

        /// <summary>
        /// Returns true if the dictionary contains a reference that matches any subtype of the reference type.
        /// </summary>
        /// <param name="reference">The reference.</param>
        /// <param name="typeTree">The type tree.</param>
        /// <returns>
        /// 	<c>true</c> if the dictionary contains key; otherwise, <c>false</c>.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="typeTree"/> is <c>null</c>.</exception>
        public bool ContainsKey(IReference reference, ITypeTable typeTree)
        {
            if (typeTree == null)
            {
                throw new ArgumentNullException(nameof(typeTree));
            }

            if (!ValidateReference(reference, false))
            {
                return false;
            }

            foreach (KeyValuePair<NodeId, ReferenceTypeEntry> entry in m_references)
            {
                if (typeTree.IsTypeOf(entry.Key, reference.ReferenceTypeId) &&
                    ContainsKey(entry.Value, reference))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns a list of references that match the direction and reference type.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <returns>A list of references that match the direction and reference type.</returns>
        public IList<IReference> Find(NodeId referenceTypeId, bool isInverse)
        {
            var hits = new List<IReference>();

            // check for null.
            if (NodeId.IsNull(referenceTypeId))
            {
                return hits;
            }

            // look up the reference type.
            if (!m_references.TryGetValue(referenceTypeId, out ReferenceTypeEntry entry))
            {
                return hits;
            }

            // find the references.
            Find(entry, isInverse, hits);

            return hits;
        }

        /// <summary>
        /// Returns a list of references that match the direction and are subtypes of the reference type.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="typeTree">The type tree.</param>
        /// <returns>A list of references that match the direction and are subtypes of the reference type.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="typeTree"/> is <c>null</c>.</exception>
        public IList<IReference> Find(NodeId referenceTypeId, bool isInverse, ITypeTable typeTree)
        {
            if (typeTree == null)
            {
                throw new ArgumentNullException(nameof(typeTree));
            }

            var hits = new List<IReference>();

            // check for null.
            if (NodeId.IsNull(referenceTypeId))
            {
                return hits;
            }

            foreach (KeyValuePair<NodeId, ReferenceTypeEntry> entry in m_references)
            {
                if (typeTree.IsTypeOf(entry.Key, referenceTypeId))
                {
                    Find(entry.Value, isInverse, hits);
                }
            }

            return hits;
        }

        /// <summary>
        /// Returns a list of references to the specified target.
        /// </summary>
        /// <param name="targetId">The target identifier.</param>
        /// <returns>A list of references to the specified target.</returns>
        public IList<IReference> FindReferencesToTarget(ExpandedNodeId targetId)
        {
            var hits = new List<IReference>();

            // check for null.
            if (NodeId.IsNull(targetId))
            {
                return hits;
            }

            // go through list of references.
            for (LinkedListNode<KeyValuePair<IReference, T>> node = m_list.First;
                node != null;
                node = node.Next)
            {
                if (node.Value.Key.TargetId == targetId)
                {
                    hits.Add(node.Value.Key);
                }
            }

            return hits;
        }

        /// <summary>
        /// Removes all of the references of the specified type and direction.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <returns>The result of removal.</returns>
        public bool RemoveAll(NodeId referenceTypeId, bool isInverse)
        {
            // check for null.
            if (NodeId.IsNull(referenceTypeId))
            {
                return false;
            }

            // look up the reference type.
            if (!m_references.TryGetValue(referenceTypeId, out ReferenceTypeEntry entry))
            {
                return false;
            }

            if (isInverse)
            {
                if (entry.InverseTargets != null)
                {
                    foreach (LinkedListNode<KeyValuePair<IReference, T>> node in entry
                        .InverseTargets
                        .Values)
                    {
                        if (ReferenceEquals(m_list, node.List))
                        {
                            m_list.Remove(node);
                        }

                        entry.InverseTargets = null;
                    }
                }

                if (entry.InverseExternalTargets != null)
                {
                    foreach (LinkedListNode<KeyValuePair<IReference, T>> node in entry
                        .InverseExternalTargets
                        .Values)
                    {
                        if (ReferenceEquals(m_list, node.List))
                        {
                            m_list.Remove(node);
                        }
                    }

                    entry.InverseExternalTargets = null;
                }
            }
            else
            {
                if (entry.ForwardTargets != null)
                {
                    foreach (LinkedListNode<KeyValuePair<IReference, T>> node in entry
                        .ForwardTargets
                        .Values)
                    {
                        if (ReferenceEquals(m_list, node.List))
                        {
                            m_list.Remove(node);
                        }
                    }

                    entry.ForwardTargets = null;
                }

                if (entry.ForwardExternalTargets != null)
                {
                    foreach (LinkedListNode<KeyValuePair<IReference, T>> node in entry
                        .ForwardExternalTargets
                        .Values)
                    {
                        if (ReferenceEquals(m_list, node.List))
                        {
                            m_list.Remove(node);
                        }
                    }

                    entry.ForwardExternalTargets = null;
                }
            }

            // check for empty set.
            if (entry.IsEmpty)
            {
                m_references.Remove(referenceTypeId);
            }

            return true;
        }

        /// <inheritdoc/>
        public void Add(IReference key, T value)
        {
            Add(key, value, false);
        }

        /// <inheritdoc/>
        public bool ContainsKey(IReference key)
        {
            return TryGetEntry(key, out _);
        }

        /// <inheritdoc/>
        public ICollection<IReference> Keys
        {
            get
            {
                var keys = new List<IReference>();

                for (LinkedListNode<KeyValuePair<IReference, T>> node = m_list.First;
                    node != null;
                    node = node.Next)
                {
                    keys.Add(node.Value.Key);
                }

                return keys;
            }
        }

        /// <inheritdoc/>
        public bool Remove(IReference key)
        {
            // validate key.
            if (!ValidateReference(key, false))
            {
                return false;
            }

            Version++;

            // look up the reference type.
            if (!m_references.TryGetValue(key.ReferenceTypeId, out ReferenceTypeEntry entry))
            {
                return false;
            }

            // handle reference to external targets.
            if (key.TargetId.IsAbsolute)
            {
                Dictionary<ExpandedNodeId, LinkedListNode<KeyValuePair<IReference, T>>> targets;
                if (key.IsInverse)
                {
                    targets = entry.InverseExternalTargets;
                }
                else
                {
                    targets = entry.ForwardExternalTargets;
                }

                if (targets == null)
                {
                    return false;
                }

                if (!targets.TryGetValue(
                    key.TargetId,
                    out LinkedListNode<KeyValuePair<IReference, T>> node))
                {
                    return false;
                }

                m_list.Remove(node);
                targets.Remove(key.TargetId);
            }
            // handle reference to internal target.
            else
            {
                NodeIdDictionary<LinkedListNode<KeyValuePair<IReference, T>>> targets;
                if (key.IsInverse)
                {
                    targets = entry.InverseTargets;
                }
                else
                {
                    targets = entry.ForwardTargets;
                }

                if (targets == null)
                {
                    return false;
                }

                if (!targets.TryGetValue(
                    (NodeId)key.TargetId,
                    out LinkedListNode<KeyValuePair<IReference, T>> node))
                {
                    return false;
                }

                m_list.Remove(node);
                targets.Remove((NodeId)key.TargetId);
            }

            // remove empty reference.
            if (entry.IsEmpty)
            {
                m_references.Remove(key.ReferenceTypeId);
            }

            return true;
        }

        /// <inheritdoc/>
        public bool TryGetValue(IReference key, out T value)
        {
            value = default;

            if (!TryGetEntry(key, out KeyValuePair<IReference, T> target))
            {
                return false;
            }

            value = target.Value;
            return true;
        }

        /// <inheritdoc/>
        public ICollection<T> Values
        {
            get
            {
                var values = new List<T>();

                for (LinkedListNode<KeyValuePair<IReference, T>> node = m_list.First;
                    node != null;
                    node = node.Next)
                {
                    values.Add(node.Value.Value);
                }

                return values;
            }
        }

        /// <summary>
        /// Gets or sets the value with the specified NodeId.
        /// </summary>
        /// <value>The value with the specified NodeId.</value>
        /// <exception cref="KeyNotFoundException"></exception>
        public T this[IReference key]
        {
            get
            {
                ValidateReference(key, true);

                if (!TryGetEntry(key, out KeyValuePair<IReference, T> target))
                {
                    throw new KeyNotFoundException();
                }

                return target.Value;
            }
            set => Add(key, value, true);
        }

        /// <inheritdoc/>
        public void Add(KeyValuePair<IReference, T> item)
        {
            Add(item.Key, item.Value);
        }

        /// <inheritdoc/>
        public void Clear()
        {
            Version++;
            m_references.Clear();
            m_list.Clear();
        }

        /// <inheritdoc/>
        public bool Contains(KeyValuePair<IReference, T> item)
        {
            if (!TryGetEntry(item.Key, out KeyValuePair<IReference, T> target))
            {
                return false;
            }

            return Equals(target.Value, item.Value);
        }

        /// <inheritdoc/>
        public void CopyTo(KeyValuePair<IReference, T>[] array, int arrayIndex)
        {
            m_list.CopyTo(array, arrayIndex);
        }

        /// <inheritdoc/>
        public int Count => m_list.Count;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public bool Remove(KeyValuePair<IReference, T> item)
        {
            return Remove(item.Key);
        }

        /// <inheritdoc/>
        public IEnumerator<KeyValuePair<IReference, T>> GetEnumerator()
        {
            return m_list.GetEnumerator();
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Stores the references for a particular reference type.
        /// </summary>
        private class ReferenceTypeEntry
        {
            public NodeIdDictionary<LinkedListNode<KeyValuePair<IReference, T>>> ForwardTargets;
            public Dictionary<ExpandedNodeId, LinkedListNode<KeyValuePair<IReference, T>>> ForwardExternalTargets;
            public NodeIdDictionary<LinkedListNode<KeyValuePair<IReference, T>>> InverseTargets;
            public Dictionary<ExpandedNodeId, LinkedListNode<KeyValuePair<IReference, T>>> InverseExternalTargets;

            /// <summary>
            /// Whether the entry is empty.
            /// </summary>
            /// <value><c>true</c> if this instance is empty; otherwise, <c>false</c>.</value>
            public bool IsEmpty
            {
                get
                {
                    if (ForwardTargets != null && ForwardTargets.Count > 0)
                    {
                        return false;
                    }

                    if (ForwardExternalTargets != null && ForwardExternalTargets.Count > 0)
                    {
                        return false;
                    }

                    if (InverseTargets != null && InverseTargets.Count > 0)
                    {
                        return false;
                    }

                    if (InverseExternalTargets != null && InverseExternalTargets.Count > 0)
                    {
                        return false;
                    }

                    return true;
                }
            }
        }

        /// <summary>
        /// Validates a reference passed as a parameter.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="throwOnError">if set to <c>true</c> [throw on error].</param>
        /// <returns>The result of the validation.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        private static bool ValidateReference(IReference key, bool throwOnError)
        {
            if (key == null)
            {
                if (throwOnError)
                {
                    throw new ArgumentNullException(nameof(key), "IReference must not be null.");
                }

                return false;
            }

            if (NodeId.IsNull(key.ReferenceTypeId))
            {
                if (throwOnError)
                {
                    throw new ArgumentNullException(
                        nameof(key),
                        "IReference does not have a valid ReferenceTypeId.");
                }

                return false;
            }

            if (NodeId.IsNull(key.TargetId))
            {
                if (throwOnError)
                {
                    throw new ArgumentNullException(
                        nameof(key),
                        "IReference does not have a valid TargetId.");
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns the target entry associated with the reference.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <returns>The target entry associated with the reference.</returns>
        private bool TryGetEntry(IReference key, out KeyValuePair<IReference, T> value)
        {
            value = new KeyValuePair<IReference, T>();

            // validate key.
            if (!ValidateReference(key, false))
            {
                return false;
            }

            // look up the reference type.
            if (!m_references.TryGetValue(key.ReferenceTypeId, out ReferenceTypeEntry entry))
            {
                return false;
            }

            // handle reference to external targets.
            if (key.TargetId.IsAbsolute)
            {
                Dictionary<ExpandedNodeId, LinkedListNode<KeyValuePair<IReference, T>>> targets;
                if (key.IsInverse)
                {
                    targets = entry.InverseExternalTargets;
                }
                else
                {
                    targets = entry.ForwardExternalTargets;
                }

                if (targets == null)
                {
                    return false;
                }

                if (targets.TryGetValue(
                    key.TargetId,
                    out LinkedListNode<KeyValuePair<IReference, T>> node))
                {
                    value = node.Value;
                    return true;
                }
            }
            // handle reference to internal target.
            else
            {
                NodeIdDictionary<LinkedListNode<KeyValuePair<IReference, T>>> targets;
                if (key.IsInverse)
                {
                    targets = entry.InverseTargets;
                }
                else
                {
                    targets = entry.ForwardTargets;
                }

                if (targets == null)
                {
                    return false;
                }

                if (targets.TryGetValue(
                    (NodeId)key.TargetId,
                    out LinkedListNode<KeyValuePair<IReference, T>> node))
                {
                    value = node.Value;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Adds or replaces a reference.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <param name="replace">if set to <c>true</c> reference is replaced.</param>
        /// <exception cref="ArgumentException"></exception>
        private void Add(IReference key, T value, bool replace)
        {
            // validate key.
            ValidateReference(key, true);

            Version++;

            // look up the reference type.
            if (!m_references.TryGetValue(key.ReferenceTypeId, out ReferenceTypeEntry entry))
            {
                entry = new ReferenceTypeEntry();
                m_references.Add(key.ReferenceTypeId, entry);
            }

            // handle reference to external targets.
            if (key.TargetId.IsAbsolute)
            {
                Dictionary<ExpandedNodeId, LinkedListNode<KeyValuePair<IReference, T>>> targets;
                if (key.IsInverse)
                {
                    entry.InverseExternalTargets ??= [];

                    targets = entry.InverseExternalTargets;
                }
                else
                {
                    entry.ForwardExternalTargets ??= [];

                    targets = entry.ForwardExternalTargets;
                }

                // create a new target.
                var node = new LinkedListNode<KeyValuePair<IReference, T>>(
                    new KeyValuePair<IReference, T>(key, value));

                // check if target already exists.
                if (!targets.TryGetValue(
                    key.TargetId,
                    out LinkedListNode<KeyValuePair<IReference, T>> existingNode))
                {
                    m_list.AddLast(node);
                }
                // need to replace reference in linked linked as well as the target list.
                else
                {
                    if (!replace)
                    {
                        throw new ArgumentException(
                            "Key already exists in dictionary.",
                            nameof(key));
                    }

                    m_list.AddAfter(existingNode, node);
                    m_list.Remove(existingNode);
                }

                targets[key.TargetId] = node;
            }
            // handle reference to internal target.
            else
            {
                NodeIdDictionary<LinkedListNode<KeyValuePair<IReference, T>>> targets;
                if (key.IsInverse)
                {
                    entry.InverseTargets ??= [];

                    targets = entry.InverseTargets;
                }
                else
                {
                    entry.ForwardTargets ??= [];

                    targets = entry.ForwardTargets;
                }

                var targetId = (NodeId)key.TargetId;

                // create a new target.
                var node = new LinkedListNode<KeyValuePair<IReference, T>>(
                    new KeyValuePair<IReference, T>(key, value));

                // check if target already exists.
                if (!targets.TryGetValue(
                    targetId,
                    out LinkedListNode<KeyValuePair<IReference, T>> existingNode))
                {
                    m_list.AddLast(node);
                }
                // need to replace reference in linked linked as well as the target list.
                else
                {
                    if (!replace)
                    {
                        throw new ArgumentException(
                            "Key already exists in dictionary.",
                            nameof(key));
                    }

                    m_list.AddAfter(existingNode, node);
                    m_list.Remove(existingNode);
                }

                targets[targetId] = node;
            }
        }

        /// <summary>
        /// Checks the isInverse flag are returns true if a specified target exists.
        /// </summary>
        /// <param name="entry">The entry.</param>
        /// <param name="reference">The reference.</param>
        /// <returns>
        /// 	<c>true</c> if the specified entry contains key; otherwise, <c>false</c>.
        /// </returns>
        private static bool ContainsKey(ReferenceTypeEntry entry, IReference reference)
        {
            // handle reference to external targets.
            if (reference.TargetId.IsAbsolute)
            {
                Dictionary<ExpandedNodeId, LinkedListNode<KeyValuePair<IReference, T>>> targets;
                if (reference.IsInverse)
                {
                    targets = entry.InverseExternalTargets;
                }
                else
                {
                    targets = entry.ForwardExternalTargets;
                }

                if (targets == null)
                {
                    return false;
                }

                return targets.ContainsKey(reference.TargetId);
            }
            // handle reference to internal target.
            else
            {
                NodeIdDictionary<LinkedListNode<KeyValuePair<IReference, T>>> targets;
                if (reference.IsInverse)
                {
                    targets = entry.InverseTargets;
                }
                else
                {
                    targets = entry.ForwardTargets;
                }

                if (targets == null)
                {
                    return false;
                }

                return targets.ContainsKey((NodeId)reference.TargetId);
            }
        }

        /// <summary>
        /// Add references to a list that match the criteria.
        /// </summary>
        /// <param name="entry">The entry.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="hits">The hits.</param>
        private static void Find(ReferenceTypeEntry entry, bool isInverse, List<IReference> hits)
        {
            if (isInverse)
            {
                if (entry.InverseTargets != null)
                {
                    foreach (LinkedListNode<KeyValuePair<IReference, T>> target in entry
                        .InverseTargets
                        .Values)
                    {
                        hits.Add(target.Value.Key);
                    }
                }

                if (entry.InverseExternalTargets != null)
                {
                    foreach (LinkedListNode<KeyValuePair<IReference, T>> target in entry
                        .InverseExternalTargets
                        .Values)
                    {
                        hits.Add(target.Value.Key);
                    }
                }
            }
            else
            {
                if (entry.ForwardTargets != null)
                {
                    foreach (LinkedListNode<KeyValuePair<IReference, T>> target in entry
                        .ForwardTargets
                        .Values)
                    {
                        hits.Add(target.Value.Key);
                    }
                }

                if (entry.ForwardExternalTargets != null)
                {
                    foreach (LinkedListNode<KeyValuePair<IReference, T>> target in entry
                        .ForwardExternalTargets
                        .Values)
                    {
                        hits.Add(target.Value.Key);
                    }
                }
            }
        }

        private readonly NodeIdDictionary<ReferenceTypeEntry> m_references;
        private readonly LinkedList<KeyValuePair<IReference, T>> m_list;
    }
}
