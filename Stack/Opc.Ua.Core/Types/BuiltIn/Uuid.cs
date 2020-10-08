/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
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
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// A wrapper for a GUID used during object serialization.
    /// </summary>
    /// <remarks>
    /// This class provides a wrapper around the <see cref="Guid"/> object, allowing it to be serialized
    /// and encoded/decoded to/from an underlying stream.
    /// </remarks>x
    [DataContract(Name = "Guid", Namespace = Namespaces.OpcUaXsd)]
    public struct Uuid : IComparable, IFormattable
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with a string.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a string. The string will be used to create a <see cref="Guid"/>.
        /// </remarks>
        /// <param name="text">The string that will be turned into a Guid</param>
        public Uuid(string text)
        {
            m_guid = new Guid(text);
        }

        /// <summary>
        /// Initializes the object with a Guid.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a Guid.
        /// </remarks>
        /// <param name="guid">The Guid to wrap</param>
        public Uuid(Guid guid)
        {
            m_guid = guid;
        }
        #endregion

        #region Static Fields

        /// <summary>
        /// A constant containing an empty GUID.
        /// </summary>
        /// <remarks>
        /// A constant containing an empty GUID.
        /// </remarks>
        public static readonly Uuid Empty = new Uuid();

        #endregion

        #region Public Properties
        /// <summary>
        /// The GUID serialized as a string.
        /// </summary>
        /// <remarks>
        /// The GUID serialized as a string.
        /// </remarks>
        [DataMember(Name = "String", Order = 1)]
        public string GuidString
        {
            get { return m_guid.ToString(); }

            set
            {
                if (String.IsNullOrEmpty(value))
                {
                    m_guid = Guid.Empty;
                }
                else
                {
                    m_guid = new Guid(value);
                }
            }
        }
        #endregion

        #region Static Members
        /// <summary>
        /// Converts Uuid to a Guid structure.
        /// </summary>
        /// <remarks>
        /// Converts Uuid to a Guid structure.
        /// </remarks>
        /// <param name="guid">The Guid to convert to a Uuid</param>
        public static implicit operator Guid(Uuid guid)
        {
            return guid.m_guid;
        }

        /// <summary>
        /// Converts Guid to a Uuid.
        /// </summary>
        /// <remarks>
        /// Converts Guid to a Uuid.
        /// </remarks>
        /// <param name="guid">The <see cref="Guid"/> to convert to a <see cref="Uuid"/></param>
        public static explicit operator Uuid(Guid guid)
        {
            return new Uuid(guid);
        }

        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are equal.
        /// </remarks>
        /// <param name="a">The first object to compare</param>
        /// <param name="b">The second object to compare</param>
        public static bool operator ==(Uuid a, Uuid b)
        {
            return a.Equals(b);
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are not equal.
        /// </remarks>
        /// <param name="a">The first object to compare</param>
        /// <param name="b">The second object being compared to</param>
        public static bool operator !=(Uuid a, Uuid b)
        {
            return !a.Equals(b);
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are not equal.
        /// </remarks>
        /// <param name="a">The first object being compared</param>
        /// <param name="b">The second object being compared to</param>
        public static bool operator ==(Uuid a, Guid b)
        {
            return a.Equals(b);
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are not equal.
        /// </remarks>
        /// <param name="a">The first object being compared</param>
        /// <param name="b">The second object being compared to</param>
        public static bool operator !=(Uuid a, Guid b)
        {
            return !a.Equals(b);
        }

        /// <summary>
        /// Returns true if the object a is less than object b.
        /// </summary>
        /// <remarks>
        /// Returns true if the object a is less than object b.
        /// </remarks>
        /// <param name="a">The first object being compared</param>
        /// <param name="b">The second object being compared to</param>
        public static bool operator <(Uuid a, Uuid b)
        {
            return a.CompareTo(b) < 0;
        }

        /// <summary>
        /// Returns true if the object a is greater than object b.
        /// </summary>
        /// <remarks>
        /// Returns true if the object a is greater than object b.
        /// </remarks>
        /// <param name="a">The first object being compared</param>
        /// <param name="b">The second object being compared to</param>
        public static bool operator >(Uuid a, Uuid b)
        {
            return a.CompareTo(b) > 0;
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are equal.
        /// </remarks>
        /// <param name="obj">The object being compared to *this* object</param>
        public override bool Equals(object obj)
        {
            return (CompareTo(obj) == 0);
        }

        /// <summary>
        /// Returns a hash code for the object.
        /// </summary>
        /// <remarks>
        /// Returns a unique hash code for the object.
        /// </remarks>
        public override int GetHashCode()
        {
            return m_guid.GetHashCode();
        }

        /// <summary>
        /// Converts the object to a string.
        /// </summary>
        /// <remarks>
        /// Converts the object to a string.
        /// </remarks>
        public override string ToString()
        {
            return m_guid.ToString();
        }
        #endregion

        #region IComparable Members
        /// <summary>
		/// Compares the current instance to the object.
		/// </summary>
        /// <remarks>
        /// Compares the current instance to the object. This function will check if the object
        /// passed in is a <see cref="Guid"/> or <see cref="Uuid"/>.
        /// </remarks>
        /// <param name="obj">The object being compared to *this* object</param>
		public int CompareTo(object obj)
        {
            // check for reference comparisons.
            if (Object.ReferenceEquals(this, obj))
            {
                return 0;
            }

            // check for uuids.
            if (obj is Uuid)
            {
                return ((Uuid)obj).m_guid.CompareTo(m_guid);
            }

            // compare guids.            
            if (obj is Guid)
            {
                return m_guid.CompareTo((Guid)obj);
            }

            return +1;
        }
        #endregion

        #region IFormattable Members
        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <remarks>
        /// Returns the string representation of the object.
        /// </remarks>
        /// <param name="format">The format you want to apply to the string</param>
        /// <param name="formatProvider">The FormatProvider</param>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            return this.m_guid.ToString(format);
        }
        #endregion

        #region Private Fields
        private Guid m_guid;
        #endregion        
    }

    #region UuidCollection Class
    /// <summary>
    /// A collection of Uuids.
    /// </summary>
    [CollectionDataContract(Name = "ListOfGuid", Namespace = Namespaces.OpcUaXsd, ItemName = "Guid")]
    public partial class UuidCollection : List<Uuid>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public UuidCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        /// <param name="collection">The collection to copy</param>
        public UuidCollection(IEnumerable<Uuid> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">The maximum size of the colletion</param>
        public UuidCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">The array of <see cref="Uuid"/> values to return as a Collection</param>
        public static UuidCollection ToUuidCollection(Uuid[] values)
        {
            if (values != null)
            {
                return new UuidCollection(values);
            }

            return new UuidCollection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">The array of <see cref="Uuid"/> values to return as a collection</param>
        public static implicit operator UuidCollection(Uuid[] values)
        {
            return ToUuidCollection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Returns a new instance of the collection, copying the contents of this collection
        /// </remarks>
        public new object MemberwiseClone()
        {
            return new UuidCollection(this);
        }
    }
    #endregion
}//namespace
