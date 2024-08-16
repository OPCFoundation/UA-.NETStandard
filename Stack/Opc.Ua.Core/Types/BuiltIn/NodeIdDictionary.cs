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

// define USE_LEGACY_IMPLEMENTATION to use the original implementation
// #define USE_LEGACY_IMPLEMENTATION

// benchmarks revealed that the use of a standard Dictionary<NodeId> class
// with efficent hash code implementations is up to 10xfaster than
// the original implementation using multiple SortedDictionary instances

using System;
using System.Collections;
using System.Collections.Generic;

namespace Opc.Ua
{
#if !USE_LEGACY_IMPLEMENTATION
    /// <summary>
    /// A dictionary designed to provide efficient lookups for objects identified by a NodeId
    /// </summary>
    public class NodeIdDictionary<T> : Dictionary<NodeId, T>
    {
        private static readonly NodeIdComparer s_comparer = new NodeIdComparer();

        /// <summary>
        /// Creates an empty dictionary.
        /// </summary>
        public NodeIdDictionary() : base(s_comparer)
        {
        }

        /// <summary>
        /// Creates an empty dictionary with capacity.
        /// </summary>
        public NodeIdDictionary(int capacity) : base(capacity, s_comparer)
        {
        }
    }

#else // USE_LEGACY_IMPLEMENTATION

    /// <summary>
    /// A dictionary designed to provide efficient lookups for objects identified by a NodeId
    /// </summary>
    public class NodeIdDictionary<T> : IDictionary<NodeId, T>
    {
        #region Constructors
        /// <summary>
        /// Creates an empty dictionary.
        /// </summary>
        public NodeIdDictionary()
        {
            m_version = 0;
            m_numericIds = new SortedDictionary<ulong, T>();
        }
        #endregion

        #region IDictionary<NodeId,T> Members
        /// <summary cref="IDictionary.Add" />
        public void Add(NodeId key, T value)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            m_version++;

            switch (key.IdType)
            {
                case IdType.Numeric:
                {
                    ulong id = ((ulong)key.NamespaceIndex) << 32;
                    id += (uint)key.Identifier;
                    m_numericIds.Add(id, value);
                    return;
                }

                case IdType.String:
                {
                    IDictionary<string, T> dictionary = GetStringDictionary(key.NamespaceIndex, true);
                    dictionary.Add((string)key.Identifier, value);
                    return;
                }

                case IdType.Guid:
                {
                    IDictionary<Guid, T> dictionary = GetGuidDictionary(key.NamespaceIndex, true);
                    dictionary.Add((Guid)key.Identifier, value);
                    return;
                }

                case IdType.Opaque:
                {
                    IDictionary<ByteKey, T> dictionary = GetOpaqueDictionary(key.NamespaceIndex, true);
                    dictionary.Add(new NodeIdDictionary<T>.ByteKey((byte[])key.Identifier), value);
                    return;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(key), "key.IdType");
        }

        /// <summary cref="IDictionary{TKey,TValue}.ContainsKey" />
        public bool ContainsKey(NodeId key)
        {
            if (key == null)
            {
                return false;
            }

            switch (key.IdType)
            {
                case IdType.Numeric:
                {
                    ulong id = ((ulong)key.NamespaceIndex) << 32;
                    id += (uint)key.Identifier;
                    return m_numericIds.ContainsKey(id);
                }

                case IdType.String:
                {
                    IDictionary<string, T> dictionary = GetStringDictionary(key.NamespaceIndex, false);

                    if (dictionary != null)
                    {
                        return dictionary.ContainsKey((string)key.Identifier);
                    }

                    break;
                }

                case IdType.Guid:
                {
                    IDictionary<Guid, T> dictionary = GetGuidDictionary(key.NamespaceIndex, false);

                    if (dictionary != null)
                    {
                        return dictionary.ContainsKey((Guid)key.Identifier);
                    }

                    break;
                }

                case IdType.Opaque:
                {
                    IDictionary<ByteKey, T> dictionary = GetOpaqueDictionary(key.NamespaceIndex, false);

                    if (dictionary != null)
                    {
                        return dictionary.ContainsKey(new ByteKey((byte[])key.Identifier));
                    }

                    break;
                }
            }

            return false;
        }

