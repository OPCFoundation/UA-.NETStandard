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

using System.Runtime.Serialization;
using System.Xml;

namespace Opc.Ua.Schema.Binary
{
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType=true, Namespace="http://opcfoundation.org/BinarySchema/")]
    [System.Xml.Serialization.XmlRootAttribute(Namespace="http://opcfoundation.org/BinarySchema/", IsNullable=false)]
    public partial class Documentation {
        
        private XmlElement[] itemsField;
        
        private string[] textField;
        
        private string[] anyAttrField;
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAnyElementAttribute()]
        public XmlElement[] Items {
            get {
                return this.itemsField;
            }
            set {
                this.itemsField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlTextAttribute()]
        public string[] Text {
            get {
                return this.textField;
            }
            set {
                this.textField = value;
            }
        }
        
        /// <remarks/>
        public string[] AnyAttr {
            get {
                return this.anyAttrField;
            }
            set {
                this.anyAttrField = value;
            }
        }
    }
    
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="http://opcfoundation.org/BinarySchema/")]
    public partial class FieldType {
        
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
        
        /// <remarks/>
        public FieldType() {
            this.isLengthInBytesField = false;
        }
        
        /// <remarks/>
        public Documentation Documentation {
            get {
                return this.documentationField;
            }
            set {
                this.documentationField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Name {
            get {
                return this.nameField;
            }
            set {
                this.nameField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public System.Xml.XmlQualifiedName TypeName {
            get {
                return this.typeNameField;
            }
            set {
                this.typeNameField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public uint Length {
            get {
                return this.lengthField;
            }
            set {
                this.lengthField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool LengthSpecified {
            get {
                return this.lengthFieldSpecified;
            }
            set {
                this.lengthFieldSpecified = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string LengthField {
            get {
                return this.lengthFieldField;
            }
            set {
                this.lengthFieldField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        [System.ComponentModel.DefaultValueAttribute(false)]
        public bool IsLengthInBytes {
            get {
                return this.isLengthInBytesField;
            }
            set {
                this.isLengthInBytesField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string SwitchField {
            get {
                return this.switchFieldField;
            }
            set {
                this.switchFieldField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public uint SwitchValue {
            get {
                return this.switchValueField;
            }
            set {
                this.switchValueField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool SwitchValueSpecified {
            get {
                return this.switchValueFieldSpecified;
            }
            set {
                this.switchValueFieldSpecified = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public SwitchOperand SwitchOperand {
            get {
                return this.switchOperandField;
            }
            set {
                this.switchOperandField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool SwitchOperandSpecified {
            get {
                return this.switchOperandFieldSpecified;
            }
            set {
                this.switchOperandFieldSpecified = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute(DataType="hexBinary")]
        public byte[] Terminator {
            get {
                return this.terminatorField;
            }
            set {
                this.terminatorField = value;
            }
        }
        
        /// <remarks/>
        public string[] AnyAttr {
            get {
                return this.anyAttrField;
            }
            set {
                this.anyAttrField = value;
            }
        }
    }
    
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="http://opcfoundation.org/BinarySchema/")]
    public enum SwitchOperand {
        
        /// <remarks/>
        Equals,
        
        /// <remarks/>
        GreaterThan,
        
        /// <remarks/>
        LessThan,
        
        /// <remarks/>
        GreaterThanOrEqual,
        
        /// <remarks/>
        LessThanOrEqual,
        
        /// <remarks/>
        NotEqual,
    }
    
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="http://opcfoundation.org/BinarySchema/")]
    public partial class EnumeratedValue {
        
        private Documentation documentationField;
        
        private string nameField;
        
        private int valueField;
        
        private bool valueFieldSpecified;
        
        /// <remarks/>
        public Documentation Documentation {
            get {
                return this.documentationField;
            }
            set {
                this.documentationField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Name {
            get {
                return this.nameField;
            }
            set {
                this.nameField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public int Value {
            get {
                return this.valueField;
            }
            set {
                this.valueField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool ValueSpecified {
            get {
                return this.valueFieldSpecified;
            }
            set {
                this.valueFieldSpecified = value;
            }
        }
    }
    
    /// <remarks/>
    [System.Xml.Serialization.XmlIncludeAttribute(typeof(StructuredType))]
    [System.Xml.Serialization.XmlIncludeAttribute(typeof(OpaqueType))]
    [System.Xml.Serialization.XmlIncludeAttribute(typeof(EnumeratedType))]
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="http://opcfoundation.org/BinarySchema/")]
    public partial class TypeDescription {
        
        private Documentation documentationField;
        
        private string nameField;
        
        private ByteOrder defaultByteOrderField;
        
        private bool defaultByteOrderFieldSpecified;
        
        /// <remarks/>
        public Documentation Documentation {
            get {
                return this.documentationField;
            }
            set {
                this.documentationField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute(DataType="NCName")]
        public string Name {
            get {
                return this.nameField;
            }
            set {
                this.nameField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public ByteOrder DefaultByteOrder {
            get {
                return this.defaultByteOrderField;
            }
            set {
                this.defaultByteOrderField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool DefaultByteOrderSpecified {
            get {
                return this.defaultByteOrderFieldSpecified;
            }
            set {
                this.defaultByteOrderFieldSpecified = value;
            }
        }
    }
    
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="http://opcfoundation.org/BinarySchema/")]
    public enum ByteOrder {
        
        /// <remarks/>
        BigEndian,
        
        /// <remarks/>
        LittleEndian,
    }
    
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="http://opcfoundation.org/BinarySchema/")]
    public partial class StructuredType : TypeDescription {
        
        private FieldType[] fieldField;
        
        private string[] anyAttrField;
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute("Field")]
        public FieldType[] Field {
            get {
                return this.fieldField;
            }
            set {
                this.fieldField = value;
            }
        }
        
        /// <remarks/>
        public string[] AnyAttr {
            get {
                return this.anyAttrField;
            }
            set {
                this.anyAttrField = value;
            }
        }
    }
    
    /// <remarks/>
    [System.Xml.Serialization.XmlIncludeAttribute(typeof(EnumeratedType))]
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="http://opcfoundation.org/BinarySchema/")]
    public partial class OpaqueType : TypeDescription {
        
        private int lengthInBitsField;
        
        private bool lengthInBitsFieldSpecified;
        
        private bool byteOrderSignificantField;
        
        /// <remarks/>
        public OpaqueType() {
            this.byteOrderSignificantField = false;
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public int LengthInBits {
            get {
                return this.lengthInBitsField;
            }
            set {
                this.lengthInBitsField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool LengthInBitsSpecified {
            get {
                return this.lengthInBitsFieldSpecified;
            }
            set {
                this.lengthInBitsFieldSpecified = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        [System.ComponentModel.DefaultValueAttribute(false)]
        public bool ByteOrderSignificant {
            get {
                return this.byteOrderSignificantField;
            }
            set {
                this.byteOrderSignificantField = value;
            }
        }
    }
    
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="http://opcfoundation.org/BinarySchema/")]
    public partial class EnumeratedType : OpaqueType {
        
        private EnumeratedValue[] enumeratedValueField;
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute("EnumeratedValue")]
        public EnumeratedValue[] EnumeratedValue {
            get {
                return this.enumeratedValueField;
            }
            set {
                this.enumeratedValueField = value;
            }
        }
    }
    
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="http://opcfoundation.org/BinarySchema/")]
    public partial class ImportDirective {
        
        private string namespaceField;
        
        private string locationField;
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Namespace {
            get {
                return this.namespaceField;
            }
            set {
                this.namespaceField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Location {
            get {
                return this.locationField;
            }
            set {
                this.locationField = value;
            }
        }
    }
    
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.312")]
    [DataContractAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType=true, Namespace="http://opcfoundation.org/BinarySchema/")]
    [System.Xml.Serialization.XmlRootAttribute(Namespace="http://opcfoundation.org/BinarySchema/", IsNullable=false)]
    public partial class TypeDictionary {
        
        private Documentation documentationField;
        
        private ImportDirective[] importField;
        
        private TypeDescription[] itemsField;
        
        private string targetNamespaceField;
        
        private ByteOrder defaultByteOrderField;
        
        private bool defaultByteOrderFieldSpecified;
        
        /// <remarks/>
        public Documentation Documentation {
            get {
                return this.documentationField;
            }
            set {
                this.documentationField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute("Import")]
        public ImportDirective[] Import {
            get {
                return this.importField;
            }
            set {
                this.importField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute("EnumeratedType", typeof(EnumeratedType))]
        [System.Xml.Serialization.XmlElementAttribute("OpaqueType", typeof(OpaqueType))]
        [System.Xml.Serialization.XmlElementAttribute("StructuredType", typeof(StructuredType))]
        public TypeDescription[] Items {
            get {
                return this.itemsField;
            }
            set {
                this.itemsField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string TargetNamespace {
            get {
                return this.targetNamespaceField;
            }
            set {
                this.targetNamespaceField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public ByteOrder DefaultByteOrder {
            get {
                return this.defaultByteOrderField;
            }
            set {
                this.defaultByteOrderField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool DefaultByteOrderSpecified {
            get {
                return this.defaultByteOrderFieldSpecified;
            }
            set {
                this.defaultByteOrderFieldSpecified = value;
            }
        }
    }
}
