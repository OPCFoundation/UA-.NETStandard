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

using System.Collections.Generic;
using Opc.Ua;

namespace UaLens.Plugins.PubSub;

/// <summary>
/// Implemented mask choices for a single, explicitly identified dataset.
/// Promoted fields and headerless/wildcard topologies are not commissioned here.
/// </summary>
internal static class PubSubContentMasks
{
    public static ArrayOf<PubSubPrerequisite> Inspect(PubSubConfiguration configuration)
    {
        var issues = new List<PubSubPrerequisite>();
        if ((configuration.UadpNetworkMask & ~AllowedUadpNetwork) != 0 ||
            (configuration.UadpDataSetMask & ~AllowedUadpDataSet) != 0 ||
            (configuration.JsonNetworkMask & ~AllowedJsonNetwork) != 0 ||
            (configuration.JsonDataSetMask & ~DefaultJsonDataSet) != 0 ||
            (configuration.FieldContentMask & ~AllowedField) != 0)
        {
            Add("The mask contains unsupported bits; only the displayed header and scalar-field flags are supported.");
        }
        if ((configuration.UadpNetworkMask & RequiredUadpNetwork) != RequiredUadpNetwork ||
            (configuration.JsonNetworkMask & DefaultJsonNetwork) != DefaultJsonNetwork ||
            (configuration.JsonDataSetMask & JsonDataSetMessageContentMask.DataSetWriterId) == 0)
        {
            Add("Publisher, group, payload and dataset-writer identity headers are required; no wildcard reception.");
        }
        if (((configuration.UadpNetworkMask & UadpNetworkMessageContentMask.PicoSeconds) != 0 &&
             (configuration.UadpNetworkMask & UadpNetworkMessageContentMask.Timestamp) == 0) ||
            ((configuration.UadpDataSetMask & UadpDataSetMessageContentMask.PicoSeconds) != 0 &&
             (configuration.UadpDataSetMask & UadpDataSetMessageContentMask.Timestamp) == 0) ||
            ((configuration.FieldContentMask & DataSetFieldContentMask.SourcePicoSeconds) != 0 &&
             (configuration.FieldContentMask & DataSetFieldContentMask.SourceTimestamp) == 0) ||
            ((configuration.FieldContentMask & DataSetFieldContentMask.ServerPicoSeconds) != 0 &&
             (configuration.FieldContentMask & DataSetFieldContentMask.ServerTimestamp) == 0))
        {
            Add("Picosecond flags require their corresponding timestamp flag.");
        }
        if ((configuration.FieldContentMask & DataSetFieldContentMask.RawData) != 0 &&
            configuration.FieldContentMask != DataSetFieldContentMask.RawData)
        {
            Add("RawData cannot be combined with status or timestamp field flags.");
        }
        if (configuration.RawDataEncoding && configuration.FieldContentMask != DefaultField &&
            configuration.FieldContentMask != DataSetFieldContentMask.RawData)
        {
            Add("Legacy RawData selection cannot override custom field-content flags.");
        }
        return [.. issues];

        void Add(string detail)
        {
            issues.Add(new PubSubPrerequisite("Content masks", PubSubReadiness.RequiresConfiguration, detail));
        }
    }

    public const UadpNetworkMessageContentMask RequiredUadpNetwork =
        UadpNetworkMessageContentMask.PublisherId | UadpNetworkMessageContentMask.GroupHeader |
        UadpNetworkMessageContentMask.WriterGroupId | UadpNetworkMessageContentMask.PayloadHeader;

    public const UadpNetworkMessageContentMask DefaultUadpNetwork = RequiredUadpNetwork |
        UadpNetworkMessageContentMask.NetworkMessageNumber | UadpNetworkMessageContentMask.SequenceNumber;

    public const UadpNetworkMessageContentMask AllowedUadpNetwork = DefaultUadpNetwork |
        UadpNetworkMessageContentMask.DataSetClassId | UadpNetworkMessageContentMask.Timestamp |
        UadpNetworkMessageContentMask.PicoSeconds | UadpNetworkMessageContentMask.GroupVersion;

    public const UadpDataSetMessageContentMask DefaultUadpDataSet =
        UadpDataSetMessageContentMask.Status | UadpDataSetMessageContentMask.SequenceNumber |
        UadpDataSetMessageContentMask.Timestamp | UadpDataSetMessageContentMask.MajorVersion |
        UadpDataSetMessageContentMask.MinorVersion;

    public const UadpDataSetMessageContentMask AllowedUadpDataSet =
        DefaultUadpDataSet | UadpDataSetMessageContentMask.PicoSeconds;

    public const JsonNetworkMessageContentMask DefaultJsonNetwork =
        JsonNetworkMessageContentMask.NetworkMessageHeader | JsonNetworkMessageContentMask.DataSetMessageHeader |
        JsonNetworkMessageContentMask.PublisherId;

    public const JsonNetworkMessageContentMask AllowedJsonNetwork =
        DefaultJsonNetwork | JsonNetworkMessageContentMask.DataSetClassId |
        JsonNetworkMessageContentMask.SingleDataSetMessage;

    public const JsonDataSetMessageContentMask DefaultJsonDataSet =
        JsonDataSetMessageContentMask.DataSetWriterId | JsonDataSetMessageContentMask.SequenceNumber |
        JsonDataSetMessageContentMask.Status | JsonDataSetMessageContentMask.Timestamp |
        JsonDataSetMessageContentMask.MetaDataVersion;

    public const DataSetFieldContentMask DefaultField =
        DataSetFieldContentMask.StatusCode | DataSetFieldContentMask.SourceTimestamp;

    public const DataSetFieldContentMask AllowedField = DefaultField |
        DataSetFieldContentMask.RawData | DataSetFieldContentMask.ServerTimestamp |
        DataSetFieldContentMask.SourcePicoSeconds | DataSetFieldContentMask.ServerPicoSeconds;
}
