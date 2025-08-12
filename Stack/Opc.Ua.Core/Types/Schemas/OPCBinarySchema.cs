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

using System.Runtime.Serialization;
using System.Xml;

namespace Opc.Ua.Schema.Binary
{
    /// <summary>
    /// The binary schema documentation.
    /// </summary>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(
        AnonymousType = true,
        Namespace = "http://opcfoundation.org/BinarySchema/"
    )]
    [System.Xml.Serialization.XmlRootAttribute(
        Namespace = "http://opcfoundation.org/BinarySchema/",
        IsNullable = false
    )]
    public partial class Documentation
    {
        private XmlElement[] itemsField;

        private string[] textField;

        private string[] anyAttrField;

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAnyElementAttribute()]
        public XmlElement[] Items
        {
            get
            {
                return this.itemsField;
            }
            set
            {
                this.itemsField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlTextAttribute()]
        public string[] Text
        {
            get
            {
                return this.textField;
            }
            set
            {
                this.textField = value;
            }
        }

        /// <inheritdoc/>
        public string[] AnyAttr
        {
            get
            {
                return this.anyAttrField;
            }
            set
            {
                this.anyAttrField = value;
            }
        }
    }

    /// <inheritdoc/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace = "http://opcfoundation.org/BinarySchema/")]
    public partial class FieldType
    {
        private Documentation documentationField;

        private string nameField;

        private System.Xml.XmlQualifiedName typeNameField;

        private uint lengthField;

        private bool lengthFieldSpecified;

        private string lengthFieldField;

        private bool isLengthInBytesField;

        private string switchFieldField;

        private uint switchValueField;

        private bool switchValueFieldSpecified;

        private SwitchOperand switchOperandField;

        private bool switchOperandFieldSpecified;

        private byte[] terminatorField;

        private string[] anyAttrField;

        /// <inheritdoc/>
        public FieldType()
        {
            this.isLengthInBytesField = false;
        }

        /// <inheritdoc/>
        public Documentation Documentation
        {
            get
            {
                return this.documentationField;
            }
            set
            {
                this.documentationField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Name
        {
            get
            {
                return this.nameField;
            }
            set
            {
                this.nameField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public System.Xml.XmlQualifiedName TypeName
        {
            get
            {
                return this.typeNameField;
            }
            set
            {
                this.typeNameField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public uint Length
        {
            get
            {
                return this.lengthField;
            }
            set
            {
                this.lengthField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool LengthSpecified
        {
            get
            {
                return this.lengthFieldSpecified;
            }
            set
            {
                this.lengthFieldSpecified = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string LengthField
        {
            get
            {
                return this.lengthFieldField;
            }
            set
            {
                this.lengthFieldField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        [System.ComponentModel.DefaultValueAttribute(false)]
        public bool IsLengthInBytes
        {
            get
            {
                return this.isLengthInBytesField;
            }
            set
            {
                this.isLengthInBytesField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string SwitchField
        {
            get
            {
                return this.switchFieldField;
            }
            set
            {
                this.switchFieldField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public uint SwitchValue
        {
            get
            {
                return this.switchValueField;
            }
            set
            {
                this.switchValueField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool SwitchValueSpecified
        {
            get
            {
                return this.switchValueFieldSpecified;
            }
            set
            {
                this.switchValueFieldSpecified = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public SwitchOperand SwitchOperand
        {
            get
            {
                return this.switchOperandField;
            }
            set
            {
                this.switchOperandField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool SwitchOperandSpecified
        {
            get
            {
                return this.switchOperandFieldSpecified;
            }
            set
            {
                this.switchOperandFieldSpecified = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttributeAttribute(DataType = "hexBinary")]
        public byte[] Terminator
        {
            get
            {
                return this.terminatorField;
            }
            set
            {
                this.terminatorField = value;
            }
        }

        /// <inheritdoc/>
        public string[] AnyAttr
        {
            get
            {
                return this.anyAttrField;
            }
            set
            {
                this.anyAttrField = value;
            }
        }
    }

    /// <inheritdoc/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace = "http://opcfoundation.org/BinarySchema/")]
    public enum SwitchOperand
    {
        /// <inheritdoc/>
        Equals,

        /// <inheritdoc/>
        GreaterThan,

        /// <inheritdoc/>
        LessThan,

        /// <inheritdoc/>
        GreaterThanOrEqual,

        /// <inheritdoc/>
        LessThanOrEqual,

        /// <inheritdoc/>
        NotEqual,
    }

    /// <inheritdoc/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace = "http://opcfoundation.org/BinarySchema/")]
    public partial class EnumeratedValue
    {
        private Documentation documentationField;

        private string nameField;

        private int valueField;

        private bool valueFieldSpecified;

        /// <inheritdoc/>
        public Documentation Documentation
        {
            get
            {
                return this.documentationField;
            }
            set
            {
                this.documentationField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Name
        {
            get
            {
                return this.nameField;
            }
            set
            {
                this.nameField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public int Value
        {
            get
            {
                return this.valueField;
            }
            set
            {
                this.valueField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool ValueSpecified
        {
            get
            {
                return this.valueFieldSpecified;
            }
            set
            {
                this.valueFieldSpecified = value;
            }
        }
    }

    /// <inheritdoc/>
    [System.Xml.Serialization.XmlInclude(typeof(StructuredType))]
    [System.Xml.Serialization.XmlInclude(typeof(OpaqueType))]
    [System.Xml.Serialization.XmlInclude(typeof(EnumeratedType))]
    [System.CodeDom.Compiler.GeneratedCode("xsd", "2.0.50727.312")]
    [DataContract]
    [System.Diagnostics.DebuggerStepThrough]
    [System.Xml.Serialization.XmlType(Namespace = "http://opcfoundation.org/BinarySchema/")]
    public partial class TypeDescription
    {
        /// <inheritdoc/>
        public Documentation Documentation { get; set; }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttribute(DataType = "NCName")]
        public string Name { get; set; }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttribute]
        public ByteOrder DefaultByteOrder { get; set; }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlIgnore]
        public bool DefaultByteOrderSpecified { get; set; }
    }

    /// <inheritdoc/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace = "http://opcfoundation.org/BinarySchema/")]
    public enum ByteOrder
    {
        /// <inheritdoc/>
        BigEndian,

        /// <inheritdoc/>
        LittleEndian,
    }

    /// <inheritdoc/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace = "http://opcfoundation.org/BinarySchema/")]
    public partial class StructuredType : TypeDescription
    {
        private FieldType[] fieldField;

        private string[] anyAttrField;

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlElementAttribute("Field")]
        public FieldType[] Field
        {
            get
            {
                return this.fieldField;
            }
            set
            {
                this.fieldField = value;
            }
        }

        /// <inheritdoc/>
        public string[] AnyAttr
        {
            get
            {
                return this.anyAttrField;
            }
            set
            {
                this.anyAttrField = value;
            }
        }
    }

    /// <inheritdoc/>
    [System.Xml.Serialization.XmlIncludeAttribute(typeof(EnumeratedType))]
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace = "http://opcfoundation.org/BinarySchema/")]
    public partial class OpaqueType : TypeDescription
    {
        private int lengthInBitsField;

        private bool lengthInBitsFieldSpecified;

        private bool byteOrderSignificantField;

        /// <inheritdoc/>
        public OpaqueType()
        {
            this.byteOrderSignificantField = false;
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public int LengthInBits
        {
            get
            {
                return this.lengthInBitsField;
            }
            set
            {
                this.lengthInBitsField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool LengthInBitsSpecified
        {
            get
            {
                return this.lengthInBitsFieldSpecified;
            }
            set
            {
                this.lengthInBitsFieldSpecified = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        [System.ComponentModel.DefaultValueAttribute(false)]
        public bool ByteOrderSignificant
        {
            get
            {
                return this.byteOrderSignificantField;
            }
            set
            {
                this.byteOrderSignificantField = value;
            }
        }
    }

    /// <inheritdoc/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace = "http://opcfoundation.org/BinarySchema/")]
    public partial class EnumeratedType : OpaqueType
    {
        private EnumeratedValue[] enumeratedValueField;

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlElementAttribute("EnumeratedValue")]
        public EnumeratedValue[] EnumeratedValue
        {
            get
            {
                return this.enumeratedValueField;
            }
            set
            {
                this.enumeratedValueField = value;
            }
        }
    }

    /// <inheritdoc/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace = "http://opcfoundation.org/BinarySchema/")]
    public partial class ImportDirective
    {
        private string namespaceField;

        private string locationField;

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Namespace
        {
            get
            {
                return this.namespaceField;
            }
            set
            {
                this.namespaceField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Location
        {
            get
            {
                return this.locationField;
            }
            set
            {
                this.locationField = value;
            }
        }
    }

    /// <inheritdoc/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(
        AnonymousType = true,
        Namespace = "http://opcfoundation.org/BinarySchema/"
    )]
    [System.Xml.Serialization.XmlRootAttribute(
        Namespace = "http://opcfoundation.org/BinarySchema/",
        IsNullable = false
    )]
    public partial class TypeDictionary
    {
        private Documentation documentationField;

        private ImportDirective[] importField;

        private TypeDescription[] itemsField;

        private string targetNamespaceField;

        private ByteOrder defaultByteOrderField;

        private bool defaultByteOrderFieldSpecified;

        /// <inheritdoc/>
        public Documentation Documentation
        {
            get
            {
                return this.documentationField;
            }
            set
            {
                this.documentationField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlElementAttribute("Import")]
        public ImportDirective[] Import
        {
            get
            {
                return this.importField;
            }
            set
            {
                this.importField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlElementAttribute("EnumeratedType", typeof(EnumeratedType))]
        [System.Xml.Serialization.XmlElementAttribute("OpaqueType", typeof(OpaqueType))]
        [System.Xml.Serialization.XmlElementAttribute("StructuredType", typeof(StructuredType))]
        public TypeDescription[] Items
        {
            get
            {
                return this.itemsField;
            }
            set
            {
                this.itemsField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string TargetNamespace
        {
            get
            {
                return this.targetNamespaceField;
            }
            set
            {
                this.targetNamespaceField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public ByteOrder DefaultByteOrder
        {
            get
            {
                return this.defaultByteOrderField;
            }
            set
            {
                this.defaultByteOrderField = value;
            }
        }

        /// <inheritdoc/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool DefaultByteOrderSpecified
        {
            get
            {
                return this.defaultByteOrderFieldSpecified;
            }
            set
            {
                this.defaultByteOrderFieldSpecified = value;
            }
        }
    }
}
