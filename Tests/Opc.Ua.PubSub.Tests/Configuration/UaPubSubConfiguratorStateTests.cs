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

        /// <summary>
        /// Verifies Enable on a non-disabled object returns BadInvalidState
        /// </summary>
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

        /// <summary>
        /// Verifies Disable on an already-disabled object returns BadInvalidState
        /// </summary>
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

        /// <summary>
        /// Enable(null) throws ArgumentException
        /// </summary>
        [Test]
        public void EnableNullThrowsArgumentException()
        {
            Assert.That(() => m_configurator.Enable((object)null), Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// Disable(null) throws ArgumentException
        /// </summary>
        [Test]
        public void DisableNullThrowsArgumentException()
        {
            Assert.That(() => m_configurator.Disable((object)null), Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// Enable on object not in configuration throws ArgumentException
        /// </summary>
        [Test]
        public void EnableUnknownObjectThrowsArgumentException()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "Unknown" };
            Assert.That(() => m_configurator.Enable(connection), Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// Disable on object not in configuration throws ArgumentException
        /// </summary>
        [Test]
        public void DisableUnknownObjectThrowsArgumentException()
        {
            var connection = new PubSubConnectionDataType { Enabled = true, Name = "Unknown" };
            Assert.That(() => m_configurator.Disable(connection), Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// Enable by id delegates to Enable(object)
        /// </summary>
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

        /// <summary>
        /// Disable by id delegates to Disable(object)
        /// </summary>
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

        /// <summary>
        /// Disable a parent propagates Paused to children
        /// </summary>
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

        /// <summary>
        /// Re-enable parent restores Operational to paused children
        /// </summary>
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

        /// <summary>
        /// Enable a child when parent is disabled results in Paused
        /// </summary>
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

        /// <summary>
        /// DataSetWriter state propagation through WriterGroup disable/enable
        /// </summary>
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

        /// <summary>
        /// ReaderGroup and DataSetReader state propagation
        /// </summary>
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

        /// <summary>
        /// FindStateForObject returns Error for unknown object
        /// </summary>
        [Test]
        public void FindStateForObjectReturnsErrorForUnknownObject()
        {
            var unknown = new PubSubConnectionDataType { Enabled = true, Name = "Unknown" };
            PubSubState state = m_configurator.FindStateForObject(unknown);
            Assert.That(state, Is.EqualTo(PubSubState.Error));
        }

        /// <summary>
        /// FindStateForId returns Error for unknown id
        /// </summary>
        [Test]
        public void FindStateForIdReturnsErrorForUnknownId()
        {
            PubSubState state = m_configurator.FindStateForId(99999);
            Assert.That(state, Is.EqualTo(PubSubState.Error));
        }

        /// <summary>
        /// FindObjectById returns null for unknown id
        /// </summary>
        [Test]
        public void FindObjectByIdReturnsNullForUnknownId()
        {
            object result = m_configurator.FindObjectById(99999);
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// FindIdForObject returns InvalidId for unknown object
        /// </summary>
        [Test]
        public void FindIdForObjectReturnsInvalidIdForUnknownObject()
        {
            uint id = m_configurator.FindIdForObject(new PubSubConnectionDataType { Enabled = true });
            Assert.That(id, Is.EqualTo(UaPubSubConfigurator.InvalidId));
        }

        /// <summary>
        /// FindParentForObject returns null for root config
        /// </summary>
        [Test]
        public void FindParentForObjectReturnsNullForRootConfig()
        {
            object parent = m_configurator.FindParentForObject(m_configurator.PubSubConfiguration);
            Assert.That(parent, Is.Null);
        }

        /// <summary>
        /// PubSubStateChanged event fires on state changes
        /// </summary>
        [Test]
        public void PubSubStateChangedEventFires()
        {
            var stateChanges = new List<PubSubStateChangedEventArgs>();
            m_configurator.PubSubStateChanged += (_, args) => stateChanges.Add(args);

            var connection = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            m_configurator.AddConnection(connection);

            m_configurator.Disable(connection);

            Assert.That(stateChanges, Is.Not.Empty);
            PubSubStateChangedEventArgs last = stateChanges[^1];
            Assert.That(last.NewState, Is.EqualTo(PubSubState.Disabled));
        }

        /// <summary>
        /// Remove connection by unknown id returns BadNodeIdUnknown
        /// </summary>
        [Test]
        public void RemoveConnectionByUnknownIdReturnsBadNodeIdUnknown()
        {
            StatusCode result = m_configurator.RemoveConnection(99999u);
            Assert.That(result, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// Remove writer group by unknown id returns BadNodeIdUnknown
        /// </summary>
        [Test]
        public void RemoveWriterGroupByUnknownIdReturnsBadNodeIdUnknown()
        {
            StatusCode result = m_configurator.RemoveWriterGroup(99999u);
            Assert.That(result, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// Remove reader group by unknown id returns BadInvalidArgument
        /// </summary>
        [Test]
        public void RemoveReaderGroupByUnknownIdReturnsBadInvalidArgument()
        {
            StatusCode result = m_configurator.RemoveReaderGroup(99999u);
            Assert.That(result, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        /// <summary>
        /// Remove data set writer by unknown id returns BadNodeIdUnknown
        /// </summary>
        [Test]
        public void RemoveDataSetWriterByUnknownIdReturnsBadNodeIdUnknown()
        {
            StatusCode result = m_configurator.RemoveDataSetWriter(99999u);
            Assert.That(result, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// Remove data set reader by unknown id returns BadNodeIdUnknown
        /// </summary>
        [Test]
        public void RemoveDataSetReaderByUnknownIdReturnsBadNodeIdUnknown()
        {
            StatusCode result = m_configurator.RemoveDataSetReader(99999u);
            Assert.That(result, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// Remove published data set by unknown id returns Good per source
        /// </summary>
        [Test]
        public void RemovePublishedDataSetByUnknownIdReturnsGood()
        {
            StatusCode result = m_configurator.RemovePublishedDataSet(99999u);
            Assert.That(StatusCode.IsGood(result), Is.True);
        }

        /// <summary>
        /// Duplicate connection name returns BadBrowseNameDuplicated
        /// </summary>
        [Test]
        public void AddDuplicateConnectionNameReturnsBadBrowseNameDuplicated()
        {
            var conn1 = new PubSubConnectionDataType { Name = "SameName", Enabled = true };
            m_configurator.AddConnection(conn1);

            var conn2 = new PubSubConnectionDataType { Name = "SameName", Enabled = true };
            StatusCode result = m_configurator.AddConnection(conn2);
            Assert.That(result, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        /// <summary>
        /// Duplicate writer group name returns BadBrowseNameDuplicated
        /// </summary>
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

        /// <summary>
        /// Duplicate reader group name returns BadBrowseNameDuplicated
        /// </summary>
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

        /// <summary>
        /// Duplicate DataSetWriter name returns BadBrowseNameDuplicated
        /// </summary>
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

        /// <summary>
        /// Duplicate DataSetReader name returns BadBrowseNameDuplicated
        /// </summary>
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

        /// <summary>
        /// LoadConfiguration with replaceExisting cleans up existing connections
        /// </summary>
        [Test]
        public void LoadConfigurationReplaceExistingRemovesPreviousConnections()
        {
            var conn = new PubSubConnectionDataType { Name = "OldConn", Enabled = true };
            m_configurator.AddConnection(conn);
            Assert.That(m_configurator.PubSubConfiguration.Connections.Count, Is.EqualTo(1));

            var newConfig = new PubSubConfigurationDataType
            {
                Enabled = true,
                Connections = [],
                PublishedDataSets = []
            };
            var newConn = new PubSubConnectionDataType { Name = "NewConn", Enabled = true };
            newConfig.Connections += newConn;

            m_configurator.LoadConfiguration(newConfig, replaceExisting: true);

            Assert.That(m_configurator.PubSubConfiguration.Connections.Count, Is.EqualTo(1));
            Assert.That(m_configurator.PubSubConfiguration.Connections[0].Name, Is.EqualTo("NewConn"));
        }

        /// <summary>
        /// LoadConfiguration with empty connection name assigns default name
        /// </summary>
        [Test]
        public void LoadConfigurationAssignsDefaultConnectionName()
        {
            var config = new PubSubConfigurationDataType
            {
                Enabled = true,
                Connections = [],
                PublishedDataSets = []
            };
            var conn = new PubSubConnectionDataType { Name = "", Enabled = true };
            config.Connections += conn;

            m_configurator.LoadConfiguration(config);
            Assert.That(m_configurator.PubSubConfiguration.Connections[0].Name,
                Does.StartWith("Connection_"));
        }

        /// <summary>
        /// Adding WriterGroup with empty name to a connection assigns default name
        /// </summary>
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

        /// <summary>
        /// Adding ReaderGroup with empty name to a connection assigns default name
        /// </summary>
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

        /// <summary>
        /// Adding a connection with existing child writers and readers
        /// </summary>
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

        /// <summary>
        /// Duplicate published data set name returns BadBrowseNameDuplicated
        /// </summary>
        [Test]
        public void AddDuplicatePublishedDataSetNameReturnsBadBrowseNameDuplicated()
        {
            var pds1 = new PublishedDataSetDataType { Name = "PDS1" };
            m_configurator.AddPublishedDataSet(pds1);

            var pds2 = new PublishedDataSetDataType { Name = "PDS1" };
            StatusCode result = m_configurator.AddPublishedDataSet(pds2);
            Assert.That(result, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        /// <summary>
        /// Removing a PDS also removes associated DataSetWriters
        /// </summary>
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

        /// <summary>
        /// Extension field CRUD on a published data set
        /// </summary>
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

        /// <summary>
        /// Add extension field duplicate key returns BadNodeIdExists
        /// </summary>
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

        /// <summary>
        /// Extension field add on invalid PDS id returns BadNodeIdInvalid
        /// </summary>
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

        /// <summary>
        /// Remove extension field on invalid PDS/field id returns BadNodeIdInvalid
        /// </summary>
        [Test]
        public void RemoveExtensionFieldOnInvalidIdsReturnsBadNodeIdInvalid()
        {
            StatusCode result = m_configurator.RemoveExtensionField(99999, 99998);
            Assert.That(result, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        /// <summary>
        /// FindChildrenIdsForObject returns empty for leaf objects
        /// </summary>
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

        /// <summary>
        /// Enables the root PubSubConfiguration
        /// </summary>
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

        /// <summary>
        /// Adding connection that is already added throws
        /// </summary>
        [Test]
        public void AddSameConnectionInstanceTwiceThrows()
        {
            var conn = new PubSubConnectionDataType { Name = "Conn1", Enabled = true };
            m_configurator.AddConnection(conn);
            Assert.That(() => m_configurator.AddConnection(conn), Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// Adding WriterGroup to non-existent parent throws
        /// </summary>
        [Test]
        public void AddWriterGroupToInvalidParentThrows()
        {
            var wg = new WriterGroupDataType { Name = "WG1", Enabled = true };
            Assert.That(() => m_configurator.AddWriterGroup(99999, wg), Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// Adding ReaderGroup to non-existent parent throws
        /// </summary>
        [Test]
        public void AddReaderGroupToInvalidParentThrows()
        {
            var rg = new ReaderGroupDataType { Name = "RG1", Enabled = true };
            Assert.That(() => m_configurator.AddReaderGroup(99999, rg), Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// Adding DataSetWriter to non-existent parent throws
        /// </summary>
        [Test]
        public void AddDataSetWriterToInvalidParentThrows()
        {
            var dsw = new DataSetWriterDataType { Name = "DSW1", Enabled = true };
            Assert.That(() => m_configurator.AddDataSetWriter(99999, dsw), Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// Adding DataSetReader to non-existent parent throws
        /// </summary>
        [Test]
        public void AddDataSetReaderToInvalidParentThrows()
        {
            var dsr = new DataSetReaderDataType { Name = "DSR1", Enabled = true };
            Assert.That(() => m_configurator.AddDataSetReader(99999, dsr), Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// Child with empty name DataSetWriter gets default name
        /// </summary>
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

        /// <summary>
        /// Child with empty name DataSetReader gets default name
        /// </summary>
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