        /// <summary cref="IDictionary{TKey,TValue}.Keys" />
        public ICollection<NodeId> Keys
        {
            get
            {
                List<NodeId> keys = new List<NodeId>();

                foreach (ulong id in m_numericIds.Keys)
                {
                    keys.Add(new NodeId((uint)(id & 0xFFFFFFFF), (ushort)((id >> 32) & 0xFFFF)));
                }

                if (m_dictionarySets == null)
                {
                    return keys;
                }

                for (ushort ii = 0; ii < (ushort)m_dictionarySets.Length; ii++)
                {
                    DictionarySet dictionarySet = m_dictionarySets[ii];

                    if (dictionarySet == null)
                    {
                        continue;
                    }

                    if (dictionarySet.String != null)
                    {
                        foreach (string id in dictionarySet.String.Keys)
                        {
                            keys.Add(new NodeId(id, ii));
                        }
                    }

                    if (dictionarySet.Guid != null)
                    {
                        foreach (Guid id in dictionarySet.Guid.Keys)
                        {
                            keys.Add(new NodeId(id, ii));
                        }
                    }

                    if (dictionarySet.Opaque != null)
                    {
                        foreach (ByteKey id in dictionarySet.Opaque.Keys)
                        {
                            keys.Add(new NodeId(id.Bytes, ii));
                        }
                    }
                }

                return keys;
            }
        }

        /// <summary cref="IDictionary.Remove" />
        public bool Remove(NodeId key)
        {
            if (key == null)
            {
                return false;
            }

            m_version++;

            switch (key.IdType)
            {
                case IdType.Numeric:
                {
                    ulong id = ((ulong)key.NamespaceIndex) << 32;
                    id += (uint)key.Identifier;
                    return m_numericIds.Remove(id);
                }

                case IdType.String:
                {
                    IDictionary<string, T> dictionary = GetStringDictionary(key.NamespaceIndex, false);

                    if (dictionary != null)
                    {
                        return dictionary.Remove((string)key.Identifier);
                    }

                    break;
                }

                case IdType.Guid:
                {
                    IDictionary<Guid, T> dictionary = GetGuidDictionary(key.NamespaceIndex, false);

                    if (dictionary != null)
                    {
                        return dictionary.Remove((Guid)key.Identifier);
                    }

                    break;
                }

                case IdType.Opaque:
                {
                    IDictionary<ByteKey, T> dictionary = GetOpaqueDictionary(key.NamespaceIndex, false);

                    if (dictionary != null)
                    {
                        return dictionary.Remove(new ByteKey((byte[])key.Identifier));
                    }

                    break;
                }
            }

            return false;
        }

        /// <summary cref="IDictionary{TKey,TValue}.TryGetValue" />
        public bool TryGetValue(NodeId key, out T value)
        {
            value = default;

            if (key == null)
            {
                return false;
            }

            switch (key.IdType)
            {
                case IdType.Numeric:
                {
                    ulong id = ((ulong)key.NamespaceIndex) << 32;
                    id += (uint)key.Identifier;
                    return m_numericIds.TryGetValue(id, out value);
                }

                case IdType.String:
                {
                    IDictionary<string, T> dictionary = GetStringDictionary(key.NamespaceIndex, false);

                    if (dictionary != null)
                    {
                        return dictionary.TryGetValue((string)key.Identifier, out value);
                    }

                    break;
                }

                case IdType.Guid:
                {
                    IDictionary<Guid, T> dictionary = GetGuidDictionary(key.NamespaceIndex, false);

                    if (dictionary != null)
                    {
                        return dictionary.TryGetValue((Guid)key.Identifier, out value);
                    }

                    break;
                }

                case IdType.Opaque:
                {
                    IDictionary<ByteKey, T> dictionary = GetOpaqueDictionary(key.NamespaceIndex, false);

                    if (dictionary != null)
                    {
                        return dictionary.TryGetValue(new ByteKey((byte[])key.Identifier), out value);
                    }

                    break;
                }
            }

            return false;
        }

