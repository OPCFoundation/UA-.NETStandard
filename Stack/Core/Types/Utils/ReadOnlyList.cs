/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// A template list class that can be used to expose members of immutable classes.
    /// </summary>
    public class ReadOnlyList<T> : IList<T>, IList
    {
        #region ICollection<T> Members
        /// <summary>
        /// Wraps an exising list.
        /// </summary>
        public ReadOnlyList(IList<T> list)
        {
            m_list = list;

            if (m_list == null)
            {
                m_list = new T[0];
            }
        }

        /// <summary>
        /// Makes a shallow copy of an exising list.
        /// </summary>
        public ReadOnlyList(IList<T> list, bool makeCopy)
        {
            if (list != null && makeCopy)
            {
                T[] values = new T[list.Count];

                for (int ii = 0; ii < values.Length; ii++)
                {
                    values[ii] = list[ii];
                }

                list = values;
            }

            m_list = list;

            if (m_list == null)
            {
                m_list = new T[0];
            }
        }
        #endregion

        #region ICollection<T> Members
        /// <summary>
        /// The number of items in the list.
        /// </summary>
        public int Count
        {
            get { return m_list.Count; }
        }

        /// <summary>
        /// Adds new item  to the list (not supported).
        /// </summary>
        public void Add(T item)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Removes all item from the list (not supported).
        /// </summary>
        public void Clear()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Returns true if the item is in the list.
        /// </summary>
        public bool Contains(T item)
        {
            return m_list.Contains(item);
        }

        /// <summary>
        /// Copies the elements of the list to an array.
        /// </summary>
        public void CopyTo(T[] array, int arrayIndex)
        {
            m_list.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Indicates that the list is read only.
        /// </summary>
        public bool IsReadOnly
        {
            get { return true; }
        }

        /// <summary>
        /// Removes an item from the list (not supported).
        /// </summary>
        public bool Remove(T item)
        {
            throw new NotSupportedException();
        }
        #endregion

        #region IList<T> Members
        /// <summary>
        /// Returns the list of the specified item in the list.
        /// </summary>
        public int IndexOf(T item)
        {
            return m_list.IndexOf(item);
        }

        /// <summary>
        /// Inserts an item into the list (not supported).
        /// </summary>
        public void Insert(int index, T item)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Removes an item from the list (not supported).
        /// </summary>
        public void RemoveAt(int index)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Gets the item at the specified index.
        /// </summary>
        public T this[int index]
        {
            get
            {
                return m_list[index];
            }

            set
            {
                throw new NotSupportedException();
            }
        }
        #endregion

        #region IEnumerable<T> Members
        /// <summary>
        /// Returns an enumerator for the list.
        /// </summary>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return m_list.GetEnumerator();
        }

        /// <summary>
        /// Returns an enumerator for the list.
        /// </summary>
        public IEnumerator<T> GetEnumerator()
        {
            return m_list.GetEnumerator();
        }
        #endregion

        #region Static Operators
        /// <summary>
        /// Creates a read-only list from a list.
        /// </summary>
        /// <param name="values">The list of values.</param>
        /// <returns>The read-only list.</returns>
        public static ReadOnlyList<T> ToList(T[] values)
        {
            return new ReadOnlyList<T>(values);
        }

        /// <summary>
        /// Creates a read-only list from a list.
        /// </summary>
        /// <param name="values">The list of values.</param>
        /// <returns>The read-only list.</returns>
        public static implicit operator ReadOnlyList<T>(T[] values)
        {
            return new ReadOnlyList<T>(values);
        }
        #endregion

        #region IList Members
        /// <summary>
        /// Adds an item to the <see cref="T:System.Collections.IList"/>.
        /// </summary>
        int IList.Add(object value)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Removes all items from the <see cref="T:System.Collections.Generic.ICollection`1"/>.
        /// </summary>
        void IList.Clear()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Determines whether the <see cref="T:System.Collections.IList"/> contains a specific value.
        /// </summary>
        bool IList.Contains(object value)
        {
            return this.Contains((T)value);
        }

        /// <summary>
        /// Determines the index of a specific item in the <see cref="T:System.Collections.IList"/>.
        /// </summary>
        int IList.IndexOf(object value)
        {
            return this.IndexOf((T)value);
        }

        /// <summary>
        /// Inserts an item to the <see cref="T:System.Collections.IList"/> at the specified index.
        /// </summary>
        void IList.Insert(int index, object value)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets a value indicating whether the <see cref="T:System.Collections.IList"/> has a fixed size.
        /// </summary>
        bool IList.IsFixedSize
        {
            get { return true; }
        }

        /// <summary>
        /// Gets a value indicating whether the <see cref="T:System.Collections.Generic.ICollection`1"/> is read-only.
        /// </summary>
        bool IList.IsReadOnly
        {
            get { return true; }
        }

        /// <summary>
        /// Removes the first occurrence of a specific object from the <see cref="T:System.Collections.IList"/>.
        /// </summary>
        void IList.Remove(object value)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Removes the <see cref="T:System.Collections.Generic.IList`1"/> item at the specified index.
        /// </summary>
        void IList.RemoveAt(int index)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets or sets the <see cref="System.Object"/> at the specified index.
        /// </summary>
        object IList.this[int index]
        {
            get
            {
                return this[index];
            }

            set
            {
                this[index] = (T)value;
            }
        }
        #endregion

        #region ICollection Members
        /// <summary>
        /// Copies the elements of the <see cref="T:System.Collections.ICollection"/> to an <see cref="T:System.Array"/>, starting at a particular <see cref="T:System.Array"/> index.
        /// </summary>
        void ICollection.CopyTo(Array array, int index)
        {
            this.CopyTo((T[])array, index);
        }

        /// <summary>
        /// Gets the number of elements contained in the <see cref="T:System.Collections.Generic.ICollection`1"/>.
        /// </summary>
        int ICollection.Count
        {
            get { return this.Count; }
        }

        /// <summary>
        /// Gets a value indicating whether access to the <see cref="T:System.Collections.ICollection"/> is synchronized (thread safe).
        /// </summary>
        bool ICollection.IsSynchronized
        {
            get { return false; }
        }

        /// <summary>
        /// Gets an object that can be used to synchronize access to the <see cref="T:System.Collections.ICollection"/>.
        /// </summary>
        object ICollection.SyncRoot
        {
            get { return false; }
        }
        #endregion

        #region Private Fields
        private IList<T> m_list;
        #endregion
    }
}
