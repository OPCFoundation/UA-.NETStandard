// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.Tests;
using JsonDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;
using JsonNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;
using UadpDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Groups
{
    /// <summary>
    /// Validates that <see cref="DataSetReader.Matches"/> honours the
    /// DataSetClassId filter from Part 14 §6.2.7.1 / §6.2.9: when the
    /// reader's <c>DataSetMetaData.DataSetClassId</c> is non-empty,
    /// inbound network messages must carry the same id.
    /// </summary>
    [TestFixture]
    [TestSpec("6.2.7.1", Summary = "DataSetReader.Matches DataSetClassId filter")]
    [TestSpec("6.2.9")]
    public class DataSetReaderMatchesTests
    {
        [Test]
        [TestSpec("6.2.7.1")]
        public void Matches_DataSetClassIdEmpty_IgnoresFilter()
        {
            DataSetReader reader = BuildReader(Uuid.Empty);
            var network = new UadpNetworkMessageV2 { DataSetClassId = new Uuid(Guid.NewGuid()) };
            var dsm = new UadpDataSetMessageV2 { DataSetWriterId = 5 };
            Assert.That(reader.Matches(network, dsm), Is.True);
        }

        [Test]
        [TestSpec("6.2.7.1")]
        public void Matches_MatchingClassId_Accepts()
        {
            var classId = new Uuid(Guid.NewGuid());
            DataSetReader reader = BuildReader(classId);
            var network = new UadpNetworkMessageV2 { DataSetClassId = classId };
            var dsm = new UadpDataSetMessageV2 { DataSetWriterId = 5 };
            Assert.That(reader.Matches(network, dsm), Is.True);
        }

        [Test]
        [TestSpec("6.2.7.1")]
        public void Matches_MismatchedClassId_Rejects()
        {
            var classId = new Uuid(Guid.NewGuid());
            DataSetReader reader = BuildReader(classId);
            var network = new UadpNetworkMessageV2 { DataSetClassId = new Uuid(Guid.NewGuid()) };
            var dsm = new UadpDataSetMessageV2 { DataSetWriterId = 5 };
            Assert.That(reader.Matches(network, dsm), Is.False);
        }

        [Test]
        [TestSpec("6.2.7.1")]
        public void Matches_ConfiguredButMessageMissing_Rejects()
        {
            DataSetReader reader = BuildReader(new Uuid(Guid.NewGuid()));
            var network = new UadpNetworkMessageV2 { DataSetClassId = Uuid.Empty };
            var dsm = new UadpDataSetMessageV2 { DataSetWriterId = 5 };
            Assert.That(reader.Matches(network, dsm), Is.False);
        }

        [Test]
        [TestSpec("6.2.9")]
        public void Matches_JsonMessage_HonoursClassId()
        {
            var classId = new Uuid(Guid.NewGuid());
            DataSetReader reader = BuildReader(classId);
            var network = new JsonNetworkMessageV2 { DataSetClassId = classId };
            var dsm = new JsonDataSetMessageV2 { DataSetWriterId = 5 };
            Assert.That(reader.Matches(network, dsm), Is.True);
        }

        private static DataSetReader BuildReader(Uuid classId)
        {
            var cfg = new DataSetReaderDataType
            {
                Name = "reader",
                DataSetWriterId = 5,
                DataSetMetaData = new DataSetMetaDataType
                {
                    DataSetClassId = classId
                }
            };
            return new DataSetReader(
                cfg,
                new NoopSink(),
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
        }

        private sealed class NoopSink : ISubscribedDataSetSink
        {
            public ValueTask WriteAsync(
                System.Collections.Generic.IReadOnlyList<DataSetField> fields,
                CancellationToken cancellationToken = default)
            {
                return default;
            }
        }
    }
}
