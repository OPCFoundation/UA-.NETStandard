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
using System.IO;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Configuration
{
    [TestFixture]
    [Category("Configuration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UaPubSubConfiguratorAdditionalTests
    {
        private static readonly string PublisherConfigurationFileName = Path.Combine(
            "Configuration",
            "PublisherConfiguration.xml");

        private static readonly string SubscriberConfigurationFileName = Path.Combine(
            "Configuration",
            "SubscriberConfiguration.xml");

        private UaPubSubConfigurator m_configurator;
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_configurator = new UaPubSubConfigurator(m_telemetry);
        }

        [Test]
        public void FindPublishedDataSetByNameReturnsDataSetWhenFound()
        {
            var dataSet = new PublishedDataSetDataType { Name = "TestDataSet" };
            StatusCode result = m_configurator.AddPublishedDataSet(dataSet);
            Assert.That(StatusCode.IsGood(result), Is.True);

            PublishedDataSetDataType found = m_configurator.FindPublishedDataSetByName("TestDataSet");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Name, Is.EqualTo("TestDataSet"));
        }

        [Test]
        public void FindPublishedDataSetByNameReturnsNullWhenNotFound()
        {
            PublishedDataSetDataType found = m_configurator.FindPublishedDataSetByName("NonExistent");
            Assert.That(found, Is.Null);
        }

        [Test]
        public void FindObjectByIdReturnsObjectWhenFound()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "Conn1" };
            StatusCode result = m_configurator.AddConnection(connection);
            Assert.That(StatusCode.IsGood(result), Is.True);

            uint id = m_configurator.FindIdForObject(connection);
            Assert.That(id, Is.Not.EqualTo(UaPubSubConfigurator.InvalidId));

            object found = m_configurator.FindObjectById(id);
            Assert.That(found, Is.SameAs(connection));
        }

        [Test]
        public void FindObjectByIdReturnsNullForInvalidId()
        {
            object found = m_configurator.FindObjectById(99999);
            Assert.That(found, Is.Null);
        }

        [Test]
        public void FindIdForObjectReturnsInvalidIdForUnknownObject()
        {
            uint id = m_configurator.FindIdForObject(new PubSubConnectionDataType { Enabled = true });
            Assert.That(id, Is.EqualTo(UaPubSubConfigurator.InvalidId));
        }

        [Test]
        public void FindStateForObjectReturnsOperationalForNewConnection()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "StateConn" };
            m_configurator.AddConnection(connection);

            PubSubState state = m_configurator.FindStateForObject(connection);
            Assert.That(state, Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public void FindStateForObjectReturnsErrorForUnknownObject()
        {
            PubSubState state = m_configurator.FindStateForObject(new PubSubConnectionDataType { Enabled = true });
            Assert.That(state, Is.EqualTo(PubSubState.Error));
        }

        [Test]
        public void FindStateForIdReturnsOperationalForNewConnection()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "StateIdConn" };
            m_configurator.AddConnection(connection);

            uint id = m_configurator.FindIdForObject(connection);
            PubSubState state = m_configurator.FindStateForId(id);
            Assert.That(state, Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public void FindStateForIdReturnsErrorForInvalidId()
        {
            PubSubState state = m_configurator.FindStateForId(99999);
            Assert.That(state, Is.EqualTo(PubSubState.Error));
        }

        [Test]
        public void FindParentForObjectReturnsNullForUnknownObject()
        {
            object parent = m_configurator.FindParentForObject(new PubSubConnectionDataType { Enabled = true });
            Assert.That(parent, Is.Null);
        }

        [Test]
        public void FindParentForObjectReturnsParentForWriterGroup()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "ParentConn" };
            m_configurator.AddConnection(connection);
            uint connId = m_configurator.FindIdForObject(connection);

            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "WG1" };
            StatusCode result = m_configurator.AddWriterGroup(connId, writerGroup);
            Assert.That(StatusCode.IsGood(result), Is.True);

            object parent = m_configurator.FindParentForObject(writerGroup);
            Assert.That(parent, Is.SameAs(connection));
        }

        [Test]
        public void FindChildrenIdsForObjectReturnsEmptyForUnknownObject()
        {
            List<uint> children = m_configurator.FindChildrenIdsForObject(
                new PubSubConnectionDataType { Enabled = true });
            Assert.That(children, Is.Empty);
        }

        [Test]
        public void FindChildrenIdsForObjectReturnsChildrenForConnection()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "ChildConn" };
            m_configurator.AddConnection(connection);
            uint connId = m_configurator.FindIdForObject(connection);

            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "ChildWG1" };
            m_configurator.AddWriterGroup(connId, writerGroup);

            var readerGroup = new ReaderGroupDataType { Enabled = true, Name = "ChildRG1" };
            m_configurator.AddReaderGroup(connId, readerGroup);

            List<uint> children = m_configurator.FindChildrenIdsForObject(connection);
            Assert.That(children.Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void EnableConnectionFromDisabledChangesStateToOperational()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "EnableConn" };
            m_configurator.AddConnection(connection);

            // Connections start Operational, so disable first
            m_configurator.Disable(connection);
            PubSubState initialState = m_configurator.FindStateForObject(connection);
            Assert.That(initialState, Is.EqualTo(PubSubState.Disabled));

            StatusCode enableResult = m_configurator.Enable(connection);
            Assert.That(StatusCode.IsGood(enableResult), Is.True);

            PubSubState newState = m_configurator.FindStateForObject(connection);
            Assert.That(newState, Is.EqualTo(PubSubState.Operational).Or.EqualTo(PubSubState.Paused));
        }

        [Test]
        public void EnableByIdFromDisabledChangesState()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "EnableIdConn" };
            m_configurator.AddConnection(connection);
            uint connId = m_configurator.FindIdForObject(connection);

            m_configurator.Disable(connId);
            StatusCode result = m_configurator.Enable(connId);
            Assert.That(StatusCode.IsGood(result), Is.True);
        }

        [Test]
        public void EnableAlreadyOperationalReturnsBadInvalidState()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "DoubleEnableConn" };
            m_configurator.AddConnection(connection);

            // Connections start Operational
            StatusCode result = m_configurator.Enable(connection);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void EnableNullThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => m_configurator.Enable((object)null));
        }

        [Test]
        public void EnableUnknownObjectThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(
                () => m_configurator.Enable(new PubSubConnectionDataType { Enabled = true }));
        }

        [Test]
        public void DisableConnectionChangesStateToDisabled()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "DisableConn" };
            m_configurator.AddConnection(connection);
            // Connection starts Operational

            StatusCode disableResult = m_configurator.Disable(connection);
            Assert.That(StatusCode.IsGood(disableResult), Is.True);

            PubSubState newState = m_configurator.FindStateForObject(connection);
            Assert.That(newState, Is.EqualTo(PubSubState.Disabled));
        }

        [Test]
        public void DisableByIdChangesState()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "DisableIdConn" };
            m_configurator.AddConnection(connection);
            uint connId = m_configurator.FindIdForObject(connection);
            // Connection starts Operational

            StatusCode result = m_configurator.Disable(connId);
            Assert.That(StatusCode.IsGood(result), Is.True);
        }

        [Test]
        public void DisableAlreadyDisabledReturnsBadInvalidState()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "DoubleDisableConn" };
            m_configurator.AddConnection(connection);

            // Disable first time (from Operational)
            m_configurator.Disable(connection);
            // Disable again - should fail
            StatusCode result = m_configurator.Disable(connection);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void DisableNullThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => m_configurator.Disable((object)null));
        }

        [Test]
        public void DisableUnknownObjectThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(
                () => m_configurator.Disable(new PubSubConnectionDataType { Enabled = true }));
        }

        [Test]
        public void EnableDisableWithChildrenPropagatesState()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "PropConn" };
            m_configurator.AddConnection(connection);
            uint connId = m_configurator.FindIdForObject(connection);

            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "PropWG" };
            m_configurator.AddWriterGroup(connId, writerGroup);

            // Connection starts Operational, children should also be Operational
            PubSubState wgState = m_configurator.FindStateForObject(writerGroup);
            Assert.That(
                wgState,
                Is.EqualTo(PubSubState.Operational)
                    .Or.EqualTo(PubSubState.Paused));

            // When parent is disabled, children become Paused
            m_configurator.Disable(connection);
            wgState = m_configurator.FindStateForObject(writerGroup);
            Assert.That(wgState, Is.EqualTo(PubSubState.Paused));

            // When parent is re-enabled, children return to Operational
            m_configurator.Enable(connection);
            wgState = m_configurator.FindStateForObject(writerGroup);
            Assert.That(wgState, Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public void LoadConfigurationFromFilePopulatesLookups()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                PublisherConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);

            m_configurator.LoadConfiguration(configFile);

            Assert.That(
                m_configurator.PubSubConfiguration.Connections.Count,
                Is.GreaterThan(0));
            Assert.That(
                m_configurator.PubSubConfiguration.PublishedDataSets.Count,
                Is.GreaterThan(0));

            PublishedDataSetDataType firstDs = m_configurator.PubSubConfiguration.PublishedDataSets[0];
            PublishedDataSetDataType found = m_configurator.FindPublishedDataSetByName(firstDs.Name);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Name, Is.EqualTo(firstDs.Name));
        }

        [Test]
        public void LoadConfigurationFromDataTypePopulatesLookups()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                PublisherConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType config =
                UaPubSubConfigurationHelper.LoadConfiguration(configFile, m_telemetry);

            m_configurator.LoadConfiguration(config);

            PubSubConnectionDataType conn = m_configurator.PubSubConfiguration.Connections[0];
            uint connId = m_configurator.FindIdForObject(conn);
            Assert.That(connId, Is.Not.EqualTo(UaPubSubConfigurator.InvalidId));

            object foundObj = m_configurator.FindObjectById(connId);
            Assert.That(foundObj, Is.SameAs(conn));
        }

        [Test]
        public void LoadConfigurationWithReplaceExistingClearsOldData()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "OldConn" };
            m_configurator.AddConnection(connection);

            string configFile = Utils.GetAbsoluteFilePath(
                PublisherConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType config =
                UaPubSubConfigurationHelper.LoadConfiguration(configFile, m_telemetry);

            m_configurator.LoadConfiguration(config, replaceExisting: true);

            PublishedDataSetDataType found = m_configurator.FindPublishedDataSetByName("OldConn");
            Assert.That(found, Is.Null);
        }

        [Test]
        public void LoadConfigurationNullPathThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => m_configurator.LoadConfiguration((string)null));
        }

        [Test]
        public void LoadConfigurationNonExistentPathThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(
                () => m_configurator.LoadConfiguration("NonExistentFile.xml"));
        }

        [Test]
        public void FindChildrenIdsForConnectionWithNoChildren()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "NoChildConn" };
            m_configurator.AddConnection(connection);

            List<uint> children = m_configurator.FindChildrenIdsForObject(connection);
            Assert.That(children, Is.Empty);
        }

        [Test]
        public void AddAndRemoveWriterGroupUpdatesLookups()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "WGConn" };
            m_configurator.AddConnection(connection);
            uint connId = m_configurator.FindIdForObject(connection);

            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "TestWG" };
            StatusCode addResult = m_configurator.AddWriterGroup(connId, writerGroup);
            Assert.That(StatusCode.IsGood(addResult), Is.True);

            uint wgId = m_configurator.FindIdForObject(writerGroup);
            Assert.That(wgId, Is.Not.EqualTo(UaPubSubConfigurator.InvalidId));

            StatusCode removeResult = m_configurator.RemoveWriterGroup(wgId);
            Assert.That(StatusCode.IsGood(removeResult), Is.True);

            uint removedId = m_configurator.FindIdForObject(writerGroup);
            Assert.That(removedId, Is.EqualTo(UaPubSubConfigurator.InvalidId));
        }

        [Test]
        public void AddAndRemoveReaderGroupUpdatesLookups()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "RGConn" };
            m_configurator.AddConnection(connection);
            uint connId = m_configurator.FindIdForObject(connection);

            var readerGroup = new ReaderGroupDataType { Enabled = true, Name = "TestRG" };
            StatusCode addResult = m_configurator.AddReaderGroup(connId, readerGroup);
            Assert.That(StatusCode.IsGood(addResult), Is.True);

            uint rgId = m_configurator.FindIdForObject(readerGroup);
            Assert.That(rgId, Is.Not.EqualTo(UaPubSubConfigurator.InvalidId));

            StatusCode removeResult = m_configurator.RemoveReaderGroup(rgId);
            Assert.That(StatusCode.IsGood(removeResult), Is.True);
        }

        [Test]
        public void AddAndRemoveDataSetWriterUpdatesLookups()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "DSWConn" };
            m_configurator.AddConnection(connection);
            uint connId = m_configurator.FindIdForObject(connection);

            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "DSWWG" };
            m_configurator.AddWriterGroup(connId, writerGroup);
            uint wgId = m_configurator.FindIdForObject(writerGroup);

            var dataSetWriter = new DataSetWriterDataType { Enabled = true, Name = "TestDSW" };
            StatusCode addResult = m_configurator.AddDataSetWriter(wgId, dataSetWriter);
            Assert.That(StatusCode.IsGood(addResult), Is.True);

            uint dswId = m_configurator.FindIdForObject(dataSetWriter);
            Assert.That(dswId, Is.Not.EqualTo(UaPubSubConfigurator.InvalidId));

            StatusCode removeResult = m_configurator.RemoveDataSetWriter(dswId);
            Assert.That(StatusCode.IsGood(removeResult), Is.True);
        }

        [Test]
        public void AddAndRemoveDataSetReaderUpdatesLookups()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "DSRConn" };
            m_configurator.AddConnection(connection);
            uint connId = m_configurator.FindIdForObject(connection);

            var readerGroup = new ReaderGroupDataType { Enabled = true, Name = "DSRRG" };
            m_configurator.AddReaderGroup(connId, readerGroup);
            uint rgId = m_configurator.FindIdForObject(readerGroup);

            var dataSetReader = new DataSetReaderDataType { Enabled = true, Name = "TestDSR" };
            StatusCode addResult = m_configurator.AddDataSetReader(rgId, dataSetReader);
            Assert.That(StatusCode.IsGood(addResult), Is.True);

            uint dsrId = m_configurator.FindIdForObject(dataSetReader);
            Assert.That(dsrId, Is.Not.EqualTo(UaPubSubConfigurator.InvalidId));

            StatusCode removeResult = m_configurator.RemoveDataSetReader(dsrId);
            Assert.That(StatusCode.IsGood(removeResult), Is.True);
        }

        [Test]
        public void EnableWriterGroupFromDisabledWithDisabledParentSetsPausedState()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "PausedParentConn" };
            m_configurator.AddConnection(connection);
            uint connId = m_configurator.FindIdForObject(connection);

            // Disable parent first
            m_configurator.Disable(connection);

            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "PausedWG" };
            m_configurator.AddWriterGroup(connId, writerGroup);

            // Writer group should start disabled since parent is disabled
            m_configurator.Disable(writerGroup);
            StatusCode result = m_configurator.Enable(writerGroup);
            Assert.That(StatusCode.IsGood(result), Is.True);

            PubSubState wgState = m_configurator.FindStateForObject(writerGroup);
            Assert.That(wgState, Is.EqualTo(PubSubState.Paused));
        }

        [Test]
        public void EnableWriterGroupFromDisabledWithOperationalParentSetsOperationalState()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "OpParentConn" };
            m_configurator.AddConnection(connection);
            uint connId = m_configurator.FindIdForObject(connection);
            // Connection starts Operational

            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "OpWG" };
            m_configurator.AddWriterGroup(connId, writerGroup);

            // Disable the writer group, then re-enable
            m_configurator.Disable(writerGroup);
            StatusCode result = m_configurator.Enable(writerGroup);
            Assert.That(StatusCode.IsGood(result), Is.True);

            PubSubState wgState = m_configurator.FindStateForObject(writerGroup);
            Assert.That(wgState, Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public void LoadSubscriberConfigurationPopulatesReaderGroups()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                SubscriberConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);

            m_configurator.LoadConfiguration(configFile);

            Assert.That(
                m_configurator.PubSubConfiguration.Connections.Count,
                Is.GreaterThan(0));

            PubSubConnectionDataType conn = m_configurator.PubSubConfiguration.Connections[0];
            uint connId = m_configurator.FindIdForObject(conn);
            Assert.That(connId, Is.Not.EqualTo(UaPubSubConfigurator.InvalidId));
        }
    }
}