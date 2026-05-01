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
using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Enum definition
    /// </summary>
    [DataContract(Namespace = Types.Namespaces.OpcUaXsd)]
    public class EnumDefinition : DataTypeDefinition, IEquatable<EnumDefinition>
    {
        /// <summary>
        /// Fields
        /// </summary>
        [DataMember(Name = "Fields", IsRequired = false, Order = 1)]
        public ArrayOf<EnumField> Fields { get; set; }

        /// <summary>
        /// If TRUE the values are bit positions rather than values.
        /// </summary>
        public bool IsOptionSet { get; set; }

        /// <inheritdoc/>
        public override ExpandedNodeId TypeId
            => DataTypeIds.EnumDefinition;

        /// <inheritdoc/>
        public override ExpandedNodeId BinaryEncodingId
            => ObjectIds.EnumDefinition_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public override ExpandedNodeId XmlEncodingId
            => ObjectIds.EnumDefinition_Encoding_DefaultXml;

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteEncodeableArray("Fields", Fields);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            Fields = decoder.ReadEncodeableArray<EnumField>("Fields");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override bool IsEqual(IEncodeable? encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not EnumDefinition value)
            {
                return false;
            }

            if (Fields != value.Fields)
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
            return HashCode.Combine(base.GetHashCode(), Fields);
        }

        /// <inheritdoc/>
        public bool Equals(EnumDefinition? other)
        {
            return IsEqual(other)!;
        }

        /// <inheritdoc/>
        public static bool operator ==(EnumDefinition? left, EnumDefinition? right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(EnumDefinition? left, EnumDefinition? right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            return (EnumDefinition)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (EnumDefinition)base.MemberwiseClone();

            clone.Fields = CoreUtils.Clone(Fields);

            return clone;
        }
    }
}
