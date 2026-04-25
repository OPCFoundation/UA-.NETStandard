#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    using System.Xml;

    /// <summary>
    /// A dynamic encodeable object that uses the complex type cache to
    /// delegate encoding and decoding its internal state to the description
    /// contained in the cache.
    /// </summary>
    public class StructureValue : IEncodeable, IJsonEncodeable,
        IStructureTypeInfo, IDynamicComplexTypeInstance
    {
        /// <inheritdoc/>
        public ExpandedNodeId TypeId { get; set; }

        /// <inheritdoc/>
        public ExpandedNodeId BinaryEncodingId
            => GetTypeInfo().BinaryEncodingId;

        /// <inheritdoc/>
        public ExpandedNodeId JsonEncodingId
            => GetTypeInfo().JsonEncodingId;

        /// <inheritdoc/>
        public ExpandedNodeId XmlEncodingId
            => GetTypeInfo().XmlEncodingId;

        /// <inheritdoc/>
        public StructureType StructureType
            => GetTypeInfo().StructureDefinition.StructureType;

        /// <summary>
        /// Create a dynamic encodeable object.
        /// </summary>
        /// <param name="typeId"></param>
        public StructureValue(ExpandedNodeId? typeId = null) => TypeId = typeId ?? ExpandedNodeId.Null;

        /// <summary>
        /// Clone the object.
        /// </summary>
        /// <param name="encodeable"></param>
        private StructureValue(StructureValue encodeable) => TypeId = encodeable.TypeId;

        /// <inheritdoc/>
        public object Clone()
        {
            return new StructureValue(this);
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new StructureValue(this);
        }

        /// <inheritdoc/>
        public void Encode(IEncoder encoder)
        {
            if (encoder.Context?.Factory is IDataTypeDescriptionResolver f)
            {
                _typeCache = f;
            }
            var typeInfo = GetTypeInfo();
            encoder.PushNamespace(typeInfo.XmlName.Namespace);
            typeInfo.Encode(encoder, _values);
            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public void Decode(IDecoder decoder)
        {
            if (decoder.Context?.Factory is IDataTypeDescriptionResolver f)
            {
                _typeCache = f;
            }
            var typeInfo = GetTypeInfo();
            decoder.PushNamespace(typeInfo.XmlName.Namespace);
            _values = typeInfo.Decode(decoder);
            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public bool IsEqual(IEncodeable encodeable)
        {
            if (encodeable is not StructureValue dynamicEncodable)
            {
                return false;
            }
            if (_values == null || dynamicEncodable._values == null)
            {
                return _values == dynamicEncodable._values;
            }
            if (dynamicEncodable._values.Length != _values.Length)
            {
                return false;
            }
            for (var i = 0; i < _values.Length; i++)
            {
                if (!Utils.IsEqual(_values[i], dynamicEncodable._values[i]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <inheritdoc/>
        public XmlQualifiedName GetXmlName(IServiceMessageContext context)
        {
            if (context.Factory is IDataTypeDescriptionResolver f)
            {
                _typeCache = f;
            }
            return GetTypeInfo().XmlName;
        }

        /// <summary>
        /// Get type info
        /// </summary>
        /// <returns></returns>
        private StructureDescription GetTypeInfo()
        {
            return _typeCache?.GetStructureDescription(TypeId) ??
                StructureDescription.Null;
        }

        private object?[]? _values;
        private IDataTypeDescriptionResolver? _typeCache;
    }
}
#endif