        /// <summary cref="IDictionary{TKey,TValue}.Values" />
        public ICollection<T> Values
        {
            get
            {
                List<T> values = new List<T>();
                values.AddRange(m_numericIds.Values);

                if (m_dictionarySets == null)
                {
                    return values;
                }

                for (int ii = 0; ii < m_dictionarySets.Length; ii++)
                {
                    DictionarySet dictionarySet = m_dictionarySets[ii];

                    if (dictionarySet == null)
                    {
                        continue;
                    }

                    if (dictionarySet.String != null)
                    {
                        values.AddRange(dictionarySet.String.Values);
                    }

                    if (dictionarySet.Guid != null)
                    {
                        values.AddRange(dictionarySet.Guid.Values);
                    }

                    if (dictionarySet.Opaque != null)
                    {
                        values.AddRange(dictionarySet.Opaque.Values);
                    }
                }

                return values;
            }
        }

        /// <summary>
        /// Gets or sets the value with the specified NodeId.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1065:DoNotRaiseExceptionsInUnexpectedLocations")]
        public T this[NodeId key]
        {
            get
            {
                if (key == null)
                {
                    throw new ArgumentNullException(nameof(key));
                }

                switch (key.IdType)
                {
                    case IdType.Numeric:
                    {
                        ulong id = ((ulong)key.NamespaceIndex) << 32;
                        id += (uint)key.Identifier;
                        return m_numericIds[id];
                    }

                    case IdType.String:
                    {
                        IDictionary<string, T> dictionary = GetStringDictionary(key.NamespaceIndex, false);

                        if (dictionary != null)
                        {
                            return dictionary[(string)key.Identifier];
                        }

                        break;
                    }

                    case IdType.Guid:
                    {
                        IDictionary<Guid, T> dictionary = GetGuidDictionary(key.NamespaceIndex, false);

                        if (dictionary != null)
                        {
                            return dictionary[(Guid)key.Identifier];
                        }

                        break;
                    }

                    case IdType.Opaque:
                    {
                        IDictionary<ByteKey, T> dictionary = GetOpaqueDictionary(key.NamespaceIndex, false);

                        if (dictionary != null)
                        {
                            return dictionary[new ByteKey((byte[])key.Identifier)];
                        }

                        break;
                    }
                }

                throw new KeyNotFoundException();
            }

            set
            {
                if (key == null)
                {
                    throw new ArgumentNullException(nameof(key));
                }

                m_version++;

                switch (key.IdType)
                {
                    case IdType.Numeric:
                    {
                        ulong id = ((ulong)key.NamespaceIndex) << 32;
                        id += (uint)key.Identifier;
                        m_numericIds[id] = value;
                        return;
                    }

                    case IdType.String:
                    {
                        IDictionary<string, T> dictionary = GetStringDictionary(key.NamespaceIndex, true);
                        dictionary[(string)key.Identifier] = value;
                        return;
                    }

                    case IdType.Guid:
                    {
                        IDictionary<Guid, T> dictionary = GetGuidDictionary(key.NamespaceIndex, true);
                        dictionary[(Guid)key.Identifier] = value;
                        return;
                    }

                    case IdType.Opaque:
                    {
                        IDictionary<ByteKey, T> dictionary = GetOpaqueDictionary(key.NamespaceIndex, true);
                        dictionary[new ByteKey((byte[])key.Identifier)] = value;
                        return;
                    }
                }

                throw new ArgumentOutOfRangeException(nameof(key), "key.IdType");
            }
        }
        #endregion

