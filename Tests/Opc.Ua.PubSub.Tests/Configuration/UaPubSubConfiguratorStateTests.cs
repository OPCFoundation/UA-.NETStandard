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
    public class UaPubSubConfiguratorStateTests
    {
        private UaPubSubConfigurator m_configurator;
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_configurator = new UaPubSubConfigurator(m_telemetry);
        }

        // Verifies Enable on a non-disabled object returns BadInvalidState
        [Test]
        public void EnableOnOperationalObjectReturnsBadInvalidState()
        {
            var connection = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            StatusCode addResult = m_configurator.AddConnection(connection);
            Assert.That(StatusCode.IsGood(addResult), Is.True);

            PubSubState state = m_configurator.FindStateForObject(connection);
            Assert.That(state, Is.EqualTo(PubSubState.Operational));

            StatusCode enableResult = m_configurator.Enable(connection);
            Assert.That(enableResult, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        // Verifies Disable on an already-disabled object returns BadInvalidState
        [Test]
        public void DisableOnDisabledObjectReturnsBadInvalidState()
        {
            var connection = new PubSubConnectionDataType { Name = "Conn1", Enabled = false };
            StatusCode addResult = m_configurator.AddConnection(connection);
            Assert.That(StatusCode.IsGood(addResult), Is.True);

            PubSubState state = m_configurator.FindStateForObject(connection);
            Assert.That(state, Is.EqualTo(PubSubState.Disabled));

            StatusCode disableResult = m_configurator.Disable(connection);
            Assert.That(disableResult, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        // Enable(null) throws ArgumentException
        [Test]
        public void EnableNullThrowsArgumentException()
        {
            Assert.That(() => m_configurator.Enable((object)null), Throws.TypeOf<ArgumentException>());
        }

        // Disable(null) throws ArgumentException
        [Test]
        public void DisableNullThrowsArgumentException()
        {
            Assert.That(() => m_configurator.Disable((object)null), Throws.TypeOf<ArgumentException>());
        }

        // Enable on object not in configuration throws ArgumentException
        [Test]
        public void EnableUnknownObjectThrowsArgumentException()
        {
            var connection = new PubSubConnectionDataType { Name = "Unknown" };
            Assert.That(() => m_configurator.Enable(connection), Throws.TypeOf<ArgumentException>());
        }

        // Disable on object not in configuration throws ArgumentException
        [Test]
        public void DisableUnknownObjectThrowsArgumentException()
        {
            var connection = new PubSubConnectionDataType { Name = "Unknown" };
            Assert.That(() => m_configurator.Disable(connection), Throws.TypeOf<ArgumentException>());
        }

        // Enable by id delegates to Enable(object)
        [Test]
        public void EnableByIdWorksForDisabledConnection()
        {
            var connection = new PubSubConnectionDataType { Name = "Conn1", Enabled = false };
            m_configurator.AddConnection(connection);
            uint id = m_configurator.FindIdForObject(connection);

            StatusCode result = m_configurator.Enable(id);
            Assert.That(StatusCode.IsGood(result), Is.True);
            Assert.That(m_configurator.FindStateForObject(connection), Is.EqualTo(PubSubState.Operational));
        }

        // Disable by id delegates to Disable(object)
        [Test]
        public void DisableByIdWorksForOperationalConnection()
        {
            var connection = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            m_configurator.AddConnection(connection);
            uint id = m_configurator.FindIdForObject(connection);

            StatusCode result = m_configurator.Disable(id);
            Assert.That(StatusCode.IsGood(result), Is.True);
            Assert.That(m_configurator.FindStateForObject(connection), Is.EqualTo(PubSubState.Disabled));
        }

        // Disable a parent propagates Paused to children
        [Test]
        public void DisableConnectionPausesChildWriterGroup()
        {
            var connection = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            m_configurator.AddConnection(connection);
            uint connId = m_configurator.FindIdForObject(connection);

            var writerGroup = new WriterGroupDataType { Name = "WG1", Enabled = true };
            m_configurator.AddWriterGroup(connId, writerGroup);

            Assert.That(m_configurator.FindStateForObject(writerGroup), Is.EqualTo(PubSubState.Operational));

            m_configurator.Disable(connection);
            Assert.That(m_configurator.FindStateForObject(writerGroup), Is.EqualTo(PubSubState.Paused));
        }

        // Re-enable parent restores Operational to paused children
        [Test]
        public void EnableConnectionRestoresOperationalToChildWriterGroup()
        {
            var connection = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            m_configurator.AddConnection(connection);
            uint connId = m_configurator.FindIdForObject(connection);

            var writerGroup = new WriterGroupDataType { Name = "WG1", Enabled = true };
            m_configurator.AddWriterGroup(connId, writerGroup);

            m_configurator.Disable(connection);
            Assert.That(m_configurator.FindStateForObject(writerGroup), Is.EqualTo(PubSubState.Paused));

            m_configurator.Enable(connection);
            Assert.That(m_configurator.FindStateForObject(writerGroup), Is.EqualTo(PubSubState.Operational));
        }

        // Enable a child when parent is disabled results in Paused
        [Test]
        public void EnableChildWithDisabledParentSetsPaused()
        {
            var connection = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            m_configurator.AddConnection(connection);
            uint connId = m_configurator.FindIdForObject(connection);

            var writerGroup = new WriterGroupDataType { Name = "WG1", Enabled = false };
            m_configurator.AddWriterGroup(connId, writerGroup);

            m_configurator.Disable(connection);

            m_configurator.Enable(writerGroup);
            Assert.That(m_configurator.FindStateForObject(writerGroup), Is.EqualTo(PubSubState.Paused));
        }

        // DataSetWriter state propagation through WriterGroup disable/enable
        [Test]
        public void DisableWriterGroupPausesDataSetWriter()
        {
            var connection = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            m_configurator.AddConnection(connection);
            uint connId = m_configurator.FindIdForObject(connection);

            var writerGroup = new WriterGroupDataType { Name = "WG1", Enabled = true };
            m_configurator.AddWriterGroup(connId, writerGroup);
            uint wgId = m_configurator.FindIdForObject(writerGroup);

            var dsWriter = new DataSetWriterDataType { Name = "DSW1", Enabled = true };
            m_configurator.AddDataSetWriter(wgId, dsWriter);
            Assert.That(m_configurator.FindStateForObject(dsWriter), Is.EqualTo(PubSubState.Operational));

            m_configurator.Disable(writerGroup);
            Assert.That(m_configurator.FindStateForObject(dsWriter), Is.EqualTo(PubSubState.Paused));
        }

        // ReaderGroup and DataSetReader state propagation
        [Test]
        public void DisableConnectionPausesReaderGroupAndDataSetReader()
        {
            var connection = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            m_configurator.AddConnection(connection);
            uint connId = m_configurator.FindIdForObject(connection);

            var readerGroup = new ReaderGroupDataType { Name = "RG1", Enabled = true };
            m_configurator.AddReaderGroup(connId, readerGroup);
            uint rgId = m_configurator.FindIdForObject(readerGroup);

            var dsReader = new DataSetReaderDataType { Name = "DSR1", Enabled = true };
            m_configurator.AddDataSetReader(rgId, dsReader);

            Assert.That(m_configurator.FindStateForObject(dsReader), Is.EqualTo(PubSubState.Operational));

            m_configurator.Disable(connection);
            Assert.That(m_configurator.FindStateForObject(readerGroup), Is.EqualTo(PubSubState.Paused));
            Assert.That(m_configurator.FindStateForObject(dsReader), Is.EqualTo(PubSubState.Paused));
        }

        // FindStateForObject returns Error for unknown object
        [Test]
        public void FindStateForObjectReturnsErrorForUnknownObject()
        {
            var unknown = new PubSubConnectionDataType { Name = "Unknown" };
            PubSubState state = m_configurator.FindStateForObject(unknown);
            Assert.That(state, Is.EqualTo(PubSubState.Error));
        }

        // FindStateForId returns Error for unknown id
        [Test]
        public void FindStateForIdReturnsErrorForUnknownId()
        {
            PubSubState state = m_configurator.FindStateForId(99999);
            Assert.That(state, Is.EqualTo(PubSubState.Error));
        }

        // FindObjectById returns null for unknown id
        [Test]
        public void FindObjectByIdReturnsNullForUnknownId()
        {
            object result = m_configurator.FindObjectById(99999);
            Assert.That(result, Is.Null);
        }

        // FindIdForObject returns InvalidId for unknown object
        [Test]
        public void FindIdForObjectReturnsInvalidIdForUnknownObject()
        {
            uint id = m_configurator.FindIdForObject(new PubSubConnectionDataType());
            Assert.That(id, Is.EqualTo(UaPubSubConfigurator.InvalidId));
        }

        // FindParentForObject returns null for root config
        [Test]
        public void FindParentForObjectReturnsNullForRootConfig()
        {
            object parent = m_configurator.FindParentForObject(m_configurator.PubSubConfiguration);
            Assert.That(parent, Is.Null);
        }

        // PubSubStateChanged event fires on state changes
        [Test]
        public void PubSubStateChangedEventFires()
        {
            var stateChanges = new List<PubSubStateChangedEventArgs>();
            m_configurator.PubSubStateChanged += (_, args) => stateChanges.Add(args);

            var connection = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            m_configurator.AddConnection(connection);

            m_configurator.Disable(connection);

            Assert.That(stateChanges.Count, Is.GreaterThan(0));
            PubSubStateChangedEventArgs last = stateChanges[^1];
            Assert.That(last.NewState, Is.EqualTo(PubSubState.Disabled));
        }

        // Remove connection by unknown id returns BadNodeIdUnknown
        [Test]
        public void RemoveConnectionByUnknownIdReturnsBadNodeIdUnknown()
        {
            StatusCode result = m_configurator.RemoveConnection(99999u);
            Assert.That(result, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        // Remove writer group by unknown id returns BadNodeIdUnknown
        [Test]
        public void RemoveWriterGroupByUnknownIdReturnsBadNodeIdUnknown()
        {
            StatusCode result = m_configurator.RemoveWriterGroup(99999u);
            Assert.That(result, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        // Remove reader group by unknown id returns BadInvalidArgument
        [Test]
        public void RemoveReaderGroupByUnknownIdReturnsBadInvalidArgument()
        {
            StatusCode result = m_configurator.RemoveReaderGroup(99999u);
            Assert.That(result, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        // Remove data set writer by unknown id returns BadNodeIdUnknown
        [Test]
        public void RemoveDataSetWriterByUnknownIdReturnsBadNodeIdUnknown()
        {
            StatusCode result = m_configurator.RemoveDataSetWriter(99999u);
            Assert.That(result, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        // Remove data set reader by unknown id returns BadNodeIdUnknown
        [Test]
        public void RemoveDataSetReaderByUnknownIdReturnsBadNodeIdUnknown()
        {
            StatusCode result = m_configurator.RemoveDataSetReader(99999u);
            Assert.That(result, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        // Remove published data set by unknown id returns Good per source
        [Test]
        public void RemovePublishedDataSetByUnknownIdReturnsGood()
        {
            StatusCode result = m_configurator.RemovePublishedDataSet(99999u);
            Assert.That(StatusCode.IsGood(result), Is.True);
        }

        // Duplicate connection name returns BadBrowseNameDuplicated
        [Test]
        public void AddDuplicateConnectionNameReturnsBadBrowseNameDuplicated()
        {
            var conn1 = new PubSubConnectionDataType { Name = "SameName", Enabled = true };
            m_configurator.AddConnection(conn1);

            var conn2 = new PubSubConnectionDataType { Name = "SameName", Enabled = true };
            StatusCode result = m_configurator.AddConnection(conn2);
            Assert.That(result, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        // Duplicate writer group name returns BadBrowseNameDuplicated
        [Test]
        public void AddDuplicateWriterGroupNameReturnsBadBrowseNameDuplicated()
        {
            var conn = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            m_configurator.AddConnection(conn);
            uint connId = m_configurator.FindIdForObject(conn);

            var wg1 = new WriterGroupDataType { Name = "WG1", Enabled = true };
            m_configurator.AddWriterGroup(connId, wg1);

            var wg2 = new WriterGroupDataType { Name = "WG1", Enabled = true };
            StatusCode result = m_configurator.AddWriterGroup(connId, wg2);
            Assert.That(result, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        // Duplicate reader group name returns BadBrowseNameDuplicated
        [Test]
        public void AddDuplicateReaderGroupNameReturnsBadBrowseNameDuplicated()
        {
            var conn = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            m_configurator.AddConnection(conn);
            uint connId = m_configurator.FindIdForObject(conn);

            var rg1 = new ReaderGroupDataType { Name = "RG1", Enabled = true };
            m_configurator.AddReaderGroup(connId, rg1);

            var rg2 = new ReaderGroupDataType { Name = "RG1", Enabled = true };
            StatusCode result = m_configurator.AddReaderGroup(connId, rg2);
            Assert.That(result, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        // Duplicate DataSetWriter name returns BadBrowseNameDuplicated
        [Test]
        public void AddDuplicateDataSetWriterNameReturnsBadBrowseNameDuplicated()
        {
            var conn = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            m_configurator.AddConnection(conn);
            uint connId = m_configurator.FindIdForObject(conn);

            var wg = new WriterGroupDataType { Name = "WG1", Enabled = true };
            m_configurator.AddWriterGroup(connId, wg);
            uint wgId = m_configurator.FindIdForObject(wg);

            var dsw1 = new DataSetWriterDataType { Name = "DSW1", Enabled = true };
            m_configurator.AddDataSetWriter(wgId, dsw1);

            var dsw2 = new DataSetWriterDataType { Name = "DSW1", Enabled = true };
            StatusCode result = m_configurator.AddDataSetWriter(wgId, dsw2);
            Assert.That(result, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        // Duplicate DataSetReader name returns BadBrowseNameDuplicated
        [Test]
        public void AddDuplicateDataSetReaderNameReturnsBadBrowseNameDuplicated()
        {
            var conn = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            m_configurator.AddConnection(conn);
            uint connId = m_configurator.FindIdForObject(conn);

            var rg = new ReaderGroupDataType { Name = "RG1", Enabled = true };
            m_configurator.AddReaderGroup(connId, rg);
            uint rgId = m_configurator.FindIdForObject(rg);

            var dsr1 = new DataSetReaderDataType { Name = "DSR1", Enabled = true };
            m_configurator.AddDataSetReader(rgId, dsr1);

            var dsr2 = new DataSetReaderDataType { Name = "DSR1", Enabled = true };
            StatusCode result = m_configurator.AddDataSetReader(rgId, dsr2);
            Assert.That(result, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        // LoadConfiguration with replaceExisting cleans up existing connections
        [Test]
        public void LoadConfigurationReplaceExistingRemovesPreviousConnections()
        {
            var conn = new PubSubConnectionDataType { Name = "OldConn", Enabled = true };
            m_configurator.AddConnection(conn);
            Assert.That(m_configurator.PubSubConfiguration.Connections.Count, Is.EqualTo(1));

            var newConfig = new PubSubConfigurationDataType
            {
                Connections = [],
                PublishedDataSets = []
            };
            var newConn = new PubSubConnectionDataType { Name = "NewConn", Enabled = true };
            newConfig.Connections += newConn;

            m_configurator.LoadConfiguration(newConfig, replaceExisting: true);

            Assert.That(m_configurator.PubSubConfiguration.Connections.Count, Is.EqualTo(1));
            Assert.That(m_configurator.PubSubConfiguration.Connections[0].Name, Is.EqualTo("NewConn"));
        }

        // LoadConfiguration with empty connection name assigns default name
        [Test]
        public void LoadConfigurationAssignsDefaultConnectionName()
        {
            var config = new PubSubConfigurationDataType
            {
                Connections = [],
                PublishedDataSets = []
            };
            var conn = new PubSubConnectionDataType { Name = "", Enabled = true };
            config.Connections += conn;

            m_configurator.LoadConfiguration(config);
            Assert.That(m_configurator.PubSubConfiguration.Connections[0].Name,
                Does.StartWith("Connection_"));
        }

        // Adding WriterGroup with empty name to a connection assigns default name
        [Test]
        public void AddConnectionWithEmptyNamedWriterGroupAssignsDefault()
        {
            var writerGroup = new WriterGroupDataType { Name = "", Enabled = true };
            var conn = new PubSubConnectionDataType
            {
                Name = "Conn1",
                Enabled = true,
                WriterGroups = [writerGroup]
            };
            m_configurator.AddConnection(conn);

            Assert.That(conn.WriterGroups.Count, Is.EqualTo(1));
            Assert.That(conn.WriterGroups[0].Name, Does.StartWith("WriterGroup_"));
        }

        // Adding ReaderGroup with empty name to a connection assigns default name
        [Test]
        public void AddConnectionWithEmptyNamedReaderGroupAssignsDefault()
        {
            var readerGroup = new ReaderGroupDataType { Name = "", Enabled = true };
            var conn = new PubSubConnectionDataType
            {
                Name = "Conn1",
                Enabled = true,
                ReaderGroups = [readerGroup]
            };
            m_configurator.AddConnection(conn);

            Assert.That(conn.ReaderGroups.Count, Is.EqualTo(1));
            Assert.That(conn.ReaderGroups[0].Name, Does.StartWith("ReaderGroup_"));
        }

        // Adding a connection with existing child writers and readers
        [Test]
        public void AddConnectionWithChildWritersAndReaders()
        {
            var dsWriter = new DataSetWriterDataType { Name = "DSW1", Enabled = true };
            var writerGroup = new WriterGroupDataType
            {
                Name = "WG1",
                Enabled = true,
                DataSetWriters = [dsWriter]
            };
            var dsReader = new DataSetReaderDataType { Name = "DSR1", Enabled = true };
            var readerGroup = new ReaderGroupDataType
            {
                Name = "RG1",
                Enabled = true,
                DataSetReaders = [dsReader]
            };
            var conn = new PubSubConnectionDataType
            {
                Name = "Conn1",
                Enabled = true,
                WriterGroups = [writerGroup],
                ReaderGroups = [readerGroup]
            };
            StatusCode result = m_configurator.AddConnection(conn);
            Assert.That(StatusCode.IsGood(result), Is.True);

            Assert.That(m_configurator.FindStateForObject(dsWriter), Is.EqualTo(PubSubState.Operational));
            Assert.That(m_configurator.FindStateForObject(dsReader), Is.EqualTo(PubSubState.Operational));
        }

        // Duplicate published data set name returns BadBrowseNameDuplicated
        [Test]
        public void AddDuplicatePublishedDataSetNameReturnsBadBrowseNameDuplicated()
        {
            var pds1 = new PublishedDataSetDataType { Name = "PDS1" };
            m_configurator.AddPublishedDataSet(pds1);

            var pds2 = new PublishedDataSetDataType { Name = "PDS1" };
            StatusCode result = m_configurator.AddPublishedDataSet(pds2);
            Assert.That(result, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        // Removing a PDS also removes associated DataSetWriters
        [Test]
        public void RemovePublishedDataSetRemovesAssociatedDataSetWriters()
        {
            var pds = new PublishedDataSetDataType { Name = "PDS1" };
            m_configurator.AddPublishedDataSet(pds);

            var conn = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            m_configurator.AddConnection(conn);
            uint connId = m_configurator.FindIdForObject(conn);

            var wg = new WriterGroupDataType { Name = "WG1", Enabled = true };
            m_configurator.AddWriterGroup(connId, wg);
            uint wgId = m_configurator.FindIdForObject(wg);

            var dsw = new DataSetWriterDataType { Name = "DSW1", Enabled = true, DataSetName = "PDS1" };
            m_configurator.AddDataSetWriter(wgId, dsw);

            m_configurator.RemovePublishedDataSet(pds);

            Assert.That(wg.DataSetWriters.Count, Is.EqualTo(0));
        }

        // Extension field CRUD on a published data set
        [Test]
        public void AddAndRemoveExtensionField()
        {
            var pds = new PublishedDataSetDataType { Name = "PDS1" };
            m_configurator.AddPublishedDataSet(pds);
            uint pdsId = m_configurator.FindIdForObject(pds);

            var field = new KeyValuePair
            {
                Key = new QualifiedName("Field1"),
                Value = new Variant(42)
            };
            StatusCode addResult = m_configurator.AddExtensionField(pdsId, field);
            Assert.That(StatusCode.IsGood(addResult), Is.True);

            uint fieldId = m_configurator.FindIdForObject(field);
            StatusCode removeResult = m_configurator.RemoveExtensionField(pdsId, fieldId);
            Assert.That(StatusCode.IsGood(removeResult), Is.True);
        }

        // Add extension field duplicate key returns BadNodeIdExists
        [Test]
        public void AddDuplicateExtensionFieldReturnsBadNodeIdExists()
        {
            var pds = new PublishedDataSetDataType { Name = "PDS1" };
            m_configurator.AddPublishedDataSet(pds);
            uint pdsId = m_configurator.FindIdForObject(pds);

            var field1 = new KeyValuePair
            {
                Key = new QualifiedName("Field1"),
                Value = new Variant(1)
            };
            m_configurator.AddExtensionField(pdsId, field1);

            var field2 = new KeyValuePair
            {
                Key = new QualifiedName("Field1"),
                Value = new Variant(2)
            };
            StatusCode result = m_configurator.AddExtensionField(pdsId, field2);
            Assert.That(result, Is.EqualTo(StatusCodes.BadNodeIdExists));
        }

        // Extension field add on invalid PDS id returns BadNodeIdInvalid
        [Test]
        public void AddExtensionFieldOnInvalidPdsIdReturnsBadNodeIdInvalid()
        {
            var field = new KeyValuePair
            {
                Key = new QualifiedName("F1"),
                Value = new Variant(1)
            };
            StatusCode result = m_configurator.AddExtensionField(99999, field);
            Assert.That(result, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        // Remove extension field on invalid PDS/field id returns BadNodeIdInvalid
        [Test]
        public void RemoveExtensionFieldOnInvalidIdsReturnsBadNodeIdInvalid()
        {
            StatusCode result = m_configurator.RemoveExtensionField(99999, 99998);
            Assert.That(result, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        // FindChildrenIdsForObject returns empty for leaf objects
        [Test]
        public void FindChildrenIdsForLeafObjectReturnsEmpty()
        {
            var conn = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            m_configurator.AddConnection(conn);
            uint connId = m_configurator.FindIdForObject(conn);

            var wg = new WriterGroupDataType { Name = "WG1", Enabled = true };
            m_configurator.AddWriterGroup(connId, wg);
            uint wgId = m_configurator.FindIdForObject(wg);

            var dsw = new DataSetWriterDataType { Name = "DSW1", Enabled = true };
            m_configurator.AddDataSetWriter(wgId, dsw);

            List<uint> children = m_configurator.FindChildrenIdsForObject(dsw);
            Assert.That(children, Is.Empty);
        }

        // Enables the root PubSubConfiguration
        [Test]
        public void DisableAndEnableRootConfiguration()
        {
            StatusCode disableResult = m_configurator.Disable(m_configurator.PubSubConfiguration);
            Assert.That(StatusCode.IsGood(disableResult), Is.True);
            Assert.That(
                m_configurator.FindStateForObject(m_configurator.PubSubConfiguration),
                Is.EqualTo(PubSubState.Disabled));

            StatusCode enableResult = m_configurator.Enable(m_configurator.PubSubConfiguration);
            Assert.That(StatusCode.IsGood(enableResult), Is.True);
            Assert.That(
                m_configurator.FindStateForObject(m_configurator.PubSubConfiguration),
                Is.EqualTo(PubSubState.Operational));
        }

        // Adding connection that is already added throws
        [Test]
        public void AddSameConnectionInstanceTwiceThrows()
        {
            var conn = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            m_configurator.AddConnection(conn);
            Assert.That(() => m_configurator.AddConnection(conn), Throws.TypeOf<ArgumentException>());
        }

        // Adding WriterGroup to non-existent parent throws
        [Test]
        public void AddWriterGroupToInvalidParentThrows()
        {
            var wg = new WriterGroupDataType { Name = "WG1", Enabled = true };
            Assert.That(() => m_configurator.AddWriterGroup(99999, wg), Throws.TypeOf<ArgumentException>());
        }

        // Adding ReaderGroup to non-existent parent throws
        [Test]
        public void AddReaderGroupToInvalidParentThrows()
        {
            var rg = new ReaderGroupDataType { Name = "RG1", Enabled = true };
            Assert.That(() => m_configurator.AddReaderGroup(99999, rg), Throws.TypeOf<ArgumentException>());
        }

        // Adding DataSetWriter to non-existent parent throws
        [Test]
        public void AddDataSetWriterToInvalidParentThrows()
        {
            var dsw = new DataSetWriterDataType { Name = "DSW1", Enabled = true };
            Assert.That(() => m_configurator.AddDataSetWriter(99999, dsw), Throws.TypeOf<ArgumentException>());
        }

        // Adding DataSetReader to non-existent parent throws
        [Test]
        public void AddDataSetReaderToInvalidParentThrows()
        {
            var dsr = new DataSetReaderDataType { Name = "DSR1", Enabled = true };
            Assert.That(() => m_configurator.AddDataSetReader(99999, dsr), Throws.TypeOf<ArgumentException>());
        }

        // Child with empty name DataSetWriter gets default name
        [Test]
        public void AddWriterGroupWithEmptyNamedDataSetWriterAssignsDefault()
        {
            var conn = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            m_configurator.AddConnection(conn);
            uint connId = m_configurator.FindIdForObject(conn);

            var dsw = new DataSetWriterDataType { Name = "", Enabled = true };
            var wg = new WriterGroupDataType
            {
                Name = "WG1",
                Enabled = true,
                DataSetWriters = [dsw]
            };
            m_configurator.AddWriterGroup(connId, wg);

            Assert.That(wg.DataSetWriters[0].Name, Does.StartWith("DataSetWriter_"));
        }

        // Child with empty name DataSetReader gets default name
        [Test]
        public void AddReaderGroupWithEmptyNamedDataSetReaderAssignsDefault()
        {
            var conn = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            m_configurator.AddConnection(conn);
            uint connId = m_configurator.FindIdForObject(conn);

            var dsr = new DataSetReaderDataType { Name = "", Enabled = true };
            var rg = new ReaderGroupDataType
            {
                Name = "RG1",
                Enabled = true,
                DataSetReaders = [dsr]
            };
            m_configurator.AddReaderGroup(connId, rg);

            Assert.That(rg.DataSetReaders[0].Name, Does.StartWith("DataSetReader_"));
        }
    }
}
