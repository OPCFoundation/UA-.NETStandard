/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// A wrapper for a GUID used during object serialization.
    /// </summary>
    /// <remarks>
    /// This class provides a wrapper around the <see cref="System.Guid"/>
    /// object, allowing it to be serialized  and encoded/decoded
    /// to/from an underlying stream.
    /// </remarks>x
    public readonly struct Uuid :
        IComparable,
        IFormattable,
        IEquatable<Uuid>,
        IEquatable<Guid>
    {
        /// <summary>
        /// Initializes the object with a string.
        /// </summary>
        /// <param name="text">The string that will be turned
        /// into a Guid</param>
        public Uuid(string text)
        {
            Guid = new Guid(text);
        }

        /// <summary>
        /// Create a new guid from a byte array.
        /// </summary>
        public Uuid(byte[] bytes)
        {
            Guid = new Guid(bytes);
        }

        /// <summary>
        /// Create a new guid from a byte array.
        /// </summary>
        public Uuid(ByteString byteString)
        {
            Guid = new Guid(byteString.ToArray());
        }

        /// <summary>
        /// Initializes the object with a Guid.
        /// </summary>
        /// <param name="guid">The Guid to wrap</param>
        [JsonConstructor]
        public Uuid(Guid guid)
        {
            Guid = guid;
        }

        /// <summary>
        /// A constant containing an empty GUID.
        /// </summary>
        public static readonly Uuid Empty;

        /// <summary>
        /// The wrapped guid value.
        /// </summary>
        public Guid Guid { get; }

        /// <summary>
        /// Parses a string into a Uuid.
        /// </summary>
        public static Uuid Parse(string value)
        {
            return new Uuid(Guid.Parse(value));
        }

        /// <summary>
        /// Try parse a string into a uuid
        /// </summary>
        public static bool TryParse(string input, out Uuid result)
        {
            bool success = Guid.TryParse(input, out Guid guid);
            result = success ? new Uuid(guid) : default;
            return success;
        }

        /// <summary>
        /// Converts Uuid to a byte array.
        /// </summary>
        public byte[] ToByteArray()
        {
            return Guid.ToByteArray();
        }

        /// <summary>
        /// Converts Uuid to a byte string
        /// </summary>
        public ByteString ToByteString()
        {
            return ToByteArray().ToByteString();
        }

        /// <summary>
        /// Create new random guid
        /// </summary>
        public static Uuid NewUuid()
        {
            return new Uuid(Guid.NewGuid());
        }

        /// <summary>
        /// Converts Uuid to a Guid structure.
        /// </summary>
        /// <param name="guid">The Guid to convert to a Uuid</param>
        public static implicit operator Guid(Uuid guid)
        {
            return guid.Guid;
        }

        /// <summary>
        /// Converts Guid to a Uuid.
        /// </summary>
        /// <param name="guid">The <see cref="System.Guid"/> to convert
        /// to a <see cref="Uuid"/></param>
        public static implicit operator Uuid(Guid guid)
        {
            return new Uuid(guid);
        }

        /// <inheritdoc/>
        public static bool operator ==(Uuid a, Uuid b)
        {
            return a.Equals(b);
        }

        /// <inheritdoc/>
        public static bool operator !=(Uuid a, Uuid b)
        {
            return !a.Equals(b);
        }

        /// <inheritdoc/>
        public static bool operator ==(Uuid a, Guid b)
        {
            return a.Equals(b);
        }

        /// <inheritdoc/>
        public static bool operator !=(Uuid a, Guid b)
        {
            return !a.Equals(b);
        }

        /// <inheritdoc/>
        public static bool operator <(Uuid a, Uuid b)
        {
            return a.CompareTo(b) < 0;
        }

        /// <inheritdoc/>
        public static bool operator >(Uuid a, Uuid b)
        {
            return a.CompareTo(b) > 0;
        }

        /// <inheritdoc/>
        public static bool operator <=(Uuid a, Uuid b)
        {
            return a.CompareTo(b) <= 0;
        }

        /// <inheritdoc/>
        public static bool operator >=(Uuid a, Uuid b)
        {
            return a.CompareTo(b) >= 0;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj switch
            {
                Uuid uuidValue => Equals(uuidValue),
                Guid guidValue => Equals(guidValue),
                _ => base.Equals(obj)
            };
        }

        /// <inheritdoc/>
        public bool Equals(Uuid other)
        {
            return Guid.Equals(other.Guid);
        }

        /// <inheritdoc/>
        public bool Equals(Guid other)
        {
            return Guid.Equals(other);
        }

        /// <inheritdoc/>
        public int CompareTo(object obj)
        {
            return obj switch
            {
                Uuid uuidValue => CompareTo(uuidValue),
                Guid guidValue => CompareTo(guidValue),
                _ => 1
            };
        }

        /// <inheritdoc/>
        public int CompareTo(Uuid other)
        {
            return Guid.CompareTo(other.Guid);
        }

        /// <inheritdoc/>
        public int CompareTo(Guid other)
        {
            return Guid.CompareTo(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Guid.GetHashCode();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Guid.ToString();
        }

        /// <inheritdoc/>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            return Guid.ToString(format);
        }
    }

    /// <summary>
    /// A wrapper for a GUID used during object serialization.
    /// </summary>
    /// <remarks>
    /// This class provides a wrapper around the <see cref="Uuid"/>
    /// object, allowing it to be serialized  and encoded/decoded
    /// to/from an underlying stream.
    /// </remarks>x
    [DataContract(Name = "Guid", Namespace = Namespaces.OpcUaXsd)]
    public sealed class SerializableUuid :
        ISurrogateFor<Uuid>
    {
        /// <inheritdoc/>
        public SerializableUuid()
        {
            Value = default;
        }

        /// <inheritdoc/>
        public SerializableUuid(Uuid guid)
        {
            Value = guid;
        }

        /// <summary>
        /// The GUID serialized as a string.
        /// </summary>
        /// <remarks>
        /// The GUID serialized as a string.
        /// </remarks>
        [DataMember(Name = "String", Order = 1)]
        public string GuidString
        {
            get => Value.ToString();
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    Value = Uuid.Empty;
                }
                else
                {
                    Value = new Uuid(value);
                }
            }
        }

        /// <inheritdoc/>
        public Uuid Value { get; private set; }

        /// <inheritdoc/>
        public object GetValue()
        {
            return Value;
        }
    }
}
