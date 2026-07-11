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
    /// Fluent builder that assembles a Part 14
    /// <see cref="PubSubConfigurationDataType"/> from connections,
    /// writer / reader groups, DataSet writers / readers and published
    /// DataSets without hand-wiring the nested DataType graph.
    /// </summary>
    /// <remarks>
    /// Mirrors the OPC UA information model defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.2">
    /// Part 14 §6.2 PubSub configuration model</see>. Use it from
    /// samples, tests or any code that needs an inline configuration to
    /// pass to <c>PubSubApplicationBuilder.UseConfiguration</c> or the
    /// DI <c>IPubSubBuilder.UseConfiguration</c>.
    /// </remarks>
    public sealed class PubSubConfigurationBuilder
    {
        private readonly List<PubSubConnectionDataType> m_connections = [];
        private readonly List<PublishedDataSetDataType> m_publishedDataSets = [];
        private bool m_enabled = true;

        /// <summary>
        /// Creates a new <see cref="PubSubConfigurationBuilder"/>.
        /// </summary>
        /// <returns>A new builder.</returns>
        public static PubSubConfigurationBuilder Create()
        {
            return new PubSubConfigurationBuilder();
        }

        /// <summary>
        /// Sets the top-level <c>Enabled</c> flag.
        /// </summary>
        /// <param name="enabled">Whether the configuration is enabled.</param>
        /// <returns>The same builder for chaining.</returns>
        public PubSubConfigurationBuilder Enabled(bool enabled = true)
        {
            m_enabled = enabled;
            return this;
        }

        /// <summary>
        /// Adds a PublishedDataSet via a nested
        /// <see cref="PublishedDataSetBuilder"/>.
        /// </summary>
        /// <param name="name">PublishedDataSet name.</param>
        /// <param name="configure">Nested builder callback.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public PubSubConfigurationBuilder AddPublishedDataSet(
            string name,
            Action<PublishedDataSetBuilder> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            var builder = new PublishedDataSetBuilder(name);
            configure(builder);
            m_publishedDataSets.Add(builder.Build());
            return this;
        }

        /// <summary>
        /// Adds a PublishedAction DataSet with request metadata and dispatch targets.
        /// </summary>
        /// <param name="name">PublishedDataSet name.</param>
        /// <param name="requestMetaData">Request DataSet metadata.</param>
        /// <param name="targets">Action targets that can receive requests.</param>
        /// <param name="configure">Optional callback for additional generated type settings.</param>
        /// <returns>The same builder for chaining.</returns>
        public PubSubConfigurationBuilder AddPublishedAction(
            string name,
            DataSetMetaDataType requestMetaData,
            ArrayOf<ActionTargetDataType> targets,
            Action<PublishedActionDataType>? configure = null)
        {
            PublishedActionDataType action = CreatePublishedAction(
                requestMetaData,
                targets);

            configure?.Invoke(action);
            m_publishedDataSets.Add(CreatePublishedActionDataSet(name, action));
            return this;
        }

        /// <summary>
        /// Adds a PublishedActionMethod DataSet with request metadata, targets and method bindings.
        /// </summary>
        /// <param name="name">PublishedDataSet name.</param>
        /// <param name="requestMetaData">Request DataSet metadata.</param>
        /// <param name="targets">Action targets that can receive requests.</param>
        /// <param name="methods">Method bindings for the action targets.</param>
        /// <param name="configure">Optional callback for additional generated type settings.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentException"></exception>
        public PubSubConfigurationBuilder AddPublishedAction(
            string name,
            DataSetMetaDataType requestMetaData,
            ArrayOf<ActionTargetDataType> targets,
            ArrayOf<ActionMethodDataType> methods,
            Action<PublishedActionMethodDataType>? configure = null)
        {
            if (methods.IsNull)
            {
                throw new ArgumentException("methods must not be null.", nameof(methods));
            }

            var action = new PublishedActionMethodDataType
            {
                RequestDataSetMetaData = ValidateRequestMetaData(requestMetaData),
                ActionTargets = ValidateTargets(targets),
                ActionMethods = methods
            };

            configure?.Invoke(action);
            m_publishedDataSets.Add(CreatePublishedActionDataSet(name, action));
            return this;
        }

        /// <summary>
        /// Adds a PubSubConnection via a nested
        /// <see cref="PubSubConnectionBuilder"/>.
        /// </summary>
        /// <param name="name">Connection name.</param>
        /// <param name="configure">Nested builder callback.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public PubSubConfigurationBuilder AddConnection(
            string name,
            Action<PubSubConnectionBuilder> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            var builder = new PubSubConnectionBuilder(name);
            configure(builder);
            m_connections.Add(builder.Build());
            return this;
        }

        private static PublishedActionDataType CreatePublishedAction(
            DataSetMetaDataType requestMetaData,
            ArrayOf<ActionTargetDataType> targets)
        {
            return new PublishedActionDataType
            {
                RequestDataSetMetaData = ValidateRequestMetaData(requestMetaData),
                ActionTargets = ValidateTargets(targets)
            };
        }

        private static PublishedDataSetDataType CreatePublishedActionDataSet(
            string name,
            PublishedActionDataType action)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name must not be empty.", nameof(name));
            }

            return new PublishedDataSetDataType
            {
                Name = name,
                DataSetMetaData = action.RequestDataSetMetaData,
                DataSetSource = new ExtensionObject(action)
            };
        }

        private static DataSetMetaDataType ValidateRequestMetaData(DataSetMetaDataType requestMetaData)
        {
            if (requestMetaData is null)
            {
                throw new ArgumentNullException(nameof(requestMetaData));
            }

            return requestMetaData;
        }

        private static ArrayOf<ActionTargetDataType> ValidateTargets(ArrayOf<ActionTargetDataType> targets)
        {
            if (targets.IsNull)
            {
                throw new ArgumentException("targets must not be null.", nameof(targets));
            }

            return targets;
        }

        /// <summary>
        /// Materialises the accumulated
        /// <see cref="PubSubConfigurationDataType"/>.
        /// </summary>
        /// <returns>The configuration.</returns>
        public PubSubConfigurationDataType Build()
        {
            return new PubSubConfigurationDataType
            {
                Enabled = m_enabled,
                Connections = new ArrayOf<PubSubConnectionDataType>(m_connections.ToArray()),
                PublishedDataSets = new ArrayOf<PublishedDataSetDataType>(m_publishedDataSets.ToArray())
            };
        }
    }

    /// <summary>
    /// Fluent builder for a <see cref="PublishedDataSetDataType"/> and
    /// its <see cref="DataSetMetaDataType"/>.
    /// </summary>
    public sealed class PublishedDataSetBuilder
    {
        private readonly string m_name;
        private readonly List<FieldMetaData> m_fields = [];
        private Uuid m_dataSetClassId = Uuid.Empty;
        private uint m_majorVersion = 1;
        private uint m_minorVersion;
        private bool m_generateFieldIds = true;

        internal PublishedDataSetBuilder(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name must not be empty.", nameof(name));
            }
            m_name = name;
        }

        /// <summary>
        /// Sets the DataSetClassId.
        /// </summary>
        /// <param name="dataSetClassId">DataSet class identifier.</param>
        /// <returns>The same builder for chaining.</returns>
        public PublishedDataSetBuilder WithDataSetClassId(Uuid dataSetClassId)
        {
            m_dataSetClassId = dataSetClassId;
            return this;
        }

        /// <summary>
        /// Sets the configuration version.
        /// </summary>
        /// <param name="majorVersion">Major version.</param>
        /// <param name="minorVersion">Minor version.</param>
        /// <returns>The same builder for chaining.</returns>
        public PublishedDataSetBuilder WithConfigurationVersion(
            uint majorVersion,
            uint minorVersion)
        {
            m_majorVersion = majorVersion;
            m_minorVersion = minorVersion;
            return this;
        }

        /// <summary>
        /// When set, suppresses automatic generation of a
        /// <c>DataSetFieldId</c> for each added field (e.g. for a
        /// subscriber-side metadata description).
        /// </summary>
        /// <returns>The same builder for chaining.</returns>
        public PublishedDataSetBuilder WithoutFieldIds()
        {
            m_generateFieldIds = false;
            return this;
        }

        /// <summary>
        /// Adds a scalar field to the DataSet metadata.
        /// </summary>
        /// <param name="name">Field name.</param>
        /// <param name="builtInType">OPC UA built-in type id.</param>
        /// <param name="dataType">DataType node id.</param>
        /// <param name="valueRank">Value rank (default scalar).</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentException"></exception>
        public PublishedDataSetBuilder AddField(
            string name,
            byte builtInType,
            NodeId dataType,
            int valueRank = ValueRanks.Scalar)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name must not be empty.", nameof(name));
            }
            m_fields.Add(new FieldMetaData
            {
                Name = name,
                DataSetFieldId = m_generateFieldIds ? Uuid.NewUuid() : Uuid.Empty,
                BuiltInType = builtInType,
                DataType = dataType,
                ValueRank = valueRank
            });
            return this;
        }

        internal DataSetMetaDataType BuildMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = m_name,
                DataSetClassId = m_dataSetClassId,
                Fields = new ArrayOf<FieldMetaData>(m_fields.ToArray()),
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = m_majorVersion,
                    MinorVersion = m_minorVersion
                }
            };
        }

        internal PublishedDataSetDataType Build()
        {
            return new PublishedDataSetDataType
            {
                Name = m_name,
                DataSetMetaData = BuildMetaData()
            };
        }
    }

    /// <summary>
    /// Fluent builder for a <see cref="PubSubConnectionDataType"/>.
    /// </summary>
    public sealed class PubSubConnectionBuilder
    {
        private readonly string m_name;
        private readonly List<WriterGroupDataType> m_writerGroups = [];
        private readonly List<ReaderGroupDataType> m_readerGroups = [];
        private Variant m_publisherId;
        private string m_transportProfileUri = string.Empty;
        private NetworkAddressUrlDataType m_address = new() { NetworkInterface = string.Empty };
        private bool m_enabled = true;

        internal PubSubConnectionBuilder(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name must not be empty.", nameof(name));
            }
            m_name = name;
        }

        /// <summary>
        /// Sets the <c>Enabled</c> flag.
        /// </summary>
        /// <param name="enabled">Whether the connection is enabled.</param>
        /// <returns>The same builder for chaining.</returns>
        public PubSubConnectionBuilder Enabled(bool enabled = true)
        {
            m_enabled = enabled;
            return this;
        }

        /// <summary>
        /// Sets the PublisherId.
        /// </summary>
        /// <param name="publisherId">PublisherId value.</param>
        /// <returns>The same builder for chaining.</returns>
        public PubSubConnectionBuilder WithPublisherId(Variant publisherId)
        {
            m_publisherId = publisherId;
            return this;
        }

        /// <summary>
        /// Sets the TransportProfileUri.
        /// </summary>
        /// <param name="transportProfileUri">Transport profile URI.</param>
        /// <returns>The same builder for chaining.</returns>
        public PubSubConnectionBuilder WithTransportProfile(string transportProfileUri)
        {
            m_transportProfileUri = transportProfileUri ?? string.Empty;
            return this;
        }

        /// <summary>
        /// Sets the network address URL and optional network interface.
        /// </summary>
        /// <param name="url">Endpoint URL.</param>
        /// <param name="networkInterface">Network interface name.</param>
        /// <returns>The same builder for chaining.</returns>
        public PubSubConnectionBuilder WithAddress(
            string url,
            string networkInterface = "")
        {
            m_address = new NetworkAddressUrlDataType
            {
                NetworkInterface = networkInterface ?? string.Empty,
                Url = url
            };
            return this;
        }

        /// <summary>
        /// Adds a WriterGroup via a nested
        /// <see cref="WriterGroupBuilder"/>.
        /// </summary>
        /// <param name="name">WriterGroup name.</param>
        /// <param name="configure">Nested builder callback.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public PubSubConnectionBuilder AddWriterGroup(
            string name,
            Action<WriterGroupBuilder> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            var builder = new WriterGroupBuilder(name);
            configure(builder);
            m_writerGroups.Add(builder.Build());
            return this;
        }

        /// <summary>
        /// Adds a ReaderGroup via a nested
        /// <see cref="ReaderGroupBuilder"/>.
        /// </summary>
        /// <param name="name">ReaderGroup name.</param>
        /// <param name="configure">Nested builder callback.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public PubSubConnectionBuilder AddReaderGroup(
            string name,
            Action<ReaderGroupBuilder> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            var builder = new ReaderGroupBuilder(name);
            configure(builder);
            m_readerGroups.Add(builder.Build());
            return this;
        }

        internal PubSubConnectionDataType Build()
        {
            return new PubSubConnectionDataType
            {
                Name = m_name,
                Enabled = m_enabled,
                PublisherId = m_publisherId,
                TransportProfileUri = m_transportProfileUri,
                Address = new ExtensionObject(m_address),
                WriterGroups = new ArrayOf<WriterGroupDataType>(m_writerGroups.ToArray()),
                ReaderGroups = new ArrayOf<ReaderGroupDataType>(m_readerGroups.ToArray())
            };
        }
    }

    /// <summary>
    /// Fluent builder for a <see cref="WriterGroupDataType"/>.
    /// </summary>
    public sealed class WriterGroupBuilder
    {
        private readonly string m_name;
        private readonly List<DataSetWriterDataType> m_writers = [];
        private ushort m_writerGroupId;
        private bool m_enabled = true;
        private double m_publishingInterval;
        private double m_keepAliveTime;
        private uint m_maxNetworkMessageSize = 1500;
        private MessageSecurityMode m_securityMode = MessageSecurityMode.None;
        private string m_securityGroupId = string.Empty;
        private ArrayOf<EndpointDescription> m_securityKeyServices;
        private ExtensionObject m_messageSettings = ExtensionObject.Null;
        private ExtensionObject m_transportSettings = ExtensionObject.Null;

        internal WriterGroupBuilder(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name must not be empty.", nameof(name));
            }
            m_name = name;
        }

        /// <summary>
        /// Sets the WriterGroupId.
        /// </summary>
        /// <param name="writerGroupId">WriterGroupId.</param>
        /// <returns>The same builder for chaining.</returns>
        public WriterGroupBuilder WithWriterGroupId(ushort writerGroupId)
        {
            m_writerGroupId = writerGroupId;
            return this;
        }

        /// <summary>
        /// Sets the <c>Enabled</c> flag.
        /// </summary>
        /// <param name="enabled">Whether the group is enabled.</param>
        /// <returns>The same builder for chaining.</returns>
        public WriterGroupBuilder Enabled(bool enabled = true)
        {
            m_enabled = enabled;
            return this;
        }

        /// <summary>
        /// Sets the publishing interval and (proportional) keep-alive time.
        /// </summary>
        /// <param name="publishingIntervalMs">Publishing interval (ms).</param>
        /// <param name="keepAliveTimeMs">
        /// Keep-alive time (ms); defaults to five publishing intervals.
        /// </param>
        /// <returns>The same builder for chaining.</returns>
        public WriterGroupBuilder WithPublishingInterval(
            double publishingIntervalMs,
            double keepAliveTimeMs = 0)
        {
            m_publishingInterval = publishingIntervalMs;
            m_keepAliveTime = keepAliveTimeMs > 0
                ? keepAliveTimeMs
                : publishingIntervalMs * 5.0;
            return this;
        }

        /// <summary>
        /// Sets the maximum NetworkMessage size in bytes.
        /// </summary>
        /// <param name="maxNetworkMessageSize">Maximum size in bytes.</param>
        /// <returns>The same builder for chaining.</returns>
        public WriterGroupBuilder WithMaxNetworkMessageSize(uint maxNetworkMessageSize)
        {
            m_maxNetworkMessageSize = maxNetworkMessageSize;
            return this;
        }

        /// <summary>
        /// Configures message security for the group.
        /// </summary>
        /// <param name="securityMode">Message security mode.</param>
        /// <param name="securityGroupId">SecurityGroupId.</param>
        /// <param name="securityKeyServiceUrls">SKS endpoint URLs.</param>
        /// <returns>The same builder for chaining.</returns>
        public WriterGroupBuilder WithSecurity(
            MessageSecurityMode securityMode,
            string securityGroupId,
            params string[] securityKeyServiceUrls)
        {
            m_securityMode = securityMode;
            m_securityGroupId = securityGroupId ?? string.Empty;
            m_securityKeyServices = FluentConfigurationHelpers
                .BuildSecurityKeyServices(securityKeyServiceUrls);
            return this;
        }

        /// <summary>
        /// Sets the WriterGroup message settings (e.g. a
        /// <c>UadpWriterGroupMessageDataType</c> or
        /// <c>JsonWriterGroupMessageDataType</c>).
        /// </summary>
        /// <param name="messageSettings">Message settings body.</param>
        /// <returns>The same builder for chaining.</returns>
        public WriterGroupBuilder WithMessageSettings(IEncodeable messageSettings)
        {
            m_messageSettings = new ExtensionObject(messageSettings);
            return this;
        }

        /// <summary>
        /// Sets the WriterGroup transport settings (e.g. a
        /// <c>DatagramWriterGroupTransportDataType</c> or
        /// <c>BrokerWriterGroupTransportDataType</c>).
        /// </summary>
        /// <param name="transportSettings">Transport settings body.</param>
        /// <returns>The same builder for chaining.</returns>
        public WriterGroupBuilder WithTransportSettings(IEncodeable transportSettings)
        {
            m_transportSettings = new ExtensionObject(transportSettings);
            return this;
        }

        /// <summary>
        /// Adds a DataSetWriter via a nested
        /// <see cref="DataSetWriterBuilder"/>.
        /// </summary>
        /// <param name="name">DataSetWriter name.</param>
        /// <param name="configure">Nested builder callback.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public WriterGroupBuilder AddDataSetWriter(
            string name,
            Action<DataSetWriterBuilder> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            var builder = new DataSetWriterBuilder(name);
            configure(builder);
            m_writers.Add(builder.Build());
            return this;
        }

        internal WriterGroupDataType Build()
        {
            return new WriterGroupDataType
            {
                Name = m_name,
                WriterGroupId = m_writerGroupId,
                Enabled = m_enabled,
                SecurityMode = m_securityMode,
                SecurityGroupId = m_securityGroupId,
                SecurityKeyServices = m_securityKeyServices,
                PublishingInterval = m_publishingInterval,
                KeepAliveTime = m_keepAliveTime,
                MaxNetworkMessageSize = m_maxNetworkMessageSize,
                MessageSettings = m_messageSettings,
                TransportSettings = m_transportSettings,
                DataSetWriters = new ArrayOf<DataSetWriterDataType>(m_writers.ToArray())
            };
        }
    }

    /// <summary>
    /// Fluent builder for a <see cref="DataSetWriterDataType"/>.
    /// </summary>
    public sealed class DataSetWriterBuilder
    {
        private readonly string m_name;
        private ushort m_dataSetWriterId;
        private bool m_enabled = true;
        private string m_dataSetName = string.Empty;
        private uint m_keyFrameCount = 1;
        private uint m_dataSetFieldContentMask;
        private ExtensionObject m_messageSettings = ExtensionObject.Null;
        private ExtensionObject m_transportSettings = ExtensionObject.Null;

        internal DataSetWriterBuilder(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name must not be empty.", nameof(name));
            }
            m_name = name;
        }

        /// <summary>
        /// Sets the DataSetWriterId.
        /// </summary>
        /// <param name="dataSetWriterId">DataSetWriterId.</param>
        /// <returns>The same builder for chaining.</returns>
        public DataSetWriterBuilder WithDataSetWriterId(ushort dataSetWriterId)
        {
            m_dataSetWriterId = dataSetWriterId;
            return this;
        }

        /// <summary>
        /// Sets the <c>Enabled</c> flag.
        /// </summary>
        /// <param name="enabled">Whether the writer is enabled.</param>
        /// <returns>The same builder for chaining.</returns>
        public DataSetWriterBuilder Enabled(bool enabled = true)
        {
            m_enabled = enabled;
            return this;
        }

        /// <summary>
        /// Sets the name of the PublishedDataSet to write.
        /// </summary>
        /// <param name="dataSetName">PublishedDataSet name.</param>
        /// <returns>The same builder for chaining.</returns>
        public DataSetWriterBuilder WithDataSetName(string dataSetName)
        {
            m_dataSetName = dataSetName ?? string.Empty;
            return this;
        }

        /// <summary>
        /// Sets the key-frame count.
        /// </summary>
        /// <param name="keyFrameCount">Key-frame count.</param>
        /// <returns>The same builder for chaining.</returns>
        public DataSetWriterBuilder WithKeyFrameCount(uint keyFrameCount)
        {
            m_keyFrameCount = keyFrameCount;
            return this;
        }

        /// <summary>
        /// Sets the DataSetFieldContentMask.
        /// </summary>
        /// <param name="mask">Field content mask.</param>
        /// <returns>The same builder for chaining.</returns>
        public DataSetWriterBuilder WithFieldContentMask(DataSetFieldContentMask mask)
        {
            m_dataSetFieldContentMask = (uint)mask;
            return this;
        }

        /// <summary>
        /// Sets the DataSetWriter message settings.
        /// </summary>
        /// <param name="messageSettings">Message settings body.</param>
        /// <returns>The same builder for chaining.</returns>
        public DataSetWriterBuilder WithMessageSettings(IEncodeable messageSettings)
        {
            m_messageSettings = new ExtensionObject(messageSettings);
            return this;
        }

        /// <summary>
        /// Sets the DataSetWriter transport settings.
        /// </summary>
        /// <param name="transportSettings">Transport settings body.</param>
        /// <returns>The same builder for chaining.</returns>
        public DataSetWriterBuilder WithTransportSettings(IEncodeable transportSettings)
        {
            m_transportSettings = new ExtensionObject(transportSettings);
            return this;
        }

        internal DataSetWriterDataType Build()
        {
            return new DataSetWriterDataType
            {
                Name = m_name,
                DataSetWriterId = m_dataSetWriterId,
                Enabled = m_enabled,
                DataSetName = m_dataSetName,
                KeyFrameCount = m_keyFrameCount,
                DataSetFieldContentMask = m_dataSetFieldContentMask,
                MessageSettings = m_messageSettings,
                TransportSettings = m_transportSettings
            };
        }
    }

    /// <summary>
    /// Fluent builder for a <see cref="ReaderGroupDataType"/>.
    /// </summary>
    public sealed class ReaderGroupBuilder
    {
        private readonly string m_name;
        private readonly List<DataSetReaderDataType> m_readers = [];
        private bool m_enabled = true;
        private uint m_maxNetworkMessageSize = 1500;
        private MessageSecurityMode m_securityMode = MessageSecurityMode.None;
        private string m_securityGroupId = string.Empty;
        private ArrayOf<EndpointDescription> m_securityKeyServices;

        internal ReaderGroupBuilder(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name must not be empty.", nameof(name));
            }
            m_name = name;
        }

        /// <summary>
        /// Sets the <c>Enabled</c> flag.
        /// </summary>
        /// <param name="enabled">Whether the group is enabled.</param>
        /// <returns>The same builder for chaining.</returns>
        public ReaderGroupBuilder Enabled(bool enabled = true)
        {
            m_enabled = enabled;
            return this;
        }

        /// <summary>
        /// Sets the maximum NetworkMessage size in bytes.
        /// </summary>
        /// <param name="maxNetworkMessageSize">Maximum size in bytes.</param>
        /// <returns>The same builder for chaining.</returns>
        public ReaderGroupBuilder WithMaxNetworkMessageSize(uint maxNetworkMessageSize)
        {
            m_maxNetworkMessageSize = maxNetworkMessageSize;
            return this;
        }

        /// <summary>
        /// Configures message security for the group.
        /// </summary>
        /// <param name="securityMode">Message security mode.</param>
        /// <param name="securityGroupId">SecurityGroupId.</param>
        /// <param name="securityKeyServiceUrls">SKS endpoint URLs.</param>
        /// <returns>The same builder for chaining.</returns>
        public ReaderGroupBuilder WithSecurity(
            MessageSecurityMode securityMode,
            string securityGroupId,
            params string[] securityKeyServiceUrls)
        {
            m_securityMode = securityMode;
            m_securityGroupId = securityGroupId ?? string.Empty;
            m_securityKeyServices = FluentConfigurationHelpers
                .BuildSecurityKeyServices(securityKeyServiceUrls);
            return this;
        }

        /// <summary>
        /// Adds a DataSetReader via a nested
        /// <see cref="DataSetReaderBuilder"/>.
        /// </summary>
        /// <param name="name">DataSetReader name.</param>
        /// <param name="configure">Nested builder callback.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public ReaderGroupBuilder AddDataSetReader(
            string name,
            Action<DataSetReaderBuilder> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            var builder = new DataSetReaderBuilder(name);
            configure(builder);
            m_readers.Add(builder.Build());
            return this;
        }

        internal ReaderGroupDataType Build()
        {
            return new ReaderGroupDataType
            {
                Name = m_name,
                Enabled = m_enabled,
                SecurityMode = m_securityMode,
                SecurityGroupId = m_securityGroupId,
                SecurityKeyServices = m_securityKeyServices,
                MaxNetworkMessageSize = m_maxNetworkMessageSize,
                MessageSettings = new ExtensionObject(new ReaderGroupMessageDataType()),
                DataSetReaders = new ArrayOf<DataSetReaderDataType>(m_readers.ToArray())
            };
        }
    }

    /// <summary>
    /// Fluent builder for a <see cref="DataSetReaderDataType"/>.
    /// </summary>
    public sealed class DataSetReaderBuilder
    {
        private readonly string m_name;
        private Variant m_publisherId;
        private bool m_enabled = true;
        private ushort m_writerGroupId;
        private ushort m_dataSetWriterId;
        private uint m_dataSetFieldContentMask;
        private double m_messageReceiveTimeout = 5000;
        private ExtensionObject m_messageSettings = ExtensionObject.Null;
        private ExtensionObject m_transportSettings = ExtensionObject.Null;
        private ExtensionObject m_subscribedDataSet = ExtensionObject.Null;
        private DataSetMetaDataType? m_dataSetMetaData;

        internal DataSetReaderBuilder(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name must not be empty.", nameof(name));
            }
            m_name = name;
        }

        /// <summary>
        /// Sets the <c>Enabled</c> flag.
        /// </summary>
        /// <param name="enabled">Whether the reader is enabled.</param>
        /// <returns>The same builder for chaining.</returns>
        public DataSetReaderBuilder Enabled(bool enabled = true)
        {
            m_enabled = enabled;
            return this;
        }

        /// <summary>
        /// Sets the PublisherId / WriterGroupId / DataSetWriterId filters.
        /// </summary>
        /// <param name="publisherId">PublisherId filter.</param>
        /// <param name="writerGroupId">WriterGroupId filter.</param>
        /// <param name="dataSetWriterId">DataSetWriterId filter.</param>
        /// <returns>The same builder for chaining.</returns>
        public DataSetReaderBuilder WithFilter(
            Variant publisherId,
            ushort writerGroupId,
            ushort dataSetWriterId)
        {
            m_publisherId = publisherId;
            m_writerGroupId = writerGroupId;
            m_dataSetWriterId = dataSetWriterId;
            return this;
        }

        /// <summary>
        /// Sets the DataSetFieldContentMask.
        /// </summary>
        /// <param name="mask">Field content mask.</param>
        /// <returns>The same builder for chaining.</returns>
        public DataSetReaderBuilder WithFieldContentMask(DataSetFieldContentMask mask)
        {
            m_dataSetFieldContentMask = (uint)mask;
            return this;
        }

        /// <summary>
        /// Sets the message receive timeout in milliseconds.
        /// </summary>
        /// <param name="messageReceiveTimeoutMs">Receive timeout (ms).</param>
        /// <returns>The same builder for chaining.</returns>
        public DataSetReaderBuilder WithMessageReceiveTimeout(double messageReceiveTimeoutMs)
        {
            m_messageReceiveTimeout = messageReceiveTimeoutMs;
            return this;
        }

        /// <summary>
        /// Sets the DataSetReader message settings.
        /// </summary>
        /// <param name="messageSettings">Message settings body.</param>
        /// <returns>The same builder for chaining.</returns>
        public DataSetReaderBuilder WithMessageSettings(IEncodeable messageSettings)
        {
            m_messageSettings = new ExtensionObject(messageSettings);
            return this;
        }

        /// <summary>
        /// Sets the DataSetReader transport settings.
        /// </summary>
        /// <param name="transportSettings">Transport settings body.</param>
        /// <returns>The same builder for chaining.</returns>
        public DataSetReaderBuilder WithTransportSettings(IEncodeable transportSettings)
        {
            m_transportSettings = new ExtensionObject(transportSettings);
            return this;
        }

        /// <summary>
        /// Configures a mirror SubscribedDataSet rooted at the supplied
        /// parent node name.
        /// </summary>
        /// <param name="parentNodeName">Parent node name.</param>
        /// <returns>The same builder for chaining.</returns>
        public DataSetReaderBuilder WithMirrorSubscribedDataSet(string parentNodeName)
        {
            m_subscribedDataSet = new ExtensionObject(new SubscribedDataSetMirrorDataType
            {
                ParentNodeName = parentNodeName
            });
            return this;
        }

        /// <summary>
        /// Sets the expected DataSet metadata via a nested
        /// <see cref="PublishedDataSetBuilder"/>.
        /// </summary>
        /// <param name="name">DataSet name.</param>
        /// <param name="configure">Metadata builder callback.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public DataSetReaderBuilder WithDataSetMetaData(
            string name,
            Action<PublishedDataSetBuilder> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            var builder = new PublishedDataSetBuilder(name);
            configure(builder);
            m_dataSetMetaData = builder.BuildMetaData();
            return this;
        }

        internal DataSetReaderDataType Build()
        {
            return new DataSetReaderDataType
            {
                Name = m_name,
                Enabled = m_enabled,
                PublisherId = m_publisherId,
                WriterGroupId = m_writerGroupId,
                DataSetWriterId = m_dataSetWriterId,
                DataSetFieldContentMask = m_dataSetFieldContentMask,
                MessageReceiveTimeout = m_messageReceiveTimeout,
                MessageSettings = m_messageSettings,
                TransportSettings = m_transportSettings,
                SubscribedDataSet = m_subscribedDataSet,
                DataSetMetaData = m_dataSetMetaData ?? new DataSetMetaDataType()
            };
        }
    }

    /// <summary>
    /// Shared helpers for the fluent PubSub configuration builders.
    /// </summary>
    internal static class FluentConfigurationHelpers
    {
        public static ArrayOf<EndpointDescription> BuildSecurityKeyServices(
            string[] securityKeyServiceUrls)
        {
            if (securityKeyServiceUrls is null || securityKeyServiceUrls.Length == 0)
            {
                return default;
            }
            var endpoints = new EndpointDescription[securityKeyServiceUrls.Length];
            for (int i = 0; i < securityKeyServiceUrls.Length; i++)
            {
                endpoints[i] = new EndpointDescription
                {
                    EndpointUrl = securityKeyServiceUrls[i]
                };
            }
            return new ArrayOf<EndpointDescription>(endpoints);
        }
    }
}