        #region ICollection<KeyValuePair<NodeId,T>> Members
        /// <summary cref="ICollection{T}.Add" />
        public void Add(KeyValuePair<NodeId, T> item)
        {
            Add(item.Key, item.Value);
        }

        /// <summary cref="ICollection{T}.Clear" />
        public void Clear()
        {
            m_version++;
            m_numericIds.Clear();
            m_dictionarySets = null;
        }

        /// <summary cref="ICollection{T}.Contains" />
        public bool Contains(KeyValuePair<NodeId, T> item)
        {
            T value;

            if (!TryGetValue(item.Key, out value))
            {
                return false;
            }

            return Object.Equals(value, item.Value);
        }

        /// <summary cref="ICollection{T}.CopyTo" />
        public void CopyTo(KeyValuePair<NodeId, T>[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            if (arrayIndex < 0 || array.Length <= arrayIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex), "arrayIndex < 0 || array.Length <= arrayIndex");
            }

            foreach (KeyValuePair<ulong, T> entry in m_numericIds)
            {
                CheckCopyTo(array, arrayIndex);

                array[arrayIndex++] = new KeyValuePair<NodeId, T>(
                    new NodeId((uint)(entry.Key & 0xFFFFFFFF), (ushort)((entry.Key >> 32) & 0xFFFF)),
                    entry.Value);
            }

            if (m_dictionarySets == null)
            {
                return;
            }

            for (int ii = 0; ii < m_dictionarySets.Length; ii++)
            {
                DictionarySet dictionarySet = m_dictionarySets[ii];

                if (dictionarySet == null)
                {
                    continue;
                }

                if (dictionarySet.String != null)
                {
                    foreach (KeyValuePair<string, T> entry in dictionarySet.String)
                    {
                        CheckCopyTo(array, arrayIndex);
                        array[arrayIndex++] = new KeyValuePair<NodeId, T>(new NodeId(entry.Key, (ushort)ii), entry.Value);
                    }
                }

                if (dictionarySet.Guid != null)
                {
                    foreach (KeyValuePair<Guid, T> entry in dictionarySet.Guid)
                    {
                        CheckCopyTo(array, arrayIndex);
                        array[arrayIndex++] = new KeyValuePair<NodeId, T>(new NodeId(entry.Key, (ushort)ii), entry.Value);
                    }
                }

                if (dictionarySet.Opaque != null)
                {
                    foreach (KeyValuePair<ByteKey, T> entry in dictionarySet.Opaque)
                    {
                        CheckCopyTo(array, arrayIndex);
                        array[arrayIndex++] = new KeyValuePair<NodeId, T>(new NodeId(entry.Key.Bytes, (ushort)ii), entry.Value);
                    }
                }
            }
        }

        /// <summary>
        /// Checks that there is enough space in the array.
        /// </summary>
        private static void CheckCopyTo(KeyValuePair<NodeId, T>[] array, int arrayIndex)
        {
            if (arrayIndex >= array.Length)
            {
                throw new ArgumentException("Not enough space in array.", nameof(array));
            }
        }

        /// <summary cref="ICollection{T}.Count" />
        public int Count
        {
            get
            {
                int count = m_numericIds.Count;

                if (m_dictionarySets == null)
                {
                    return count;
                }

                for (int ii = 0; ii < m_dictionarySets.Length; ii++)
                {
                    DictionarySet dictionarySet = m_dictionarySets[ii];

                    if (dictionarySet == null)
                    {
                        continue;
                    }

                    if (dictionarySet.String != null)
                    {
                        count += dictionarySet.String.Count;
                    }

                    if (dictionarySet.Guid != null)
                    {
                        count += dictionarySet.Guid.Count;
                    }

                    if (dictionarySet.Opaque != null)
                    {
                        count += dictionarySet.Opaque.Count;
                    }
                }

                return count;
            }
        }

