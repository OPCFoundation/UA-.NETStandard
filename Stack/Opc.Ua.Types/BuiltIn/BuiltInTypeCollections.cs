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
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Linq;
using Microsoft.Win32;

namespace Opc.Ua
{
    /// <summary>
    /// A collection of UInt32 values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfUInt32",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "UInt32")]
    public class UInt32Collection : List<uint>, ICloneable
    {
        /// <inheritdoc/>
        public UInt32Collection()
        {
        }

        /// <inheritdoc/>
        public UInt32Collection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public UInt32Collection(IEnumerable<uint> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static UInt32Collection ToUInt32Collection(ArrayOf<uint> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator UInt32Collection(ArrayOf<uint> values)
        {
            return ToUInt32Collection(values);
        }

        /// <inheritdoc/>
        public static explicit operator UInt32Collection(uint[] values)
        {
            return ToUInt32Collection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new UInt32Collection(this);
        }
    }

    /// <summary>
    /// A collection of DiagnosticInfo objects.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfDiagnosticInfo",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "DiagnosticInfo")]
    public class DiagnosticInfoCollection : List<DiagnosticInfo>, ICloneable
    {
        /// <inheritdoc/>
        public DiagnosticInfoCollection()
        {
        }

        /// <inheritdoc/>
        public DiagnosticInfoCollection(IEnumerable<DiagnosticInfo> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public DiagnosticInfoCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static DiagnosticInfoCollection ToDiagnosticInfoCollection(ArrayOf<DiagnosticInfo> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator DiagnosticInfoCollection(ArrayOf<DiagnosticInfo> values)
        {
            return ToDiagnosticInfoCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new DiagnosticInfoCollection(Count);

            foreach (DiagnosticInfo element in this)
            {
                clone.Add(CoreUtils.Clone(element));
            }

            return clone;
        }
    }

    /// <summary>
    /// A collection of StatusCodes.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfStatusCode",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "StatusCode")]
    public class StatusCodeCollection : List<StatusCode>, ICloneable
    {
        /// <inheritdoc/>
        public StatusCodeCollection()
        {
        }

        /// <inheritdoc/>
        public StatusCodeCollection(IEnumerable<StatusCode> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public StatusCodeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static StatusCodeCollection ToStatusCodeCollection(ArrayOf<StatusCode> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator StatusCodeCollection(ArrayOf<StatusCode> values)
        {
            return ToStatusCodeCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator StatusCodeCollection(StatusCode[] values)
        {
            return ToStatusCodeCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new StatusCodeCollection(this);
        }
    }

    /// <summary>
    /// A collection of Uuids.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfGuid",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Guid")]
    public class UuidCollection : List<Uuid>, ICloneable
    {
        /// <inheritdoc/>
        public UuidCollection()
        {
        }

        /// <inheritdoc/>
        public UuidCollection(IEnumerable<Uuid> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public UuidCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static UuidCollection ToUuidCollection(ArrayOf<Uuid> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static UuidCollection ToUuidCollection(ArrayOf<Guid> values)
        {
            return new UuidCollection(values.ToList().ConvertAll(g => new Uuid(g)));
        }

        /// <inheritdoc/>
        public static explicit operator UuidCollection(ArrayOf<Guid> values)
        {
            return ToUuidCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator ArrayOf<Guid>(UuidCollection values)
        {
            return values != null ? [.. values.Select(g => g.Guid)] : [];
        }

        /// <inheritdoc/>
        public static explicit operator UuidCollection(Guid[] values)
        {
            return ToUuidCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator UuidCollection(ArrayOf<Uuid> values)
        {
            return ToUuidCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator UuidCollection(Uuid[] values)
        {
            return ToUuidCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new UuidCollection(this);
        }
    }

    /// <summary>
    /// A collection of DateTime values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfDateTime",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "DateTime")]
    public class DateTimeCollection : List<DateTimeUtc>, ICloneable
    {
        /// <inheritdoc/>
        public DateTimeCollection()
        {
        }

        /// <inheritdoc/>
        public DateTimeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public DateTimeCollection(IEnumerable<DateTimeUtc> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static DateTimeCollection ToDateTimeCollection(ArrayOf<DateTimeUtc> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator DateTimeCollection(ArrayOf<DateTimeUtc> values)
        {
            return ToDateTimeCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator DateTimeCollection(DateTimeUtc[] values)
        {
            return ToDateTimeCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new DateTimeCollection(this);
        }
    }

    /// <summary>
    /// A collection of Variant objects.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfVariant",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Variant")]
    public class VariantCollection : List<Variant>, ICloneable
    {
        /// <inheritdoc/>
        public VariantCollection()
        {
        }

        /// <inheritdoc/>
        public VariantCollection(IEnumerable<Variant> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public VariantCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static VariantCollection ToVariantCollection(ArrayOf<Variant> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator VariantCollection(ArrayOf<Variant> values)
        {
            return ToVariantCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator VariantCollection(Variant[] values)
        {
            return ToVariantCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new VariantCollection(this);
        }
    }

    /// <summary>
    /// A collection of XmlElement values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfXmlElement",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "XmlElement")]
    public class XmlElementCollection : List<XmlElement>, ICloneable
    {
        /// <inheritdoc/>
        public XmlElementCollection()
        {
        }

        /// <inheritdoc/>
        public XmlElementCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public XmlElementCollection(IEnumerable<XmlElement> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public XmlElementCollection(IEnumerable<System.Xml.XmlElement> collection)
            : this(collection.Select(x => XmlElement.From(x)))
        {
        }

        /// <inheritdoc/>
        public static XmlElementCollection ToXmlElementCollection(ArrayOf<XmlElement> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator XmlElementCollection(ArrayOf<XmlElement> values)
        {
            return ToXmlElementCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator XmlElementCollection(XmlElement[] values)
        {
            return ToXmlElementCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new XmlElementCollection(this);
        }
    }

    /// <summary>
    /// List of expanded node ids
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfExpandedNodeId",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "ExpandedNodeId")]
    public class ExpandedNodeIdCollection : List<ExpandedNodeId>, ICloneable
    {
        /// <inheritdoc/>
        public ExpandedNodeIdCollection()
        {
        }

        /// <inheritdoc/>
        public ExpandedNodeIdCollection(IEnumerable<ExpandedNodeId> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public ExpandedNodeIdCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static ExpandedNodeIdCollection ToExpandedNodeIdCollection(ArrayOf<ExpandedNodeId> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator ExpandedNodeIdCollection(ArrayOf<ExpandedNodeId> values)
        {
            return ToExpandedNodeIdCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator ExpandedNodeIdCollection(ExpandedNodeId[] values)
        {
            return ToExpandedNodeIdCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new ExpandedNodeIdCollection(this);
        }
    }

    /// <summary>
    /// A collection of NodeIds.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfNodeId",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "NodeId")]
    public class NodeIdCollection : List<NodeId>, ICloneable
    {
        /// <inheritdoc/>
        public NodeIdCollection()
        {
        }

        /// <inheritdoc/>
        public NodeIdCollection(IEnumerable<NodeId> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public NodeIdCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static NodeIdCollection ToNodeIdCollection(ArrayOf<NodeId> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator NodeIdCollection(ArrayOf<NodeId> values)
        {
            return ToNodeIdCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator NodeIdCollection(NodeId[] values)
        {
            return ToNodeIdCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new NodeIdCollection(this);
        }
    }

    /// <summary>
    /// A collection of DataValues.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfDataValue",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "DataValue")]
    public class DataValueCollection : List<DataValue>, ICloneable
    {
        /// <inheritdoc/>
        public DataValueCollection()
        {
        }

        /// <inheritdoc/>
        public DataValueCollection(IEnumerable<DataValue> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public DataValueCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static DataValueCollection ToDataValueCollection(ArrayOf<DataValue> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator DataValueCollection(ArrayOf<DataValue> values)
        {
            return ToDataValueCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator DataValueCollection(DataValue[] values)
        {
            return ToDataValueCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new DataValueCollection(Count);

            foreach (DataValue element in this)
            {
                clone.Add(CoreUtils.Clone(element));
            }

            return clone;
        }
    }

    /// <summary>
    /// A collection of QualifiedName objects.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfQualifiedName",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "QualifiedName")]
    public class QualifiedNameCollection : List<QualifiedName>, ICloneable
    {
        /// <inheritdoc/>
        public QualifiedNameCollection()
        {
        }

        /// <inheritdoc/>
        public QualifiedNameCollection(IEnumerable<QualifiedName> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public QualifiedNameCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static QualifiedNameCollection ToQualifiedNameCollection(ArrayOf<QualifiedName> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator QualifiedNameCollection(ArrayOf<QualifiedName> values)
        {
            return ToQualifiedNameCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator QualifiedNameCollection(QualifiedName[] values)
        {
            return ToQualifiedNameCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new QualifiedNameCollection(this);
        }
    }

    /// <summary>
    /// A strongly-typed collection of LocalizedText objects.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfLocalizedText",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "LocalizedText")]
    public class LocalizedTextCollection : List<LocalizedText>, ICloneable
    {
        /// <inheritdoc/>
        public LocalizedTextCollection()
        {
        }

        /// <inheritdoc/>
        public LocalizedTextCollection(IEnumerable<LocalizedText> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public LocalizedTextCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static LocalizedTextCollection ToLocalizedTextCollection(ArrayOf<LocalizedText> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator LocalizedTextCollection(ArrayOf<LocalizedText> values)
        {
            return ToLocalizedTextCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator LocalizedTextCollection(LocalizedText[] values)
        {
            return ToLocalizedTextCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new LocalizedTextCollection(this);
        }
    }

    /// <summary>
    /// A collection of ByteString values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfByteString",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "ByteString")]
    public class ByteStringCollection : List<ByteString>, ICloneable
    {
        /// <inheritdoc/>
        public ByteStringCollection()
        {
        }

        /// <inheritdoc/>
        public ByteStringCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public ByteStringCollection(IEnumerable<ByteString> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static ByteStringCollection ToByteStringCollection(ArrayOf<ByteString> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator ByteStringCollection(ArrayOf<ByteString> values)
        {
            return ToByteStringCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator ByteStringCollection(ByteString[] values)
        {
            return ToByteStringCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new ByteStringCollection(this);
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

        /// <inheritdoc/>
        public static ExtensionObjectCollection ToExtensionObjectCollection(ArrayOf<ExtensionObject> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator ExtensionObjectCollection(ArrayOf<ExtensionObject> values)
        {
            return ToExtensionObjectCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator ExtensionObjectCollection(ExtensionObject[] values)
        {
            return ToExtensionObjectCollection(values);
        }

        /// <summary>
        /// TODO Remove
        /// </summary>
        /// <param name="encodeables"></param>
        /// <returns></returns>
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
            return new ExtensionObjectCollection(this);
        }
    }

    /// <summary>
    /// Browse description collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfBrowseDescription",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "BrowseDescription")]
    public class BrowseDescriptionCollection : List<BrowseDescription>, ICloneable
    {
        /// <inheritdoc/>
        public BrowseDescriptionCollection()
        {
        }

        /// <inheritdoc/>
        public BrowseDescriptionCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public BrowseDescriptionCollection(IEnumerable<BrowseDescription> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static BrowseDescriptionCollection ToBrowseDescriptionCollection(ArrayOf<BrowseDescription> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator BrowseDescriptionCollection(ArrayOf<BrowseDescription> values)
        {
            return ToBrowseDescriptionCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator BrowseDescriptionCollection(BrowseDescription[] values)
        {
            return ToBrowseDescriptionCollection(values);
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (BrowseDescriptionCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new BrowseDescriptionCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// Argument collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfArgument",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Argument")]
    public class ArgumentCollection : List<Argument>, ICloneable
    {
        /// <inheritdoc/>
        public ArgumentCollection()
        {
        }

        /// <inheritdoc/>
        public ArgumentCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public ArgumentCollection(IEnumerable<Argument> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static ArgumentCollection ToArgumentCollection(ArrayOf<Argument> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator ArgumentCollection(ArrayOf<Argument> values)
        {
            return ToArgumentCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator ArgumentCollection(Argument[] values)
        {
            return ToArgumentCollection(values);
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (ArgumentCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new ArgumentCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// Structure definition collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfStructureDefinition",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "StructureDefinition")]
    public class StructureDefinitionCollection : List<StructureDefinition>, ICloneable
    {
        /// <inheritdoc/>
        public StructureDefinitionCollection()
        {
        }

        /// <inheritdoc/>
        public StructureDefinitionCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public StructureDefinitionCollection(IEnumerable<StructureDefinition> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static StructureDefinitionCollection ToStructureDefinitionCollection(ArrayOf<StructureDefinition> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator StructureDefinitionCollection(ArrayOf<StructureDefinition> values)
        {
            return ToStructureDefinitionCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator StructureDefinitionCollection(StructureDefinition[] values)
        {
            return ToStructureDefinitionCollection(values);
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (StructureDefinitionCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new StructureDefinitionCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// List of EnumField objects
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfEnumField",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "EnumField")]
    public class EnumFieldCollection : List<EnumField>, ICloneable
    {
        /// <inheritdoc/>
        public EnumFieldCollection()
        {
        }

        /// <inheritdoc/>
        public EnumFieldCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public EnumFieldCollection(IEnumerable<EnumField> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static EnumFieldCollection ToEnumFieldCollection(ArrayOf<EnumField> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator EnumFieldCollection(ArrayOf<EnumField> values)
        {
            return ToEnumFieldCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator EnumFieldCollection(EnumField[] values)
        {
            return ToEnumFieldCollection(values);
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (EnumFieldCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new EnumFieldCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// Structure field collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfStructureField",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "StructureField")]
    public class StructureFieldCollection : List<StructureField>, ICloneable
    {
        /// <inheritdoc/>
        public StructureFieldCollection()
        {
        }

        /// <inheritdoc/>
        public StructureFieldCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public StructureFieldCollection(IEnumerable<StructureField> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static StructureFieldCollection ToStructureFieldCollection(ArrayOf<StructureField> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator StructureFieldCollection(ArrayOf<StructureField> values)
        {
            return ToStructureFieldCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator StructureFieldCollection(StructureField[] values)
        {
            return ToStructureFieldCollection(values);
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (StructureFieldCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new StructureFieldCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// List of enum value types
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfEnumValueType",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "EnumValueType")]
    public class EnumValueTypeCollection : List<EnumValueType>, ICloneable
    {
        /// <inheritdoc/>
        public EnumValueTypeCollection()
        {
        }

        /// <inheritdoc/>
        public EnumValueTypeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public EnumValueTypeCollection(IEnumerable<EnumValueType> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static EnumValueTypeCollection ToEnumValueTypeCollection(ArrayOf<EnumValueType> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator EnumValueTypeCollection(ArrayOf<EnumValueType> values)
        {
            return ToEnumValueTypeCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator EnumValueTypeCollection(EnumValueType[] values)
        {
            return ToEnumValueTypeCollection(values);
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (EnumValueTypeCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new EnumValueTypeCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// Id type collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfIdType",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "IdType")]
    public class IdTypeCollection : List<IdType>, ICloneable
    {
        /// <inheritdoc/>
        public IdTypeCollection()
        {
        }

        /// <inheritdoc/>
        public IdTypeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public IdTypeCollection(IEnumerable<IdType> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static IdTypeCollection ToIdTypeCollection(ArrayOf<IdType> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator IdTypeCollection(ArrayOf<IdType> values)
        {
            return ToIdTypeCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator IdTypeCollection(IdType[] values)
        {
            return ToIdTypeCollection(values);
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (IdTypeCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new IdTypeCollection(this);
        }
    }

    /// <summary>
    /// Data type definition collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfDataTypeDefinition",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "DataTypeDefinition")]
    public class DataTypeDefinitionCollection : List<DataTypeDefinition>, ICloneable
    {
        /// <inheritdoc/>
        public DataTypeDefinitionCollection()
        {
        }

        /// <inheritdoc/>
        public DataTypeDefinitionCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public DataTypeDefinitionCollection(IEnumerable<DataTypeDefinition> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static DataTypeDefinitionCollection ToDataTypeDefinitionCollection(ArrayOf<DataTypeDefinition> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator DataTypeDefinitionCollection(ArrayOf<DataTypeDefinition> values)
        {
            return ToDataTypeDefinitionCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator DataTypeDefinitionCollection(DataTypeDefinition[] values)
        {
            return ToDataTypeDefinitionCollection(values);
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (DataTypeDefinitionCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new DataTypeDefinitionCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// Role permission collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfRolePermissionType",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "RolePermissionType")]
    public class RolePermissionTypeCollection : List<RolePermissionType>, ICloneable
    {
        /// <inheritdoc/>
        public RolePermissionTypeCollection()
        {
        }

        /// <inheritdoc/>
        public RolePermissionTypeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public RolePermissionTypeCollection(IEnumerable<RolePermissionType> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static RolePermissionTypeCollection ToRolePermissionTypeCollection(ArrayOf<RolePermissionType> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator RolePermissionTypeCollection(ArrayOf<RolePermissionType> values)
        {
            return ToRolePermissionTypeCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator RolePermissionTypeCollection(RolePermissionType[] values)
        {
            return ToRolePermissionTypeCollection(values);
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (RolePermissionTypeCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new RolePermissionTypeCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// Reference description collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfReferenceDescription",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "ReferenceDescription")]
    public class ReferenceDescriptionCollection : List<ReferenceDescription>, ICloneable
    {
        /// <inheritdoc/>
        public ReferenceDescriptionCollection()
        {
        }

        /// <inheritdoc/>
        public ReferenceDescriptionCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public ReferenceDescriptionCollection(IEnumerable<ReferenceDescription> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static ReferenceDescriptionCollection ToReferenceDescriptionCollection(ArrayOf<ReferenceDescription> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator ReferenceDescriptionCollection(ArrayOf<ReferenceDescription> values)
        {
            return ToReferenceDescriptionCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator ReferenceDescriptionCollection(ReferenceDescription[] values)
        {
            return ToReferenceDescriptionCollection(values);
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (ReferenceDescriptionCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new ReferenceDescriptionCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// List of RelativePathElement objects
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfRelativePathElement",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "RelativePathElement")]
    public class RelativePathElementCollection : List<RelativePathElement>, ICloneable
    {
        /// <inheritdoc/>
        public RelativePathElementCollection()
        {
        }

        /// <inheritdoc/>
        public RelativePathElementCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public RelativePathElementCollection(IEnumerable<RelativePathElement> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static RelativePathElementCollection ToRelativePathElementCollection(ArrayOf<RelativePathElement> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator RelativePathElementCollection(ArrayOf<RelativePathElement> values)
        {
            return ToRelativePathElementCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator RelativePathElementCollection(RelativePathElement[] values)
        {
            return ToRelativePathElementCollection(values);
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (RelativePathElementCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new RelativePathElementCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// Enum definition collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfEnumDefinition",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "EnumDefinition")]
    public class EnumDefinitionCollection : List<EnumDefinition>, ICloneable
    {
        /// <inheritdoc/>
        public EnumDefinitionCollection()
        {
        }

        /// <inheritdoc/>
        public EnumDefinitionCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public EnumDefinitionCollection(IEnumerable<EnumDefinition> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static EnumDefinitionCollection ToEnumDefinitionCollection(ArrayOf<EnumDefinition> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator EnumDefinitionCollection(ArrayOf<EnumDefinition> values)
        {
            return ToEnumDefinitionCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator EnumDefinitionCollection(EnumDefinition[] values)
        {
            return ToEnumDefinitionCollection(values);
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (EnumDefinitionCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new EnumDefinitionCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// Node collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfNode",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Node")]
    public class NodeCollection : List<Node>, ICloneable
    {
        /// <inheritdoc/>
        public NodeCollection()
        {
        }

        /// <inheritdoc/>
        public NodeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public NodeCollection(IEnumerable<Node> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static NodeCollection ToNodeCollection(ArrayOf<Node> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator NodeCollection(ArrayOf<Node> values)
        {
            return ToNodeCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator NodeCollection(Node[] values)
        {
            return ToNodeCollection(values);
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (NodeCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new NodeCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// Reference node collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfReferenceNode",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "ReferenceNode")]
    public class ReferenceNodeCollection : List<ReferenceNode>, ICloneable
    {
        /// <inheritdoc/>
        public ReferenceNodeCollection()
        {
        }

        /// <inheritdoc/>
        public ReferenceNodeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public ReferenceNodeCollection(IEnumerable<ReferenceNode> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static ReferenceNodeCollection ToReferenceNodeCollection(ArrayOf<ReferenceNode> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static explicit operator ReferenceNodeCollection(ArrayOf<ReferenceNode> values)
        {
            return ToReferenceNodeCollection(values);
        }

        /// <inheritdoc/>
        public static explicit operator ReferenceNodeCollection(ReferenceNode[] values)
        {
            return ToReferenceNodeCollection(values);
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (ReferenceNodeCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new ReferenceNodeCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }
}
