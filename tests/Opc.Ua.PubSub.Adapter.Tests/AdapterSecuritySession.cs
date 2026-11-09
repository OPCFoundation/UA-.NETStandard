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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Models a source server with complete values and server-side range projection, without a transport.
    /// </summary>
    internal sealed class AdapterSecuritySession
    {
        /// <summary>
        /// Creates a recording session whose reads and monitored notifications project the supplied complete values.
        /// </summary>
        public AdapterSecuritySession(Dictionary<(NodeId Node, uint Attribute), DataValue> values)
        {
            m_values = values;
            Session = AdapterTestHelpers.ConnectedSession();
            Session.Setup(s => s.CreateDataChangeSubscriptionAsync(
                    It.IsAny<double>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    var subscription = new FakeDataChangeSubscription();
                    Subscriptions.Add(subscription);
                    return new ValueTask<IDataChangeSubscription>(subscription);
                });
            Session.Setup(s => s.ReadAsync(
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((ArrayOf<ReadValueId> reads, CancellationToken ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    Reads.Add(reads);
                    var results = new DataValue[reads.Count];
                    for (int i = 0; i < reads.Count; i++)
                    {
                        ReadValueId read = reads[i];
                        uint attribute = read.AttributeId == 0 ? Attributes.Value : read.AttributeId;
                        results[i] = m_values.TryGetValue((read.NodeId, attribute), out DataValue value)
                            ? Project(value, read.IndexRange)
                            : DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown);
                    }
                    return new ValueTask<ArrayOf<DataValue>>(results.ToArrayOf());
                });
        }

        /// <summary>
        /// The external-session boundary consumed by the real adapter.
        /// </summary>
        public Mock<IServerSession> Session { get; }

        /// <summary>
        /// The exact read requests submitted by the coordinator and metadata builder.
        /// </summary>
        public List<ArrayOf<ReadValueId>> Reads { get; } = [];

        /// <summary>
        /// The subscription instances created on this session.
        /// </summary>
        public List<FakeDataChangeSubscription> Subscriptions { get; } = [];

        /// <summary>
        /// Delivers server-projected changes using each monitored item's own range and handle.
        /// </summary>
        public void Notify(NodeId nodeId, uint attributeId, in DataValue completeValue)
        {
            m_values[(nodeId, attributeId)] = completeValue;
            foreach (FakeDataChangeSubscription subscription in Subscriptions)
            {
                for (int i = 0; i < subscription.MonitoredItems.Count; i++)
                {
                    (NodeId NodeId, uint AttributeId, double SamplingMs) item = subscription.MonitoredItems[i];
                    if (item.NodeId == nodeId && item.AttributeId == attributeId)
                    {
                        subscription.Raise(
                            (uint)i + 1, nodeId, Project(completeValue, subscription.IndexRanges[i]));
                    }
                }
            }
        }

        /// <summary>
        /// Builds a configured dataset with declared Variant metadata so sampling does not need metadata I/O.
        /// </summary>
        public static PublishedDataSetDataType DataSet(
            string name,
            params PublishedVariableDataType[] variables)
        {
            var fields = new FieldMetaData[variables.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                fields[i] = new FieldMetaData
                {
                    Name = $"Field{i + 1}",
                    BuiltInType = (byte)BuiltInType.Variant,
                    DataType = DataTypeIds.BaseDataType,
                    ValueRank = ValueRanks.Any
                };
            }
            return new PublishedDataSetDataType
            {
                Name = name,
                DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                {
                    PublishedData = variables.ToArrayOf()
                }),
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = name,
                    Fields = fields.ToArrayOf(),
                    ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 1 }
                }
            };
        }

        /// <summary>
        /// Applies the standard numeric-range semantics at the modeled server boundary, retaining quality and time.
        /// </summary>
        private static DataValue Project(in DataValue value, string? indexRange)
        {
            if (string.IsNullOrEmpty(indexRange) || value.IsBad)
            {
                return value;
            }
            Variant projection = value.WrappedValue;
            StatusCode status = NumericRange.Parse(indexRange).ApplyRange(ref projection);
            return StatusCode.IsBad(status)
                ? value.WithWrappedValue(Variant.Null).WithStatus(status)
                : value.WithWrappedValue(projection);
        }

        /// <summary>
        /// Complete source values, including elements that the configured publisher must never export.
        /// </summary>
        private readonly Dictionary<(NodeId Node, uint Attribute), DataValue> m_values;
    }
}