        /// <summary cref="ICollection{T}.IsReadOnly" />
        public bool IsReadOnly => false;

        /// <summary cref="ICollection{T}.Remove" />
        public bool Remove(KeyValuePair<NodeId, T> item)
        {
            return Remove(item.Key);
        }
        #endregion

        #region IEnumerable<KeyValuePair<NodeId,T>> Members
        /// <summary cref="System.Collections.IEnumerable.GetEnumerator()" />
        public IEnumerator<KeyValuePair<NodeId, T>> GetEnumerator()
        {
            return new Enumerator(this);
        }
        #endregion

        #region IEnumerable Members
        /// <summary cref="System.Collections.IEnumerable.GetEnumerator()" />
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Returns the dictionary set for the specified namespace.
        /// </summary>
        private DictionarySet GetDictionarySet(ushort namespaceIndex, bool create)
        {
            if (m_dictionarySets == null || m_dictionarySets.Length <= namespaceIndex)
            {
                if (!create)
                {
                    return null;
                }

                DictionarySet[] dictionarySets = new NodeIdDictionary<T>.DictionarySet[namespaceIndex + 1];

                if (m_dictionarySets != null)
                {
                    Array.Copy(m_dictionarySets, dictionarySets, m_dictionarySets.Length);
                }

                m_dictionarySets = dictionarySets;
            }

            DictionarySet dictionarySet = m_dictionarySets[namespaceIndex];

            if (dictionarySet == null)
            {
                if (!create)
                {
                    return null;
                }

                m_dictionarySets[namespaceIndex] = dictionarySet = new NodeIdDictionary<T>.DictionarySet();
            }

            return dictionarySet;
        }

        /// <summary>
        /// Returns the dictionary set for String identifiers in the specified namespace.
        /// </summary>
        private IDictionary<string, T> GetStringDictionary(ushort namespaceIndex, bool create)
        {
            DictionarySet dictionarySet = GetDictionarySet(namespaceIndex, create);

            if (dictionarySet == null)
            {
                return null;
            }

            IDictionary<string, T> dictionary = dictionarySet.String;

            if (dictionary == null)
            {
                if (!create)
                {
                    return null;
                }

                dictionary = dictionarySet.String = new SortedDictionary<string, T>();
            }

            return dictionary;
        }

        /// <summary>
        /// Returns the dictionary set for Guid identifiers in the specified namespace.
        /// </summary>
        private IDictionary<Guid, T> GetGuidDictionary(ushort namespaceIndex, bool create)
        {
            DictionarySet dictionarySet = GetDictionarySet(namespaceIndex, create);

            if (dictionarySet == null)
            {
                return null;
            }

            IDictionary<Guid, T> dictionary = dictionarySet.Guid;

            if (dictionary == null)
            {
                if (!create)
                {
                    return null;
                }

                dictionary = dictionarySet.Guid = new SortedDictionary<Guid, T>();
            }

            return dictionary;
        }

        /// <summary>
        /// Returns the dictionary set for Opaque identifiers in the specified namespace.
        /// </summary>
        private IDictionary<ByteKey, T> GetOpaqueDictionary(ushort namespaceIndex, bool create)
        {
            DictionarySet dictionarySet = GetDictionarySet(namespaceIndex, create);

            if (dictionarySet == null)
            {
                return null;
            }

            IDictionary<ByteKey, T> dictionary = dictionarySet.Opaque;

            if (dictionary == null)
            {
                if (!create)
                {
                    return null;
                }

                dictionary = dictionarySet.Opaque = new SortedDictionary<ByteKey, T>();
            }

            return dictionary;
        }
        #endregion

        #region DictionarySet Class
        /// <summary>
        /// Stores the dictionaries for a single namespace index.
        /// </summary>
        private class DictionarySet
        {
            public SortedDictionary<string, T> String;
            public SortedDictionary<Guid, T> Guid;
            public SortedDictionary<ByteKey, T> Opaque;
        }
        #endregion

