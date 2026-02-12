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
using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// View description
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class ViewDescription : IEncodeable, IJsonEncodeable
    {
        /// <inheritdoc/>
        public ViewDescription()
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
            ViewId = default;
            Timestamp = DateTime.MinValue;
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
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// View version
        /// </summary>
        [DataMember(Name = "ViewVersion", IsRequired = false, Order = 3)]
        public uint ViewVersion { get; set; }

        /// <summary>
        /// A handle assigned to the item during processing.
        /// </summary>
        [IgnoreDataMember]
        public object Handle { get; set; }

        /// <inheritdoc/>
        public virtual ExpandedNodeId TypeId => DataTypeIds.ViewDescription;

        /// <inheritdoc/>
        public virtual ExpandedNodeId BinaryEncodingId => ObjectIds.ViewDescription_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public virtual ExpandedNodeId XmlEncodingId => ObjectIds.ViewDescription_Encoding_DefaultXml;

        /// <inheritdoc/>
        public virtual ExpandedNodeId JsonEncodingId => ObjectIds.ViewDescription_Encoding_DefaultJson;

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
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not ViewDescription value)
            {
                return false;
            }

            if (!CoreUtils.IsEqual(ViewId, value.ViewId))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(Timestamp, value.Timestamp))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(ViewVersion, value.ViewVersion))
            {
                return false;
            }

            return true;
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
        public static bool IsDefault(ViewDescription view)
        {
            if (view == null)
            {
                return true;
            }

            if (view.ViewId.IsNull &&
                view.ViewVersion == 0 &&
                view.Timestamp == DateTime.MinValue)
            {
                return true;
            }

            return false;
        }
    }
}
