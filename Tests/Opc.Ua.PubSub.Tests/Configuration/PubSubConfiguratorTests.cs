/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.PubSub.Tests.Configuration
{
    [TestFixture(Description = "Tests for UaPubSubApplication class")]
    [Parallelizable]
    public class UaPubSubConfiguratorTests
    {
        internal static int CallCountPublishedDataSetAdded;
        internal static int CallCountPublishedDataSetRemoved;
        internal static int CallCountConnectionRemoved;
        internal static int CallCountConnectionAdded;
        internal static int CallCountDataSetReaderAdded;
        internal static int CallCountDataSetReaderRemoved;
        internal static int CallCountDataSetWriterAdded;
        internal static int CallCountDataSetWriterRemoved;
        internal static int CallCountReaderGroupAdded;
        internal static int CallCountReaderGroupRemoved;
        internal static int CallCountWriterGroupAdded;
        internal static int CallCountWriterGroupRemoved;

        internal static readonly string PublisherConfigurationFileName = Path.Combine(
            "Configuration",
            "PublisherConfiguration.xml");

        internal static readonly string SubscriberConfigurationFileName = Path.Combine(
            "Configuration",
            "SubscriberConfiguration.xml");

        private UaPubSubConfigurator m_uaPubSubConfigurator;
        private PubSubConfigurationDataType m_pubConfigurationLoaded;
        private PubSubConfigurationDataType m_subConfigurationLoaded;

        [SetUp]
        public void MyTestInitialize()
        {
            m_uaPubSubConfigurator = new UaPubSubConfigurator();

            // Attach triggers that count calls
            m_uaPubSubConfigurator.ConnectionAdded += (sender, e) => ++CallCountConnectionAdded;
            m_uaPubSubConfigurator.ConnectionRemoved += (sender, e) => ++CallCountConnectionRemoved;
            m_uaPubSubConfigurator.PublishedDataSetAdded
                += (sender, e) => ++CallCountPublishedDataSetAdded;
            m_uaPubSubConfigurator.PublishedDataSetRemoved
                += (sender, e) => ++CallCountPublishedDataSetRemoved;
            m_uaPubSubConfigurator.DataSetReaderAdded
                += (sender, e) => ++CallCountDataSetReaderAdded;
            m_uaPubSubConfigurator.DataSetReaderRemoved
                += (sender, e) => ++CallCountDataSetReaderRemoved;
            m_uaPubSubConfigurator.DataSetWriterAdded
                += (sender, e) => ++CallCountDataSetWriterAdded;
            m_uaPubSubConfigurator.DataSetWriterRemoved
                += (sender, e) => ++CallCountDataSetWriterRemoved;
            m_uaPubSubConfigurator.ReaderGroupAdded += (sender, e) => ++CallCountReaderGroupAdded;
            m_uaPubSubConfigurator.ReaderGroupRemoved
                += (sender, e) => ++CallCountReaderGroupRemoved;
            m_uaPubSubConfigurator.WriterGroupAdded += (sender, e) => ++CallCountWriterGroupAdded;
            m_uaPubSubConfigurator.WriterGroupRemoved
                += (sender, e) => ++CallCountWriterGroupRemoved;

            // A publisher configuration source
            string publisherConfigFile = Utils.GetAbsoluteFilePath(
                PublisherConfigurationFileName,
                true,
                true,
                false);
            m_pubConfigurationLoaded = UaPubSubConfigurationHelper.LoadConfiguration(
                publisherConfigFile);
            // A subscriber configuration source
            string subscriberConfigFile = Utils.GetAbsoluteFilePath(
                SubscriberConfigurationFileName,
                true,
                true,
                false);
            m_subConfigurationLoaded = UaPubSubConfigurationHelper.LoadConfiguration(
                subscriberConfigFile);
        }

        [Test(Description = "Validate ConnectionAdded event is triggered")]
        public void ValidateConnectionAdded()
        {
            int expected = CallCountConnectionAdded + 1;
            StatusCode result = m_uaPubSubConfigurator.AddConnection(
                new PubSubConnectionDataType());
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
            Assert.AreEqual(
                expected,
                CallCountConnectionAdded,
                0,
                "Expected value of CallCountConnectionAdded not equal to {0}",
                expected);
        }

        [Test(
            Description = "Validate AddConnection returns code BadBrowseNameDuplicated if duplicate name connections added."
        )]
        public void ValidateAddConnectionReturnsBadBrowseNameDuplicated()
        {
            var connection1 = new PubSubConnectionDataType { Name = "Name" };
            StatusCode result = m_uaPubSubConfigurator.AddConnection(connection1);
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

            var connection2 = new PubSubConnectionDataType { Name = "Name" };
            result = m_uaPubSubConfigurator.AddConnection(connection2);

            Assert.IsTrue(
                result == StatusCodes.BadBrowseNameDuplicated,
                "Status code received {0} instead of BadBrowseNameDuplicated",
                result);
        }

        [Test(Description = "Validate AddConnection throws ArgumentException if a connection is added twice")]
        public void ValidateAddConnectionThrowsArgumentException()
        {
            var connection1 = new PubSubConnectionDataType { Name = "Name" };
            StatusCode result = m_uaPubSubConfigurator.AddConnection(connection1);
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

            NUnit.Framework.Assert.Throws<ArgumentException>(
                () => m_uaPubSubConfigurator.AddConnection(connection1),
                "AddConnection shall throw ArgumentException if same connection is added twice");
        }

        [Test(Description = "Validate ConnectionRemoved event is triggered")]
        public void ValidateConnectionRemoved()
        {
            int expected = CallCountConnectionRemoved + 1;
            var newConnection = new PubSubConnectionDataType();
            m_uaPubSubConfigurator.AddConnection(newConnection);
            uint lastAddedConnId = m_uaPubSubConfigurator.FindIdForObject(newConnection);
            Assert.IsTrue(
                StatusCode.IsGood(m_uaPubSubConfigurator.RemoveConnection(lastAddedConnId)));
            Assert.AreEqual(expected, CallCountConnectionRemoved, 0);
        }

        [Test(Description = "Validate PublishedDataSetAdded event is triggered")]
        public void ValidatePublishedDataSetAdded()
        {
            int expected = CallCountPublishedDataSetAdded + 1;
            StatusCode result = m_uaPubSubConfigurator.AddPublishedDataSet(
                new PublishedDataSetDataType());
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
            Assert.AreEqual(expected, CallCountPublishedDataSetAdded, 0);
        }

        [Test(Description = "Validate AddPublishedDataSet returns AddPublishedDataSet")]
        public void ValidateAddPublishedDataSetBadBrowseNameDuplicated()
        {
            var publishedDataSetDataType = new PublishedDataSetDataType { Name = "Name" };
            StatusCode result = m_uaPubSubConfigurator.AddPublishedDataSet(
                publishedDataSetDataType);
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

            var publishedDataSetDataType2 = new PublishedDataSetDataType { Name = "Name" };
            result = m_uaPubSubConfigurator.AddPublishedDataSet(publishedDataSetDataType2);
            Assert.IsTrue(
                result == StatusCodes.BadBrowseNameDuplicated,
                "Status code received {0} instead of BadBrowseNameDuplicated",
                result);
        }

        [Test(Description = "Validate PublishedDataSetRemoved event is triggered")]
        public void ValidatePublishedDataSetRemoved()
        {
            int expected = CallCountPublishedDataSetRemoved + 1;
            var publishedDataSet = new PublishedDataSetDataType();
            StatusCode result = m_uaPubSubConfigurator.AddPublishedDataSet(publishedDataSet);
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

            uint lastAddedPubDsId = m_uaPubSubConfigurator.FindIdForObject(publishedDataSet);
            result = m_uaPubSubConfigurator.RemovePublishedDataSet(lastAddedPubDsId);
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
            Assert.AreEqual(expected, CallCountConnectionRemoved, 0);
        }

        [Test(Description = "Validate ReaderGroupAdded event is triggered")]
        public void ValidateReaderGroupAdded()
        {
            int expected = CallCountReaderGroupAdded + 1;
            var newConnection = new PubSubConnectionDataType();
            m_uaPubSubConfigurator.AddConnection(newConnection);
            uint lastAddedConnId = m_uaPubSubConfigurator.FindIdForObject(newConnection);
            Assert.IsTrue(
                StatusCode.IsGood(m_uaPubSubConfigurator.AddReaderGroup(
                    lastAddedConnId,
                    new ReaderGroupDataType())));
            Assert.AreEqual(expected, CallCountReaderGroupAdded, 0);
        }

        [Test(Description = "Validate ReaderGroupRemoved event is triggered")]
        public void ValidateReaderGroupRemoved()
        {
            int expected = CallCountReaderGroupRemoved + 1;
            var newConnection = new PubSubConnectionDataType();
            m_uaPubSubConfigurator.AddConnection(newConnection);
            uint lastAddedConnId = m_uaPubSubConfigurator.FindIdForObject(newConnection);
            var readerGroup = new ReaderGroupDataType();
            Assert.IsTrue(StatusCode.IsGood(
                m_uaPubSubConfigurator.AddReaderGroup(lastAddedConnId, readerGroup)));
            Assert.IsTrue(StatusCode.IsGood(m_uaPubSubConfigurator.RemoveReaderGroup(readerGroup)));
            Assert.AreEqual(expected, CallCountReaderGroupRemoved, 0);
        }

        [Test(Description = "Validate AddReaderGroup throws ArgumentException if a reader-group is added twice")]
        public void ValidateAddReaderGroupThrowsArgumentExceptionIfAddedTwice()
        {
            var newConnection = new PubSubConnectionDataType();
            m_uaPubSubConfigurator.AddConnection(newConnection);
            uint lastAddedConnId = m_uaPubSubConfigurator.FindIdForObject(newConnection);
            var readerGroup1 = new ReaderGroupDataType { Name = "Name" };
            StatusCode result = m_uaPubSubConfigurator.AddReaderGroup(
                lastAddedConnId,
                readerGroup1);
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

            NUnit.Framework.Assert.Throws<ArgumentException>(
                () => m_uaPubSubConfigurator.AddReaderGroup(lastAddedConnId, readerGroup1),
                "AddReaderGroup shall throw ArgumentException if same reader-group is added twice");
        }

        [Test(
            Description = "Validate AddReaderGroup returns code BadBrowseNameDuplicated if duplicate name group added."
        )]
        public void ValidateAddReaderGroupReturnsBadBrowseNameDuplicated()
        {
            var newConnection = new PubSubConnectionDataType();
            m_uaPubSubConfigurator.AddConnection(newConnection);
            uint lastAddedConnId = m_uaPubSubConfigurator.FindIdForObject(newConnection);
            var readerGroup = new ReaderGroupDataType { Name = "Name" };
            StatusCode result = m_uaPubSubConfigurator.AddReaderGroup(lastAddedConnId, readerGroup);
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

            var readerGroup2 = new ReaderGroupDataType { Name = "Name" };
            result = m_uaPubSubConfigurator.AddReaderGroup(lastAddedConnId, readerGroup2);

            Assert.IsTrue(
                result == StatusCodes.BadBrowseNameDuplicated,
                "Status code received {0} instead of BadBrowseNameDuplicated",
                result);
        }

        [Test(
            Description = "Validate AddReaderGroup returns code BadInvalidArgument if parentConnectionId is not a connection object."
        )]
        public void ValidateAddReaderGroupReturnsBadInvalidArgument()
        {
            var readerGroup = new ReaderGroupDataType { Name = "Name" };
            StatusCode result = m_uaPubSubConfigurator.AddReaderGroup(1, readerGroup);
            Assert.IsTrue(
                result == StatusCodes.BadInvalidArgument,
                "Status code received {0} instead of BadInvalidArgument",
                result);
        }

        [Test(
            Description = "Validate AddREaderGroup throws ArgumentException if parent id is unknown")]
        public void ValidateAddReaderGroupThrowsArgumentExceptionIfInvalidParent()
        {
            const uint lastAddedConnId = 7;
            var readerGroup = new ReaderGroupDataType { Name = "Name" };
            NUnit.Framework.Assert.Throws<ArgumentException>(
                () => m_uaPubSubConfigurator.AddReaderGroup(lastAddedConnId, readerGroup),
                "AddReaderGroup shall throw ArgumentException if readerGroup is added to invalid parent id");
        }

        [Test(Description = "Validate WriterGroupAdded event is triggered")]
        public void ValidateWriterGroupAdded()
        {
            int expected = CallCountWriterGroupAdded + 1;
            var newConnection = new PubSubConnectionDataType();
            m_uaPubSubConfigurator.AddConnection(newConnection);
            uint lastAddedConnId = m_uaPubSubConfigurator.FindIdForObject(newConnection);
            Assert.IsTrue(
                StatusCode.IsGood(m_uaPubSubConfigurator.AddWriterGroup(
                    lastAddedConnId,
                    new WriterGroupDataType())));
            Assert.AreEqual(expected, CallCountWriterGroupAdded, 0);
        }

        [Test(Description = "Validate WriterGroupRemoved event is triggered")]
        public void ValidateWriterGroupRemoved()
        {
            int expected = CallCountWriterGroupRemoved + 1;
            var newConnection = new PubSubConnectionDataType();
            m_uaPubSubConfigurator.AddConnection(newConnection);
            uint lastAddedConnId = m_uaPubSubConfigurator.FindIdForObject(newConnection);
            var writerGrp = new WriterGroupDataType();
            Assert.IsTrue(
                StatusCode.IsGood(
                    m_uaPubSubConfigurator.AddWriterGroup(lastAddedConnId, writerGrp)));
            Assert.IsTrue(StatusCode.IsGood(m_uaPubSubConfigurator.RemoveWriterGroup(writerGrp)));
            Assert.AreEqual(expected, CallCountWriterGroupRemoved, 0);
        }

        [Test(
            Description = "Validate AddWriterGroup returns code BadBrowseNameDuplicated if duplicate name writer-group added."
        )]
        public void ValidateAddWriterGroupReturnsBadBrowseNameDuplicated()
        {
            var newConnection = new PubSubConnectionDataType();
            m_uaPubSubConfigurator.AddConnection(newConnection);
            uint lastAddedConnId = m_uaPubSubConfigurator.FindIdForObject(newConnection);
            var writerGroup1 = new WriterGroupDataType { Name = "Name" };
            StatusCode result = m_uaPubSubConfigurator.AddWriterGroup(
                lastAddedConnId,
                writerGroup1);
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

            var writerGroup2 = new WriterGroupDataType { Name = "Name" };
            result = m_uaPubSubConfigurator.AddWriterGroup(lastAddedConnId, writerGroup2);

            Assert.IsTrue(
                result == StatusCodes.BadBrowseNameDuplicated,
                "Status code received {0} instead of BadBrowseNameDuplicated",
                result);
        }

        [Test(
            Description = "Validate AddWriterGroup returns code BadInvalidArgument if parentConnectionId is not a connection object."
        )]
        public void ValidateAddWriterGroupReturnsBadInvalidArgument()
        {
            var writerGroup1 = new WriterGroupDataType { Name = "Name" };
            StatusCode result = m_uaPubSubConfigurator.AddWriterGroup(1, writerGroup1);
            Assert.IsTrue(
                result == StatusCodes.BadInvalidArgument,
                "Status code received {0} instead of BadInvalidArgument",
                result);
        }

        [Test(Description = "Validate AddWriterGroup throws ArgumentException if a WriterGroup is added twice")]
        public void ValidateAddWriterGroupThrowsArgumentExceptionIfAddedTwice()
        {
            var newConnection = new PubSubConnectionDataType();
            m_uaPubSubConfigurator.AddConnection(newConnection);
            uint lastAddedConnId = m_uaPubSubConfigurator.FindIdForObject(newConnection);
            var writerGroup1 = new WriterGroupDataType { Name = "Name" };
            StatusCode result = m_uaPubSubConfigurator.AddWriterGroup(
                lastAddedConnId,
                writerGroup1);
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

            NUnit.Framework.Assert.Throws<ArgumentException>(
                () => m_uaPubSubConfigurator.AddWriterGroup(lastAddedConnId, writerGroup1),
                "AddWriterGroup shall throw ArgumentException if same writerGroup is added twice");
        }

        [Test(
            Description = "Validate AddWriterGroup throws ArgumentException if parent id is unknown")]
        public void ValidateAddWriterGroupThrowsArgumentExceptionIfInvalidParent()
        {
            const uint lastAddedConnId = 7;
            var writerGroup1 = new WriterGroupDataType { Name = "Name" };
            NUnit.Framework.Assert.Throws<ArgumentException>(
                () => m_uaPubSubConfigurator.AddWriterGroup(lastAddedConnId, writerGroup1),
                "AddWriterGroup shall throw ArgumentException if writerGroup is added to invalid parent id");
        }

        [Test(Description = "Validate DataSetReaderAdded event is triggered")]
        public void ValidateDataSetReaderAdded()
        {
            int expected = CallCountDataSetReaderAdded + 1;
            var newConnection = new PubSubConnectionDataType();
            m_uaPubSubConfigurator.AddConnection(newConnection);
            uint lastAddedConnId = m_uaPubSubConfigurator.FindIdForObject(newConnection);

            var newReaderGroup = new ReaderGroupDataType();
            m_uaPubSubConfigurator.AddReaderGroup(lastAddedConnId, newReaderGroup);
            uint lastAddedReaderGroupId = m_uaPubSubConfigurator.FindIdForObject(newReaderGroup);

            Assert.IsTrue(
                StatusCode.IsGood(
                    m_uaPubSubConfigurator.AddDataSetReader(
                        lastAddedReaderGroupId,
                        new DataSetReaderDataType())));
            Assert.AreEqual(expected, CallCountDataSetReaderAdded, 0);
        }

        [Test(Description = "Validate DataSetReaderRemoved event is triggered")]
        public void ValidateDataSetReaderRemoved()
        {
            int expected = CallCountDataSetReaderRemoved + 1;
            var newConnection = new PubSubConnectionDataType();
            m_uaPubSubConfigurator.AddConnection(newConnection);
            uint lastAddedConnId = m_uaPubSubConfigurator.FindIdForObject(newConnection);

            var newReaderGroup = new ReaderGroupDataType();
            m_uaPubSubConfigurator.AddReaderGroup(lastAddedConnId, newReaderGroup);
            uint lastAddedReaderGroupId = m_uaPubSubConfigurator.FindIdForObject(newReaderGroup);

            var dsReader = new DataSetReaderDataType();

            Assert.IsTrue(StatusCode.IsGood(
                m_uaPubSubConfigurator.AddDataSetReader(lastAddedReaderGroupId, dsReader)));
            Assert.IsTrue(StatusCode.IsGood(m_uaPubSubConfigurator.RemoveDataSetReader(dsReader)));
            Assert.AreEqual(expected, CallCountDataSetReaderRemoved, 0);
        }

        [Test(
            Description = "Validate AddDataSetReader returns code BadBrowseNameDuplicated if duplicate name dataset added."
        )]
        public void ValidateAddDataSetReaderReturnsBadBrowseNameDuplicated()
        {
            var newConnection = new PubSubConnectionDataType();
            m_uaPubSubConfigurator.AddConnection(newConnection);
            uint lastAddedConnId = m_uaPubSubConfigurator.FindIdForObject(newConnection);
            var readerGroup1 = new ReaderGroupDataType { Name = "Name" };
            StatusCode result = m_uaPubSubConfigurator.AddReaderGroup(
                lastAddedConnId,
                readerGroup1);
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

            uint lastAddedGroup = m_uaPubSubConfigurator.FindIdForObject(readerGroup1);
            var reader1 = new DataSetReaderDataType { Name = "Name" };
            result = m_uaPubSubConfigurator.AddDataSetReader(lastAddedGroup, reader1);
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

            var reader2 = new DataSetReaderDataType { Name = "Name" };
            result = m_uaPubSubConfigurator.AddDataSetReader(lastAddedGroup, reader2);

            Assert.IsTrue(
                result == StatusCodes.BadBrowseNameDuplicated,
                "Status code received {0} instead of BadBrowseNameDuplicated",
                result);
        }

        [Test(Description = "Validate AddDataSetReader throws ArgumentException if a dataset-reader is added twice")]
        public void ValidateAddDataSetReaderThrowsArgumentException()
        {
            var newConnection = new PubSubConnectionDataType();
            m_uaPubSubConfigurator.AddConnection(newConnection);
            uint lastAddedConnId = m_uaPubSubConfigurator.FindIdForObject(newConnection);
            var readerGroup1 = new ReaderGroupDataType { Name = "Name" };
            StatusCode result = m_uaPubSubConfigurator.AddReaderGroup(
                lastAddedConnId,
                readerGroup1);
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

            uint lastAddedGroup = m_uaPubSubConfigurator.FindIdForObject(readerGroup1);
            var reader1 = new DataSetReaderDataType { Name = "Name" };
            result = m_uaPubSubConfigurator.AddDataSetReader(lastAddedGroup, reader1);
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

            NUnit.Framework.Assert.Throws<ArgumentException>(
                () => m_uaPubSubConfigurator.AddDataSetReader(lastAddedGroup, reader1),
                "AddDataSetReader shall throw ArgumentException if same dataset-reader is added twice");
        }

        [Test(
            Description = "Validate AddDataSetReader returns code BadInvalidArgument if parentgroupId is not a reader-group object."
        )]
        public void ValidateAddDataSetReaderReturnsBadInvalidArgument()
        {
            var reader1 = new DataSetReaderDataType { Name = "Name" };
            StatusCode result = m_uaPubSubConfigurator.AddDataSetReader(1, reader1);
            Assert.IsTrue(
                result == StatusCodes.BadInvalidArgument,
                "Status code received {0} instead of BadInvalidArgument",
                result);
        }

        [Test(Description = "Validate DataSetWriterAdded event is triggered")]
        public void ValidateDataSetWriterAdded()
        {
            int expected = CallCountDataSetWriterAdded + 1;
            var newConnection = new PubSubConnectionDataType();
            m_uaPubSubConfigurator.AddConnection(newConnection);

            uint lastAddedConnId = m_uaPubSubConfigurator.FindIdForObject(newConnection);

            var newWriterGroup = new WriterGroupDataType();
            m_uaPubSubConfigurator.AddWriterGroup(lastAddedConnId, newWriterGroup);
            uint lastAddedWriterGroupId = m_uaPubSubConfigurator.FindIdForObject(newWriterGroup);

            Assert.IsTrue(
                StatusCode.IsGood(
                    m_uaPubSubConfigurator.AddDataSetWriter(
                        lastAddedWriterGroupId,
                        new DataSetWriterDataType())));
            Assert.AreEqual(expected, CallCountDataSetWriterAdded, 0);
        }

        [Test(Description = "Validate DataSetWriterRemoved event is triggered")]
        public void ValidateDataSetWriterRemoved()
        {
            int expected = CallCountDataSetWriterRemoved + 1;
            var newConnection = new PubSubConnectionDataType();
            m_uaPubSubConfigurator.AddConnection(newConnection);

            uint lastAddedConnId = m_uaPubSubConfigurator.FindIdForObject(newConnection);

            var newWriterGroup = new WriterGroupDataType();
            m_uaPubSubConfigurator.AddWriterGroup(lastAddedConnId, newWriterGroup);
            uint lastAddedWriterGroupId = m_uaPubSubConfigurator.FindIdForObject(newWriterGroup);

            var dsWriter = new DataSetWriterDataType();
            Assert.IsTrue(StatusCode.IsGood(
                m_uaPubSubConfigurator.AddDataSetWriter(lastAddedWriterGroupId, dsWriter)));
            Assert.IsTrue(StatusCode.IsGood(m_uaPubSubConfigurator.RemoveDataSetWriter(dsWriter)));
            Assert.AreEqual(expected, CallCountDataSetWriterRemoved, 0);
        }

        [Test(
            Description = "Validate AddDataSetWriter returns code BadBrowseNameDuplicated if duplicate name dataset added."
        )]
        public void ValidateAddDataSetWriterReturnsBadBrowseNameDuplicated()
        {
            var newConnection = new PubSubConnectionDataType();
            m_uaPubSubConfigurator.AddConnection(newConnection);
            uint lastAddedConnId = m_uaPubSubConfigurator.FindIdForObject(newConnection);
            var writerGroup1 = new WriterGroupDataType { Name = "Name" };
            StatusCode result = m_uaPubSubConfigurator.AddWriterGroup(
                lastAddedConnId,
                writerGroup1);
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

            uint lastAddedGroup = m_uaPubSubConfigurator.FindIdForObject(writerGroup1);
            var writer1 = new DataSetWriterDataType { Name = "Name" };
            result = m_uaPubSubConfigurator.AddDataSetWriter(lastAddedGroup, writer1);
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

            var writer2 = new DataSetWriterDataType { Name = "Name" };
            result = m_uaPubSubConfigurator.AddDataSetWriter(lastAddedGroup, writer2);

            Assert.IsTrue(
                result == StatusCodes.BadBrowseNameDuplicated,
                "Status code received {0} instead of BadBrowseNameDuplicated",
                result);
        }

        [Test(Description = "Validate AddDataSetWriter throws ArgumentException if a dataset-reader is added twice")]
        public void ValidateAddDataSetWriterThrowsArgumentException()
        {
            var newConnection = new PubSubConnectionDataType();
            m_uaPubSubConfigurator.AddConnection(newConnection);
            uint lastAddedConnId = m_uaPubSubConfigurator.FindIdForObject(newConnection);
            var writerGroup1 = new WriterGroupDataType { Name = "Name" };
            StatusCode result = m_uaPubSubConfigurator.AddWriterGroup(
                lastAddedConnId,
                writerGroup1);
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

            uint lastAddedGroup = m_uaPubSubConfigurator.FindIdForObject(writerGroup1);
            var writer1 = new DataSetWriterDataType { Name = "Name" };
            result = m_uaPubSubConfigurator.AddDataSetWriter(lastAddedGroup, writer1);
            Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

            NUnit.Framework.Assert.Throws<ArgumentException>(
                () => m_uaPubSubConfigurator.AddDataSetWriter(lastAddedGroup, writer1),
                "AddDataSetWriter shall throw ArgumentException if same dataset-reader is added twice");
        }

        [Test(
            Description = "Validate AddDataSetWriter returns code BadInvalidArgument if parentgroupId is not a reader-group object."
        )]
        public void ValidateAddDataSetWriterReturnsBadInvalidArgument()
        {
            var writer1 = new DataSetWriterDataType { Name = "Name" };
            StatusCode result = m_uaPubSubConfigurator.AddDataSetWriter(1, writer1);
            Assert.IsTrue(
                result == StatusCodes.BadInvalidArgument,
                "Status code received {0} instead of BadInvalidArgument",
                result);
        }

        [Test(Description = "Validate Publisher ConnectionAdded event is reflected in the parent UaPubSubApplication")]
        public void ValidatePubConnectionAddedAndReflectedInApplication()
        {
            // Prepare an empty configuration for testing the interaction between UaPubSubApplication
            // and UaPubSubConfigurator
            var uaPubSubApplication = UaPubSubApplication.Create();

            int targetIdx = uaPubSubApplication.PubSubConnections.Count;
            foreach (PubSubConnectionDataType pscon in m_pubConfigurationLoaded.Connections)
            {
                StatusCode result = uaPubSubApplication.UaPubSubConfigurator.AddConnection(pscon);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
                Assert.AreEqual(
                    uaPubSubApplication.PubSubConnections[targetIdx].PubSubConnectionConfiguration,
                    pscon);

                targetIdx++;
            }
        }

        [Test(
            Description = "Validate Publisher ConnectionRemoved event is reflected in the parent UaPubSubApplication"
        )]
        public void ValidatePubConnectionRemovedAndReflectedInApplication()
        {
            // Prepare an empty configuration for testing the interaction between UaPubSubApplication
            // and UaPubSubConfigurator
            var uaPubSubApplication = UaPubSubApplication.Create();

            int initialCount = uaPubSubApplication.PubSubConnections.Count;
            foreach (PubSubConnectionDataType pscon in m_pubConfigurationLoaded.Connections)
            {
                StatusCode result = uaPubSubApplication.UaPubSubConfigurator.AddConnection(pscon);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
                result = uaPubSubApplication.UaPubSubConfigurator.RemoveConnection(pscon);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
                Assert.AreEqual(initialCount, uaPubSubApplication.PubSubConnections.Count);
            }
        }

        [Test(Description = "Validate Publisher AddWriterGroup  is reflected in the parent UaPubSubApplication")]
        public void ValidateWriterGroupAddedAndReflectedInApplication()
        {
            // Create an UaPubSubApplication with an empty configuration
            var uaPubSubApplication = UaPubSubApplication.Create();

            int targetIdx = uaPubSubApplication.PubSubConnections.Count;
            foreach (PubSubConnectionDataType pscon in m_pubConfigurationLoaded.Connections)
            {
                var psconNew = (PubSubConnectionDataType)pscon.MemberwiseClone();

                StatusCode result = uaPubSubApplication.UaPubSubConfigurator
                    .AddConnection(psconNew);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

                // Add the first writer group in the configuration and check that it is reflected in Application
                int lastAddedWriterGroupIdx = uaPubSubApplication
                    .PubSubConnections[targetIdx]
                    .PubSubConnectionConfiguration
                    .WriterGroups
                    .Count;

                var writerGroup = (WriterGroupDataType)psconNew.WriterGroups[0].MemberwiseClone();
                writerGroup.Name += "_";

                uint lastAddedConnId = uaPubSubApplication.UaPubSubConfigurator
                    .FindIdForObject(psconNew);
                uaPubSubApplication.UaPubSubConfigurator
                    .AddWriterGroup(lastAddedConnId, writerGroup);

                Assert.AreEqual(
                    uaPubSubApplication.PubSubConnections[targetIdx].PubSubConnectionConfiguration,
                    psconNew);
                Assert.AreEqual(
                    uaPubSubApplication.PubSubConnections[targetIdx].PubSubConnectionConfiguration
                        .WriterGroups[
                            lastAddedWriterGroupIdx
                            ],
                    writerGroup);
                break;
            }
        }

        [Test(Description = "Validate Publisher RemoveWriterGroup  is reflected in the parent UaPubSubApplication")]
        public void ValidateWriterGroupRemovedAndReflectedInApplication()
        {
            // Create an UaPubSubApplication with an empty configuration
            var uaPubSubApplication = UaPubSubApplication.Create();

            int targetIdx = uaPubSubApplication.PubSubConnections.Count;
            foreach (PubSubConnectionDataType pscon in m_pubConfigurationLoaded.Connections)
            {
                var psconNew = (PubSubConnectionDataType)pscon.MemberwiseClone();

                StatusCode result = uaPubSubApplication.UaPubSubConfigurator
                    .AddConnection(psconNew);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

                // Add the first writer group in the configuration and check that it is reflected in Application

                var writerGroup = (WriterGroupDataType)psconNew.WriterGroups[0].MemberwiseClone();
                writerGroup.Name += "_";

                uint lastAddedConnId = uaPubSubApplication.UaPubSubConfigurator
                    .FindIdForObject(psconNew);

                int nrInitialWriterGroups = uaPubSubApplication
                    .PubSubConnections[targetIdx]
                    .PubSubConnectionConfiguration
                    .WriterGroups
                    .Count;

                result = uaPubSubApplication.UaPubSubConfigurator
                    .AddWriterGroup(lastAddedConnId, writerGroup);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
                result = uaPubSubApplication.UaPubSubConfigurator.RemoveWriterGroup(writerGroup);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

                int nrActualWriterGroups = uaPubSubApplication
                    .PubSubConnections[targetIdx]
                    .PubSubConnectionConfiguration
                    .WriterGroups
                    .Count;

                Assert.AreEqual(nrInitialWriterGroups, nrActualWriterGroups);

                break;
            }
        }

        [Test(Description = "Validate Publisher AddDataSetWriter  is reflected in the parent UaPubSubApplication")]
        public void ValidateDataSetWriterAddedAndReflectedInApplication()
        {
            // Create an UaPubSubApplication with an empty configuration
            var uaPubSubApplication = UaPubSubApplication.Create();

            int targetIdx = uaPubSubApplication.PubSubConnections.Count;
            foreach (PubSubConnectionDataType pscon in m_pubConfigurationLoaded.Connections)
            {
                var psconNew = (PubSubConnectionDataType)pscon.MemberwiseClone();

                StatusCode result = uaPubSubApplication.UaPubSubConfigurator
                    .AddConnection(psconNew);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

                // Add the first writer group in the configuration and check that it is reflected in Application
                int lastAddedWriterGroupIdx = uaPubSubApplication
                    .PubSubConnections[targetIdx]
                    .PubSubConnectionConfiguration
                    .WriterGroups
                    .Count;

                var writerGroup = (WriterGroupDataType)psconNew.WriterGroups[0].MemberwiseClone();
                uint lastAddedConnId = uaPubSubApplication.UaPubSubConfigurator
                    .FindIdForObject(psconNew);
                writerGroup.Name += "_";

                result = uaPubSubApplication.UaPubSubConfigurator
                    .AddWriterGroup(lastAddedConnId, writerGroup);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

                uint addedWriterGroupId = uaPubSubApplication.UaPubSubConfigurator
                    .FindIdForObject(writerGroup);
                Assert.AreEqual(
                    uaPubSubApplication.PubSubConnections[targetIdx].PubSubConnectionConfiguration,
                    psconNew);
                Assert.AreEqual(
                    uaPubSubApplication.PubSubConnections[targetIdx].PubSubConnectionConfiguration
                        .WriterGroups[
                            lastAddedWriterGroupIdx
                            ],
                    writerGroup);

                // Add the first data set writer in the configuration and check that it is reflected in Application
                int lastAddedDataSetWriterIdx = uaPubSubApplication
                    .PubSubConnections[targetIdx]
                    .PubSubConnectionConfiguration
                    .WriterGroups[0]
                    .DataSetWriters
                    .Count;
                var dataSetWriter = (DataSetWriterDataType)psconNew.WriterGroups[0]
                    .DataSetWriters[0]
                    .MemberwiseClone();
                dataSetWriter.Name += "_";
                result = uaPubSubApplication.UaPubSubConfigurator
                    .AddDataSetWriter(addedWriterGroupId, dataSetWriter);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

                Assert.AreEqual(
                    uaPubSubApplication
                        .PubSubConnections[targetIdx]
                        .PubSubConnectionConfiguration
                        .WriterGroups[lastAddedWriterGroupIdx]
                        .DataSetWriters[lastAddedDataSetWriterIdx],
                    dataSetWriter);
                break;
            }
        }

        [Test(Description = "Validate Publisher RemoveDataSetWriter  is reflected in the parent UaPubSubApplication")]
        public void ValidateDataSetWriterRemovedAndReflectedInApplication()
        {
            // Create an UaPubSubApplication with an empty configuration
            var uaPubSubApplication = UaPubSubApplication.Create();

            int targetIdx = uaPubSubApplication.PubSubConnections.Count;
            foreach (PubSubConnectionDataType pscon in m_pubConfigurationLoaded.Connections)
            {
                var psconNew = (PubSubConnectionDataType)pscon.MemberwiseClone();

                StatusCode result = uaPubSubApplication.UaPubSubConfigurator
                    .AddConnection(psconNew);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

                // Add the first writer group in the configuration and check that it is reflected in Application
                int lastAddedWriterGroupIdx = uaPubSubApplication
                    .PubSubConnections[targetIdx]
                    .PubSubConnectionConfiguration
                    .WriterGroups
                    .Count;

                var writerGroup = (WriterGroupDataType)psconNew.WriterGroups[0].MemberwiseClone();
                uint lastAddedConnId = uaPubSubApplication.UaPubSubConfigurator
                    .FindIdForObject(psconNew);
                writerGroup.Name += "_";

                result = uaPubSubApplication.UaPubSubConfigurator
                    .AddWriterGroup(lastAddedConnId, writerGroup);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

                uint addedWriterGroupId = uaPubSubApplication.UaPubSubConfigurator
                    .FindIdForObject(writerGroup);
                Assert.AreEqual(
                    uaPubSubApplication.PubSubConnections[targetIdx].PubSubConnectionConfiguration,
                    psconNew);
                Assert.AreEqual(
                    uaPubSubApplication.PubSubConnections[targetIdx].PubSubConnectionConfiguration
                        .WriterGroups[
                            lastAddedWriterGroupIdx
                            ],
                    writerGroup);

                // Add the first data set writer in the configuration and check that it is reflected in Application
                var dataSetWriter = (DataSetWriterDataType)psconNew.WriterGroups[0]
                    .DataSetWriters[0]
                    .MemberwiseClone();
                dataSetWriter.Name += "_";

                int nrInitialDsWriters = uaPubSubApplication
                    .PubSubConnections[targetIdx]
                    .PubSubConnectionConfiguration
                    .WriterGroups[0]
                    .DataSetWriters
                    .Count;

                result = uaPubSubApplication.UaPubSubConfigurator
                    .AddDataSetWriter(addedWriterGroupId, dataSetWriter);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
                result = uaPubSubApplication.UaPubSubConfigurator
                    .RemoveDataSetWriter(dataSetWriter);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

                int nrActualDsWriters = uaPubSubApplication
                    .PubSubConnections[targetIdx]
                    .PubSubConnectionConfiguration
                    .WriterGroups[0]
                    .DataSetWriters
                    .Count;

                Assert.AreEqual(nrInitialDsWriters, nrActualDsWriters);
                break;
            }
        }

        [Test(Description = "Validate Publisher AddPublishedSet is reflected in the parent UaPubSubApplication")]
        public void ValidatePublishedDataSetAddedAndReflectedInApplication()
        {
            // Prepare an empty configuration for testing the interaction between UaPubSubApplication
            // and UaPubSubConfigurator
            var appConfPubSubConfiguration = new PubSubConfigurationDataType
            {
                Connections = [],
                PublishedDataSets = []
            };
            var uaPubSubApplication = UaPubSubApplication.Create(appConfPubSubConfiguration);

            int targetIdx = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration
                .PublishedDataSets
                .Count;
            foreach (PublishedDataSetDataType pds in m_pubConfigurationLoaded.PublishedDataSets)
            {
                StatusCode result = uaPubSubApplication.UaPubSubConfigurator
                    .AddPublishedDataSet(pds);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
                Assert.AreEqual(
                    uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration
                        .PublishedDataSets[targetIdx],
                    pds);

                targetIdx++;
            }
        }

        [Test(Description = "Validate Publisher RemovePublishedSet is reflected in the parent UaPubSubApplication")]
        public void ValidatePublishedDataSetRemovedAndReflectedInApplication()
        {
            // Prepare an empty configuration for testing the interaction between UaPubSubApplication
            // and UaPubSubConfigurator
            var appConfPubSubConfiguration = new PubSubConfigurationDataType
            {
                Connections = [],
                PublishedDataSets = []
            };
            var uaPubSubApplication = UaPubSubApplication.Create(appConfPubSubConfiguration);

            int initialNrPublishedDs = uaPubSubApplication
                .UaPubSubConfigurator
                .PubSubConfiguration
                .PublishedDataSets
                .Count;
            foreach (PublishedDataSetDataType pds in m_pubConfigurationLoaded.PublishedDataSets)
            {
                StatusCode result = uaPubSubApplication.UaPubSubConfigurator
                    .AddPublishedDataSet(pds);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
                result = uaPubSubApplication.UaPubSubConfigurator.RemovePublishedDataSet(pds);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
            }
            int actualNrPublishedDs = uaPubSubApplication
                .UaPubSubConfigurator
                .PubSubConfiguration
                .PublishedDataSets
                .Count;
            Assert.AreEqual(initialNrPublishedDs, actualNrPublishedDs);
        }

        [Test(Description = "Validate Subscriber ConnectionAdded event is reflected in the parent UaPubSubApplication")]
        public void ValidateSubConnectionAddedAndReflectedInApplication()
        {
            // Prepare an empty configuration for testing the interaction between UaPubSubApplication
            // and UaPubSubConfigurator
            var appConfPubSubConfiguration = new PubSubConfigurationDataType
            {
                Connections = [],
                PublishedDataSets = []
            };
            var uaPubSubApplication = UaPubSubApplication.Create(appConfPubSubConfiguration);

            int targetIdx = uaPubSubApplication.PubSubConnections.Count;
            foreach (PubSubConnectionDataType pscon in m_subConfigurationLoaded.Connections)
            {
                StatusCode result = uaPubSubApplication.UaPubSubConfigurator.AddConnection(pscon);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
                Assert.AreEqual(
                    uaPubSubApplication.PubSubConnections[targetIdx].PubSubConnectionConfiguration,
                    pscon);

                targetIdx++;
            }
        }

        [Test(
            Description = "Validate Subscriber ConnectionRemoved event is reflected in the parent UaPubSubApplication"
        )]
        public void ValidateSubConnectionRemovedAndReflectedInApplication()
        {
            // Prepare an empty configuration for testing the interaction between UaPubSubApplication
            // and UaPubSubConfigurator
            var uaPubSubApplication = UaPubSubApplication.Create();

            int initialCount = uaPubSubApplication.PubSubConnections.Count;
            foreach (PubSubConnectionDataType pscon in m_subConfigurationLoaded.Connections)
            {
                StatusCode result = uaPubSubApplication.UaPubSubConfigurator.AddConnection(pscon);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
                result = uaPubSubApplication.UaPubSubConfigurator.RemoveConnection(pscon);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
                Assert.AreEqual(initialCount, uaPubSubApplication.PubSubConnections.Count);
            }
        }

        [Test(Description = "Validate Subscriber AddReaderGroup  is reflected in the parent UaPubSubApplication")]
        public void ValidateReaderGroupAddedAndReflectedInApplication()
        {
            // Prepare an empty configuration for testing the interaction between UaPubSubApplication
            // and UaPubSubConfigurator
            var appConfPubSubConfiguration = new PubSubConfigurationDataType
            {
                Connections = [],
                PublishedDataSets = []
            };
            var uaPubSubApplication = UaPubSubApplication.Create(appConfPubSubConfiguration);

            int targetIdx = uaPubSubApplication.PubSubConnections.Count;
            foreach (PubSubConnectionDataType pscon in m_subConfigurationLoaded.Connections)
            {
                var psconNew = (PubSubConnectionDataType)pscon.MemberwiseClone();

                StatusCode result = uaPubSubApplication.UaPubSubConfigurator
                    .AddConnection(psconNew);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

                // Add the first writer group in the configuration and check that it is reflected in Application
                int lastAddedReaderGroupIdx = uaPubSubApplication
                    .PubSubConnections[targetIdx]
                    .PubSubConnectionConfiguration
                    .ReaderGroups
                    .Count;

                var readerGroup = (ReaderGroupDataType)psconNew.ReaderGroups[0].MemberwiseClone();
                readerGroup.Name += "_";
                uint lastAddedConnId = uaPubSubApplication.UaPubSubConfigurator
                    .FindIdForObject(psconNew);

                uaPubSubApplication.UaPubSubConfigurator
                    .AddReaderGroup(lastAddedConnId, readerGroup);

                Assert.AreEqual(
                    uaPubSubApplication.PubSubConnections[targetIdx].PubSubConnectionConfiguration,
                    psconNew);
                Assert.AreEqual(
                    uaPubSubApplication.PubSubConnections[targetIdx].PubSubConnectionConfiguration
                        .ReaderGroups[
                            lastAddedReaderGroupIdx
                            ],
                    readerGroup);
                break;
            }
        }

        [Test(Description = "Validate Subscriber RemoveReaderGroup  is reflected in the parent UaPubSubApplication")]
        public void ValidateReaderGroupRemovedAndReflectedInApplication()
        {
            // Prepare an empty configuration for testing the interaction between UaPubSubApplication
            // and UaPubSubConfigurator
            var appConfPubSubConfiguration = new PubSubConfigurationDataType
            {
                Connections = [],
                PublishedDataSets = []
            };
            var uaPubSubApplication = UaPubSubApplication.Create(appConfPubSubConfiguration);

            int targetIdx = uaPubSubApplication.PubSubConnections.Count;
            foreach (PubSubConnectionDataType pscon in m_subConfigurationLoaded.Connections)
            {
                var psconNew = (PubSubConnectionDataType)pscon.MemberwiseClone();

                StatusCode result = uaPubSubApplication.UaPubSubConfigurator
                    .AddConnection(psconNew);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

                // Add the first writer group in the configuration and check that it is reflected in Application
                var readerGroup = (ReaderGroupDataType)psconNew.ReaderGroups[0].MemberwiseClone();
                readerGroup.Name += "_";
                uint lastAddedConnId = uaPubSubApplication.UaPubSubConfigurator
                    .FindIdForObject(psconNew);

                int nrInitialReaderGroups = uaPubSubApplication
                    .PubSubConnections[targetIdx]
                    .PubSubConnectionConfiguration
                    .ReaderGroups
                    .Count;

                result = uaPubSubApplication.UaPubSubConfigurator
                    .AddReaderGroup(lastAddedConnId, readerGroup);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
                result = uaPubSubApplication.UaPubSubConfigurator.RemoveReaderGroup(readerGroup);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

                int nrActualReaderGroups = uaPubSubApplication
                    .PubSubConnections[targetIdx]
                    .PubSubConnectionConfiguration
                    .ReaderGroups
                    .Count;

                Assert.AreEqual(nrInitialReaderGroups, nrActualReaderGroups);
                break;
            }
        }

        [Test(Description = "Validate Subscriber AddDataSetReader  is reflected in the parent UaPubSubApplication")]
        public void ValidateDataSetReaderAddedAndReflectedInApplication()
        {
            // Create UaPubSubConfigurator with empty configuration
            var uaPubSubApplication = UaPubSubApplication.Create();

            int targetIdx = uaPubSubApplication.PubSubConnections.Count;
            foreach (PubSubConnectionDataType pscon in m_subConfigurationLoaded.Connections)
            {
                var psconNew = (PubSubConnectionDataType)pscon.MemberwiseClone();

                StatusCode result = uaPubSubApplication.UaPubSubConfigurator
                    .AddConnection(psconNew);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

                // Add the first Reader group in the configuration and check that it is reflected in Application
                int lastAddedReaderGroupIdx = uaPubSubApplication
                    .PubSubConnections[targetIdx]
                    .PubSubConnectionConfiguration
                    .ReaderGroups
                    .Count;

                var readerGroup = (ReaderGroupDataType)psconNew.ReaderGroups[0].MemberwiseClone();
                readerGroup.Name += "_";
                uint lastAddedConnId = uaPubSubApplication.UaPubSubConfigurator
                    .FindIdForObject(psconNew);

                result = uaPubSubApplication.UaPubSubConfigurator
                    .AddReaderGroup(lastAddedConnId, readerGroup);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
                Assert.AreEqual(
                    uaPubSubApplication.PubSubConnections[targetIdx].PubSubConnectionConfiguration,
                    psconNew);
                Assert.AreEqual(
                    uaPubSubApplication.PubSubConnections[targetIdx].PubSubConnectionConfiguration
                        .ReaderGroups[
                            lastAddedReaderGroupIdx
                            ],
                    readerGroup);

                // Add the first data set Reader in the configuration and check that it is reflected in Application
                int lastAddedDataSetReaderIdx = uaPubSubApplication
                    .PubSubConnections[targetIdx]
                    .PubSubConnectionConfiguration
                    .ReaderGroups[0]
                    .DataSetReaders
                    .Count;
                var dataSetReader = (DataSetReaderDataType)psconNew.ReaderGroups[0]
                    .DataSetReaders[0]
                    .MemberwiseClone();
                dataSetReader.Name += "_";
                uint addedReaderGroupId = uaPubSubApplication.UaPubSubConfigurator
                    .FindIdForObject(readerGroup);
                result = uaPubSubApplication.UaPubSubConfigurator
                    .AddDataSetReader(addedReaderGroupId, dataSetReader);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
                Assert.AreEqual(
                    uaPubSubApplication
                        .PubSubConnections[targetIdx]
                        .PubSubConnectionConfiguration
                        .ReaderGroups[lastAddedReaderGroupIdx]
                        .DataSetReaders[lastAddedDataSetReaderIdx],
                    dataSetReader);
                break;
            }
        }

        [Test(Description = "Validate Subscriber AddDataSetReader  is reflected in the parent UaPubSubApplication")]
        public void ValidateDataSetReaderRemovedAndReflectedInApplication()
        {
            // Create UaPubSubConfigurator with empty configuration
            var uaPubSubApplication = UaPubSubApplication.Create();

            int targetIdx = uaPubSubApplication.PubSubConnections.Count;
            foreach (PubSubConnectionDataType pscon in m_subConfigurationLoaded.Connections)
            {
                var psconNew = (PubSubConnectionDataType)pscon.MemberwiseClone();

                StatusCode result = uaPubSubApplication.UaPubSubConfigurator
                    .AddConnection(psconNew);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

                // Add the first Reader group in the configuration and check that it is reflected in Application
                int lastAddedReaderGroupIdx = uaPubSubApplication
                    .PubSubConnections[targetIdx]
                    .PubSubConnectionConfiguration
                    .ReaderGroups
                    .Count;

                var readerGroup = (ReaderGroupDataType)psconNew.ReaderGroups[0].MemberwiseClone();
                readerGroup.Name += "_";
                uint lastAddedConnId = uaPubSubApplication.UaPubSubConfigurator
                    .FindIdForObject(psconNew);

                result = uaPubSubApplication.UaPubSubConfigurator
                    .AddReaderGroup(lastAddedConnId, readerGroup);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
                uint addedReaderGroupId = uaPubSubApplication.UaPubSubConfigurator
                    .FindIdForObject(readerGroup);
                Assert.AreEqual(
                    uaPubSubApplication.PubSubConnections[targetIdx].PubSubConnectionConfiguration,
                    psconNew);
                Assert.AreEqual(
                    uaPubSubApplication.PubSubConnections[targetIdx].PubSubConnectionConfiguration
                        .ReaderGroups[
                            lastAddedReaderGroupIdx
                            ],
                    readerGroup);

                // Add the first data set Reader in the configuration and check that it is reflected in Application
                var dataSetReader = (DataSetReaderDataType)psconNew.ReaderGroups[0]
                    .DataSetReaders[0]
                    .MemberwiseClone();
                dataSetReader.Name += "_";

                int nrInitialDsReaders = uaPubSubApplication
                    .PubSubConnections[targetIdx]
                    .PubSubConnectionConfiguration
                    .ReaderGroups[0]
                    .DataSetReaders
                    .Count;

                result = uaPubSubApplication.UaPubSubConfigurator
                    .AddDataSetReader(addedReaderGroupId, dataSetReader);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);
                result = uaPubSubApplication.UaPubSubConfigurator
                    .RemoveDataSetReader(dataSetReader);
                Assert.IsTrue(StatusCode.IsGood(result), "Status code received: " + result);

                int nrActualDsReaders = uaPubSubApplication
                    .PubSubConnections[targetIdx]
                    .PubSubConnectionConfiguration
                    .ReaderGroups[0]
                    .DataSetReaders
                    .Count;

                Assert.AreEqual(nrInitialDsReaders, nrActualDsReaders);

                break;
            }
        }
    }
}