        #region ByteKey Class
        /// <summary>
        /// Wraps a byte array for use as a key in a dictionary.
        /// </summary>
        private struct ByteKey : IEquatable<ByteKey>, IComparable<ByteKey>
        {
            #region Public Interface
            /// <summary>
            /// Initializes the key with an array of bytes.
            /// </summary>
            public ByteKey(byte[] bytes)
            {
                Bytes = bytes;
            }

            /// <summary>
            /// The array of bytes.
            /// </summary>
            public byte[] Bytes;
            #endregion

            #region IEquatable<ByteKey> Members
            /// <summary cref="IEquatable{T}"></summary>
            public bool Equals(ByteKey other)
            {
                if (other.Bytes == null || Bytes == null)
                {
                    return (other.Bytes == null && Bytes == null);
                }

                if (other.Bytes.Length != Bytes.Length)
                {
                    return false;
                }

                for (int ii = 0; ii < other.Bytes.Length; ii++)
                {
                    if (other.Bytes[ii] != Bytes[ii])
                    {
                        return false;
                    }
                }

                return false;
            }
            #endregion

            #region IComparable<ByteKey> Members
            /// <summary cref="IComparable{T}.CompareTo"></summary>
            public int CompareTo(ByteKey other)
            {
                if (other.Bytes == null || Bytes == null)
                {
                    return (other.Bytes == null) ? +1 : -1;
                }

                if (other.Bytes.Length != Bytes.Length)
                {
                    return (other.Bytes.Length < Bytes.Length) ? +1 : -1;
                }

                for (int ii = 0; ii < other.Bytes.Length; ii++)
                {
                    if (other.Bytes[ii] != Bytes[ii])
                    {
                        return (other.Bytes[ii] < Bytes[ii]) ? +1 : -1;
                    }
                }

                return 0;
            }
            #endregion
        }
        #endregion

        #region Enumerator Class
        /// <summary>
        /// The enumerator for the node dictionary.
        /// </summary>
        private class Enumerator : IEnumerator<KeyValuePair<NodeId, T>>
        {
            #region Constructors
            /// <summary>
            /// Constructs the enumerator for the specified dictionary.
            /// </summary>
            public Enumerator(NodeIdDictionary<T> dictionary)
            {
                m_dictionary = dictionary;
                m_version = dictionary.m_version;
                m_idType = 0;
                m_namespaceIndex = 0;
            }
            #endregion

            #region IEnumerator<KeyValuePair<NodeId,T>> Members
            /// <summary cref="IEnumerator{T}.Current" />
            public KeyValuePair<NodeId, T> Current
            {
                get
                {
                    CheckVersion();

                    if (m_enumerator == null)
                    {
                        throw new InvalidOperationException("The enumerator is positioned before the first element of the collection or after the last element.");
                    }

                    NodeId id = null;

                    switch (m_idType)
                    {
                        case IdType.Numeric:
                        {
                            ulong key = (ulong)m_enumerator.Key;
                            id = new NodeId((uint)(key & 0xFFFFFFFF), (ushort)((key >> 32) & 0xFFFF));
                            break;
                        }

                        case IdType.String:
                        {
                            id = new NodeId((string)m_enumerator.Key, m_namespaceIndex);
                            break;
                        }

                        case IdType.Guid:
                        {
                            id = new NodeId((Guid)m_enumerator.Key, m_namespaceIndex);
                            break;
                        }

                        case IdType.Opaque:
                        {
                            id = new NodeId(((ByteKey)m_enumerator.Key).Bytes, m_namespaceIndex);
                            break;
                        }
                    }

                    return new KeyValuePair<NodeId, T>(id, (T)m_enumerator.Value);
                }
            }
            #endregion

            #region IDisposable Members
            /// <summary>
            /// Frees any unmanaged resources.
            /// </summary>
            public void Dispose()
            {
                Dispose(true);
            }

