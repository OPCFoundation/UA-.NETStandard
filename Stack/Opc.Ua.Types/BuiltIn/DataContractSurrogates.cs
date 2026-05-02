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
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Surrogates for data contract serializer. Used to swap types that
    /// are not directly supported by the serializer for types that are.
    /// </summary>
    [RequiresUnreferencedCode("Uses DataContractSerializer which might need unreferenced code.")]
    [RequiresDynamicCode("Uses DataContractSerializer which might need unreferenced code.")]
    public class DataContractSurrogates : ISerializationSurrogateProvider
    {
        /// <summary>
        /// Known types
        /// </summary>
        public static Type[] KnownTypes =>
        [
            .. SurrogateMappings.Keys,
            .. SurrogateMappings.Values
        ];

        /// <summary>
        /// Create surrogate provider
        /// </summary>
        /// <param name="messageContext"></param>
        public DataContractSurrogates(IServiceMessageContext messageContext)
        {
            MessageContext = messageContext;
        }

        /// <summary>
        /// Access to message context passed to provider.
        /// </summary>
        public IServiceMessageContext MessageContext { get; }

        /// <inheritdoc/>
        public object GetDeserializedObject(object obj, Type targetType)
        {
            if (obj is ISurrogate surrogate)
            {
                return surrogate.GetValue();
            }

            if (targetType == typeof(XmlElement))
            {
                return XmlElement.From((System.Xml.XmlElement)obj);
            }

            // Not a surrogated type or null (default)
            return obj;
        }

        /// <inheritdoc/>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2067",
            Justification = "Surrogate types are statically registered and their constructors are preserved.")]
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2072",
            Justification = "Surrogate types are statically registered and their constructors are preserved.")]
        public object GetObjectToSerialize(object obj, Type targetType)
        {
            // Fast path for already surrogated objects.
            if (obj is ISurrogate surrogate)
            {
                return surrogate; // Surrogate already - serialize as is
            }
            Type surrogateType = GetSurrogateType(targetType);
            if (surrogateType == targetType) // Original type == not found
            {
                if (obj is XmlElement xmlElement)
                {
                    return xmlElement.ToXmlElement();
                }
                Type sourceType = obj?.GetType();
                surrogateType = GetSurrogateType(sourceType);
                if (surrogateType == sourceType) // Original type == not found
                {
                    // Handle the case where we are passed the surrogate type
                    // as target. Could do a reverse lookup here too
                    if (!typeof(ISurrogate).IsAssignableFrom(targetType))
                    {
                        // Not a surrogated type
                        return obj;
                    }
                    surrogateType = targetType;
                }
            }
            // Create surrogate instance - all surrogates have a constructor
            // that takes the original object it is surrogate for or a default
            // constructor to create a null instance.
            try
            {
                object instance = obj is null ?
                    Activator.CreateInstance(surrogateType) :
                    Activator.CreateInstance(surrogateType, [obj]);
                if (instance is ISurrogateWithContext ctx)
                {
                    ctx.Context = MessageContext;
                }
                return instance;
            }
            catch
            {
                return obj;
            }
        }

        /// <inheritdoc/>
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "Type.MakeGenericType is used with known OPC UA surrogate types.")]
        public Type GetSurrogateType(Type type)
        {
            if (SurrogateMappings.TryGetValue(type, out Type surrogateType))
            {
                return surrogateType;
            }
            if (type == typeof(XmlElement))
            {
                return typeof(System.Xml.XmlElement);
            }
            if (type.IsGenericType)
            {
                if (type.GetGenericTypeDefinition() == typeof(ArrayOf<>))
                {
                    Type elementType = type.GetGenericArguments()[0];
                    return typeof(SerializableArrayOf<>).MakeGenericType([elementType]);
                }
                if (type.GetGenericTypeDefinition() == typeof(MatrixOf<>))
                {
                    Type elementType = type.GetGenericArguments()[0];
                    return typeof(SerializableMatrixOf<>).MakeGenericType([elementType]);
                }
            }
            return type;
        }

        /// <summary>
        /// Surrogate mappings (Could get from assembly)
        /// </summary>
        public static readonly Dictionary<Type, Type> SurrogateMappings = new()
        {
            { typeof(NodeId), typeof(SerializableNodeId) },
            { typeof(ExpandedNodeId), typeof(SerializableExpandedNodeId) },
            { typeof(ExtensionObject), typeof(SerializableExtensionObject) },
            { typeof(Uuid), typeof(SerializableUuid) },
            { typeof(StatusCode), typeof(SerializableStatusCode) },
            { typeof(QualifiedName), typeof(SerializableQualifiedName) },
            { typeof(Variant), typeof(SerializableVariant) },
            { typeof(LocalizedText), typeof(SerializableLocalizedText) },
            { typeof(ByteString), typeof(SerializableByteString) },
            { typeof(DateTimeUtc), typeof(DateTimeUtc) },
            { typeof(ArrayOf<XmlElement>), typeof(SerializableXmlElementCollection) }
        };
    }

    /// <summary>
    /// Surrogate marker interface
    /// </summary>
    public interface ISurrogate
    {
        /// <summary>
        /// Get the value being surrogated
        /// </summary>
        /// <returns></returns>
        object GetValue();
    }

    /// <summary>
    /// Denotes a surrogate for a specific type.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface ISurrogateFor<out T> : ISurrogate
    {
        /// <summary>
        /// Which value is surrogated
        /// </summary>
        T Value { get; }
    }

    /// <summary>
    /// Context receiver
    /// </summary>
    public interface ISurrogateWithContext
    {
        /// <summary>
        /// Receives context if implemented on a surrogate
        /// </summary>
        IServiceMessageContext Context { get; set; }
    }

    /// <summary>
    /// Surrogate for matrix of type T
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [DataContract(
        Name = "Matrix",
        Namespace = Namespaces.OpcUaXsd)]
    public class SerializableMatrixOf<T> :
        ISurrogateFor<MatrixOf<T>>
    {
        /// <inheritdoc/>
        public SerializableMatrixOf()
        {
            Elements = [];
            Dimensions = [];
        }

        /// <inheritdoc/>
        public SerializableMatrixOf(MatrixOf<T> value)
        {
            Elements = value.ToArrayOf(out int[] dimensions).ToArray();
            Dimensions = dimensions;
        }

        /// <summary>
        /// The dimensions of the matrix.
        /// </summary>
        /// <value>The dimensions of the array.</value>
        [DataMember(Name = "Dimensions", Order = 0)]
        public int[] Dimensions { get; set; }

        /// <summary>
        /// The elements of the matrix.
        /// </summary>
        /// <value>An array of elements.</value>
        [DataMember(Name = "Elements", Order = 1)]
        public T[] Elements { get; set; }

        /// <inheritdoc/>
        public MatrixOf<T> Value => new(Elements, Dimensions);

        /// <inheritdoc/>
        public object GetValue()
        {
            return Value;
        }
    }

    /// <summary>
    /// Surrogate for arrays of type T
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [XmlSchemaProvider("GetSchemaMethod")]
    public class SerializableArrayOf<T> :
        List<T>,
        ISurrogateFor<ArrayOf<T>>,
        ISurrogateWithContext,
        IXmlSerializable
    {
        /// <inheritdoc/>
        public SerializableArrayOf()
        {
        }

        /// <inheritdoc/>
        public SerializableArrayOf(ArrayOf<T> value)
            : base(value.ToList())
        {
        }

        /// <inheritdoc/>
        public IServiceMessageContext Context { get; set; }

        /// <inheritdoc/>
        public ArrayOf<T> Value => this.ToArrayOf();

        /// <inheritdoc/>
        public object GetValue()
        {
            return Value;
        }

        /// <inheritdoc/>
        public XmlSchema GetSchema()
        {
            return null;
        }

        /// <inheritdoc/>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
            Justification = "DataContractSerializer is used with known OPC UA types.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "DataContractSerializer is used with known OPC UA types.")]
        public void WriteXml(XmlWriter writer)
        {
            XmlQualifiedName xmlName = TypeInfo.GetXmlName(typeof(T));
#pragma warning disable CS0618 // Type or member is obsolete
            DataContractSerializer serializer = CoreUtils.CreateDataContractSerializer<T>(
                Context,
                rootName: xmlName);
#pragma warning restore CS0618 // Type or member is obsolete

            foreach (T item in this)
            {
                serializer.WriteObject(writer, item);
            }
        }

        /// <inheritdoc/>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
            Justification = "DataContractSerializer is used with known OPC UA types.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "DataContractSerializer is used with known OPC UA types.")]
        public void ReadXml(XmlReader reader)
        {
            XmlQualifiedName xmlName = TypeInfo.GetXmlName(typeof(T));
#pragma warning disable CS0618 // Type or member is obsolete
            DataContractSerializer serializer = CoreUtils.CreateDataContractSerializer<T>(
                Context,
                rootName: xmlName);
#pragma warning restore CS0618 // Type or member is obsolete

            if (reader.IsEmptyElement)
            {
                reader.Read();
                return;
            }

            reader.ReadStartElement();
            while (reader.NodeType != XmlNodeType.EndElement)
            {
                var item = (T)serializer.ReadObject(reader);
                Add(item);
                reader.MoveToContent();
            }
            reader.ReadEndElement();
        }

        /// <summary>
        /// Returns the name and fills schema
        /// </summary>
#pragma warning disable RCS1158 // Static member in generic type should use a type parameter
        public static XmlQualifiedName GetSchemaMethod(XmlSchemaSet xs)
#pragma warning restore RCS1158 // Static member in generic type should use a type parameter
        {
            XmlQualifiedName xmlName = TypeInfo.GetXmlName(typeof(T));
            return new XmlQualifiedName("ListOf" + xmlName.Name, xmlName.Namespace);
        }
    }

    /// <summary>
    /// Helper to allow data contract serialization of Variant
    /// </summary>
    [DataContract(
        Name = "Variant",
        Namespace = Namespaces.OpcUaXsd)]
    public class SerializableVariant :
        ISurrogateFor<Variant>,
        ISurrogateWithContext,
        IEquatable<Variant>,
        IEquatable<SerializableVariant>
    {
        /// <inheritdoc/>
        public SerializableVariant()
        {
            Value = default;
        }

        /// <inheritdoc/>
        public SerializableVariant(Variant value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public IServiceMessageContext Context { get; set; }

        /// <inheritdoc/>
        public Variant Value { get; private set; }

        /// <inheritdoc/>
        public object GetValue()
        {
            return Value;
        }

        /// <summary>
        /// The value stored within the Variant object.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        [DataMember(Name = "Value", Order = 1)]
        internal System.Xml.XmlElement XmlEncodedValue
        {
            get
            {
                // check for null.
                if (Value.IsNull)
                {
                    return null;
                }

                // create encoder.
                using var encoder = new XmlEncoder(
                    Context ?? AmbientMessageContext.CurrentContext);
                // write value.
                encoder.WriteVariantValue(null, Value);

                // create document from encoder.
                var document = new XmlDocument();
                document.LoadInnerXml(encoder.CloseAndReturnText());

                // return element.
                return document.DocumentElement;
            }
            set
            {
                // check for null values.
                if (value == null)
                {
                    Value = Variant.Null;
                    return;
                }

                // create decoder.
                using var decoder = new XmlDecoder(
                    value,
                    Context ?? AmbientMessageContext.CurrentContext);
                try
                {
                    // read value.
                    Value = decoder.ReadVariantValue();
                }
                catch (Exception e)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        e,
                        "Error decoding Variant value.");
                }
                finally
                {
                    // close decoder.
                    decoder.Close();
                }
            }
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj switch
            {
                SerializableVariant s => Equals(s),
                Variant n => Equals(n),
                _ => ((object)Value).Equals(obj)
            };
        }

        /// <inheritdoc/>
        public bool Equals(Variant obj)
        {
            return Value.Equals(obj);
        }

        /// <inheritdoc/>
        public bool Equals(SerializableVariant obj)
        {
            return Value.Equals(obj?.Value ?? default);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(
            SerializableVariant left,
            SerializableVariant right)
        {
            return EqualityComparer<SerializableVariant>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(
            SerializableVariant left,
            SerializableVariant right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator ==(
            SerializableVariant left,
            Variant right)
        {
            return EqualityComparer<SerializableVariant>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(
            SerializableVariant left,
            Variant right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static implicit operator SerializableVariant(Variant value)
        {
            return new SerializableVariant(value);
        }

        /// <inheritdoc/>
        public static implicit operator Variant(SerializableVariant value)
        {
            return value.Value;
        }

        /// <inheritdoc/>
        public static explicit operator System.Xml.XmlElement(SerializableVariant value)
        {
            return value.XmlEncodedValue;
        }

        /// <inheritdoc/>
        public static explicit operator SerializableVariant(System.Xml.XmlElement value)
        {
            return new SerializableVariant { XmlEncodedValue = value };
        }
    }

    /// <summary>
    /// A wrapper for a GUID used during object serialization.
    /// </summary>
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

    /// <summary>
    /// Helper to allow data contract serialization of StatusCode
    /// </summary>
    [DataContract(
        Name = "StatusCode",
        Namespace = Namespaces.OpcUaXsd)]
    public class SerializableStatusCode : ISurrogateFor<StatusCode>
    {
        /// <inheritdoc/>
        public SerializableStatusCode()
        {
            Value = default;
        }

        /// <inheritdoc/>
        public SerializableStatusCode(StatusCode value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public StatusCode Value { get; private set; }

        /// <inheritdoc/>
        public object GetValue()
        {
            return Value;
        }

        /// <summary>
        /// The entire 32-bit status value.
        /// </summary>
        [DataMember(Name = "Code", Order = 1, IsRequired = false)]
        public uint Code
        {
            get => Value.Code;
            set => Value = new StatusCode(value);
        }
    }

    /// <summary>
    /// Serializable representation of a QualifiedName
    /// </summary>
    [DataContract(
        Name = "QualifiedName",
        Namespace = Namespaces.OpcUaXsd)]
    public class SerializableQualifiedName :
        ISurrogateFor<QualifiedName>
    {
        /// <inheritdoc/>
        public SerializableQualifiedName()
        {
            Value = default;
        }

        /// <inheritdoc/>
        public SerializableQualifiedName(QualifiedName value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public QualifiedName Value { get; private set; }

        /// <inheritdoc/>
        public object GetValue()
        {
            return Value;
        }

        /// <summary>
        /// Namespace index
        /// </summary>
        [DataMember(Name = "NamespaceIndex", Order = 1)]
        internal ushort XmlEncodedNamespaceIndex
        {
            get => Value.NamespaceIndex;
            set => Value = Value.WithNamespaceIndex(value);
        }

        /// <summary>
        /// Xml encoded name
        /// </summary>
        [DataMember(Name = "Name", Order = 2)]
        internal string XmlEncodedName
        {
            get => Value.Name;
            set => Value = Value.WithName(value);
        }
    }

    /// <summary>
    /// Helper to allow data contract serialization of NodeId
    /// </summary>
    [DataContract(
        Name = "NodeId",
        Namespace = Namespaces.OpcUaXsd)]
    public class SerializableNodeId : ISurrogateFor<NodeId>
    {
        /// <inheritdoc/>
        public SerializableNodeId()
        {
            Value = default;
        }

        /// <inheritdoc/>
        public SerializableNodeId(NodeId value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public NodeId Value { get; private set; }

        /// <inheritdoc/>
        public object GetValue()
        {
            return Value;
        }

        /// <summary>
        /// The node identifier formatted as a URI.
        /// </summary>
        [DataMember(Name = "Identifier", Order = 1)]
        internal string IdentifierText
        {
            get => Value.Format(CultureInfo.InvariantCulture);
            set => Value = NodeId.Parse(value);
        }
    }

    /// <summary>
    /// Helper to allow data contract serialization of LocalizedText
    /// </summary>
    [DataContract(
        Name = "LocalizedText",
        Namespace = Namespaces.OpcUaXsd)]
    public class SerializableLocalizedText :
        IEquatable<LocalizedText>,
        IEquatable<SerializableLocalizedText>,
        ISurrogateFor<LocalizedText>
    {
        /// <summary>
        /// Create initialized localized text
        /// </summary>
        public SerializableLocalizedText()
        {
            Value = default;
        }

        /// <summary>
        /// Create initialized localized text
        /// </summary>
        public SerializableLocalizedText(LocalizedText value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public LocalizedText Value { get; private set; }

        /// <inheritdoc/>
        public object GetValue()
        {
            return Value;
        }

        /// <inheritdoc/>
        [DataMember(Name = "Locale", Order = 1)]
        internal string XmlEncodedLocale
        {
            get => Value.Locale;
            set => Value = new LocalizedText(value, XmlEncodedText);
        }

        /// <inheritdoc/>
        [DataMember(Name = "Text", Order = 2)]
        internal string XmlEncodedText
        {
            get => Value.Text;
            set => Value = new LocalizedText(XmlEncodedLocale, value);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj switch
            {
                SerializableLocalizedText s => Equals(s),
                LocalizedText n => Equals(n),
                _ => Value.Equals(obj)
            };
        }

        /// <inheritdoc/>
        public bool Equals(LocalizedText obj)
        {
            return Value.Equals(obj);
        }

        /// <inheritdoc/>
        public bool Equals(SerializableLocalizedText obj)
        {
            return Value.Equals(obj?.Value ?? default);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(SerializableLocalizedText left, SerializableLocalizedText right)
        {
            return left is null ? right is null : left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(SerializableLocalizedText left, SerializableLocalizedText right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator ==(SerializableLocalizedText left, LocalizedText right)
        {
            return left is null ? right.IsNullOrEmpty : left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(SerializableLocalizedText left, LocalizedText right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static implicit operator SerializableLocalizedText(
            LocalizedText value)
        {
            return new SerializableLocalizedText(value);
        }

        /// <inheritdoc/>
        public static implicit operator LocalizedText(
            SerializableLocalizedText value)
        {
            return value.Value;
        }
    }

    /// <summary>
    /// Helper to allow data contract serialization of ExtensionObject
    /// </summary>
    [DataContract(
        Name = "ExtensionObject",
        Namespace = Namespaces.OpcUaXsd)]
    public class SerializableExtensionObject :
        ISurrogateFor<ExtensionObject>,
        ISurrogateWithContext
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
        public IServiceMessageContext Context { get; set; }

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
                IServiceMessageContext context =
                    Context ?? AmbientMessageContext.CurrentContext;
                // must use the XML encoding id if encoding in an XML stream.
                if (Value.TryGetValue(out IEncodeable encodeable))
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
                IServiceMessageContext context =
                    Context ?? AmbientMessageContext.CurrentContext;

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
        internal System.Xml.XmlElement XmlEncodedBody
        {
            get
            {
                // check for null.
                if (Value.IsNull)
                {
                    return null;
                }

                // create encoder.
                IServiceMessageContext context =
                    Context ?? AmbientMessageContext.CurrentContext;
                using var encoder = new XmlEncoder(context);
                // write body.
                encoder.WriteExtensionObjectBody(Value);

                // create document from encoder.
                var document = new XmlDocument();
                document.LoadInnerXml(encoder.CloseAndReturnText());

                // return root element.
                return document.DocumentElement;
            }
            set
            {
                // check null bodies.
                if (value == null)
                {
                    Value = new ExtensionObject(Value.TypeId);
                    return;
                }

                // create decoder.
                IServiceMessageContext context =
                    Context ?? AmbientMessageContext.CurrentContext;
                using var decoder = new XmlDecoder(value, context);
                Value = decoder.ReadExtensionObjectBody(Value.TypeId);
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
    /// Helper to allow data contract serialization of ExpadedNodeId
    /// </summary>
    [DataContract(
        Name = "ExpandedNodeId",
        Namespace = Namespaces.OpcUaXsd)]
    public class SerializableExpandedNodeId :
        ISurrogateFor<ExpandedNodeId>,
        IEquatable<ExpandedNodeId>,
        IEquatable<SerializableExpandedNodeId>
    {
        /// <inheritdoc/>
        public SerializableExpandedNodeId()
        {
            Value = default;
        }

        /// <inheritdoc/>
        public SerializableExpandedNodeId(ExpandedNodeId value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public ExpandedNodeId Value { get; private set; }

        /// <inheritdoc/>
        public object GetValue()
        {
            return Value;
        }

        /// <summary>
        /// The node identifier formatted as a URI.
        /// </summary>
        [DataMember(Name = "Identifier", Order = 1, IsRequired = true)]
        internal string IdentifierText
        {
            get => Value.Format(CultureInfo.InvariantCulture);
            set => Value = ExpandedNodeId.Parse(value);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj switch
            {
                SerializableExpandedNodeId s => Equals(s),
                ExpandedNodeId n => Equals(n),
                _ => Value.Equals(obj)
            };
        }

        /// <inheritdoc/>
        public bool Equals(ExpandedNodeId obj)
        {
            return Value.Equals(obj);
        }

        /// <inheritdoc/>
        public bool Equals(SerializableExpandedNodeId obj)
        {
            return Value.Equals(obj?.Value ?? default);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(
            SerializableExpandedNodeId left,
            SerializableExpandedNodeId right)
        {
            return left is null ? right is null : left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(
            SerializableExpandedNodeId left,
            SerializableExpandedNodeId right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator ==(
            SerializableExpandedNodeId left,
            ExpandedNodeId right)
        {
            return left is null ? right.IsNull : left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(
            SerializableExpandedNodeId left,
            ExpandedNodeId right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static implicit operator SerializableExpandedNodeId(
            ExpandedNodeId expandedNodeId)
        {
            return new SerializableExpandedNodeId(expandedNodeId);
        }

        /// <inheritdoc/>
        public static implicit operator ExpandedNodeId(
            SerializableExpandedNodeId expandedNodeId)
        {
            return expandedNodeId.Value;
        }

        /// <inheritdoc/>
        public static explicit operator string(
            SerializableExpandedNodeId expandedNodeId)
        {
            return expandedNodeId.IdentifierText;
        }

        /// <inheritdoc/>
        public static explicit operator SerializableExpandedNodeId(
            string expandedNodeId)
        {
            return new SerializableExpandedNodeId
            {
                IdentifierText = expandedNodeId
            };
        }
    }

    /// <summary>
    /// Helper to allow serialization of ByteString
    /// </summary>
    [XmlSchemaProvider("GetSchemaMethod")]
    public class SerializableByteString : ISurrogateFor<ByteString>,
        IXmlSerializable
    {
        /// <inheritdoc/>
        public SerializableByteString()
        {
            Value = default;
        }

        /// <inheritdoc/>
        public SerializableByteString(ByteString value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public ByteString Value { get; private set; }

        /// <inheritdoc/>
        public object GetValue()
        {
            return Value;
        }

        /// <inheritdoc/>
        public XmlSchema GetSchema()
        {
            return null;
        }

        /// <inheritdoc/>
        public void ReadXml(XmlReader reader)
        {
            reader.MoveToContent();
            Value = ByteString.FromBase64(reader.ReadElementContentAsString());
        }

        /// <inheritdoc/>
        public void WriteXml(XmlWriter writer)
        {
            writer.WriteValue(
                Value.ToBase64(Base64FormattingOptions.InsertLineBreaks));
        }

        /// <summary>
        /// Get schema for this type
        /// </summary>
        public static XmlQualifiedName GetSchemaMethod(XmlSchemaSet xs)
        {
            return new XmlQualifiedName("ByteString", Namespaces.OpcUaXsd);
        }
    }

    /// <summary>
    /// A collection of XmlElement values.
    /// </summary>
    [CollectionDataContract(
       Name = "ListOfXmlElement",
       Namespace = Namespaces.OpcUaXsd,
       ItemName = "XmlElement")]
    public class SerializableXmlElementCollection : List<System.Xml.XmlElement>,
        ISurrogateFor<ArrayOf<XmlElement>>
    {
        /// <inheritdoc/>
        public SerializableXmlElementCollection()
        {
        }

        /// <inheritdoc/>
        public SerializableXmlElementCollection(
            IEnumerable<System.Xml.XmlElement> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public SerializableXmlElementCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public SerializableXmlElementCollection(ArrayOf<XmlElement> collection)
            : this(collection.ToList().Select(x => x.ToXmlElement()))
        {
        }

        /// <inheritdoc/>
        public ArrayOf<XmlElement> Value =>
            this.ConvertAll(x => XmlElement.From(x)).ToArrayOf();

        /// <inheritdoc/>
        public object GetValue()
        {
            return Value;
        }
    }
}
