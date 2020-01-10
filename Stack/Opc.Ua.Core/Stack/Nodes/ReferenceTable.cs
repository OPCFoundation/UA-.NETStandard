/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
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
    public interface IReferenceCollection : ICollection<IReference>, IEnumerable<IReference>
    {
        /// <summary>
        /// Adds the reference to the node.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="targetId">The target identifier.</param>
        void Add(
            NodeId         referenceTypeId, 
            bool           isInverse, 
            ExpandedNodeId targetId);

        /// <summary>
        /// Removes the reference from the node.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="targetId">The target identifier.</param>
        /// <returns>The result of removal.</returns>
        bool Remove(
            NodeId         referenceTypeId, 
            bool           isInverse, 
            ExpandedNodeId targetId);

        /// <summary>
        /// Removes all of the specified references.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <returns>The result of removal.</returns>
        bool RemoveAll(
            NodeId referenceTypeId, 
            bool   isInverse);

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
            NodeId         referenceTypeId, 
            bool           isInverse, 
            ExpandedNodeId targetId, 
            bool           includeSubtypes, 
            ITypeTable      typeTree);

        /// <summary>
        /// Returns a list of references which match the specified criteria.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="includeSubtypes">if set to <c>true</c> subtypes are included.</param>
        /// <param name="typeTree">The type tree.</param>
        /// <returns>A list of references which match the specified criteria.</returns>
        IList<IReference> Find(
            NodeId    referenceTypeId, 
            bool      isInverse, 
            bool      includeSubtypes, 
            ITypeTable typeTree);

        /// <summary>
        /// Returns a single target that meets the specifed criteria.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="includeSubtypes">if set to <c>true</c> subtypes are included.</param>
        /// <param name="typeTree">The type tree.</param>
        /// <param name="index">The index.</param>
        /// <returns>A single target that meets the specifed criteria.</returns>
        ExpandedNodeId FindTarget(
            NodeId    referenceTypeId, 
            bool      isInverse, 
            bool      includeSubtypes, 
            ITypeTable typeTree, 
            int       index);

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
        #region Constructors
        /// <summary>
        /// Initializes the object.
        /// </summary>
        public ReferenceCollection()
        {
            m_references = new IReferenceDictionary<object>();
        }
        #endregion
        
        #region IFormattable Members
        /// <summary>
        /// Returns a string representation of the ReferenceCollection.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Returns a string representation of the ReferenceCollection.
        /// </summary>
        /// <param name="format">The <see cref="T:System.String"/> specifying the format to use.
        /// -or-
        /// null to use the default format defined for the type of the <see cref="T:System.IFormattable"/> implementation.</param>
        /// <param name="formatProvider">The <see cref="T:System.IFormatProvider"/> to use to format the value.
        /// -or-
        /// null to obtain the numeric format information from the current locale setting of the operating system.</param>
        /// <returns>
        /// A <see cref="T:System.String"/> containing the value of the current instance in the specified format.
        /// </returns>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format != null)
            {
                throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
            }

            return Utils.Format("References {0}", m_references.Count);
        }
        #endregion

        #region IReferenceCollection Members
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
            NodeId         referenceTypeId, 
            bool           isInverse, 
            ExpandedNodeId targetId, 
            bool           includeSubtypes,
            ITypeTable     typeTree)
        {
            ReferenceNode reference = new ReferenceNode(referenceTypeId, isInverse, targetId);

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
            NodeId     referenceTypeId, 
            bool       isInverse, 
            bool       includeSubtypes, 
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
        /// Returns a single target that meets the specifed criteria.
        /// </summary>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="includeSubtypes">if set to <c>true</c> subtypes are included.</param>
        /// <param name="typeTree">The type tree.</param>
        /// <param name="index">The index.</param>
        /// <returns>A single target that meets the specifed criteria.</returns>
        public ExpandedNodeId FindTarget(
            NodeId     referenceTypeId,
            bool       isInverse,
            bool       includeSubtypes,
            ITypeTable typeTree, 
            int        index)
        {
            // get the list of matching references.
            IList<IReference> references = null;

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
        #endregion

        #region ICollection<IReference> Members
        /// <summary cref="ICollection{T}.Count" />
        public int Count
        {
            get 
            {
                return m_references.Count;
            }
        }
        
        /// <summary cref="ICollection{T}.IsReadOnly" />
        public bool IsReadOnly
        {
            get { return false; }
        }
        
        /// <summary cref="ICollection{T}.Add" />
        public void Add(IReference item)
        {
            m_references.Add(item, null);
        }
        
        /// <summary cref="ICollection{T}.Remove" />
        public bool Remove(IReference item)
        {
            return m_references.Remove(item);
        }

        /// <summary cref="ICollection{T}.Clear" />
        public void Clear()
        {
            m_references.Clear();
        }

        /// <summary cref="ICollection{T}.Contains" />
        public bool Contains(IReference item)
        {
            return m_references.ContainsKey(item);
        }
        
        /// <summary cref="ICollection{T}.CopyTo" />
        public void CopyTo(IReference[] array, int arrayIndex)
        {
            if (array == null) 
                throw new ArgumentNullException(nameof(array));
            
            if (arrayIndex < 0 || arrayIndex >= array.Length) 
                throw new ArgumentOutOfRangeException(nameof(arrayIndex), "arrayIndex < 0 || arrayIndex >= array.Length");

            KeyValuePair<IReference,object>[] elements = new KeyValuePair<IReference,object>[array.Length-arrayIndex];
            m_references.CopyTo(elements, 0);

            for (int ii = 0; ii < elements.Length; ii++)
            {
                array[arrayIndex+ii] = elements[ii].Key;
            }
        }
        #endregion

        #region IEnumerable<IReference> Members
        /// <summary cref="IEnumerable{T}.GetEnumerator" />
        public IEnumerator<IReference> GetEnumerator()
        {
            return m_references.Keys.GetEnumerator();
        }
        #endregion

        #region IEnumerable Members
        /// <summary cref="System.Collections.IEnumerable.GetEnumerator" />
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        #endregion
        
        #region Private Fields
        private IReferenceDictionary<object> m_references;
        #endregion
    }
    
    /// <summary>
    /// A dictionary designed to provide efficient lookups for references.
    /// </summary>
    public class IReferenceDictionary<T> : IDictionary<IReference,T>
    {        
        #region Constructors
        /// <summary>
        /// Creates an empty dictionary.
        /// </summary>
        public IReferenceDictionary()
        {
            m_version = 0;
            m_references = new NodeIdDictionary<ReferenceTypeEntry>();
            m_list = new LinkedList<KeyValuePair<IReference,T>>();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Returns true if the dictionary contains a reference that matches any subtype of the reference type.
        /// </summary>
        /// <param name="reference">The reference.</param>
        /// <param name="typeTree">The type tree.</param>
        /// <returns>
        /// 	<c>true</c> if the dictionary contains key; otherwise, <c>false</c>.
        /// </returns>
        public bool ContainsKey(
            IReference reference,
            ITypeTable typeTree)
        {
            if (typeTree == null) throw new ArgumentNullException(nameof(typeTree));

            if (!ValidateReference(reference, false))
            {
                return false;
            }
            
            foreach (KeyValuePair<NodeId,ReferenceTypeEntry> entry in m_references)
            {
                if (typeTree.IsTypeOf(entry.Key, reference.ReferenceTypeId))
                {
                    if (ContainsKey(entry.Value, reference))
                    {
                        return true;
                    }
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
        public IList<IReference> Find(
            NodeId referenceTypeId,
            bool   isInverse)
        {            
            List<IReference> hits = new List<IReference>();
            
            // check for null.
            if (NodeId.IsNull(referenceTypeId))
            {
                return hits;
            }
            
            // look up the reference type.
            ReferenceTypeEntry entry = null;

            if (!m_references.TryGetValue(referenceTypeId, out entry))
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
        public IList<IReference> Find(
            NodeId     referenceTypeId,
            bool       isInverse,
            ITypeTable typeTree)
        {
            if (typeTree == null) throw new ArgumentNullException(nameof(typeTree));

            List<IReference> hits = new List<IReference>();
            
            // check for null.
            if (NodeId.IsNull(referenceTypeId))
            {
                return hits;
            }
            
            foreach (KeyValuePair<NodeId,ReferenceTypeEntry> entry in m_references)
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
            List<IReference> hits = new List<IReference>();
            
            // check for null.
            if (NodeId.IsNull(targetId))
            {
                return hits;
            }

            // go throw list of references.
            for (LinkedListNode<KeyValuePair<IReference,T>> node = m_list.First; node != null; node = node.Next)
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
            ReferenceTypeEntry entry = null;

            if (!m_references.TryGetValue(referenceTypeId, out entry))
            {
                return false;
            }
                        
            if (isInverse)
            {
                if (entry.InverseTargets != null)
                {
                    foreach (LinkedListNode<KeyValuePair<IReference,T>> node in entry.InverseTargets.Values)
                    {
                        if (Object.ReferenceEquals(m_list, node.List))
                        {
                            m_list.Remove(node);
                        }

                        entry.InverseTargets = null;
                    }
                }
                
                if (entry.InverseExternalTargets != null)
                {
                    foreach (LinkedListNode<KeyValuePair<IReference,T>> node in entry.InverseExternalTargets.Values)
                    {
                        if (Object.ReferenceEquals(m_list, node.List))
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
                    foreach (LinkedListNode<KeyValuePair<IReference,T>> node in entry.ForwardTargets.Values)
                    {
                        if (Object.ReferenceEquals(m_list, node.List))
                        {
                            m_list.Remove(node);
                        }
                    }

                    entry.ForwardTargets = null;
                }
                
                if (entry.ForwardExternalTargets != null)
                {
                    foreach (LinkedListNode<KeyValuePair<IReference,T>> node in entry.ForwardExternalTargets.Values)
                    {
                        if (Object.ReferenceEquals(m_list, node.List))
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
        #endregion

        #region IDictionary<IReference,T> Members
        /// <summary cref="IDictionary.Add" />
        public void Add(IReference key, T value)
        {
            Add(key, value, false);
        }
        
        /// <summary cref="IDictionary{TKey,TValue}.ContainsKey" />
        public bool ContainsKey(IReference key)
        {
            KeyValuePair<IReference,T> target;

            if (!TryGetEntry(key, out target))
            {
                return false;
            }
            
            return true;
        }
        
        /// <summary cref="IDictionary{TKey,TValue}.Keys" />
        public ICollection<IReference> Keys
        {
            get 
            { 
                List<IReference> keys = new List<IReference>();

                for (LinkedListNode<KeyValuePair<IReference,T>> node = m_list.First; node != null; node = node.Next)
                {
                    keys.Add(node.Value.Key);
                }
                    
                return keys;
            }
        }
        
        /// <summary cref="IDictionary.Remove" />
        public bool Remove(IReference key)
        {
            // validate key.
            if (!ValidateReference(key, false))
            {
                return false;
            }

            m_version++;
            
            // look up the reference type.
            ReferenceTypeEntry entry = null;

            if (!m_references.TryGetValue(key.ReferenceTypeId, out entry))
            {
                return false;
            }

            // handle reference to external targets.            
            if (key.TargetId.IsAbsolute)
            {
                Dictionary<ExpandedNodeId,LinkedListNode<KeyValuePair<IReference,T>>> targets = null;

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

                LinkedListNode<KeyValuePair<IReference,T>> node;

                if (!targets.TryGetValue(key.TargetId, out node))
                {
                    return false;
                }

                m_list.Remove(node);
                targets.Remove(key.TargetId);
            }

            // handle reference to internal target.
            else
            {
                NodeIdDictionary<LinkedListNode<KeyValuePair<IReference,T>>> targets = null;

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

                LinkedListNode<KeyValuePair<IReference,T>> node;

                if (!targets.TryGetValue((NodeId)key.TargetId, out node))
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
        
        /// <summary cref="IDictionary{TKey,TValue}.TryGetValue" />
        public bool TryGetValue(IReference key, out T value)
        {
            value = default(T);

            KeyValuePair<IReference,T> target;

            if (!TryGetEntry(key, out target))
            {
                return false;
            }
            
            value = target.Value;
            return true;
        }
                

        /// <summary cref="IDictionary{TKey,TValue}.Values" />
        public ICollection<T> Values
        {
            get 
            { 
                List<T> values = new List<T>();

                for (LinkedListNode<KeyValuePair<IReference,T>> node = m_list.First; node != null; node = node.Next)
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1065:DoNotRaiseExceptionsInUnexpectedLocations")]
        public T this[IReference key]
        {
            get
            {
                ValidateReference(key, true);
                
                KeyValuePair<IReference,T> target;

                if (!TryGetEntry(key, out target))
                {
                    throw new KeyNotFoundException();
                }
                
                return target.Value;       
            }

            set
            {
                Add(key, value, true);
            }
        }
        #endregion

        #region ICollection<KeyValuePair<IReference,T>> Members
        /// <summary cref="ICollection{T}.Add" />
        public void Add(KeyValuePair<IReference,T> item)
        {
            Add(item.Key, item.Value);
        }

        /// <summary cref="ICollection{T}.Clear" />
        public void Clear()
        {
            m_version++;
            m_references.Clear();
            m_list.Clear();
        }
        
        /// <summary cref="ICollection{T}.Contains" />
        public bool Contains(KeyValuePair<IReference,T> item)
        {
            KeyValuePair<IReference,T> target;

            if (!TryGetEntry(item.Key, out target))
            {
                return false;
            }
            
            return Object.Equals(target.Value, item.Value);
        }
        
        /// <summary cref="ICollection{T}.CopyTo" />
        public void CopyTo(KeyValuePair<IReference,T>[] array, int arrayIndex)
        { 
            m_list.CopyTo(array, arrayIndex);
        }

        /// <summary cref="ICollection{T}.Count" />
        public int Count
        {
            get 
            { 
                return m_list.Count;
            }
        }
        
        /// <summary cref="ICollection{T}.IsReadOnly" />
        public bool IsReadOnly
        {
            get { return false; }
        }
        
        /// <summary cref="ICollection{T}.Remove" />
        public bool Remove(KeyValuePair<IReference, T> item)
        {
            return Remove(item.Key);
        }
        #endregion

        #region IEnumerable<KeyValuePair<IReference,T>> Members
        /// <summary cref="System.Collections.IEnumerable.GetEnumerator()" />
        public IEnumerator<KeyValuePair<IReference, T>> GetEnumerator()
        {                      
             return m_list.GetEnumerator();
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
        #region ReferenceTypeEntry Class
        /// <summary>
        /// Stores the references for a particular reference type.
        /// </summary>
        private class ReferenceTypeEntry
        {
            public NodeIdDictionary<LinkedListNode<KeyValuePair<IReference,T>>> ForwardTargets;
            public Dictionary<ExpandedNodeId,LinkedListNode<KeyValuePair<IReference,T>>> ForwardExternalTargets;
            public NodeIdDictionary<LinkedListNode<KeyValuePair<IReference,T>>> InverseTargets;
            public Dictionary<ExpandedNodeId,LinkedListNode<KeyValuePair<IReference,T>>> InverseExternalTargets;

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
        #endregion 
        
        /// <summary>
        /// Validates a reference passed as a parameter.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="throwOnError">if set to <c>true</c> [throw on error].</param>
        /// <returns>The result of the validation.</returns>
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
                    throw new ArgumentNullException(nameof(key), "IReference does not have a valid ReferenceTypeId.");
                }
                
                return false;
            }

            if (NodeId.IsNull(key.TargetId))
            {
                if (throwOnError)
                {
                    throw new ArgumentNullException(nameof(key), "IReference does not have a valid TargetId.");
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
        private bool TryGetEntry(IReference key, out KeyValuePair<IReference,T> value)
        {
            value = new KeyValuePair<IReference,T>();

            // validate key.
            if (!ValidateReference(key, false))
            {
                return false;
            }

            // look up the reference type.
            ReferenceTypeEntry entry = null;

            if (!m_references.TryGetValue(key.ReferenceTypeId, out entry))
            {
                return false;
            }

            // handle reference to external targets.            
            if (key.TargetId.IsAbsolute)
            {
                Dictionary<ExpandedNodeId,LinkedListNode<KeyValuePair<IReference,T>>> targets = null;

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

                LinkedListNode<KeyValuePair<IReference,T>> node;

                if (targets.TryGetValue(key.TargetId, out node))
                {
                    value = node.Value;
                    return true;
                }
            }

            // handle reference to internal target.
            else
            {
                NodeIdDictionary<LinkedListNode<KeyValuePair<IReference,T>>> targets = null;

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
                
                LinkedListNode<KeyValuePair<IReference,T>> node;

                if (targets.TryGetValue((NodeId)key.TargetId, out node))
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
        private void Add(IReference key, T value, bool replace)
        {
            // validate key.
            ValidateReference(key, true);
            
            m_version++;

            // look up the reference type.
            ReferenceTypeEntry entry = null;

            if (!m_references.TryGetValue(key.ReferenceTypeId, out entry))
            {
                entry = new ReferenceTypeEntry();
                m_references.Add(key.ReferenceTypeId, entry);
            }

            // handle reference to external targets.            
            if (key.TargetId.IsAbsolute)
            {
                Dictionary<ExpandedNodeId,LinkedListNode<KeyValuePair<IReference,T>>> targets = null;

                if (key.IsInverse)
                {
                    if (entry.InverseExternalTargets == null)
                    {
                        entry.InverseExternalTargets = new Dictionary<ExpandedNodeId,LinkedListNode<KeyValuePair<IReference,T>>>();
                    }

                    targets = entry.InverseExternalTargets;
                }
                else
                {
                    if (entry.ForwardExternalTargets == null)
                    {
                        entry.ForwardExternalTargets = new Dictionary<ExpandedNodeId,LinkedListNode<KeyValuePair<IReference,T>>>();
                    }

                    targets = entry.ForwardExternalTargets;
                }

                // create a new target.
                LinkedListNode<KeyValuePair<IReference,T>> node = new LinkedListNode<KeyValuePair<IReference,T>>(new KeyValuePair<IReference,T>(key, value));
                
                // check if target already exists.
                LinkedListNode<KeyValuePair<IReference,T>> existingNode = null;

                if (!targets.TryGetValue(key.TargetId, out existingNode))
                {
                    existingNode = node;
                    m_list.AddLast(node);
                }

                // need to replace reference in linked linked as well as the target list.
                else
                {
                    if (!replace)
                    {
                        throw new ArgumentException("Key already exists in dictionary.", nameof(key));
                    }

                    m_list.AddAfter(existingNode, node);
                    m_list.Remove(existingNode);
                }
                
                targets[key.TargetId] = node;
            }

            // handle reference to internal target.
            else
            {
                NodeIdDictionary<LinkedListNode<KeyValuePair<IReference,T>>> targets = null;

                if (key.IsInverse)
                {
                    if (entry.InverseTargets == null)
                    {
                        entry.InverseTargets = new NodeIdDictionary<LinkedListNode<KeyValuePair<IReference,T>>>();
                    }

                    targets = entry.InverseTargets;
                }
                else
                {
                    if (entry.ForwardTargets == null)
                    {
                        entry.ForwardTargets = new NodeIdDictionary<LinkedListNode<KeyValuePair<IReference,T>>>();
                    }

                    targets = entry.ForwardTargets;
                }
                
                NodeId targetId = (NodeId)key.TargetId;

                // create a new target.
                LinkedListNode<KeyValuePair<IReference,T>> node = new LinkedListNode<KeyValuePair<IReference,T>>(new KeyValuePair<IReference,T>(key, value));
                
                // check if target already exists.
                LinkedListNode<KeyValuePair<IReference,T>> existingNode = null;

                if (!targets.TryGetValue(targetId, out existingNode))
                {
                    existingNode = node;
                    m_list.AddLast(node);
                }

                // need to replace reference in linked linked as well as the target list.
                else
                {
                    if (!replace)
                    {
                        throw new ArgumentException("Key already exists in dictionary.", nameof(key));
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
                Dictionary<ExpandedNodeId,LinkedListNode<KeyValuePair<IReference,T>>> targets = null;

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
                NodeIdDictionary<LinkedListNode<KeyValuePair<IReference,T>>> targets = null;

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
        private static void Find(
            ReferenceTypeEntry entry, 
            bool               isInverse,
            List<IReference>   hits)
        {
            if (isInverse)
            {
                if (entry.InverseTargets != null)
                {
                    foreach (LinkedListNode<KeyValuePair<IReference,T>> target in entry.InverseTargets.Values)
                    {
                        hits.Add(target.Value.Key);
                    }
                }
                
                if (entry.InverseExternalTargets != null)
                {
                    foreach (LinkedListNode<KeyValuePair<IReference,T>> target in entry.InverseExternalTargets.Values)
                    {
                        hits.Add(target.Value.Key);
                    }
                }
            }
            else
            {
                if (entry.ForwardTargets != null)
                {
                    foreach (LinkedListNode<KeyValuePair<IReference,T>> target in entry.ForwardTargets.Values)
                    {
                        hits.Add(target.Value.Key);
                    }
                }
                
                if (entry.ForwardExternalTargets != null)
                {
                    foreach (LinkedListNode<KeyValuePair<IReference,T>> target in entry.ForwardExternalTargets.Values)
                    {
                        hits.Add(target.Value.Key);
                    }
                }
            }
        }
        #endregion

        #region Private Fields
        private NodeIdDictionary<ReferenceTypeEntry> m_references;
        private LinkedList<KeyValuePair<IReference,T>> m_list;
        private ulong m_version;
        #endregion
    }
}
