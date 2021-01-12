/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.PubSub.Tests
{
    [TestFixture(Description ="Tests for UAPublisher class")]
    public class UaPublisherTests
    {
        static IList<DateTime> PublishTimes = new List<DateTime>();

        [Test(Description ="Test that PublishMessage method is called after a UAPublisher is started.")]
        [Combinatorial]
        public void ValidateUaPublisherPublishIntevalDeviation(
            [Values(100, 1000, 2000)] double publishingInterval,
            [Values(20, 30)]double maxDeviation,
            [Values(10)] int  publishTimeInSecods)
        {
            //Arrange
            PublishTimes.Clear();
            var mockConnection = new Mock<IUaPubSubConnection>();
            mockConnection.Setup(x => x.CanPublish(It.IsAny<WriterGroupDataType>())).Returns(true);
            mockConnection.Setup(x => x.CreateNetworkMessage(It.IsAny<WriterGroupDataType>()))
                .Callback(()=>PublishTimes.Add(DateTime.Now));

            WriterGroupDataType writerGroupDataType = new WriterGroupDataType();
            writerGroupDataType.PublishingInterval = publishingInterval;
            
            //Act 
            UaPublisher publisher = new UaPublisher(mockConnection.Object, writerGroupDataType);
            publisher.Start();

            //wait so many seconds
            Thread.Sleep(publishTimeInSecods*1000);
            publisher.Stop();
            List<double> publishTimesIntervals = new List<double>();
            int faultIndex = -1;
            double faultDeviation = 0;

            //Assert
            for (int i = 1; i < PublishTimes.Count; i++)
            {
                double interval = PublishTimes[i].Subtract(PublishTimes[i - 1]).TotalMilliseconds;
                double deviation = Math.Abs(publishingInterval - interval);
                if (deviation >= maxDeviation && deviation > faultDeviation)
                {
                    faultIndex = i;
                    faultDeviation = deviation;
                }                
            }            
            Assert.IsTrue(faultIndex < 0, "publishingInterval={0}, maxDeviation={1}, publishTimeInSecods={2}, deviation[{3}] = {4} has maximum deviation", publishingInterval, maxDeviation, publishTimeInSecods, faultIndex, faultDeviation);
        }
    }
}
