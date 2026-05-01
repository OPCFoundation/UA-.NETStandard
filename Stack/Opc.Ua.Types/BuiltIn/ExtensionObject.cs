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

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Text.Json.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// An object used to wrap data types that the receiver may not
    /// understand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class is a wrapper/helper class for storing data types that
    /// a receiver might not understand, or be prepared to handle.
    /// </para>
    /// <para>
    /// An instance of the <see cref="ExtensionObject"/> is a container for
    /// any complex data types which cannot be encoded as one of the other
    /// built-in data types. The ExtensionObject contains a complex value
    /// serialized as a sequence of bytes or as an XML element. It also
    /// contains an identifier which indicates what data it contains and
    /// how it is encoded.
    /// </para>
    /// </remarks>
    public readonly struct ExtensionObject :
        IFormattable,
        INullable,
        IEquatable<ExtensionObject>
    {
        /// <summary>
        /// Returns an instance of a null ExtensionObject.
        /// </summary>
        public static readonly ExtensionObject Null;

        /// <summary>
        /// Initializes the object with a <paramref name="typeId"/>.
        /// </summary>
        /// <param name="typeId">The type to copy and create an
        /// instance from</param>
        public ExtensionObject(ExpandedNodeId typeId)
        {
            TypeId = typeId;
            m_body = null;
        }

        /// <summary>
        /// Create extension object from encodeable object
        /// </summary>
        /// <param name="body">Encodeable body</param>
        /// <param name="copy">Clone the encodeable</param>
        public ExtensionObject(IEncodeable body, bool copy = false)
        {
            if (body != null)
            {
                TypeId = body.TypeId;
                m_body = copy ? body.Clone() : body;
            }
        }

        /// <summary>
        /// Create extension object from encodeable object
        /// </summary>
        /// <param name="typeId">Alternative type id</param>
        /// <param name="body">Encodeable body</param>
        internal ExtensionObject(ExpandedNodeId typeId, IEncodeable body)
        {
            if (body != null)
            {
                TypeId = typeId.IsNull ? body.TypeId : typeId;
                m_body = body;
            }
        }

        /// <summary>
        /// Create extension object from byte buffer
        /// </summary>
        /// <param name="typeId"></param>
        /// <param name="body"></param>
        public ExtensionObject(ExpandedNodeId typeId, ByteString body)
        {
            TypeId = typeId;
            m_body = body;
        }

        /// <summary>
        /// Create extension object from xml element
        /// </summary>
        /// <param name="typeId"></param>
        /// <param name="body"></param>
        public ExtensionObject(ExpandedNodeId typeId, XmlElement body)
        {
            TypeId = typeId;
            m_body = body;
        }

        /// <summary>
        /// Create extension object from json string
        /// </summary>
        /// <param name="typeId"></param>
        /// <param name="body"></param>
        public ExtensionObject(ExpandedNodeId typeId, string body)
        {
            TypeId = typeId;
            m_body = body;
        }

        /// <summary>
        /// Initializes the object with an encodeable object.
        /// </summary>
        /// <param name="typeId">The type describing the body</param>
        /// <param name="body">The underlying data/body to wrap</param>
        [Obsolete("Use concrete constructor with typed body values instead.")]
        [JsonConstructor]
        public ExtensionObject(ExpandedNodeId typeId, object body)
        {
            TypeId = typeId;
            m_body = body;

            if (body is
                not null and
                not IEncodeable and
                not ByteString and
                not string and
                not XmlElement)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    CoreUtils.Format(
                        "Cannot add an object with type '{0}' to an extension object.",
                        body.GetType().FullName!));
            }
        }

        /// <summary>
        /// The encoding to use when the deserializing/serializing
        /// the body.
        /// </summary>
        /// <value>The encoding for the embedded object.</value>
        public ExtensionObjectEncoding Encoding => m_body switch
        {
            string => ExtensionObjectEncoding.Json,
            XmlElement => ExtensionObjectEncoding.Xml,
            ByteString => ExtensionObjectEncoding.Binary,
            IEncodeable => ExtensionObjectEncoding.EncodeableObject,
            _ => ExtensionObjectEncoding.None
        };

        /// <summary>
        /// The body of the extension object.
        /// </summary>
        [Obsolete("Use TryGetAsXXX API for type safe access to body.")]
