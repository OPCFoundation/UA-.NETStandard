/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// View description
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class ViewDescription :
        IEncodeable,
        IEquatable<ViewDescription>
    {
        /// <inheritdoc/>
        public ViewDescription()
        {
            ViewId = default;
            Timestamp = DateTimeUtc.MinValue;
            ViewVersion = 0;
        }

        /// <summary>
        /// View id
        /// </summary>
        [DataMember(Name = "ViewId", IsRequired = false, Order = 1)]
        public NodeId ViewId { get; set; }

        /// <summary>
        /// Time stamp
        /// </summary>
        [DataMember(Name = "Timestamp", IsRequired = false, Order = 2)]
        public DateTimeUtc Timestamp { get; set; }

        /// <summary>
        /// View version
        /// </summary>
        [DataMember(Name = "ViewVersion", IsRequired = false, Order = 3)]
        public uint ViewVersion { get; set; }

        /// <summary>
        /// A handle assigned to the item during processing.
        /// </summary>
        [IgnoreDataMember]
        public object? Handle { get; set; }

        /// <inheritdoc/>
        public virtual ExpandedNodeId TypeId => DataTypeIds.ViewDescription;

        /// <inheritdoc/>
        public virtual ExpandedNodeId BinaryEncodingId => ObjectIds.ViewDescription_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public virtual ExpandedNodeId XmlEncodingId => ObjectIds.ViewDescription_Encoding_DefaultXml;

        /// <inheritdoc/>
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteNodeId("ViewId", ViewId);
            encoder.WriteDateTime("Timestamp", Timestamp);
            encoder.WriteUInt32("ViewVersion", ViewVersion);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ViewId = decoder.ReadNodeId("ViewId");
            Timestamp = decoder.ReadDateTime("Timestamp");
            ViewVersion = decoder.ReadUInt32("ViewVersion");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable? encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not ViewDescription value)
            {
                return false;
            }

            if (ViewId != value.ViewId)
            {
                return false;
            }

            if (Timestamp != value.Timestamp)
            {
                return false;
            }

            if (ViewVersion != value.ViewVersion)
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
                ViewId,
                Timestamp,
                ViewVersion);
        }

        /// <inheritdoc/>
        public bool Equals(ViewDescription? other)
        {
            return IsEqual(other)!;
        }

        /// <inheritdoc/>
        public static bool operator ==(ViewDescription? left, ViewDescription? right)
        {
            if (left is null)
            {
                return right is null;
            }
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(ViewDescription? left, ViewDescription? right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return (ViewDescription)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (ViewDescription)base.MemberwiseClone();

            clone.ViewId = ViewId;
            clone.Timestamp = CoreUtils.Clone(Timestamp);
            clone.ViewVersion = CoreUtils.Clone(ViewVersion);

            return clone;
        }

        /// <summary>
        /// Returns true if the view description represents the default (null) view.
        /// </summary>
        public static bool IsDefault(ViewDescription? view)
        {
            if (view == null)
            {
                return true;
            }

            if (view.ViewId.IsNull &&
                view.ViewVersion == 0 &&
                view.Timestamp == DateTimeUtc.MinValue)
            {
                return true;
            }

            return false;
        }
    }
}
