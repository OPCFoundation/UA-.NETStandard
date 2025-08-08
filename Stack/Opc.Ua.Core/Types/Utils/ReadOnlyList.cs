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
    /// A template list class that can be used to expose members of immutable classes.
    /// </summary>
    public class ReadOnlyList<T> : IList<T>, IList
    {
        /// <summary>
        /// Wraps an existing list.
        /// </summary>
        public ReadOnlyList(IList<T> list)
        {
            m_list = list ?? Array.Empty<T>();
        }

        /// <summary>
        /// Makes a shallow copy of an existing list.
        /// </summary>
        public ReadOnlyList(IList<T> list, bool makeCopy)
        {
            if (list != null && makeCopy)
            {
                var values = new T[list.Count];

                for (int ii = 0; ii < values.Length; ii++)
                {
                    values[ii] = list[ii];
                }

                list = values;
            }

            m_list = list ?? Array.Empty<T>();
        }

        /// <summary>
        /// The number of items in the list.
        /// </summary>
        public int Count => m_list.Count;

        /// <summary>
        /// Adds new item to the list (not supported).
        /// </summary>
        public void Add(T item)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Removes all items from the list (not supported).
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
        public bool IsReadOnly => true;

        /// <summary>
        /// Removes an item from the list (not supported).
        /// </summary>
        public bool Remove(T item)
        {
            throw new NotSupportedException();
        }

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
            get => m_list[index];

            set => throw new NotSupportedException();
        }

        /// <summary>
        /// Returns an enumerator for the list.
        /// </summary>
        IEnumerator IEnumerable.GetEnumerator()
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

        /// <summary>
        /// Adds an item to the <see cref="IList"/>.
        /// </summary>
        int IList.Add(object value)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Removes all items from the <see cref="ICollection"/>.
        /// </summary>
        void IList.Clear()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Determines whether the <see cref="IList"/> contains a specific value.
        /// </summary>
        bool IList.Contains(object value)
        {
            return Contains((T)value);
        }

        /// <summary>
        /// Determines the index of a specific item in the <see cref="IList"/>.
        /// </summary>
        int IList.IndexOf(object value)
        {
            return IndexOf((T)value);
        }

        /// <summary>
        /// Inserts an item to the <see cref="IList"/> at the specified index.
        /// </summary>
        void IList.Insert(int index, object value)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets a value indicating whether the <see cref="IList"/> has a fixed size.
        /// </summary>
        bool IList.IsFixedSize => true;

        /// <summary>
        /// Gets a value indicating whether the <see cref="IList"/> is read-only.
        /// </summary>
        bool IList.IsReadOnly => true;

        /// <summary>
        /// Removes the first occurrence of a specific object from the <see cref="IList"/>.
        /// </summary>
        void IList.Remove(object value)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Removes the <see cref="IList"/> item at the specified index.
        /// </summary>
        void IList.RemoveAt(int index)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets or sets the <see cref="object"/> at the specified index.
        /// </summary>
        object IList.this[int index]
        {
            get => this[index];

            set => this[index] = (T)value;
        }

        /// <summary>
        /// Copies the elements of the <see cref="ICollection"/> to an <see cref="Array"/>, starting at a particular <see cref="Array"/> index.
        /// </summary>
        void ICollection.CopyTo(Array array, int index)
        {
            CopyTo((T[])array, index);
        }

        /// <summary>
        /// Gets the number of elements contained in the <see cref="ICollection"/>.
        /// </summary>
        int ICollection.Count => Count;

        /// <summary>
        /// Gets a value indicating whether access to the <see cref="ICollection"/> is synchronized (thread safe).
        /// </summary>
        bool ICollection.IsSynchronized => false;

        /// <summary>
        /// Gets an object that can be used to synchronize access to the <see cref="ICollection"/>.
        /// </summary>
        object ICollection.SyncRoot => false;

        private readonly IList<T> m_list;
    }
}
