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

namespace UaLens.Plugins.PubSub;

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
            throw new InvalidOperationException("Metadata without a writer-group ID cannot configure a UADP reader.");
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
        if (!PubSubIdentity.TryCreate(publisher.Type, number, name, publisher.Type == PublisherIdType.Guid, out _) ||
            key.DataSetWriterId == 0 || metadata.Fields.Count is < 1 or > PubSubConfigurationValidation.MaxFields)
        {
            prerequisite = "Metadata requires bounded, nonzero identities and between 1 and 32 scalar fields.";
        }
        else
        {
            foreach (FieldMetaData field in metadata.Fields)
            {
                if (field is null || string.IsNullOrWhiteSpace(field.Name) ||
                    field.ValueRank != ValueRanks.Scalar || field.ArrayDimensions.Count != 0 ||
                    !PubSubConfigurationValidation.IsScalarType((BuiltInType)field.BuiltInType) ||
                    field.DataType != new NodeId(field.BuiltInType) ||
                    field.MaxStringLength > PubSubConfigurationValidation.MaxValueCharacters ||
                    field.FieldFlags != 0)
                {
                    prerequisite = "Complex, array, custom-type or promoted-field metadata needs a configured " +
                        "schema/encoding context; it cannot be converted to scalar commissioning fields.";
                    fields.Clear();
                    break;
                }
                fields.Add(new PubSubFieldConfiguration
                {
                    Name = field.Name,
                    Type = (BuiltInType)field.BuiltInType,
                    FieldId = (Guid)field.DataSetFieldId
                });
            }
            if (prerequisite.Length == 0)
            {
                ArrayOf<PubSubPrerequisite> issues = PubSubConfigurationValidation.Inspect(
                    new PubSubConfiguration { Fields = [.. fields] }, requireEndpoint: false);
                if (issues.Count > 0)
                {
                    prerequisite = "The metadata has invalid field names, identifiers or scalar types.";
                    fields.Clear();
                }
            }
        }
        return new PubSubMetadataSchema(
            publisher.Type, number, PubSubValueDisplay.Bound(name, 96),
            key.WriterGroupId, key.DataSetWriterId, (Guid)metadata.DataSetClassId,
            metadata.ConfigurationVersion?.MajorVersion ?? 0,
            metadata.ConfigurationVersion?.MinorVersion ?? 0,
            [.. fields], prerequisite);
    }
}
