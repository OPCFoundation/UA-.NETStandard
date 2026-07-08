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

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Validates a <see cref="PubSubConfigurationDataType"/> against
    /// the structural and semantic rules defined by OPC UA Part 14.
    /// Issues are collected and returned; the validator never throws.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.4">
    /// Part 14 §9.1.4 PubSub configuration object model</see> and the
    /// related security rules in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.5">
    /// Part 14 §6.2.5</see>.
    /// </remarks>
    public sealed class PubSubConfigurationValidator
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubConfigurationValidator"/>.
        /// </summary>
        /// <param name="registeredTransportProfileUris">
        /// Transport profile URIs for which a transport factory has
        /// been registered. The validator will flag any
        /// <see cref="PubSubConnectionDataType.TransportProfileUri"/>
        /// not in this set as an error.
        /// </param>
        public PubSubConfigurationValidator(
            IEnumerable<string> registeredTransportProfileUris)
        {
            if (registeredTransportProfileUris is null)
            {
                throw new ArgumentNullException(nameof(registeredTransportProfileUris));
            }
            var registered = new HashSet<string>(StringComparer.Ordinal);
            foreach (string profile in registeredTransportProfileUris)
            {
                if (!string.IsNullOrEmpty(profile))
                {
                    registered.Add(profile);
                }
            }
            m_registeredTransportProfileUris = registered;
        }

        /// <summary>
        /// Suppresses warnings for groups that intentionally disable message-layer security.
        /// </summary>
        public bool SuppressInsecureSecurityModeWarnings { get; init; }

        /// <summary>
        /// Runs all validation rules against
        /// <paramref name="configuration"/> and returns the aggregated
        /// result. Never throws; missing or malformed sub-trees produce
        /// issues instead.
        /// </summary>
        /// <param name="configuration">Configuration to validate.</param>
        /// <returns>The aggregated <see cref="PubSubConfigurationValidationResult"/>.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public PubSubConfigurationValidationResult Validate(
            PubSubConfigurationDataType configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            var issues = new List<PubSubConfigurationIssue>();
            Dictionary<string, DataSetMetaDataType?> publishedDataSets =
                ValidatePublishedDataSets(configuration, issues);
            var connectionNames = new HashSet<string>(StringComparer.Ordinal);

            if (!configuration.Connections.IsNull)
            {
                int connectionIndex = 0;
                foreach (PubSubConnectionDataType connection in configuration.Connections)
                {
                    string path = $"Connections[{connectionIndex}]";
                    ValidateConnection(connection, path, connectionNames, issues);
                    ValidateWriterGroups(connection, path, publishedDataSets, issues);
                    ValidateReaderGroups(connection, path, issues);
                    connectionIndex++;
                }
            }
            return new PubSubConfigurationValidationResult(issues);
        }

        private static Dictionary<string, DataSetMetaDataType?> ValidatePublishedDataSets(
            PubSubConfigurationDataType configuration,
            List<PubSubConfigurationIssue> issues)
        {
            var lookup = new Dictionary<string, DataSetMetaDataType?>(StringComparer.Ordinal);
            if (configuration.PublishedDataSets.IsNull)
            {
                return lookup;
            }
            int index = 0;
            foreach (PublishedDataSetDataType publishedDataSet in configuration.PublishedDataSets)
            {
                string path = $"PublishedDataSets[{index}]";
                string name = publishedDataSet.Name ?? string.Empty;
                if (name.Length == 0)
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Error,
                        IssueCodes.MissingPublishedDataSetName,
                        "PublishedDataSet has an empty Name.",
                        path,
                        SpecClauses.PubSubObjectModel));
                }
                else if (lookup.ContainsKey(name))
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Error,
                        IssueCodes.DuplicatePublishedDataSetName,
                        $"Duplicate PublishedDataSet name '{name}'.",
                        path,
                        SpecClauses.PubSubObjectModel));
                }
                else
                {
                    lookup[name] = publishedDataSet.DataSetMetaData;
                }
                index++;
            }
            return lookup;
        }

        private void ValidateConnection(
            PubSubConnectionDataType connection,
            string path,
            HashSet<string> connectionNames,
            List<PubSubConfigurationIssue> issues)
        {
            string name = connection.Name ?? string.Empty;
            if (name.Length == 0)
            {
                issues.Add(new PubSubConfigurationIssue(
                    PubSubConfigurationIssueSeverity.Error,
                    IssueCodes.MissingConnectionName,
                    "PubSubConnection has an empty Name.",
                    path,
                    SpecClauses.PubSubConnection));
            }
            else if (!connectionNames.Add(name))
            {
                issues.Add(new PubSubConfigurationIssue(
                    PubSubConfigurationIssueSeverity.Error,
                    IssueCodes.DuplicateConnectionName,
                    $"Duplicate PubSubConnection name '{name}'.",
                    path,
                    SpecClauses.PubSubConnection));
            }
            string profile = connection.TransportProfileUri ?? string.Empty;
            if (profile.Length == 0)
            {
                issues.Add(new PubSubConfigurationIssue(
                    PubSubConfigurationIssueSeverity.Error,
                    IssueCodes.MissingTransportProfile,
                    "PubSubConnection has an empty TransportProfileUri.",
                    path,
                    SpecClauses.PubSubConnection));
            }
            else if (m_registeredTransportProfileUris.Count > 0 &&
                !m_registeredTransportProfileUris.Contains(profile))
            {
                issues.Add(new PubSubConfigurationIssue(
                    PubSubConfigurationIssueSeverity.Error,
                    IssueCodes.UnsupportedTransportProfile,
                    $"TransportProfileUri '{profile}' has no registered transport factory.",
                    path,
                    SpecClauses.PubSubConnection));
            }
            ValidateConnectionAddress(connection, path, profile, issues);
            ValidateConnectionTransportSettings(connection, path, issues);
        }

        private static void ValidateConnectionAddress(
            PubSubConnectionDataType connection,
            string path,
            string profile,
            List<PubSubConfigurationIssue> issues)
        {
            if (connection.Address.IsNull)
            {
                issues.Add(new PubSubConfigurationIssue(
                    PubSubConfigurationIssueSeverity.Error,
                    IssueCodes.MissingConnectionAddress,
                    "PubSubConnection has no Address.",
                    path,
                    SpecClauses.PubSubConnection));
                return;
            }
            string? url = connection.Address.TryGetValue(
                out NetworkAddressUrlDataType? networkAddress)
                ? networkAddress.Url
                : null;
            if (string.IsNullOrEmpty(url))
            {
                issues.Add(new PubSubConfigurationIssue(
                    PubSubConfigurationIssueSeverity.Warning,
                    IssueCodes.AddressUrlMissing,
                    "PubSubConnection.Address is not a NetworkAddressUrlDataType or has an empty Url.",
                    path,
                    SpecClauses.PubSubConnection));
                return;
            }
            if (string.IsNullOrEmpty(profile))
            {
                return;
            }
            (string scheme, string description)[] expected = SchemesForProfile(profile);
            if (expected.Length == 0)
            {
                return;
            }
            bool matched = false;
            for (int i = 0; i < expected.Length; i++)
            {
                if (url.StartsWith(expected[i].scheme, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    break;
                }
            }
            if (!matched)
            {
                string schemes = string.Join(
                    " or ",
                    Array.ConvertAll(expected, static s => $"'{s.scheme}'"));
                issues.Add(new PubSubConfigurationIssue(
                    PubSubConfigurationIssueSeverity.Error,
                    IssueCodes.AddressSchemeMismatch,
                    $"Address Url '{url}' does not match the expected scheme {schemes} for transport profile '{profile}'.",
                    path,
                    SpecClauses.PubSubConnection));
            }
        }

        private static void ValidateConnectionTransportSettings(
            PubSubConnectionDataType connection,
            string path,
            List<PubSubConfigurationIssue> issues)
        {
            if (connection.TransportSettings.IsNull)
            {
                return;
            }
            if (connection.TransportSettings.TryGetValue(
                out DatagramConnectionTransport2DataType? v2))
            {
                if (v2.DiscoveryAnnounceRate != 0 ||
                    v2.DiscoveryMaxMessageSize != 0 ||
                    !string.IsNullOrEmpty(v2.QosCategory))
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Info,
                        IssueCodes.DatagramV2InUse,
                        "PubSubConnection uses DatagramConnectionTransport2DataType v2-only fields; consider documenting the v2 dependency to consumers.",
                        path + ".TransportSettings",
                        SpecClauses.DatagramTransport));
                }
            }
        }

        private void ValidateWriterGroups(
            PubSubConnectionDataType connection,
            string connectionPath,
            Dictionary<string, DataSetMetaDataType?> publishedDataSets,
            List<PubSubConfigurationIssue> issues)
        {
            if (connection.WriterGroups.IsNull)
            {
                return;
            }
            var seenIds = new HashSet<ushort>();
            int wgIndex = 0;
            foreach (WriterGroupDataType writerGroup in connection.WriterGroups)
            {
                string path = $"{connectionPath}.WriterGroups[{wgIndex}]";
                if (writerGroup.WriterGroupId == 0)
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Error,
                        IssueCodes.WriterGroupIdZero,
                        "WriterGroupId must be non-zero.",
                        path,
                        SpecClauses.WriterGroup));
                }
                else if (!seenIds.Add(writerGroup.WriterGroupId))
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Error,
                        IssueCodes.DuplicateWriterGroupId,
                        $"Duplicate WriterGroupId '{writerGroup.WriterGroupId}'.",
                        path,
                        SpecClauses.WriterGroup));
                }
                if (writerGroup.PublishingInterval <= 0.0)
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Error,
                        IssueCodes.PublishingIntervalNotPositive,
                        $"PublishingInterval must be > 0 ms (was {writerGroup.PublishingInterval}).",
                        path,
                        SpecClauses.WriterGroup));
                }
                if (writerGroup.KeepAliveTime > 0.0 &&
                    writerGroup.PublishingInterval > 0.0 &&
                    writerGroup.KeepAliveTime < writerGroup.PublishingInterval)
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Error,
                        IssueCodes.KeepAliveBelowPublishingInterval,
                        $"KeepAliveTime ({writerGroup.KeepAliveTime} ms) must be >= PublishingInterval ({writerGroup.PublishingInterval} ms).",
                        path,
                        SpecClauses.WriterGroup));
                }
                ValidateGroupSecurity(
                    writerGroup.SecurityMode,
                    writerGroup.SecurityGroupId,
                    writerGroup.SecurityKeyServices,
                    path,
                    issues);
                ValidatePlaintextMqttWithoutMessageSecurity(
                    connection,
                    writerGroup.SecurityMode,
                    path,
                    issues);
                ValidateDataSetWriters(writerGroup, path, publishedDataSets, issues);
                wgIndex++;
            }
        }

        private static void ValidateDataSetWriters(
            WriterGroupDataType writerGroup,
            string writerGroupPath,
            Dictionary<string, DataSetMetaDataType?> publishedDataSets,
            List<PubSubConfigurationIssue> issues)
        {
            if (writerGroup.DataSetWriters.IsNull)
            {
                return;
            }
            var seenIds = new HashSet<ushort>();
            int dswIndex = 0;
            foreach (DataSetWriterDataType writer in writerGroup.DataSetWriters)
            {
                string path = $"{writerGroupPath}.DataSetWriters[{dswIndex}]";
                if (writer.DataSetWriterId == 0)
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Error,
                        IssueCodes.DataSetWriterIdZero,
                        "DataSetWriterId must be non-zero.",
                        path,
                        SpecClauses.DataSetWriter));
                }
                else if (!seenIds.Add(writer.DataSetWriterId))
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Error,
                        IssueCodes.DuplicateDataSetWriterId,
                        $"Duplicate DataSetWriterId '{writer.DataSetWriterId}'.",
                        path,
                        SpecClauses.DataSetWriter));
                }
                string dataSetName = writer.DataSetName ?? string.Empty;
                DataSetMetaDataType? metaData = null;
                if (dataSetName.Length == 0)
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Error,
                        IssueCodes.DataSetNameMissing,
                        "DataSetWriter.DataSetName must reference a PublishedDataSet.",
                        path,
                        SpecClauses.DataSetWriter));
                }
                else if (!publishedDataSets.TryGetValue(dataSetName, out metaData))
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Error,
                        IssueCodes.DataSetNameUnresolved,
                        $"DataSetWriter.DataSetName '{dataSetName}' does not reference any PublishedDataSet.",
                        path,
                        SpecClauses.DataSetWriter));
                }
                if (writer.KeyFrameCount == 0)
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Warning,
                        IssueCodes.KeyFrameCountZero,
                        "KeyFrameCount is 0; non-event DataSetWriters should publish a periodic key frame.",
                        path,
                        SpecClauses.DataSetWriter));
                }
                ValidateRawDataPaddingBounds(writer, metaData, path, issues);
                dswIndex++;
            }
        }

        private static void ValidateRawDataPaddingBounds(
            DataSetWriterDataType writer,
            DataSetMetaDataType? metaData,
            string writerPath,
            List<PubSubConfigurationIssue> issues)
        {
            if (((DataSetFieldContentMask)writer.DataSetFieldContentMask &
                DataSetFieldContentMask.RawData) == 0)
            {
                return;
            }
            if (metaData is null || metaData.Fields.IsNull || metaData.Fields.Count == 0)
            {
                return;
            }
            string writerName = string.IsNullOrEmpty(writer.Name)
                ? $"DataSetWriterId={writer.DataSetWriterId}"
                : writer.Name!;
            for (int i = 0; i < metaData.Fields.Count; i++)
            {
                FieldMetaData? field = metaData.Fields[i];
                if (field is null)
                {
                    continue;
                }
                var builtIn = (BuiltInType)field.BuiltInType;
                bool isVariableLengthScalar =
                    field.ValueRank == ValueRanks.Scalar &&
                    (builtIn == BuiltInType.String ||
                        builtIn == BuiltInType.ByteString ||
                        builtIn == BuiltInType.XmlElement);
                bool needsArrayDimensions =
                    field.ValueRank > 0 &&
                    (field.ArrayDimensions.IsNull || field.ArrayDimensions.Count == 0);
                bool needsMaxStringLength =
                    isVariableLengthScalar && field.MaxStringLength == 0;
                if (!needsArrayDimensions && !needsMaxStringLength)
                {
                    continue;
                }
                string fieldName = string.IsNullOrEmpty(field.Name)
                    ? $"Fields[{i}]"
                    : field.Name!;
                string reason = needsMaxStringLength
                    ? "MaxStringLength is 0"
                    : "ArrayDimensions is empty";
                string fieldPath = $"{writerPath}.PublishedDataSet.Fields[{i}]";
                issues.Add(new PubSubConfigurationIssue(
                    PubSubConfigurationIssueSeverity.Warning,
                    IssueCodes.RawDataMissingFieldBound,
                    $"DataSetWriter '{writerName}' uses RawData encoding for field '{fieldName}' " +
                    $"but {reason}; the field will be encoded with a variable-length prefix, " +
                    "breaking interop with strict v1.05.06 subscribers.",
                    fieldPath,
                    SpecClauses.RawDataFieldEncoding));
            }
        }

        private void ValidateReaderGroups(
            PubSubConnectionDataType connection,
            string connectionPath,
            List<PubSubConfigurationIssue> issues)
        {
            if (connection.ReaderGroups.IsNull)
            {
                return;
            }
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            int rgIndex = 0;
            foreach (ReaderGroupDataType readerGroup in connection.ReaderGroups)
            {
                string path = $"{connectionPath}.ReaderGroups[{rgIndex}]";
                string name = readerGroup.Name ?? string.Empty;
                if (name.Length == 0)
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Error,
                        IssueCodes.ReaderGroupNameMissing,
                        "ReaderGroup has an empty Name.",
                        path,
                        SpecClauses.ReaderGroup));
                }
                else if (!seenNames.Add(name))
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Warning,
                        IssueCodes.DuplicateReaderGroupName,
                        $"Duplicate ReaderGroup name '{name}' within connection.",
                        path,
                        SpecClauses.ReaderGroup));
                }
                ValidateGroupSecurity(
                    readerGroup.SecurityMode,
                    readerGroup.SecurityGroupId,
                    readerGroup.SecurityKeyServices,
                    path,
                    issues);
                ValidatePlaintextMqttWithoutMessageSecurity(
                    connection,
                    readerGroup.SecurityMode,
                    path,
                    issues);
                ValidateDataSetReaders(connection, readerGroup, path, issues);
                rgIndex++;
            }
        }

        private void ValidateDataSetReaders(
            PubSubConnectionDataType connection,
            ReaderGroupDataType readerGroup,
            string readerGroupPath,
            List<PubSubConfigurationIssue> issues)
        {
            if (readerGroup.DataSetReaders.IsNull)
            {
                return;
            }
            int drIndex = 0;
            foreach (DataSetReaderDataType reader in readerGroup.DataSetReaders)
            {
                string path = $"{readerGroupPath}.DataSetReaders[{drIndex}]";
                if (reader.DataSetWriterId == 0)
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Error,
                        IssueCodes.ReaderDataSetWriterIdZero,
                        "DataSetReader.DataSetWriterId must be non-zero.",
                        path,
                        SpecClauses.DataSetReader));
                }
                if (reader.MessageReceiveTimeout <= 0.0)
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Error,
                        IssueCodes.MessageReceiveTimeoutNotPositive,
                        $"DataSetReader.MessageReceiveTimeout must be > 0 ms (was {reader.MessageReceiveTimeout}).",
                        path,
                        SpecClauses.DataSetReader));
                }
                if (reader.SubscribedDataSet.IsNull)
                {
                    issues.Add(new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Error,
                        IssueCodes.SubscribedDataSetMissing,
                        "DataSetReader.SubscribedDataSet must be set (TargetVariablesDataType or SubscribedDataSetMirrorDataType).",
                        path,
                        SpecClauses.DataSetReader));
                }
                ValidateGroupSecurity(
                    reader.SecurityMode,
                    reader.SecurityGroupId,
                    reader.SecurityKeyServices,
                    path,
                    issues);
                ValidatePlaintextMqttWithoutMessageSecurity(
                    connection,
                    reader.SecurityMode,
                    path,
                    issues);
                drIndex++;
            }
        }

        private void ValidateGroupSecurity(
            MessageSecurityMode securityMode,
            string? securityGroupId,
            ArrayOf<EndpointDescription> securityKeyServices,
            string path,
            List<PubSubConfigurationIssue> issues)
        {
            bool hasGroup = !string.IsNullOrEmpty(securityGroupId);
            bool hasServices = !securityKeyServices.IsNull && securityKeyServices.Count > 0;
            switch (securityMode)
            {
                case MessageSecurityMode.Sign:
                case MessageSecurityMode.SignAndEncrypt:
                    if (!hasGroup)
                    {
                        issues.Add(new PubSubConfigurationIssue(
                            PubSubConfigurationIssueSeverity.Error,
                            IssueCodes.SecurityGroupIdMissing,
                            $"SecurityMode '{securityMode}' requires a non-empty SecurityGroupId.",
                            path,
                            SpecClauses.SecurityKeyServices));
                    }
                    if (!hasServices)
                    {
                        issues.Add(new PubSubConfigurationIssue(
                            PubSubConfigurationIssueSeverity.Error,
                            IssueCodes.SecurityKeyServicesMissing,
                            $"SecurityMode '{securityMode}' requires at least one SecurityKeyService endpoint.",
                            path,
                            SpecClauses.SecurityKeyServices));
                    }
                    break;
                case MessageSecurityMode.None:
                    if (!SuppressInsecureSecurityModeWarnings)
                    {
                        issues.Add(new PubSubConfigurationIssue(
                            PubSubConfigurationIssueSeverity.Warning,
                            IssueCodes.SecurityModeNone,
                            "SecurityMode None disables PubSub message-layer security.",
                            path,
                            SpecClauses.PubSubSecurity));
                    }
                    if (hasGroup)
                    {
                        issues.Add(new PubSubConfigurationIssue(
                            PubSubConfigurationIssueSeverity.Error,
                            IssueCodes.SecurityGroupIdUnexpected,
                            "SecurityGroupId must be empty when SecurityMode is None or Invalid.",
                            path,
                            SpecClauses.SecurityKeyServices));
                    }
                    if (hasServices)
                    {
                        issues.Add(new PubSubConfigurationIssue(
                            PubSubConfigurationIssueSeverity.Error,
                            IssueCodes.SecurityKeyServicesUnexpected,
                            "SecurityKeyServices must be empty when SecurityMode is None or Invalid.",
                            path,
                            SpecClauses.SecurityKeyServices));
                    }
                    break;
                case MessageSecurityMode.Invalid:
                    if (!SuppressInsecureSecurityModeWarnings)
                    {
                        issues.Add(new PubSubConfigurationIssue(
                            PubSubConfigurationIssueSeverity.Warning,
                            IssueCodes.SecurityModeInvalid,
                            "SecurityMode is unset (Invalid) and is treated as None; " +
                            "configure an explicit SecurityMode to silence this warning.",
                            path,
                            SpecClauses.PubSubSecurity));
                    }
                    if (hasGroup)
                    {
                        issues.Add(new PubSubConfigurationIssue(
                            PubSubConfigurationIssueSeverity.Error,
                            IssueCodes.SecurityGroupIdUnexpected,
                            "SecurityGroupId must be empty when SecurityMode is None or Invalid.",
                            path,
                            SpecClauses.SecurityKeyServices));
                    }
                    if (hasServices)
                    {
                        issues.Add(new PubSubConfigurationIssue(
                            PubSubConfigurationIssueSeverity.Error,
                            IssueCodes.SecurityKeyServicesUnexpected,
                            "SecurityKeyServices must be empty when SecurityMode is None or Invalid.",
                            path,
                            SpecClauses.SecurityKeyServices));
                    }
                    break;
            }
        }

        private static void ValidatePlaintextMqttWithoutMessageSecurity(
            PubSubConnectionDataType connection,
            MessageSecurityMode securityMode,
            string groupPath,
            List<PubSubConfigurationIssue> issues)
        {
            if (!IsPlaintextMqttConnection(connection) ||
                (securityMode != MessageSecurityMode.None && securityMode != MessageSecurityMode.Invalid))
            {
                return;
            }

            issues.Add(new PubSubConfigurationIssue(
                PubSubConfigurationIssueSeverity.Warning,
                IssueCodes.PlaintextMqttWithoutMessageSecurity,
                "Plaintext mqtt:// transport is used without PubSub message-layer security.",
                groupPath,
                SpecClauses.PubSubSecurity));
        }

        private static bool IsPlaintextMqttConnection(PubSubConnectionDataType connection)
        {
            if (!string.Equals(
                connection.TransportProfileUri,
                Profiles.PubSubMqttUadpTransport,
                StringComparison.Ordinal) &&
                !string.Equals(
                    connection.TransportProfileUri,
                    Profiles.PubSubMqttJsonTransport,
                    StringComparison.Ordinal))
            {
                return false;
            }

            return connection.Address.TryGetValue(out NetworkAddressUrlDataType? networkAddress) &&
                networkAddress.Url?.StartsWith(PubSubMqttScheme, StringComparison.OrdinalIgnoreCase) == true;
        }

        private static (string Scheme, string Description)[] SchemesForProfile(string profile)
        {
            if (string.Equals(profile, Profiles.PubSubUdpUadpTransport, StringComparison.Ordinal))
            {
                return [(PubSubUdpScheme, "UDP unicast / multicast")];
            }
            if (string.Equals(profile, Profiles.PubSubMqttUadpTransport, StringComparison.Ordinal) ||
                string.Equals(profile, Profiles.PubSubMqttJsonTransport, StringComparison.Ordinal))
            {
                return
                [
                    (PubSubMqttScheme, "MQTT"),
                    (PubSubMqttsScheme, "MQTT over TLS")
                ];
            }
            return [];
        }

        private readonly HashSet<string> m_registeredTransportProfileUris;

        private const string PubSubUdpScheme = "opc.udp://";
        private const string PubSubMqttScheme = "mqtt://";
        private const string PubSubMqttsScheme = "mqtts://";

        private static class IssueCodes
        {
            public const string MissingConnectionName = "PSC0001";
            public const string DuplicateConnectionName = "PSC0002";
            public const string MissingTransportProfile = "PSC0003";
            public const string UnsupportedTransportProfile = "PSC0004";
            public const string MissingConnectionAddress = "PSC0005";
            public const string AddressUrlMissing = "PSC0006";
            public const string AddressSchemeMismatch = "PSC0007";
            public const string DatagramV2InUse = "PSC0008";
            public const string WriterGroupIdZero = "PSC0010";
            public const string DuplicateWriterGroupId = "PSC0011";
            public const string PublishingIntervalNotPositive = "PSC0012";
            public const string KeepAliveBelowPublishingInterval = "PSC0013";
            public const string DataSetWriterIdZero = "PSC0020";
            public const string DuplicateDataSetWriterId = "PSC0021";
            public const string DataSetNameMissing = "PSC0022";
            public const string DataSetNameUnresolved = "PSC0023";
            public const string KeyFrameCountZero = "PSC0024";
            public const string RawDataMissingFieldBound = "PSC0025";
            public const string ReaderGroupNameMissing = "PSC0030";
            public const string DuplicateReaderGroupName = "PSC0031";
            public const string ReaderDataSetWriterIdZero = "PSC0040";
            public const string MessageReceiveTimeoutNotPositive = "PSC0041";
            public const string SubscribedDataSetMissing = "PSC0042";
            public const string SecurityGroupIdMissing = "PSC0050";
            public const string SecurityKeyServicesMissing = "PSC0051";
            public const string SecurityGroupIdUnexpected = "PSC0052";
            public const string SecurityKeyServicesUnexpected = "PSC0053";
            public const string SecurityModeNone = "PSC0054";
            public const string SecurityModeInvalid = "PSC0055";
            public const string PlaintextMqttWithoutMessageSecurity = "PSC0056";
            public const string MissingPublishedDataSetName = "PSC0060";
            public const string DuplicatePublishedDataSetName = "PSC0061";
        }

        private static class SpecClauses
        {
            public const string PubSubObjectModel = "9.1.4";
            public const string PubSubConnection = "9.1.4.1";
            public const string WriterGroup = "9.1.6";
            public const string DataSetWriter = "9.1.7";
            public const string ReaderGroup = "9.1.8";
            public const string DataSetReader = "9.1.9";
            public const string PubSubSecurity = "6.2.5";
            public const string SecurityKeyServices = "6.2.5.4";
            public const string DatagramTransport = "9.1.5.2";
            public const string RawDataFieldEncoding = "7.2.4.5.11";
        }
    }
}
