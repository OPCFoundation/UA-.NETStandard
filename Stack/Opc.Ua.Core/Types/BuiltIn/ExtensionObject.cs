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
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// An object used to wrap data types that the receiver may not understand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class is a wrapper/helper class for storing data types that a receiver might not
    /// understand, or be prepared to handle. This class may use <see cref="System.Reflection"/> to
    /// analyze an object and retrieve the values of its public properties directly, and then
    /// encode those into a string representation that can then be easily encoded as a single string.
    /// <br/>
    /// </para>
    /// <para>
    /// An instance of the <see cref="ExtensionObject"/> is a container for any complex data types which cannot be encoded as one of the 
    /// other built-in data types. The ExtensionObject contains a complex value serialized as a sequence of 
    /// bytes or as an XML element. It also contains an identifier which indicates what data it contains and 
    /// how it is encoded. 
    /// </para>
    /// </remarks>
    /// <example>
    /// <para>
    /// The following example demonstrates a simple class containing 3 public properties of
    /// type int, DateTime and string. This class implements the <see cref="IEncodeable"/>
    /// interface, and is then encoded using the <b>WriteExtensionObject</b> method.
    /// <br/></para>
    /// <code lang="C#">
    /// //First, we will define a very simple class object that will represent
    /// //some real-world process.
    /// class simpleClass : IEncodeable
    /// {
    /// 
    ///     //fields
    ///     public string PublicFieldNotVisible = "I should not be encoded";
    ///  
    ///     //properties
    ///     private string stringField;
    ///     public string StringProperty
    ///     {
    ///         get
    ///         {
    ///             return (stringField);
    ///         }
    ///         set
    ///         {
    ///             stringField = value;
    ///         }
    ///     }
    /// 
    ///     private int intField;
    ///     public int IntProperty
    ///     {
    ///         get
    ///         {
    ///             return (intField);
    ///         }
    ///         set
    ///         {
    ///             intField = value;
    ///         }
    ///     }
    ///  
    ///     private DateTime datetimeField;
    ///     public DateTime DatetimeProperty
    ///     {
    ///         get
    ///         {   
    ///             return (datetimeField);
    ///         }
    ///         set
    ///         {
    ///             datetimeField = value;
    ///         }
    ///     }
    /// 
    ///     //class constructor
    ///     public simpleClass(string StringValue, int IntValue, DateTime DateTimeValue)
    ///     {
    ///         StringProperty = StringValue;
    ///         IntProperty = IntValue;
    ///         DatetimeProperty = DateTimeValue;
    ///     }
    ///     public simpleClass(simpleClass SimpleClassInstance)
    ///     {
    ///         StringProperty = SimpleClassInstance.StringProperty;
    ///         IntProperty = SimpleClassInstance.IntProperty;
    ///         DatetimeProperty = SimpleClassInstance.DatetimeProperty;
    ///     }
    /// 
    ///     #region IEncodeable Members
    /// 
    ///     public ExpandedNodeId TypeId
    ///     {
    ///         get
    ///         {
    ///             return (new ExpandedNodeId(Guid.NewGuid()));
    ///         }
    ///     }
    /// 
    ///     public void Encode(IEncoder encoder)
    ///     {
    ///         if (encoder != null)
    ///         {
    ///             //our simple object has 3 properies: string, int and datetime
    ///             encoder.WriteString("StringProperty", this.StringProperty);
    ///             encoder.WriteInt32("IntProperty", this.IntProperty);
    ///             encoder.WriteDateTime("DateTimeProperty", this.DatetimeProperty);
    ///         }
    ///     }
    /// 
    ///     public void Decode(IDecoder decoder)
    ///     {
    ///         if (decoder != null)
    ///         {
    ///             this.StringProperty = decoder.ReadString("StringProperty");
    ///             this.IntProperty = decoder.ReadInt16("IntProperty");
    ///             this.DatetimeProperty = decoder.ReadDateTime("DateTimeProperty");
    ///         }
    ///     }
    /// 
    ///     public bool IsEqual(IEncodeable encodeable)
    ///     {
    ///         return (encodeable.Equals(this));
    ///     }
    /// 
    ///     #endregion
    /// 
    ///     #region ICloneable Members
    /// 
    ///     public new object MemberwiseClone()
    ///     {
    ///         return (new simpleClass(this));
    ///     }
    /// 
    ///     #endregion
    /// 
    /// }
    /// 
    /// public void EncodeExample()
    /// {
    ///     //define an instance of our class object, defined above.
    ///     simpleClass mySimpleClassInstance1 = new simpleClass("String", int.MaxValue, DateTime.Now);
    /// 
    ///     //define an object that will encapsulate/extend our simple instance above
    ///     ExtensionObject extendedSimpleClassInstance = new ExtensionObject(mySimpleClassInstance1);
    /// 
    ///     /// 
    ///     //encode our class object into the stream
    ///     uaEncoderInstance.WriteExtensionObject( "Extended1", extendedSimpleClassInstance);
    /// }
    /// </code>
    /// <code lang="Visual Basic">
    /// 
    /// 'First, we will define a very simple class object that will represent
    /// 'some real-world process.
    /// 
    /// Class simpleClass
    ///    Inherits IEncodeable
    ///    
    ///    'fields
    ///    Public PublicFieldNotVisible As String = "I should not be encoded"
    ///    
    ///    'properties
    ///    Private stringField As String
    ///    Private intField As Integer
    ///    Private datetimeField As DateTime
    ///    
    ///    'class constructor
    ///    Public Sub New(ByVal StringValue As String, ByVal IntValue As Integer, ByVal DateTimeValue As DateTime)
    ///        StringProperty = StringValue
    ///        IntProperty = IntValue
    ///        DatetimeProperty = DateTimeValue
    ///    End Sub
    ///    
    ///    Public Sub New(ByVal SimpleClassInstance As simpleClass)
    ///        StringProperty = SimpleClassInstance.StringProperty
    ///        IntProperty = SimpleClassInstance.IntProperty
    ///        DatetimeProperty = SimpleClassInstance.DatetimeProperty
    ///    End Sub
    ///    
    ///    Public Property StringProperty As String
    ///        Get
    ///            Return stringField
    ///        End Get
    ///        Set
    ///            stringField = value
    ///        End Set
    ///    End Property
    ///    
    ///    Public Property IntProperty As Integer
    ///        Get
    ///            Return intField
    ///        End Get
    ///        Set
    ///            intField = value
    ///        End Set
    ///    End Property
    ///    
    ///    Public Property DatetimeProperty As DateTime
    ///        Get
    ///            Return datetimeField
    ///        End Get
    ///        Set
    ///            datetimeField = value
    ///        End Set
    ///    End Property
    ///    
    ///    Public ReadOnly Property TypeId As ExpandedNodeId
    ///        Get
    ///            Return New ExpandedNodeId(Guid.NewGuid)
    ///        End Get
    ///    End Property
    ///    
    ///    Public Sub Encode(ByVal encoder As IEncoder)
    ///        If encoder Isnot Nothing Then
    ///            'our simple object has 3 properies: string, int and datetime
    ///            encoder.WriteString("StringProperty", Me.StringProperty)
    ///            encoder.WriteInt32("IntProperty", Me.IntProperty)
    ///            encoder.WriteDateTime("DateTimeProperty", Me.DatetimeProperty)
    ///        End If
    ///    End Sub
    ///    
    ///    Public Sub Decode(ByVal decoder As IDecoder)
    ///        If decoder Isnot Nothing Then
    ///            Me.StringProperty = decoder.ReadString("StringProperty")
    ///            Me.IntProperty = decoder.ReadInt16("IntProperty")
    ///            Me.DatetimeProperty = decoder.ReadDateTime("DateTimeProperty")
    ///        End If
    ///    End Sub
    ///    
    ///    Public Function IsEqual(ByVal encodeable As IEncodeable) As Boolean
    ///        Return encodeable.Equals(Me)
    ///    End Function
    ///    
    ///    Public Function Clone() As Object
    ///        Return New simpleClass(Me)
    ///    End Function
    ///End Class
    ///
    ///   Public Sub nodeid()
    ///    'define an instance of our class object, defined above.
    ///    Dim mySimpleClassInstance1 As simpleClass = New simpleClass("String", int.MaxValue, DateTime.Now)
    /// 
    ///    'define an object that will encapsulate/extend our simple instance above
    ///    Dim extendedSimpleClassInstance As ExtensionObject = New ExtensionObject(mySimpleClassInstance1)
    /// 
    ///    'encode our class object into the stream
    ///    uaEncoderInstance.WriteExtensionObject("Extended1", extendedSimpleClassInstance)
    ///End Sub
    /// </code>
    /// </example>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class ExtensionObject : IFormattable
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        /// <remarks>
        /// Initializes the object with default values.
        /// </remarks>
        public ExtensionObject()
        {
            m_typeId = ExpandedNodeId.Null;
            m_encoding = ExtensionObjectEncoding.None;
            m_body = null;
            m_context = MessageContextExtension.CurrentContext;
        }

        /// <summary>
        /// Creates a deep copy of the value.
        /// </summary>
        /// <param name="value">The value to be copied.</param>
        /// <remarks>
        /// Creates a deep copy of the value.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when the value is null</exception>
        public ExtensionObject(ExtensionObject value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            TypeId = value.TypeId;
            Body = Utils.Clone(value.Body);
        }

        /// <summary>
        /// Initializes the object with a <paramref name="typeId"/>.
        /// </summary>
        /// <param name="typeId">The type to copy and create an instance from</param>
        public ExtensionObject(ExpandedNodeId typeId)
        {
            TypeId = typeId;
            Body = null;
        }

        /// <summary>
        /// Initializes the object with a body.
        /// </summary>
        /// <param name="body">The body of the object: IEncodeable, XmlElement or Byte-array</param>
        public ExtensionObject(object body)
            : this(ExpandedNodeId.Null, body)
        {
        }

        /// <summary>
        /// Initializes the object with an encodeable object.
        /// </summary>
        /// <param name="typeId">The type describing the body</param>
        /// <param name="body">The underlying data/body to wrap</param>
        /// <remarks>
        /// Initializes the object with an encodeable object.
        /// </remarks>
        public ExtensionObject(ExpandedNodeId typeId, object body)
        {
            TypeId = typeId;
            Body = body;
        }

        [OnSerializing]
        private void UpdateContext(StreamingContext context)
        {
            m_context = MessageContextExtension.CurrentContext;
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            m_typeId = ExpandedNodeId.Null;
            m_encoding = ExtensionObjectEncoding.None;
            m_body = null;
            m_context = MessageContextExtension.CurrentContext;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The data type node id for the extension object.
        /// </summary>
        /// <value>The type id.</value>
        public ExpandedNodeId TypeId
        {
            get { return m_typeId; }
            set { m_typeId = value; }
        }

        /// <summary>
        /// The encoding to use when the deserializing/serializing the body.
        /// </summary>
        /// <value>The encoding for the embedd object.</value>
        public ExtensionObjectEncoding Encoding => m_encoding;

        /// <summary>
        /// The body (embeded object) of the extension object.
        /// </summary>
        /// <value>The object to be embeded.</value>
        /// <remarks>
        /// The body of the extension object. This property will work with objects of the
        /// following types:
        /// <list type="bullet">
        /// 		<item><see cref="IEncodeable"/></item>
        /// 		<item>byte-array ( C# = byte[]    or VB.NET Byte() )</item>
        /// 		<item><see cref="XmlElement"/></item>
        /// 	</list>
        /// </remarks>
        /// <exception cref="ServiceResultException">Thrown when the body is not one of the types listed above</exception>
        public object Body
        {
            get { return m_body; }

            set
            {
                m_body = value;

                if (m_body == null)
                {
                    m_encoding = ExtensionObjectEncoding.None;
                }

                else if (m_body is IEncodeable)
                {
                    m_encoding = ExtensionObjectEncoding.EncodeableObject;
                }

                else if (m_body is byte[])
                {
                    m_encoding = ExtensionObjectEncoding.Binary;
                }

                else if (m_body is XmlElement)
                {
                    m_encoding = ExtensionObjectEncoding.Xml;
                }

                else
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        Utils.Format("Cannot add a object with type '{0}' to an extension object.", m_body.GetType().FullName));
                }
            }
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Determines if the specified object is equal to the <paramref name="obj"/>.
        /// </summary>
        /// <param name="obj">The object to compare to this instance of object</param>
        /// <returns>
        /// true if the specified <see cref="T:System.Object"/> is equal to the current embeded object; otherwise, false.
        /// </returns>
        public override bool Equals(object obj)
        {
            if (Object.ReferenceEquals(obj, null))
            {
                return IsNull(this);
            }

            if (Object.ReferenceEquals(this, obj))
            {
                return true;
            }

            ExtensionObject value = obj as ExtensionObject;

            if (value != null)
            {
                if (this.m_typeId != value.m_typeId)
                {
                    return false;
                }

                return Utils.IsEqual(this.m_body, value.m_body);
            }

            return false;
        }

        /// <summary>
        /// Returns a unique hashcode for the embeded object.
        /// </summary>
        /// <returns>
        /// A hash code for the current embeded object.
        /// </returns>
        public override int GetHashCode()
        {
            if (this.m_body != null)
            {
                return this.m_body.GetHashCode();
            }

            if (this.m_typeId != null)
            {
                return this.m_typeId.GetHashCode();
            }

            return 0;
        }

        /// <summary>
        /// Converts the value to a human readable string.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, null);
        }
        #endregion

        #region IFormattable Members
        /// <summary>
        /// Returns the string representation of the embededobject.
        /// </summary>
        /// <param name="format">(Unused). Leave this as null</param>
        /// <param name="formatProvider">The provider of a mechanism for retrieving an object to control formatting.</param>
        /// <returns>
        /// A <see cref="T:System.String"/> containing the value of the current embeded instance in the specified format.
        /// </returns>
        /// <exception cref="FormatException">Thrown if the <i>format</i> parameter is not null</exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                if (m_body is byte[])
                {
                    return String.Format(formatProvider, "Byte[{0}]", ((byte[])m_body).Length);
                }

                if (m_body is XmlElement)
                {
                    return String.Format(formatProvider, "<{0}>", ((XmlElement)m_body).Name);
                }

                if (m_body is IFormattable)
                {
                    return String.Format(formatProvider, "{0}", ((IFormattable)m_body).ToString(null, formatProvider));
                }

                if (m_body is IEncodeable)
                {
                    StringBuilder body = new StringBuilder();

                    PropertyInfo[] properties = m_body.GetType().GetProperties(BindingFlags.Public | BindingFlags.FlattenHierarchy | BindingFlags.Instance);

                    foreach (PropertyInfo property in properties)
                    {
                        object[] attributes = property.GetCustomAttributes(typeof(DataMemberAttribute), true).ToArray();

                        for (int ii = 0; ii < attributes.Length; ii++)
                        {
                            DataMemberAttribute contract = attributes[ii] as DataMemberAttribute;

                            if (contract != null)
                            {
                                if (body.Length == 0)
                                {
                                    body.Append("{");
                                }
                                else
                                {
                                    body.Append(" | ");
                                }

                                body.AppendFormat("{0}", property.GetGetMethod().Invoke(m_body, null));
                            }
                        }
                    }

                    if (body.Length > 0)
                    {
                        body.Append("}");
                    }

                    return String.Format(formatProvider, "{0}", body);
                }

                if (!NodeId.IsNull(this.m_typeId))
                {
                    return String.Format(formatProvider, "{{{0}}}", this.m_typeId);
                }

                return "(null)";
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion

        #region ICloneable Members
        /// <summary>
        /// Makes a deep copy of the object.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public new object MemberwiseClone()
        {
            return new ExtensionObject(this);
        }
        #endregion

        #region Static Members
        /// <summary>
        /// Tests if the extension or embedd objects are null value.
        /// </summary>
        /// <param name="extension">The object to check if null</param>
        /// <returns>
        /// 	<c>true</c> if the specified <paramref name="extension"/> is null of the embeded object is null; otherwise, <c>false</c>.
        /// </returns>
        /// <remarks>
        /// Tests is the  extension object is null value.
        /// </remarks>
        public static bool IsNull(ExtensionObject extension)
        {
            if (extension != null && extension.m_body != null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Converts an extension object to an encodeable object.
        /// </summary>
        /// <param name="extension">The extension object to convert to an encodeable object</param>
        /// <returns>Instance of <see cref="IEncodeable"/> for the embeded object.</returns>
        /// <remarks>
        /// Converts an extension object to an encodeable object.
        /// </remarks>
        public static IEncodeable ToEncodeable(ExtensionObject extension)
        {
            if (extension == null)
            {
                return null;
            }

            return extension.Body as IEncodeable;
        }

        /// <summary>
        /// Converts an array of extension objects to an array of the specified type.
        /// </summary>
        /// <param name="extensions">The array to convert.</param>
        /// <param name="elementType">The type of each element.</param>
        /// <returns>The new array</returns>
        /// <remarks>
        /// Will add null elements if individual elements cannot be converted.
        /// </remarks>
        public static Array ToArray(object source, Type elementType)
        {
            var extensions = source as Array;

            if (extensions == null)
            {
                return null;
            }

            Array output = Array.CreateInstance(elementType, extensions.Length);

            for (int ii = 0; ii < output.Length; ii++)
            {
                IEncodeable element = ToEncodeable(extensions.GetValue(ii) as ExtensionObject);

                if (elementType.IsInstanceOfType(element))
                {
                    output.SetValue(element, ii);
                }
            }

            return output;
        }

        /// <summary>
        /// Converts an array of extension objects to a List of the specified type.
        /// </summary>
        /// <param name="extensions">The array to convert.</param>
        /// <returns>The new typed List</returns>
        /// <remarks>
        /// Will add null elements if individual elements cannot be converted.
        /// </remarks>
        public static List<T> ToList<T>(object source) where T : class
        {
            var extensions = source as Array;

            if (extensions == null)
            {
                return null;
            }

            List<T> list = new List<T>();

            for (int ii = 0; ii < extensions.Length; ii++)
            {
                IEncodeable element = ToEncodeable(extensions.GetValue(ii) as ExtensionObject);

                if (typeof(T).IsInstanceOfType(element))
                {
                    list.Add((T)element);
                }
                else
                {
                    list.Add(null);
                }
            }

            return list;
        }

        /// <summary>
        /// Returns an instance of a null ExtensionObject.
        /// </summary>
        public static ExtensionObject Null => s_Null;
        private static readonly ExtensionObject s_Null = new ExtensionObject();
        #endregion

        #region Private Members
        [DataMember(Name = "TypeId", Order = 1, IsRequired = false, EmitDefaultValue = true)]
        private NodeId XmlEncodedTypeId
        {
            get
            {
                // must use the XML encoding id if encoding in an XML stream.
                IEncodeable encodeable = m_body as IEncodeable;

                if (encodeable != null)
                {
                    return ExpandedNodeId.ToNodeId(encodeable.XmlEncodingId, m_context.NamespaceUris);
                }

                // check for null Id.
                if (m_typeId.IsNull)
                {
                    return NodeId.Null;
                }

                return ExpandedNodeId.ToNodeId(m_typeId, m_context.NamespaceUris);
            }

            set
            {
                m_typeId = NodeId.ToExpandedNodeId(value, m_context.NamespaceUris);
            }
        }

        [DataMember(Name = "Body", Order = 2, IsRequired = false, EmitDefaultValue = true)]
        private XmlElement XmlEncodedBody
        {
            get
            {
                // check for null.
                if (m_body == null)
                {
                    return null;
                }

                // create encoder.
                XmlEncoder encoder = new XmlEncoder(m_context);

                // write body.
                encoder.WriteExtensionObjectBody(m_body);

                // create document from encoder.
                XmlDocument document = new XmlDocument();
                document.InnerXml = encoder.Close();

                // return root element.
                return document.DocumentElement;
            }

            set
            {
                // check null bodies.
                if (value == null)
                {
                    Body = null;
                    return;
                }

                // create decoder.
                XmlDecoder decoder = new XmlDecoder(value, m_context);

                // read body.
                Body = decoder.ReadExtensionObjectBody(m_typeId);

                // clear the type id for encodeables.
                IEncodeable encodeable = m_body as IEncodeable;

                if (encodeable != null)
                {
                    m_typeId = ExpandedNodeId.Null;
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
                        Utils.Format("Did not read all of a extension object body: '{0}'", m_typeId),
                        e);
                }
            }
        }
        #endregion

        #region Private Fields
        private ExpandedNodeId m_typeId;
        private ExtensionObjectEncoding m_encoding;
        private object m_body;
        private ServiceMessageContext m_context;
        #endregion
    }

    #region ExtensionObjectEncoding Enumeration
    /// <summary>
    /// The types of encodings that may used with an object.
    /// </summary>
    /// <remarks>
    /// The types of encodings that may used with an object.
    /// </remarks>
    public enum ExtensionObjectEncoding
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
    #endregion

    #region ExtensionObjectCollection Class
    /// <summary>
    /// A collection of ExtensionObjects.
    /// </summary>   
    /// <remarks>
    /// A strongly-typed collection of ExtensionObjects.
    /// </remarks>
    [CollectionDataContract(Name = "ListOfExtensionObject", Namespace = Namespaces.OpcUaXsd, ItemName = "ExtensionObject")]
    public class ExtensionObjectCollection : List<ExtensionObject>
    {
        #region Constructors
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public ExtensionObjectCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        /// <param name="collection">The collection containing the objects to copy into this new instance</param>
        public ExtensionObjectCollection(IEnumerable<ExtensionObject> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">Max capacity of the collection</param>
        public ExtensionObjectCollection(int capacity) : base(capacity) { }
        #endregion

        #region Static Members
        /// <summary>
        /// Converts an array of ExtensionObjects to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array of ExtensionObjects to a collection.
        /// </remarks>
        /// <param name="values">An array of ExtensionObjects to convert to a collection</param>
        public static implicit operator ExtensionObjectCollection(ExtensionObject[] values)
        {
            if (values != null)
            {
                return new ExtensionObjectCollection(values);
            }

            return new ExtensionObjectCollection();
        }

        /// <summary>
        /// Converts an encodeable object to an extension object.
        /// </summary>
        /// <remarks>
        /// Converts an encodeable object to an extension object.
        /// </remarks>
        /// <param name="encodeables">An enumerable array of ExtensionObjects to convert to a collection</param>
        public static ExtensionObjectCollection ToExtensionObjects(IEnumerable<IEncodeable> encodeables)
        {
            // return null if the input list is null.
            if (encodeables == null)
            {
                return null;
            }

            // convert each encodeable to an extension object.
            ExtensionObjectCollection extensibles = new ExtensionObjectCollection();

            if (encodeables != null)
            {
                foreach (IEncodeable encodeable in encodeables)
                {
                    // check if already an extension object.
                    ExtensionObject extensible = encodeable as ExtensionObject;

                    if (extensible != null)
                    {
                        extensibles.Add(extensible);
                    }

                    // wrap the encodeable with an extension object and let the serializer choose the encoding.
                    else
                    {
                        extensibles.Add(new ExtensionObject(encodeable));
                    }
                }
            }

            return extensibles;
        }
        #endregion

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public new object MemberwiseClone()
        {
            ExtensionObjectCollection clone = new ExtensionObjectCollection(this.Count);

            foreach (ExtensionObject element in this)
            {
                clone.Add((ExtensionObject)Utils.Clone(element));
            }

            return clone;
        }
        #endregion
    }//class
}//namespace
