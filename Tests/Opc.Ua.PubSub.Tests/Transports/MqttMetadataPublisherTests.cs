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

// MqttMetadataPublisher references IMqttPubSubConnection which derives from
// IUaPubSubConnection (UA0023). Suppress the obsolete-API diagnostic.
#pragma warning disable UA0023
#pragma warning disable CS0618

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Transport;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Transports
{
    /// <summary>
    /// Coverage for <see cref="MqttMetadataPublisher"/> and its nested
    /// <see cref="MqttMetadataPublisher.MetaDataState"/>: constructor
    /// initialisation, lifecycle (Start / Stop), CanPublish delegation, and
    /// MetaDataState interval calculations. All tests are deterministic and
    /// do not open any real MQTT connections.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class MqttMetadataPublisherTests
    {
        [Test]
        public void MetaDataState_Constructor_WithoutTransportSettings_SetsUpdateTimeToZero()
        {
            var writer = new DataSetWriterDataType
            {
                DataSetWriterId = 1,
                Name = "w",
                // No TransportSettings → MetaDataUpdateTime defaults to 0
            };

            var state = new MqttMetadataPublisher.MetaDataState(writer);

            Assert.That(state.MetaDataUpdateTime, Is.Zero);
        }

        [Test]
        public void MetaDataState_Constructor_SetsDataSetWriterProperty()
        {
            var writer = new DataSetWriterDataType { DataSetWriterId = 42, Name = "w42" };

            var state = new MqttMetadataPublisher.MetaDataState(writer);

            Assert.That(state.DataSetWriter, Is.SameAs(writer));
        }

        [Test]
        public void MetaDataState_Constructor_SetsLastSendTimeToDateTimeMinValue()
        {
            var writer = new DataSetWriterDataType { DataSetWriterId = 3 };

            var state = new MqttMetadataPublisher.MetaDataState(writer);

            Assert.That(state.LastSendTime, Is.EqualTo(DateTime.MinValue));
        }

        [Test]
        public void MetaDataState_Constructor_WithBrokerTransportSettings_ExtractsMetaDataUpdateTime()
        {
            const double expectedInterval = 30_000.0;
            var transport = new BrokerDataSetWriterTransportDataType
            {
                MetaDataUpdateTime = expectedInterval
            };
            var writer = new DataSetWriterDataType
            {
                DataSetWriterId = 7,
                TransportSettings = new ExtensionObject(transport)
            };

            var state = new MqttMetadataPublisher.MetaDataState(writer);

            Assert.That(state.MetaDataUpdateTime, Is.EqualTo(expectedInterval));
        }

        [Test]
        public void MetaDataState_GetNextPublishInterval_WhenNeverSent_ReturnsZero()
        {
            // LastSendTime = DateTime.MinValue → elapsed is extremely large →
            // MetaDataUpdateTime - elapsed < 0 → Math.Max(0, negative) = 0
            const double updateTime = 5_000.0;
            var writer = new DataSetWriterDataType { DataSetWriterId = 8 };
            var state = new MqttMetadataPublisher.MetaDataState(writer)
            {
                MetaDataUpdateTime = updateTime
            };
            // LastSendTime defaults to DateTime.MinValue → return 0 (send immediately)

            double interval = state.GetNextPublishInterval();

            Assert.That(interval, Is.Zero);
        }

        [Test]
        public void MetaDataState_GetNextPublishInterval_WhenJustSent_ReturnsPositiveValue()
        {
            // Just sent → elapsed ≈ 0 → next interval ≈ MetaDataUpdateTime
            const double updateTime = 10_000.0; // 10 seconds
            var writer = new DataSetWriterDataType { DataSetWriterId = 9 };
            var state = new MqttMetadataPublisher.MetaDataState(writer)
            {
                MetaDataUpdateTime = updateTime,
                LastSendTime = DateTime.UtcNow
            };

            double interval = state.GetNextPublishInterval();

            // Should be positive and at most updateTime
            Assert.That(interval, Is.GreaterThan(0.0).And.LessThanOrEqualTo(updateTime));
        }

        [Test]
        public void MetaDataState_GetNextPublishInterval_WhenZeroUpdateTime_ReturnsZero()
        {
            var writer = new DataSetWriterDataType { DataSetWriterId = 10 };
            var state = new MqttMetadataPublisher.MetaDataState(writer)
            {
                MetaDataUpdateTime = 0,
                LastSendTime = DateTime.UtcNow
            };

            double interval = state.GetNextPublishInterval();

            Assert.That(interval, Is.Zero);
        }

        [Test]
        public void MetaDataState_LastMetaDataProperty_CanBeSetAndRetrieved()
        {
            var writer = new DataSetWriterDataType { DataSetWriterId = 11 };
            var state = new MqttMetadataPublisher.MetaDataState(writer);
            var meta = new DataSetMetaDataType { Name = "test" };

            state.LastMetaData = meta;

            Assert.That(state.LastMetaData, Is.SameAs(meta));
        }

        [Test]
        public void MetaDataState_LastSendTimeProperty_CanBeUpdated()
        {
            var writer = new DataSetWriterDataType { DataSetWriterId = 12 };
            var state = new MqttMetadataPublisher.MetaDataState(writer);
            DateTime now = DateTime.UtcNow;

            state.LastSendTime = now;

            Assert.That(state.LastSendTime, Is.EqualTo(now));
        }

        [Test]
        public void MqttMetadataPublisher_StartThenStop_DoesNotThrow()
        {
            // Use FakeTimeProvider so the IntervalRunner never actually fires
            // (no time advances automatically).
            var fakeTime = new FakeTimeProvider();
            (MqttMetadataPublisher publisher, _) = NewPublisher(fakeTime: fakeTime);

            Assert.DoesNotThrow(() =>
            {
                publisher.Start();
                publisher.Stop();
            });
        }

        [Test]
        public void MqttMetadataPublisher_StopWithoutStart_DoesNotThrow()
        {
            var fakeTime = new FakeTimeProvider();
            (MqttMetadataPublisher publisher, _) = NewPublisher(fakeTime: fakeTime);

            Assert.DoesNotThrow(() => publisher.Stop());
        }

        [Test]
        public void MqttMetadataPublisher_MultipleStartStop_DoesNotThrow()
        {
            var fakeTime = new FakeTimeProvider();
            (MqttMetadataPublisher publisher, _) = NewPublisher(fakeTime: fakeTime);

            Assert.DoesNotThrow(() =>
            {
                publisher.Start();
                publisher.Stop();
                publisher.Start();
                publisher.Stop();
            });
        }

        [Test]
        public void CanPublish_WhenConnectionAllows_ReturnsTrue()
        {
            (MqttMetadataPublisher publisher, Mock<IMqttPubSubConnection> connMock) =
                NewPublisher(canPublish: true);

            bool result = InvokeCanPublish(publisher);

            Assert.That(result, Is.True);
            connMock.Verify(
                c => c.CanPublishMetaData(
                    It.IsAny<WriterGroupDataType>(),
                    It.IsAny<DataSetWriterDataType>()),
                Times.Once);
        }

        [Test]
        public void CanPublish_WhenConnectionDenies_ReturnsFalse()
        {
            (MqttMetadataPublisher publisher, _) = NewPublisher(canPublish: false);

            bool result = InvokeCanPublish(publisher);

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Invokes the private <c>CanPublish</c> method via reflection.
        /// Reflection is used because CanPublish is private and cannot be
        /// made testable without changing production code.
        /// </summary>
        private static bool InvokeCanPublish(MqttMetadataPublisher publisher)
        {
            MethodInfo method = typeof(MqttMetadataPublisher)
                .GetMethod("CanPublish", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (bool)method.Invoke(publisher, null)!;
        }

        private static (MqttMetadataPublisher Publisher, Mock<IMqttPubSubConnection> ConnMock)
            NewPublisher(
                bool canPublish = true,
                double metaDataUpdateTime = 60_000.0,
                FakeTimeProvider? fakeTime = null)
        {
            var writerGroup = new WriterGroupDataType
            {
                WriterGroupId = 1,
                Name = "wg"
            };
            var writer = new DataSetWriterDataType
            {
                DataSetWriterId = 5,
                Name = "dw"
            };

            var connMock = new Mock<IMqttPubSubConnection>();
            connMock
                .Setup(c => c.CanPublishMetaData(
                    It.IsAny<WriterGroupDataType>(),
                    It.IsAny<DataSetWriterDataType>()))
                .Returns(canPublish);
            connMock
                .Setup(c => c.PublishNetworkMessageAsync(It.IsAny<UaNetworkMessage>()))
                .ReturnsAsync(true);
            connMock
                .Setup(c => c.CreateDataSetMetaDataNetworkMessage(
                    It.IsAny<WriterGroupDataType>(),
                    It.IsAny<DataSetWriterDataType>()))
                .Returns((UaNetworkMessage?)null);

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var publisher = new MqttMetadataPublisher(
                connMock.Object,
                writerGroup,
                writer,
                metaDataUpdateTime,
                telemetry,
                fakeTime ?? TimeProvider.System);

            return (publisher, connMock);
        }
    }
}
