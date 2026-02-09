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
using System.Globalization;
using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Reference description
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class ReferenceDescription : IEncodeable, IJsonEncodeable, IFormattable
    {
        /// <inheritdoc/>
        public ReferenceDescription()
        {
            Initialize();
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        private void Initialize()
        {
            ReferenceTypeId = default;
            IsForward = true;
            NodeId = null;
            BrowseName = null;
            DisplayName = null;
            NodeClass = NodeClass.Unspecified;
            TypeDefinition = null;
        }

        /// <summary>
        /// Reference Type Id
        /// </summary>
        [DataMember(Name = "ReferenceTypeId", IsRequired = false, Order = 1)]
        public NodeId ReferenceTypeId { get; set; }

        /// <summary>
        /// Is Forward reference
        /// </summary>
        [DataMember(Name = "IsForward", IsRequired = false, Order = 2)]
        public bool IsForward { get; set; }

        /// <summary>
        /// Node Id
        /// </summary>
        [DataMember(Name = "NodeId", IsRequired = false, Order = 3)]
        public ExpandedNodeId NodeId { get; set; }

        /// <summary>
        /// Browse name
        /// </summary>
        [DataMember(Name = "BrowseName", IsRequired = false, Order = 4)]
        public QualifiedName BrowseName { get; set; }

        /// <summary>
        /// Display name
        /// </summary>
        [DataMember(Name = "DisplayName", IsRequired = false, Order = 5)]
        public LocalizedText DisplayName { get; set; }

        /// <summary>
        /// Node class
        /// </summary>
        [DataMember(Name = "NodeClass", IsRequired = false, Order = 6)]
        public NodeClass NodeClass { get; set; }

        /// <summary>
        /// Type definition
        /// </summary>
        [DataMember(Name = "TypeDefinition", IsRequired = false, Order = 7)]
        public ExpandedNodeId TypeDefinition { get; set; }

        /// <inheritdoc/>
        public virtual ExpandedNodeId TypeId => DataTypeIds.ReferenceDescription;

        /// <inheritdoc/>
        public virtual ExpandedNodeId BinaryEncodingId => ObjectIds.ReferenceDescription_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public virtual ExpandedNodeId XmlEncodingId => ObjectIds.ReferenceDescription_Encoding_DefaultXml;

        /// <inheritdoc/>
        public virtual ExpandedNodeId JsonEncodingId => ObjectIds.ReferenceDescription_Encoding_DefaultJson;

        /// <inheritdoc/>
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteNodeId("ReferenceTypeId", ReferenceTypeId);
            encoder.WriteBoolean("IsForward", IsForward);
            encoder.WriteExpandedNodeId("NodeId", NodeId);
            encoder.WriteQualifiedName("BrowseName", BrowseName);
            encoder.WriteLocalizedText("DisplayName", DisplayName);
            encoder.WriteEnumerated("NodeClass", NodeClass);
            encoder.WriteExpandedNodeId("TypeDefinition", TypeDefinition);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ReferenceTypeId = decoder.ReadNodeId("ReferenceTypeId");
            IsForward = decoder.ReadBoolean("IsForward");
            NodeId = decoder.ReadExpandedNodeId("NodeId");
            BrowseName = decoder.ReadQualifiedName("BrowseName");
            DisplayName = decoder.ReadLocalizedText("DisplayName");
            NodeClass = (NodeClass)decoder.ReadEnumerated("NodeClass", typeof(NodeClass));
            TypeDefinition = decoder.ReadExpandedNodeId("TypeDefinition");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not ReferenceDescription value)
            {
                return false;
            }

            if (!CoreUtils.IsEqual(ReferenceTypeId, value.ReferenceTypeId))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(IsForward, value.IsForward))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(NodeId, value.NodeId))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(BrowseName, value.BrowseName))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(DisplayName, value.DisplayName))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(NodeClass, value.NodeClass))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(TypeDefinition, value.TypeDefinition))
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return (ReferenceDescription)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (ReferenceDescription)base.MemberwiseClone();

            clone.ReferenceTypeId = CoreUtils.Clone(ReferenceTypeId);
            clone.IsForward = (bool)CoreUtils.Clone(IsForward);
            clone.NodeId = CoreUtils.Clone(NodeId);
            clone.BrowseName = CoreUtils.Clone(BrowseName);
            clone.DisplayName = CoreUtils.Clone(DisplayName);
            clone.NodeClass = (NodeClass)CoreUtils.Clone(NodeClass);
            clone.TypeDefinition = CoreUtils.Clone(TypeDefinition);

            return clone;
        }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                if (DisplayName != null && !string.IsNullOrEmpty(DisplayName.Text))
                {
                    return DisplayName.Text;
                }

                if (!QualifiedName.IsNull(BrowseName))
                {
                    return BrowseName.Name;
                }

                return CoreUtils.Format(
                    "(unknown {0})",
                    NodeClass.ToString().ToLower(CultureInfo.InvariantCulture));
            }

            throw new FormatException(CoreUtils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// True if the reference filter has not been applied.
        /// </summary>
        public bool Unfiltered { get; set; }
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
        public static implicit operator ReferenceDescriptionCollection(ReferenceDescription[] values)
        {
            if (values != null)
            {
                return [.. values];
            }
            return [];
        }

        /// <inheritdoc/>
        public static explicit operator ReferenceDescription[](ReferenceDescriptionCollection values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return null;
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
}