            /// <summary>
            /// An overrideable version of the Dispose.
            /// </summary>
            protected virtual void Dispose(bool disposing)
            {
                if (disposing)
                {
                    // do to nothing.
                }
            }
            #endregion

            #region IEnumerator Members
            /// <summary cref="IEnumerator.Current" />
            object System.Collections.IEnumerator.Current => this.Current;

            /// <summary cref="IEnumerator.MoveNext" />
            public bool MoveNext()
            {
                CheckVersion();

                if (m_enumerator == null)
                {
                    m_enumerator = m_dictionary.m_numericIds.GetEnumerator();
                    m_idType = IdType.Numeric;
                    m_namespaceIndex = 0;
                }

                bool result = m_enumerator.MoveNext();

                if (result)
                {
                    return true;
                }

                while (m_dictionary.m_dictionarySets != null && m_namespaceIndex < m_dictionary.m_dictionarySets.Length)
                {
                    if (m_idType == IdType.Numeric)
                    {
                        m_idType = IdType.String;

                        IDictionary<string, T> dictionary = m_dictionary.GetStringDictionary(m_namespaceIndex, false);

                        if (dictionary != null)
                        {
                            ReleaseEnumerator();
                            m_enumerator = (IDictionaryEnumerator)dictionary.GetEnumerator();

                            if (m_enumerator.MoveNext())
                            {
                                return true;
                            }
                        }
                    }

                    if (m_idType == IdType.String)
                    {
                        m_idType = IdType.Guid;

                        IDictionary<Guid, T> dictionary = m_dictionary.GetGuidDictionary(m_namespaceIndex, false);

                        if (dictionary != null)
                        {
                            ReleaseEnumerator();
                            m_enumerator = (IDictionaryEnumerator)dictionary.GetEnumerator();

                            if (m_enumerator.MoveNext())
                            {
                                return true;
                            }
                        }
                    }

                    if (m_idType == IdType.Guid)
                    {
                        m_idType = IdType.Opaque;

                        IDictionary<ByteKey, T> dictionary = m_dictionary.GetOpaqueDictionary(m_namespaceIndex, false);

                        if (dictionary != null)
                        {
                            ReleaseEnumerator();
                            m_enumerator = (IDictionaryEnumerator)dictionary.GetEnumerator();

                            if (m_enumerator.MoveNext())
                            {
                                return true;
                            }
                        }
                    }

                    m_idType = IdType.Numeric;
                    m_namespaceIndex++;
                }

                ReleaseEnumerator();
                return false;
            }

            /// <summary cref="IEnumerator.Reset" />
            public void Reset()
            {
                CheckVersion();
                ReleaseEnumerator();
                m_idType = 0;
                m_namespaceIndex = 0;
            }
            #endregion

            #region Private Methods
            /// <summary>
            /// Releases and disposes the current enumerator.
            /// </summary>
            private void ReleaseEnumerator()
            {
                if (m_enumerator != null)
                {
                    if (m_enumerator is IDisposable diposeable)
                    {
                        diposeable.Dispose();
                    }

                    m_enumerator = null;
                }
            }

            /// <summary>
            /// Checks if the dictionary has changed.
            /// </summary>
            private void CheckVersion()
            {
                if (m_version != m_dictionary.m_version)
                {
                    throw new InvalidOperationException("The dictionary was modified after the enumerator was created.");
                }
            }
            #endregion

            #region Private Fields
            private NodeIdDictionary<T> m_dictionary;
            private ushort m_namespaceIndex;
            private IdType m_idType;
            private IDictionaryEnumerator m_enumerator;
            private ulong m_version;
            #endregion
        }
        #endregion

        #region Private Fields
        private DictionarySet[] m_dictionarySets;
        private SortedDictionary<ulong, T> m_numericIds;
        private ulong m_version;
        #endregion
    }
#endif
}
