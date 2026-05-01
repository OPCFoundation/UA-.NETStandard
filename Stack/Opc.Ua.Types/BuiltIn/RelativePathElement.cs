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
    /// An element of a relative path
    /// </summary>
    [DataContract(Namespace = Types.Namespaces.OpcUaXsd)]
    public class RelativePathElement :
        IEncodeable,
        IEquatable<RelativePathElement>
    {
        /// <inheritdoc/>
        public RelativePathElement()
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
            IsInverse = true;
            IncludeSubtypes = true;
        }

        /// <summary>
        /// Reference type id
        /// </summary>
        [DataMember(Name = "ReferenceTypeId", IsRequired = false, Order = 1)]
        public NodeId ReferenceTypeId { get; set; }

        /// <summary>
        /// Is inverse
        /// </summary>
        [DataMember(Name = "IsInverse", IsRequired = false, Order = 2)]
        public bool IsInverse { get; set; }

        /// <summary>
        /// Include sub types
        /// </summary>
        [DataMember(Name = "IncludeSubtypes", IsRequired = false, Order = 3)]
        public bool IncludeSubtypes { get; set; }

        /// <summary>
        /// Target name
        /// </summary>
        [DataMember(Name = "TargetName", IsRequired = false, Order = 4)]
        public QualifiedName TargetName { get; set; }

        /// <inheritdoc/>
        public virtual ExpandedNodeId TypeId => DataTypeIds.RelativePathElement;

        /// <inheritdoc/>
        public virtual ExpandedNodeId BinaryEncodingId => ObjectIds.RelativePathElement_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public virtual ExpandedNodeId XmlEncodingId => ObjectIds.RelativePathElement_Encoding_DefaultXml;

        /// <inheritdoc/>
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteNodeId("ReferenceTypeId", ReferenceTypeId);
            encoder.WriteBoolean("IsInverse", IsInverse);
            encoder.WriteBoolean("IncludeSubtypes", IncludeSubtypes);
            encoder.WriteQualifiedName("TargetName", TargetName);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ReferenceTypeId = decoder.ReadNodeId("ReferenceTypeId");
            IsInverse = decoder.ReadBoolean("IsInverse");
            IncludeSubtypes = decoder.ReadBoolean("IncludeSubtypes");
            TargetName = decoder.ReadQualifiedName("TargetName");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable? encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not RelativePathElement value)
            {
                return false;
            }

            if (ReferenceTypeId != value.ReferenceTypeId)
            {
                return false;
            }

            if (IsInverse != value.IsInverse)
            {
                return false;
            }

            if (IncludeSubtypes != value.IncludeSubtypes)
            {
                return false;
            }

            if (TargetName != value.TargetName)
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
            return HashCode.Combine(ReferenceTypeId, IsInverse, IncludeSubtypes, TargetName);
        }

        /// <inheritdoc/>
        public bool Equals(RelativePathElement? other)
        {
            return IsEqual(other)!;
        }

        /// <inheritdoc/>
        public static bool operator ==(RelativePathElement? left, RelativePathElement? right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(RelativePathElement? left, RelativePathElement? right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return (RelativePathElement)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (RelativePathElement)base.MemberwiseClone();

            clone.ReferenceTypeId = ReferenceTypeId;
            clone.IsInverse = CoreUtils.Clone(IsInverse);
            clone.IncludeSubtypes = CoreUtils.Clone(IncludeSubtypes);
            clone.TargetName = TargetName;

            return clone;
        }
    }
}
