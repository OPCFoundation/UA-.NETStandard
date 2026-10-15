/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;

namespace UaLens.Plugins.PubSub
{
    /// <summary>
    /// A bounded copy of received metadata, never a retained arbitrary type graph.
    /// Applying it creates read-only intent and clears all UA mappings.
    /// </summary>
    internal sealed record PubSubMetadataSchema(
        PublisherIdType PublisherType,
        ulong PublisherNumber,
        string PublisherName,
        ushort WriterGroupId,
        ushort DataSetWriterId,
        Guid DataSetClassId,
        uint MajorVersion,
        uint MinorVersion,
        ArrayOf<PubSubFieldConfiguration> Fields,
        string Prerequisite)
    {
        public ArrayOf<string> Namespaces { get; init; } = [];

        public ArrayOf<PubSubStructureConfiguration> Structures { get; init; } = [];

        public bool CanApply => Prerequisite.Length == 0;

        public PubSubConfiguration Apply(PubSubConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (!CanApply)
            {
                throw new InvalidOperationException(Prerequisite);
            }
            if (WriterGroupId == 0 && !configuration.IsJson)
            {
                throw new InvalidOperationException(
                    "Metadata without a writer-group ID cannot configure a UADP reader.");
            }
            PubSubConfiguration result = configuration with
            {
                PublisherFilterType = PublisherType,
                PublisherFilter = PublisherNumber,
                PublisherFilterName = PublisherName,
                WriterGroupId = WriterGroupId == 0 ? configuration.WriterGroupId : WriterGroupId,
                DataSetWriterId = DataSetWriterId,
                DataSetClassId = DataSetClassId,
                MetadataMajorVersion = MajorVersion,
                MetadataMinorVersion = MinorVersion,
                Fields = Fields,
                Namespaces = Namespaces,
                Structures = Structures,
                ReceiveEnabled = true,
                Publication = PubSubPublication.Disabled,
                WriteBackEnabled = false,
                ActionResponderEnabled = false,
                AdapterProviderId = string.Empty,
                ActionObjectNodeId = string.Empty,
                ActionMethodNodeId = string.Empty
            };
            PubSubConfigurationValidation.RequireValid(result, requireEndpoint: false);
            return result;
        }

        public static PubSubMetadataSchema Create(in DataSetMetaDataKey key, DataSetMetaDataType metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            PublisherId publisher = key.PublisherId;
            ulong number = 0;
            string name = string.Empty;
            if (publisher.TryGetByte(out byte byteId))
            {
                number = byteId;
            }
            else if (publisher.TryGetUInt16(out ushort shortId))
            {
                number = shortId;
            }
            else if (publisher.TryGetUInt32(out uint intId))
            {
                number = intId;
            }
            else if (publisher.TryGetUInt64(out ulong longId))
            {
                number = longId;
            }
            else if (publisher.TryGetString(out string? stringId))
            {
                name = stringId ?? string.Empty;
            }
            else if (publisher.TryGetGuid(out Guid guidId))
            {
                name = guidId.ToString("D");
            }
            string prerequisite = string.Empty;
            var fields = new List<PubSubFieldConfiguration>();
            ArrayOf<PubSubStructureConfiguration> structures = [];
            if (!PubSubIdentity.TryCreate(
                publisher.Type, number, name, publisher.Type == PublisherIdType.Guid, out _) ||
                key.DataSetWriterId == 0 ||
                metadata.Fields.Count is < 1 or > PubSubConfigurationValidation.MaxFields)
            {
                prerequisite = "Metadata requires bounded, nonzero identities and between 1 and 32 fields.";
            }
            else
            {
                try
                {
                    structures = PubSubStructureSchema.Capture(metadata);
                    NamespaceTable namespaces = PubSubStructureSchema.NamespaceTable(
                        new PubSubConfiguration { Namespaces = metadata.Namespaces });
                    foreach (FieldMetaData field in metadata.Fields)
                    {
                        if (field is null ||
                            string.IsNullOrWhiteSpace(field.Name) ||
                            !PubSubFieldSchema.IsValidShape(field.ValueRank, field.ArrayDimensions) ||
                            (!PubSubConfigurationValidation.IsScalarType((BuiltInType)field.BuiltInType) &&
                                field.BuiltInType != (byte)BuiltInType.ExtensionObject) ||
                            field.MaxStringLength > PubSubConfigurationValidation.MaxValueCharacters ||
                            field.FieldFlags != 0)
                        {
                            throw new ArgumentException(
                                "Invalid dimensions, promoted fields or unsupported field metadata.");
                        }
                        fields.Add(new PubSubFieldConfiguration
                        {
                            Name = field.Name,
                            Type = (BuiltInType)field.BuiltInType,
                            DataTypeId = field.DataType == new NodeId(field.BuiltInType) ? string.Empty
                                : PubSubStructureSchema.Portable(field.DataType, namespaces),
                            FieldId = (Guid)field.DataSetFieldId,
                            ValueRank = field.ValueRank,
                            ArrayDimensions = CoreUtils.Clone(field.ArrayDimensions),
                            MaxStringLength = field.MaxStringLength
                        });
                    }
                }
                catch (ArgumentException error)
                {
                    prerequisite = error.Message;
                    fields.Clear();
                    structures = [];
                }
                if (prerequisite.Length == 0)
                {
                    ArrayOf<PubSubPrerequisite> issues = PubSubConfigurationValidation.Inspect(
                        new PubSubConfiguration
                        {
                            Fields = [.. fields],
                            Structures = structures,
                            Namespaces = metadata.Namespaces
                        }, requireEndpoint: false);
                    if (issues.Count > 0)
                    {
                        prerequisite = issues[0].Detail;
                        fields.Clear();
                    }
                }
            }
            return new PubSubMetadataSchema(
                publisher.Type, number, PubSubValueDisplay.Bound(name, 96),
                key.WriterGroupId, key.DataSetWriterId, (Guid)metadata.DataSetClassId,
                metadata.ConfigurationVersion?.MajorVersion ?? 0,
                metadata.ConfigurationVersion?.MinorVersion ?? 0,
                [.. fields], prerequisite)
            {
                Namespaces = CoreUtils.Clone(metadata.Namespaces),
                Structures = structures
            };
        }
    }
}