#pragma warning disable RCS1085 // Use auto-implemented property
        public object? Body => m_body;
#pragma warning restore RCS1085 // Use auto-implemented property

        /// <summary>
        /// The data type node id for the extension object.
        /// </summary>
        public ExpandedNodeId TypeId { get; }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            if (IsNull)
            {
                return 0;
            }
            return HashCode.Combine(TypeId.GetHashCode(), m_body switch
            {
                ByteString b => b.GetHashCode(),
                string s => StringComparer.Ordinal.GetHashCode(s),
                XmlElement x => x.GetHashCode(),
                IEncodeable e => e.GetHashCode(),
                _ => 0
            });
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj switch
            {
                null => IsNull,
                ExtensionObject v => Equals(v),
                _ => base.Equals(obj)
            };
        }

        /// <inheritdoc/>
        public bool Equals(ExtensionObject value)
        {
            bool isNull1 = IsNull;
            bool isNull2 = value.IsNull;
            if (isNull1 || isNull2)
            {
                return isNull1 == isNull2;
            }
            if (TypeId != value.TypeId)
            {
                return false;
            }
            if (Encoding == value.Encoding)
            {
                return m_body switch
                {
                    ByteString b =>
                        b == (value.TryGetAsBinary(out ByteString b2) ? b2 : default),
                    string s => StringComparer.Ordinal.Equals(
                        s,
                        value.TryGetAsJson(out string? s2) ? s2 : default),
                    XmlElement x =>
                        x == (value.TryGetAsXml(out XmlElement x2) ? x2 : default),
                    IEncodeable e => e.IsEqual(
                        value.TryGetEncodeable(out IEncodeable? e2) ? e2 : default),
                    _ => false
                };
            }
            return CoreUtils.IsEqual(m_body!, value.m_body!);
        }

        /// <inheritdoc/>
        public static bool operator ==(ExtensionObject left, ExtensionObject right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(ExtensionObject left, ExtensionObject right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <inheritdoc/>
        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            if (format == null)
            {
                switch (m_body)
                {
                    case ByteString byteString:
                        return string.Format(
                            formatProvider,
                            "Byte[{0}]",
                            byteString.Length);
                    case XmlElement element:
                        return string.Format(
                            formatProvider,
                            "<{0}>",
                            element.OuterXml);
                    case string json:
                        return string.Format(
                            formatProvider,
                            "{0}",
                            json);
                    case IFormattable formattable:
                        return string.Format(
                            formatProvider,
                            "{0}",
                            formattable.ToString(null, formatProvider));
                    case IEncodeable encodeable:
                        return string.Format(
                            formatProvider,
                            "{0}",
                            encodeable);
                    default:
                        if (TypeId.IsNull)
                        {
                            return "(null)";
                        }
                        return string.Format(
                            formatProvider,
                            "{{{0}}}",
                            TypeId);
                }
            }

            throw new FormatException(
                CoreUtils.Format("Invalid format string: '{0}'.",
                format));
        }

        /// <summary>
        /// Tests if the extension object is null.
        /// </summary>
        public bool IsNull => TypeId.IsNull && m_body == null;

        /// <summary>
        /// Try get encodeable from the extension object
        /// </summary>
        public bool TryGetEncodeable(
            out IEncodeable? encodeable,
            IServiceMessageContext? messageContext = null)
        {
            if (m_body is IEncodeable e)
            {
                encodeable = e;
                return true;
            }

            if (messageContext == null)
            {
                encodeable = default;
                return false;
            }

            // TODO: Decode if possible
            encodeable = default;
            return false;
        }

        /// <summary>
        /// Get encoded value
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public bool TryGetEncodeable<T>(
            out T? encodeable,
            IServiceMessageContext? messageContext = null)
            where T : IEncodeable
        {
            if (TryGetEncodeable(out IEncodeable? e, messageContext) &&
                e is T typedEncodeable)
            {
                encodeable = typedEncodeable;
                return true;
            }
            encodeable = default;
            return false;
        }

        /// <summary>
        /// Try get as json
        /// </summary>
        public bool TryGetAsJson(
            [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out string json,
            IServiceMessageContext? messageContext = null)
        {
            if (m_body is string s)
            {
                json = s;
                return true;
            }

            if (messageContext == null)
            {
                json = default;
                return false;
            }

            // TODO: Reencode if possible
            json = default;
            return false;
        }

        /// <summary>
        /// Try get as xml
        /// </summary>
        public bool TryGetAsXml(
            out XmlElement xml,
            IServiceMessageContext? messageContext = null)
        {
            if (m_body is XmlElement x)
            {
                xml = x;
                return true;
            }

            if (messageContext == null)
            {
                xml = default;
                return false;
            }

            // TODO: Reencode if possible
            xml = default;
            return false;
        }

        /// <summary>
        /// Try get as binary
        /// </summary>
        public bool TryGetAsBinary(
            out ByteString binary,
            IServiceMessageContext? messageContext = null)
        {
            if (m_body is ByteString b)
            {
                binary = b;
                return true;
            }

            if (messageContext == null)
            {
                binary = default;
                return false;
            }

            // TODO: Reencode if possible
            binary = default;
            return false;
        }

        /// <summary>
        /// Converts an extension object to an encodeable object.
        /// </summary>
        /// <param name="extension">The extension object to convert to an encodeable object</param>
        /// <returns>Instance of <see cref="IEncodeable"/> for the embedded object.</returns>
        public static IEncodeable? ToEncodeable(ExtensionObject extension)
        {
            return extension.m_body as IEncodeable;
        }

        /// <summary>
        /// Converts an array of extension objects to an array of
        /// the specified type.
        /// </summary>
        /// <param name="extensions">The array to convert.</param>
        /// <typeparam name="T">The type of each element.</typeparam>
        /// <returns>The new array</returns>
        /// <remarks>
        /// Will leave entry in returned array as default if
        /// individual elements cannot be obtained from the
        /// extension object.
        /// </remarks>
        public static ArrayOf<T> ToArray<T>(ArrayOf<ExtensionObject> extensions)
            where T : IEncodeable
        {
            var output = new T[extensions.Count];
            for (int ii = 0; ii < output.Length; ii++)
            {
                if (extensions[ii].TryGetEncodeable(out IEncodeable? element) &&
                    element is T typedElement)
                {
                    output[ii] = typedElement;
                }
            }
            return output;
        }

        /// <summary>
        /// Update the type id
        /// </summary>
        [Pure]
        public ExtensionObject WithTypeId(ExpandedNodeId typeId)
        {
            return m_body switch
            {
                ByteString v => new ExtensionObject(typeId, v),
                string v => new ExtensionObject(typeId, v),
                XmlElement v => new ExtensionObject(typeId, v),
                IEncodeable v => new ExtensionObject(typeId, v),
                _ => new ExtensionObject(typeId)
            };
        }

        private readonly object? m_body;
    }

    /// <summary>
    /// Extension object extension methods
    /// </summary>
    public static class ExtensionObjectExtensions
    {
        /// <summary>
        /// Convert encodeables to extension array
        /// </summary>
        /// <param name="encodeables"></param>
        /// <returns></returns>
        public static ArrayOf<ExtensionObject> ToExtensionObjects(
            this IEnumerable<IEncodeable> encodeables)
        {
            // return null if the input list is null.
            if (encodeables == null)
            {
                return default;
            }

            // convert each encodeable to an extension object.
            var extensibles = new List<ExtensionObject>();
            foreach (IEncodeable encodeable in encodeables)
            {
                extensibles.Add(new ExtensionObject(encodeable));
            }

            return extensibles;
        }

        /// <summary>
        /// Get structures of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static ArrayOf<T> GetStructuresOf<T>(
            this ArrayOf<ExtensionObject> extensions)
            where T : IEncodeable
        {
            return ExtensionObject.ToArray<T>(extensions);
        }
    }

    /// <summary>
    /// The types of encodings that may used with an object.
    /// </summary>
    public enum ExtensionObjectEncoding : byte
    {
        /// <summary>
        /// The extension object has no body.
        /// </summary>
        None = 0,

        /// <summary>
        /// The extension object has a binary encoded body.
        /// </summary>
        Binary = 1,

        /// <summary>
        /// The extension object has an XML encoded body.
        /// </summary>
        Xml = 2,

        /// <summary>
        /// The extension object has an encodeable object body.
        /// </summary>
        EncodeableObject = 3,

        /// <summary>
        /// The extension object has a JSON encoded body.
        /// </summary>
        Json = 4
    }
}
