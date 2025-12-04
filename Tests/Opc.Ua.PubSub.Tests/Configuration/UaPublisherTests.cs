/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Threading;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.PubSub.Tests.Configuration
{
    [TestFixture(Description = "Tests for UAPublisher class")]
    [SingleThreaded]
    public class UaPublisherTests
    {
        private static List<long> s_publishTicks = [];
        private static readonly Lock s_lock = new();

        [Test(Description = "Test that PublishMessage method is called after a UAPublisher is started.")]
        [Combinatorial]
#if !CUSTOM_TESTS
        [Ignore("This test should be executed locally")]
#endif
        public void ValidateUaPublisherPublishIntervalDeviation(
            [Values(100, 1000, 2000)] double publishingInterval,
            [Values(30, 40)] double maxDeviation,
            [Values(10)] int publishTimeInSeconds)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            //Arrange
            s_publishTicks.Clear();
            var mockConnection = new Mock<IUaPubSubConnection>();
            mockConnection.Setup(x => x.CanPublish(It.IsAny<WriterGroupDataType>())).Returns(true);

            mockConnection
                .Setup(x =>
                    x.CreateNetworkMessages(
                        It.IsAny<WriterGroupDataType>(),
                        It.IsAny<WriterGroupPublishState>()))
                .Callback(() =>
                {
                    lock (s_lock)
                    {
                        s_publishTicks.Add(HiResClock.Ticks);
                    }
                });

            var writerGroupDataType = new WriterGroupDataType
            {
                PublishingInterval = publishingInterval
            };

            //Act
            var publisher = new UaPublisher(mockConnection.Object, writerGroupDataType, telemetry);
            publisher.Start();

            //wait so many seconds
            Thread.Sleep(publishTimeInSeconds * 1000);
            publisher.Stop();

            s_publishTicks = [.. from t in s_publishTicks orderby t select t];

            //Assert
            AssertPublishTicks(s_publishTicks, publishingInterval, maxDeviation, publishTimeInSeconds);
        }

        [Test(Description = "Test that PublishMessage method is called after a running UAPublisher is stopped and aftwerwords started.")]
        [Combinatorial]
#if !CUSTOM_TESTS
        [Ignore("This test should be executed locally")]
#endif
        public void ValidateRunningUaPublisherRestart(
            [Values(100, 1000, 2000)] double publishingInterval,
            [Values(30, 40)] double maxDeviation,
            [Values(10)] int publishTimeInSeconds)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            //Arrange
            s_publishTicks.Clear();
            var mockConnection = new Mock<IUaPubSubConnection>();
            mockConnection.Setup(x => x.CanPublish(It.IsAny<WriterGroupDataType>())).Returns(true);

            mockConnection
                .Setup(x =>
                    x.CreateNetworkMessages(
                        It.IsAny<WriterGroupDataType>(),
                        It.IsAny<WriterGroupPublishState>()))
                .Callback(() =>
                {
                    lock (s_lock)
                    {
                        s_publishTicks.Add(HiResClock.Ticks);
                    }
                });

            var writerGroupDataType = new WriterGroupDataType
            {
                PublishingInterval = publishingInterval
            };

            //Act
            var publisher = new UaPublisher(mockConnection.Object, writerGroupDataType, telemetry);
            publisher.Start();

            //wait so many seconds
            Thread.Sleep(publishTimeInSeconds * 1000);
            publisher.Stop();

            s_publishTicks = [.. from t in s_publishTicks orderby t select t];

            //Assert
            AssertPublishTicks(s_publishTicks, publishingInterval, maxDeviation, publishTimeInSeconds);

            s_publishTicks.Clear();
            publisher.Start();

            //wait so many seconds
            Thread.Sleep(publishTimeInSeconds * 1000);
            publisher.Stop();

            s_publishTicks = [.. from t in s_publishTicks orderby t select t];

            //Assert
            AssertPublishTicks(s_publishTicks, publishingInterval, maxDeviation, publishTimeInSeconds);
        }

        /// <summary>
        /// Assert that the publish time between two consecutive intervals is within
        /// the limit of the accepted maxDeviation
        /// </summary>
        /// <param name="publishTicks"></param>
        /// <param name="publishingInterval"></param>
        /// <param name="maxDeviation"></param>
        /// <param name="publishTimeInSeconds"></param>
        private static void AssertPublishTicks(
            List<long> publishTicks,
            double publishingInterval,
            double maxDeviation,
            int publishTimeInSeconds)
        {
            Assert.Greater(publishTicks.Count, 0);

            int faultIndex = -1;
            double faultDeviation = 0;
            for (int i = 1; i < publishTicks.Count; i++)
            {
                double interval = (publishTicks[i] - publishTicks[i - 1]) /
                    HiResClock.TicksPerMillisecond;
                if (interval != 0)
                {
                    double deviation = -1;
                    if (interval != publishingInterval)
                    {
                        deviation = Math.Abs(publishingInterval - interval);
                    }
                    if (deviation >= maxDeviation && deviation > faultDeviation)
                    {
                        faultIndex = i;
                        faultDeviation = deviation;
                    }
                }
            }
            Assert.IsTrue(
                faultIndex < 0,
                "publishingInterval={0}, maxDeviation={1}, publishTimeInSecods={2}, deviation[{3}] = {4} as max deviation",
                publishingInterval,
                maxDeviation,
                publishTimeInSeconds,
                faultIndex,
                faultDeviation);
        }
    }
}
