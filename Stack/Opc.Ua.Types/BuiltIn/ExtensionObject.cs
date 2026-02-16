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
using System.Diagnostics.Contracts;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json.Serialization;
using System.Xml;
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
            Body = null;
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
                Body = copy ? body.Clone() : body;
            }
        }

        /// <summary>
        /// Create extension object from byte buffer
        /// </summary>
        /// <param name="typeId"></param>
        /// <param name="body"></param>
        public ExtensionObject(ExpandedNodeId typeId, byte[] body)
        {
            TypeId = typeId;
            Body = body;
        }

        /// <summary>
        /// Create extension object from xml element
        /// </summary>
        /// <param name="typeId"></param>
        /// <param name="body"></param>
        public ExtensionObject(ExpandedNodeId typeId, XmlElement body)
        {
            TypeId = typeId;
            Body = body;
        }

        /// <summary>
        /// Create extension object from json string
        /// </summary>
        /// <param name="typeId"></param>
        /// <param name="body"></param>
        public ExtensionObject(ExpandedNodeId typeId, string body)
        {
            TypeId = typeId;
            Body = body;
        }

        /// <summary>
        /// Initializes the object with an encodeable object.
        /// </summary>
        /// <param name="typeId">The type describing the body</param>
        /// <param name="body">The underlying data/body to wrap</param>
       // [Obsolete("Use concrete constructor with typed body values instead.")]
        [JsonConstructor]
        public ExtensionObject(ExpandedNodeId typeId, object body = null)
        {
            TypeId = typeId;
            Body = body;

            if (body is
                not null and
                not IEncodeable and
                not byte[] and
                not string and
                not XmlElement)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    CoreUtils.Format(
                        "Cannot add an object with type '{0}' to an extension object.",
                        Body.GetType().FullName));
            }
        }

        /// <summary>
        /// The encoding to use when the deserializing/serializing
        /// the body.
        /// </summary>
        /// <value>The encoding for the embedded object.</value>
        public ExtensionObjectEncoding Encoding => Body switch
        {
            string => ExtensionObjectEncoding.Json,
            XmlElement => ExtensionObjectEncoding.Xml,
            byte[] => ExtensionObjectEncoding.Binary,
            IEncodeable => ExtensionObjectEncoding.EncodeableObject,
            _ => ExtensionObjectEncoding.None
        };

        /// <summary>
        /// The body of the extension object.
        /// </summary>
        public object Body { get; }

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
            return HashCode.Combine(TypeId.GetHashCode(), Body switch
            {
                byte[] b => ByteStringEqualityComparer.Default.GetHashCode(b),
                string s => StringComparer.Ordinal.GetHashCode(s),
                XmlElement x => x.GetHashCode(),
                IEncodeable e => e.GetHashCode(),
                _ => 0
            });
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
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
                return Body switch
                {
                    byte[] b => ByteStringEqualityComparer.Default.Equals(
                        b,
                        value.TryGetAsBinary(out byte[] b2) ? b2 : default),
                    string s => StringComparer.Ordinal.Equals(
                        s,
                        value.TryGetAsJson(out string s2) ? s2 : default),
                    XmlElement x =>
                        x == (value.TryGetAsXml(out XmlElement x2) ? x2 : default),
                    IEncodeable e => e.IsEqual(
                        value.TryGetEncodeable(out IEncodeable e2) ? e2 : default),
                    _ => false
                };
            }
            return CoreUtils.IsEqual(Body, value.Body);
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
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                if (Body is byte[] byteString)
                {
                    return string.Format(
                        formatProvider,
                        "Byte[{0}]",
                        byteString.Length);
                }

                if (Body is XmlElement element)
                {
                    return string.Format(
                        formatProvider,
                        "<{0}>",
                        element.OuterXml);
                }

                if (Body is string json)
                {
                    return string.Format(
                        formatProvider,
                        "{0}",
                        json);
                }

                if (Body is IFormattable formattable)
                {
                    return string.Format(
                        formatProvider,
                        "{0}",
                        formattable.ToString(null, formatProvider));
                }

                if (Body is IEncodeable)
                {
                    var body = new StringBuilder();

                    foreach (
                        PropertyInfo property in Body
                            .GetType()
                            .GetProperties(BindingFlags.Public |
                                BindingFlags.FlattenHierarchy |
                                BindingFlags.Instance))
                    {
                        object[] attributes = [.. property.GetCustomAttributes(
                            typeof(DataMemberAttribute),
                            true)];

                        for (int ii = 0; ii < attributes.Length; ii++)
                        {
                            if (attributes[ii] is DataMemberAttribute)
                            {
                                if (body.Length == 0)
                                {
                                    body.Append('{');
                                }
                                else
                                {
                                    body.Append(" | ");
                                }

                                body.AppendFormat(
                                    formatProvider,
                                    "{0}",
                                    property.GetGetMethod().Invoke(Body, null));
                            }
                        }
                    }

                    if (body.Length > 0)
                    {
                        body.Append('}');
                    }

                    return string.Format(formatProvider, "{0}", body);
                }

                if (!TypeId.IsNull)
                {
                    return string.Format(formatProvider, "{{{0}}}", TypeId);
                }

                return "(null)";
            }

            throw new FormatException(CoreUtils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Tests if the extension object is null.
        /// </summary>
        public bool IsNull => TypeId.IsNull && Body == null;

        /// <summary>
        /// Try get encodeable from the extension object
        /// </summary>
        public bool TryGetEncodeable(
            out IEncodeable encodeable,
            IServiceMessageContext messageContext = null)
        {
            if (Body is IEncodeable e)
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
            out T encodeable,
            IServiceMessageContext messageContext = null)
            where T : IEncodeable
        {
            if (TryGetEncodeable(out IEncodeable e, messageContext) &&
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
            out string json,
            IServiceMessageContext messageContext = null)
        {
            if (Body is string s)
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
            IServiceMessageContext messageContext = null)
        {
            if (Body is XmlElement x)
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
            out byte[] binary,
            IServiceMessageContext messageContext = null)
        {
            if (Body is byte[] b)
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
        public static IEncodeable ToEncodeable(ExtensionObject extension)
        {
            return extension.Body as IEncodeable;
        }

        /// <summary>
        /// Converts an array of extension objects to an array of
        /// the specified type.
        /// </summary>
        /// <param name="source">The array to convert.</param>
        /// <param name="elementType">The type of each element.</param>
        /// <returns>The new array</returns>
        /// <remarks>
        /// Will add null elements if individual elements cannot be converted.
        /// </remarks>
        public static Array ToArray(object source, Type elementType)
        {
            if (source is not Array extensions)
            {
                return null;
            }

            var output = Array.CreateInstance(elementType, extensions.Length);

            for (int ii = 0; ii < output.Length; ii++)
            {
                if (extensions.GetValue(ii) is ExtensionObject e &&
                    e.TryGetEncodeable(out IEncodeable element) &&
                    elementType.IsInstanceOfType(element))
                {
                    output.SetValue(element, ii);
                }
            }

            return output;
        }

        /// <summary>
        /// Converts an array of extension objects to a List of the specified
        /// type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source">The array to convert.</param>
        /// <returns>The new typed List</returns>
        /// <remarks>
        /// Will add null elements if individual elements cannot be converted.
        /// </remarks>
        public static List<T> ToList<T>(object source)
            where T : class
        {
            if (source is not Array extensions)
            {
                return null;
            }

            var list = new List<T>();

            for (int ii = 0; ii < extensions.Length; ii++)
            {
                if (extensions.GetValue(ii) is ExtensionObject e &&
                    e.TryGetEncodeable(out IEncodeable element) &&
                    element is T typedElement)
                {
                    list.Add(typedElement);
                }
                else
                {
                    list.Add(null);
                }
            }

            return list;
        }

        /// <summary>
        /// Update the type id
        /// </summary>
        [Pure]
        public ExtensionObject WithTypeId(ExpandedNodeId typeId)
        {
            return Body switch
            {
                byte[] v => new ExtensionObject(typeId, v),
                string v => new ExtensionObject(typeId, v),
                XmlElement v => new ExtensionObject(typeId, v),
                IEncodeable v => new ExtensionObject(typeId, v),
                _ => new ExtensionObject(typeId)
            };
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

    /// <summary>
    /// Helper to allow data contract serialization of ExtensionObject
    /// </summary>
    [DataContract(
        Name = "ExtensionObject",
        Namespace = Namespaces.OpcUaXsd)]
    public class SerializableExtensionObject : ISurrogateFor<ExtensionObject>
    {
        /// <inheritdoc/>
        public SerializableExtensionObject()
        {
            Value = default;
        }

        /// <inheritdoc/>
        public SerializableExtensionObject(ExtensionObject value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public ExtensionObject Value { get; private set; }

        /// <inheritdoc/>
        public object GetValue()
        {
            return Value;
        }

        /// <summary>
        /// Serialized type id
        /// </summary>
        [DataMember(
            Name = "TypeId",
            Order = 1,
            IsRequired = false,
            EmitDefaultValue = true)]
        internal NodeId XmlEncodedTypeId
        {
            get
            {
                IServiceMessageContext context = AmbientMessageContext.CurrentContext;
                // must use the XML encoding id if encoding in an XML stream.
                if (Value.TryGetEncodeable(out IEncodeable encodeable))
                {
                    return ExpandedNodeId.ToNodeId(
                        encodeable.XmlEncodingId,
                        context.NamespaceUris);
                }
                // check for null Id.
                if (Value.TypeId.IsNull)
                {
                    // note: this NodeId is modified when the ExtensionObject is
                    // deserialized.
                    return default;
                }
                return
                    ExpandedNodeId.ToNodeId(Value.TypeId, context.NamespaceUris);
            }
            set
            {
                IServiceMessageContext context = AmbientMessageContext.CurrentContext;

                Value = Value.WithTypeId(
                    NodeId.ToExpandedNodeId(value, context.NamespaceUris));
            }
        }

        /// <summary>
        /// Serialized extension object body
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        [DataMember(
            Name = "Body",
            Order = 2,
            IsRequired = false,
            EmitDefaultValue = true)]
        internal XmlElement XmlEncodedBody
        {
            get
            {
                // check for null.
                if (Value.IsNull)
                {
                    return XmlElement.Empty;
                }

                // create encoder.
                IServiceMessageContext context = AmbientMessageContext.CurrentContext;
                using var encoder = new XmlEncoder(context);
                // write body.
                encoder.WriteExtensionObjectBody(Value.Body);
                return new XmlElement(encoder.CloseAndReturnText());
            }
            set
            {
                // check null bodies.
                if (value.IsEmpty)
                {
                    Value = new ExtensionObject(Value.TypeId);
                    return;
                }

                // create decoder.
                IServiceMessageContext context = AmbientMessageContext.CurrentContext;
                using var decoder = new XmlDecoder(value, context);
                switch (decoder.ReadExtensionObjectBody(Value.TypeId))
                {
                    case IEncodeable encodeable:
                        Value = new ExtensionObject(encodeable);
                        break;
                    case byte[] bytes:
                        Value = new ExtensionObject(Value.TypeId, bytes);
                        break;
                    case XmlElement xml:
                        Value = new ExtensionObject(Value.TypeId, xml);
                        break;
                    case string json:
                        Value = new ExtensionObject(Value.TypeId, json);
                        break;
                }
                // close decoder.
                try
                {
                    decoder.Close(true);
                }
                catch (Exception e)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        CoreUtils.Format(
                            "Did not read all of a extension object body: '{0}'",
                            Value.TypeId),
                        e);
                }
            }
        }
    }

    /// <summary>
    /// A strongly-typed collection of ExtensionObjects.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfExtensionObject",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "ExtensionObject")]
    public class ExtensionObjectCollection : List<ExtensionObject>, ICloneable
    {
        /// <inheritdoc/>
        public ExtensionObjectCollection()
        {
        }

        /// <inheritdoc/>
        public ExtensionObjectCollection(
            IEnumerable<ExtensionObject> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public ExtensionObjectCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Converts an array of ExtensionObjects to a collection.
        /// </summary>
        /// <param name="values">An array of ExtensionObjects to
        /// convert to a collection</param>
        public static implicit operator ExtensionObjectCollection(
            ExtensionObject[] values)
        {
            return values != null ? [.. values] : [];
        }

        /// <summary>
        /// Converts an encodeable object to an extension object.
        /// </summary>
        /// <param name="encodeables">An enumerable array of
        /// ExtensionObjects to convert to a collection</param>
        public static ExtensionObjectCollection ToExtensionObjects(
            IEnumerable<IEncodeable> encodeables)
        {
            // return null if the input list is null.
            if (encodeables == null)
            {
                return null;
            }

            // convert each encodeable to an extension object.
            var extensibles = new ExtensionObjectCollection();
            foreach (IEncodeable encodeable in encodeables)
            {
                extensibles.Add(new ExtensionObject(encodeable));
            }

            return extensibles;
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new ExtensionObjectCollection(Count);

            foreach (ExtensionObject element in this)
            {
                clone.Add(element);
            }

            return clone;
        }
    }
}
