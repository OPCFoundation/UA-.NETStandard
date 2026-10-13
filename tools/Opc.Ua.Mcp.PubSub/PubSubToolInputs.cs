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
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Compact tool settings; connection and typed fields are separate root arguments.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubApplicationSettingsInput
    {
        /// <summary>
        /// Creates settings for direct construction with explicit required identity.
        /// </summary>
        public PubSubApplicationSettingsInput()
        {
        }

        /// <summary>
        /// Preserves optional defaults in the source-generated tool binder.
        /// </summary>
        [JsonConstructor]
        [SetsRequiredMembers]
        public PubSubApplicationSettingsInput(
            string name, string mode, PubSubPublisherIdInput publisherId,
            ushort writerGroupId = 1, ushort dataSetWriterId = 1, string dataSetName = "McpDataSet",
            string? dataSetClassId = null, int publishingIntervalMilliseconds = 100,
            PubSubSecurityInput? security = null)
        {
            Name = name;
            Mode = mode;
            PublisherId = publisherId;
            WriterGroupId = writerGroupId;
            DataSetWriterId = dataSetWriterId;
            DataSetName = dataSetName;
            DataSetClassId = dataSetClassId;
            PublishingIntervalMilliseconds = publishingIntervalMilliseconds;
            Security = security;
        }

        /// <summary>
        /// Gets the caller's application name.
        /// </summary>
        [JsonRequired]
        public required string Name { get; init; }

        /// <summary>
        /// Gets Publisher or Subscriber.
        /// </summary>
        [JsonRequired]
        public required string Mode { get; init; }

        /// <summary>
        /// Gets the exact publisher discriminator and value.
        /// </summary>
        [JsonRequired]
        public required PubSubPublisherIdInput PublisherId { get; init; }

        /// <summary>
        /// Gets the writer group identifier.
        /// </summary>
        public ushort WriterGroupId { get; init; } = 1;

        /// <summary>
        /// Gets the dataset writer identifier.
        /// </summary>
        public ushort DataSetWriterId { get; init; } = 1;

        /// <summary>
        /// Gets the dataset name.
        /// </summary>
        public string DataSetName { get; init; } = "McpDataSet";

        /// <summary>
        /// Gets an optional dataset class GUID.
        /// </summary>
        public string? DataSetClassId { get; init; }

        /// <summary>
        /// Gets the publishing interval in milliseconds.
        /// </summary>
        public int PublishingIntervalMilliseconds { get; init; } = 100;

        /// <summary>
        /// Gets optional message security represented only by a protected key reference.
        /// </summary>
        public PubSubSecurityInput? Security { get; init; }

        /// <summary>
        /// Reconstructs the direct runtime plan without changing or defaulting explicit caller values.
        /// </summary>
        public PubSubApplicationInput ToApplication(
            PubSubConnectionInput connection, ArrayOf<PubSubFieldInput> fields)
        {
            ArgumentNullException.ThrowIfNull(connection);
            return new PubSubApplicationInput
            {
                Name = Name,
                Mode = Mode,
                PublisherId = PublisherId,
                WriterGroupId = WriterGroupId,
                DataSetWriterId = DataSetWriterId,
                DataSetName = DataSetName,
                DataSetClassId = DataSetClassId,
                PublishingIntervalMilliseconds = PublishingIntervalMilliseconds,
                Security = Security,
                Connection = connection,
                Fields = fields
            };
        }

        /// <summary>
        /// Extracts compact settings from an existing direct application plan.
        /// </summary>
        public static PubSubApplicationSettingsInput FromApplication(PubSubApplicationInput input)
        {
            ArgumentNullException.ThrowIfNull(input);
            return new PubSubApplicationSettingsInput
            {
                Name = input.Name,
                Mode = input.Mode,
                PublisherId = input.PublisherId,
                WriterGroupId = input.WriterGroupId,
                DataSetWriterId = input.DataSetWriterId,
                DataSetName = input.DataSetName,
                DataSetClassId = input.DataSetClassId,
                PublishingIntervalMilliseconds = input.PublishingIntervalMilliseconds,
                Security = input.Security
            };
        }
    }

    /// <summary>
    /// Compact route settings; endpoint plans, fields and object-array renames are separate root arguments.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubBridgeSettingsInput
    {
        /// <summary>
        /// Creates settings for direct construction with an explicit route name.
        /// </summary>
        public PubSubBridgeSettingsInput()
        {
        }

        /// <summary>
        /// Preserves default metadata retention and empty projections in source-generated JSON.
        /// </summary>
        [JsonConstructor]
        [SetsRequiredMembers]
        public PubSubBridgeSettingsInput(
            string name, ArrayOf<string> selectFields = default, ArrayOf<string> excludeFields = default,
            ArrayOf<string> promoteFields = default, PubSubPublisherIdInput? publisherId = null,
            ushort? writerGroupId = null, bool preserveMetadataVersion = true,
            bool dropKeepAlive = false, bool allowSecurityDowngrade = false)
        {
            Name = name;
            SelectFields = selectFields.IsNull ? [] : selectFields;
            ExcludeFields = excludeFields.IsNull ? [] : excludeFields;
            PromoteFields = promoteFields.IsNull ? [] : promoteFields;
            PublisherId = publisherId;
            WriterGroupId = writerGroupId;
            PreserveMetadataVersion = preserveMetadataVersion;
            DropKeepAlive = dropKeepAlive;
            AllowSecurityDowngrade = allowSecurityDowngrade;
        }

        /// <summary>
        /// Gets the route name.
        /// </summary>
        [JsonRequired]
        public required string Name { get; init; }

        /// <summary>
        /// Gets an optional ordered projection, mutually exclusive with ExcludeFields.
        /// </summary>
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> SelectFields { get; init; } = [];

        /// <summary>
        /// Gets fields removed from the output.
        /// </summary>
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> ExcludeFields { get; init; } = [];

        /// <summary>
        /// Gets fields promoted into bounded transport properties.
        /// </summary>
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> PromoteFields { get; init; } = [];

        /// <summary>
        /// Gets an optional exact publisher remap.
        /// </summary>
        public PubSubPublisherIdInput? PublisherId { get; init; }

        /// <summary>
        /// Gets an optional writer group remap.
        /// </summary>
        public ushort? WriterGroupId { get; init; }

        /// <summary>
        /// Gets whether metadata versions remain unchanged.
        /// </summary>
        public bool PreserveMetadataVersion { get; init; } = true;

        /// <summary>
        /// Gets whether keep-alive messages are dropped.
        /// </summary>
        public bool DropKeepAlive { get; init; }

        /// <summary>
        /// Gets route consent to a downgrade, independently requiring host permission.
        /// </summary>
        public bool AllowSecurityDowngrade { get; init; }

        /// <summary>
        /// Reconstructs one direct route from the separately bound typed root arguments.
        /// </summary>
        public PubSubBridgeInput ToBridge(
            PubSubApplicationSettingsInput source, PubSubConnectionInput sourceConnection,
            ArrayOf<PubSubFieldInput> sourceFields, PubSubApplicationSettingsInput target,
            PubSubConnectionInput targetConnection, ArrayOf<PubSubFieldInput> targetFields,
            ArrayOf<PubSubFieldRenameInput> renameFields)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(target);
            return new PubSubBridgeInput
            {
                Name = Name,
                Source = source.ToApplication(sourceConnection, sourceFields),
                Target = target.ToApplication(targetConnection, targetFields),
                RenameFields = renameFields.IsNull ? [] : renameFields,
                SelectFields = SelectFields,
                ExcludeFields = ExcludeFields,
                PromoteFields = PromoteFields,
                PublisherId = PublisherId,
                WriterGroupId = WriterGroupId,
                PreserveMetadataVersion = PreserveMetadataVersion,
                DropKeepAlive = DropKeepAlive,
                AllowSecurityDowngrade = AllowSecurityDowngrade
            };
        }

        /// <summary>
        /// Extracts compact route settings from an existing direct bridge plan.
        /// </summary>
        public static PubSubBridgeSettingsInput FromBridge(PubSubBridgeInput input)
        {
            ArgumentNullException.ThrowIfNull(input);
            return new PubSubBridgeSettingsInput
            {
                Name = input.Name,
                SelectFields = input.SelectFields,
                ExcludeFields = input.ExcludeFields,
                PromoteFields = input.PromoteFields,
                PublisherId = input.PublisherId,
                WriterGroupId = input.WriterGroupId,
                PreserveMetadataVersion = input.PreserveMetadataVersion,
                DropKeepAlive = input.DropKeepAlive,
                AllowSecurityDowngrade = input.AllowSecurityDowngrade
            };
        }
    }
}
