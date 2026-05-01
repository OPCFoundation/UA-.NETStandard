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
using System.Globalization;
using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Reference description
    /// </summary>
    [DataContract(Namespace = Types.Namespaces.OpcUaXsd)]
    public class ReferenceDescription :
        IEncodeable,
        IEquatable<ReferenceDescription>,
        IFormattable
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
            NodeId = default;
            BrowseName = default;
            DisplayName = default;
            NodeClass = NodeClass.Unspecified;
            TypeDefinition = default;
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
            NodeClass = decoder.ReadEnumerated<NodeClass>("NodeClass");
            TypeDefinition = decoder.ReadExpandedNodeId("TypeDefinition");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable? encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not ReferenceDescription value)
            {
                return false;
            }

            if (ReferenceTypeId != value.ReferenceTypeId)
            {
                return false;
            }

            if (IsForward != value.IsForward)
            {
                return false;
            }

            if (NodeId != value.NodeId)
            {
                return false;
            }

            if (BrowseName != value.BrowseName)
            {
                return false;
            }

            if (DisplayName != value.DisplayName)
            {
                return false;
            }

            if (NodeClass != value.NodeClass)
            {
                return false;
            }

            if (TypeDefinition != value.TypeDefinition)
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return IsEqual(obj as IEncodeable);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                ReferenceTypeId,
                IsForward,
                NodeId,
                BrowseName,
                DisplayName,
                NodeClass,
                TypeDefinition);
        }

        /// <inheritdoc/>
        public bool Equals(ReferenceDescription? other)
        {
            return IsEqual(other)!;
        }

        /// <inheritdoc/>
        public static bool operator ==(ReferenceDescription? left, ReferenceDescription? right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(ReferenceDescription? left, ReferenceDescription? right)
        {
            return !(left == right);
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

            clone.ReferenceTypeId = ReferenceTypeId;
            clone.IsForward = CoreUtils.Clone(IsForward);
            clone.NodeId = NodeId;
            clone.BrowseName = BrowseName;
            clone.DisplayName = DisplayName;
            clone.NodeClass = CoreUtils.Clone(NodeClass);
            clone.TypeDefinition = TypeDefinition;

            return clone;
        }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            if (format == null)
            {
                if (!string.IsNullOrEmpty(DisplayName.Text))
                {
                    return DisplayName.Text!;
                }

                if (!BrowseName.IsNull)
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
}
