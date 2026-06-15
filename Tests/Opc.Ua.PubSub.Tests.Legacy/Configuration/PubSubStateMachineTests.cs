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
    public partial class PubSubStateMachineTests
    {
        private static UaPubSubConfigurator CreateConfigurator()
        {
            return new UaPubSubConfigurator(NUnitTelemetryContext.Create());
        }

        private static PubSubConnectionDataType CreateConnection(string name = "Conn1")
        {
            return new PubSubConnectionDataType
            {
                Name = name,
                Enabled = true,
                PublisherId = Variant.From(name),
                WriterGroups = [],
                ReaderGroups = []
            };
        }

        [Test]
        public void NewConfiguratorHasOperationalRootState()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            PubSubState state = configurator.FindStateForObject(configurator.PubSubConfiguration);
            Assert.That(state, Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public void DisableRootTransitionsToDisabled()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            StatusCode result = configurator.Disable(configurator.PubSubConfiguration);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(
                configurator.FindStateForObject(configurator.PubSubConfiguration),
                Is.EqualTo(PubSubState.Disabled));
        }

        [Test]
        public void EnableRootAfterDisableTransitionsToOperational()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            configurator.Disable(configurator.PubSubConfiguration);
            StatusCode result = configurator.Enable(configurator.PubSubConfiguration);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(
                configurator.FindStateForObject(configurator.PubSubConfiguration),
                Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public void EnableAlreadyEnabledReturnsInvalidState()
        {
            // Root is already Operational by default
            UaPubSubConfigurator configurator = CreateConfigurator();
            StatusCode result = configurator.Enable(configurator.PubSubConfiguration);
            Assert.That(result, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void DisableAlreadyDisabledReturnsInvalidState()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            configurator.Disable(configurator.PubSubConfiguration);
            StatusCode result = configurator.Disable(configurator.PubSubConfiguration);
            Assert.That(result, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void EnableNullThrowsArgumentException()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            Assert.Throws<ArgumentException>(() => configurator.Enable(null));
        }

        [Test]
        public void DisableNullThrowsArgumentException()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            Assert.Throws<ArgumentException>(() => configurator.Disable(null));
        }

        [Test]
        public void EnableUnknownObjectThrowsArgumentException()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            Assert.Throws<ArgumentException>(
                () => configurator.Enable(new PubSubConnectionDataType { Enabled = true }));
        }

        [Test]
        public void DisableUnknownObjectThrowsArgumentException()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            Assert.Throws<ArgumentException>(
                () => configurator.Disable(new PubSubConnectionDataType { Enabled = true }));
        }

        [Test]
        public void AddConnectionRegistersConnection()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            PubSubConnectionDataType conn = CreateConnection();
            StatusCode result = configurator.AddConnection(conn);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ConnectionStateIsPausedWhenRootDisabled()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            configurator.Disable(configurator.PubSubConfiguration);
            PubSubConnectionDataType conn = CreateConnection();
            configurator.AddConnection(conn);
            PubSubState state = configurator.FindStateForObject(conn);
            Assert.That(state, Is.EqualTo(PubSubState.Paused));
        }

        [Test]
        public void ConnectionStateIsOperationalWhenRootEnabled()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            PubSubConnectionDataType conn = CreateConnection();
            configurator.AddConnection(conn);
            PubSubState state = configurator.FindStateForObject(conn);
            Assert.That(state, Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public void DisableConnectionTransitionsToDisabled()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            PubSubConnectionDataType conn = CreateConnection();
            configurator.AddConnection(conn);
            configurator.Disable(conn);
            Assert.That(
                configurator.FindStateForObject(conn),
                Is.EqualTo(PubSubState.Disabled));
        }

        [Test]
        public void EnableDisabledConnectionTransitionsToOperational()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            PubSubConnectionDataType conn = CreateConnection();
            configurator.AddConnection(conn);
            configurator.Disable(conn);
            configurator.Enable(conn);
            Assert.That(
                configurator.FindStateForObject(conn),
                Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public void EnableConnectionWhenParentDisabledBecomesPaused()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            configurator.Disable(configurator.PubSubConfiguration);
            PubSubConnectionDataType conn = CreateConnection();
            configurator.AddConnection(conn);
            // conn is Paused because root is Disabled. Disable conn, then re-enable.
            configurator.Disable(conn);
            configurator.Enable(conn);
            // Root is still disabled, so enabling conn makes it Paused
            Assert.That(
                configurator.FindStateForObject(conn),
                Is.EqualTo(PubSubState.Paused));
        }

        [Test]
        public void DisablingParentPausesChildren()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            PubSubConnectionDataType conn = CreateConnection();
            configurator.AddConnection(conn);
            Assert.That(
                configurator.FindStateForObject(conn),
                Is.EqualTo(PubSubState.Operational));
            configurator.Disable(configurator.PubSubConfiguration);
            Assert.That(
                configurator.FindStateForObject(conn),
                Is.EqualTo(PubSubState.Paused));
        }

        [Test]
        public void EnablingParentResumesChildren()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            PubSubConnectionDataType conn = CreateConnection();
            configurator.AddConnection(conn);
            configurator.Disable(configurator.PubSubConfiguration);
            configurator.Enable(configurator.PubSubConfiguration);
            Assert.That(
                configurator.FindStateForObject(conn),
                Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public void PubSubStateChangedEventFires()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            var stateChanges = new List<PubSubStateChangedEventArgs>();
            configurator.PubSubStateChanged += (sender, e) => stateChanges.Add(e);
            configurator.Disable(configurator.PubSubConfiguration);
            Assert.That(stateChanges, Is.Not.Empty);
            Assert.That(stateChanges[0].NewState, Is.EqualTo(PubSubState.Disabled));
        }

        [Test]
        public void FindStateForUnknownObjectReturnsError()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            PubSubState state = configurator.FindStateForObject(
                new PubSubConnectionDataType { Enabled = true });
            Assert.That(state, Is.EqualTo(PubSubState.Error));
        }

        [Test]
        public void FindStateForIdUnknownReturnsError()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            PubSubState state = configurator.FindStateForId(999);
            Assert.That(state, Is.EqualTo(PubSubState.Error));
        }

        [Test]
        public void FindIdForUnknownObjectReturnsInvalidId()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            uint id = configurator.FindIdForObject(new PubSubConnectionDataType { Enabled = true });
            Assert.That(id, Is.EqualTo(UaPubSubConfigurator.InvalidId));
        }

        [Test]
        public void FindObjectByIdReturnsNullForUnknownId()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            object obj = configurator.FindObjectById(999);
            Assert.That(obj, Is.Null);
        }

        [Test]
        public void FindParentForUnknownObjectReturnsNull()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            object parent = configurator.FindParentForObject(
                new PubSubConnectionDataType { Enabled = true });
            Assert.That(parent, Is.Null);
        }

        [Test]
        public void FindChildrenIdsForUnknownObjectReturnsEmpty()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            List<uint> children = configurator.FindChildrenIdsForObject(
                new PubSubConnectionDataType { Enabled = true });
            Assert.That(children, Is.Empty);
        }

        [Test]
        public void RemoveConnectionRemovesObject()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            PubSubConnectionDataType conn = CreateConnection();
            configurator.AddConnection(conn);
            StatusCode result = configurator.RemoveConnection(conn);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            uint id = configurator.FindIdForObject(conn);
            Assert.That(id, Is.EqualTo(UaPubSubConfigurator.InvalidId));
        }

        [Test]
        public void AddPublishedDataSetRegisters()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            var pds = new PublishedDataSetDataType { Name = "PDS1" };
            StatusCode result = configurator.AddPublishedDataSet(pds);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(configurator.FindPublishedDataSetByName("PDS1"), Is.SameAs(pds));
        }

        [Test]
        public void AddDuplicatePublishedDataSetReturnsDuplicate()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            var pds1 = new PublishedDataSetDataType { Name = "PDS1" };
            var pds2 = new PublishedDataSetDataType { Name = "PDS1" };
            configurator.AddPublishedDataSet(pds1);
            StatusCode result = configurator.AddPublishedDataSet(pds2);
            Assert.That(
                result,
                Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public void RemovePublishedDataSetByIdSucceeds()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            var pds = new PublishedDataSetDataType { Name = "PDS1" };
            configurator.AddPublishedDataSet(pds);
            uint id = configurator.FindIdForObject(pds);
            StatusCode result = configurator.RemovePublishedDataSet(id);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(configurator.FindPublishedDataSetByName("PDS1"), Is.Null);
        }

        [Test]
        public void RemovePublishedDataSetByUnknownIdReturnsGood()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            StatusCode result = configurator.RemovePublishedDataSet(999u);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void EnableByIdDelegatesToEnableByObject()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            configurator.Disable(configurator.PubSubConfiguration);
            uint rootId = configurator.FindIdForObject(configurator.PubSubConfiguration);
            StatusCode result = configurator.Enable(rootId);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void DisableByIdDelegatesToDisableByObject()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            uint rootId = configurator.FindIdForObject(configurator.PubSubConfiguration);
            StatusCode result = configurator.Disable(rootId);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void FindPublishedDataSetByNameReturnsNullWhenNotFound()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();
            Assert.That(
                configurator.FindPublishedDataSetByName("NonExistent"),
                Is.Null);
        }

        [Test]
        public void WriterGroupStateFollowsConnectionState()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();

            var writerGroup = new WriterGroupDataType
            {
                Name = "WG1",
                Enabled = true,
                DataSetWriters = []
            };
            var conn = new PubSubConnectionDataType
            {
                Name = "Conn1",
                Enabled = true,
                PublisherId = Variant.From("Conn1"),
                WriterGroups = [writerGroup],
                ReaderGroups = []
            };
            configurator.AddConnection(conn);

            Assert.That(
                configurator.FindStateForObject(writerGroup),
                Is.EqualTo(PubSubState.Operational));

            configurator.Disable(conn);
            Assert.That(
                configurator.FindStateForObject(writerGroup),
                Is.EqualTo(PubSubState.Paused));
        }

        [Test]
        public void DisabledWriterGroupStaysDisabledWhenConnectionEnabled()
        {
            UaPubSubConfigurator configurator = CreateConfigurator();

            var writerGroup = new WriterGroupDataType
            {
                Name = "WG1",
                Enabled = false,
                DataSetWriters = []
            };
            var conn = new PubSubConnectionDataType
            {
                Name = "Conn1",
                Enabled = true,
                PublisherId = Variant.From("Conn1"),
                WriterGroups = [writerGroup],
                ReaderGroups = []
            };
            configurator.AddConnection(conn);

            Assert.That(
                configurator.FindStateForObject(writerGroup),
                Is.EqualTo(PubSubState.Disabled));
        }
    }
}
