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

using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub;
using Opc.Ua.PubSub.Configuration;

namespace Opc.Ua.PubSub.Tests
{
    partial class PubSubStateMachineTests
    {
        private const string PublisherConfigurationFileName = "PublisherConfiguration.xml";

        [OneTimeSetUp()]
        public void MyTestInitialize()
        {
        }

        [Test(Description = "Validate transition of state Disabled_0 to Paused_1 on Publisher")]
        public void ValidateDisabled_0ToPause_1_Publisher()
        {
            UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(PublisherConfigurationFileName);

            UaPubSubConfigurator configurator = uaPubSubApplication.UaPubSubConfigurator;

            // The hierarchy PubSub -> PubSubConnection -> PubSubWriterGroup -> DataSetWriter brought to [Disabled, Disabled, Disabled, Disabled]

            PubSubConfigurationDataType pubSub = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration;
            PubSubConnectionDataType publisherConnection = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration.Connections[0];
            WriterGroupDataType writerGroup = publisherConnection.WriterGroups[0];
            DataSetWriterDataType datasetWriter = writerGroup.DataSetWriters[0];

            configurator.Disable(pubSub);
            configurator.Disable(publisherConnection);
            configurator.Disable(writerGroup);
            configurator.Disable(datasetWriter);

            PubSubState psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            PubSubState conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            PubSubState wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            PubSubState dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState  == PubSubState.Disabled, Is.True);
            Assert.That(conState == PubSubState.Disabled, Is.True);
            Assert.That(wgState == PubSubState.Disabled, Is.True);
            Assert.That(dswState == PubSubState.Disabled, Is.True);

            // Bring connection to Enabled
            configurator.Enable(publisherConnection);

            psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Disabled, Is.True);
            Assert.That(conState == PubSubState.Paused, Is.True);
            Assert.That(wgState == PubSubState.Disabled, Is.True);
            Assert.That(dswState == PubSubState.Disabled, Is.True);

            // Bring writerGroup to Enabled
            configurator.Enable(writerGroup);

            psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Disabled, Is.True);
            Assert.That(conState == PubSubState.Paused, Is.True);
            Assert.That(wgState == PubSubState.Paused, Is.True);
            Assert.That(dswState == PubSubState.Disabled, Is.True);

            // Bring datasetWriter to Enabled
            configurator.Enable(datasetWriter);

            psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Disabled, Is.True);
            Assert.That(conState == PubSubState.Paused, Is.True);
            Assert.That(wgState == PubSubState.Paused, Is.True);
            Assert.That(dswState == PubSubState.Paused, Is.True);
        }

        [Test(Description = "Validate transition of state Disabled_0 to Operational_2 on Publisher")]
        public void ValidateDisabled_0ToOperational_2_Publisher()
        {
            UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(PublisherConfigurationFileName);

            UaPubSubConfigurator configurator = uaPubSubApplication.UaPubSubConfigurator;

            // The hierarchy PubSub -> PubSubConnection -> PubSubWriterGroup -> DataSetWriter brought to [Disabled, Disabled, Disabled, Disabled]
            PubSubConfigurationDataType pubSub = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration;
            PubSubConnectionDataType publisherConnection = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration.Connections[0];
            WriterGroupDataType writerGroup = publisherConnection.WriterGroups[0];
            DataSetWriterDataType datasetWriter = writerGroup.DataSetWriters[0];

            configurator.Disable(pubSub);
            configurator.Disable(publisherConnection);
            configurator.Disable(writerGroup);
            configurator.Disable(datasetWriter);

            PubSubState psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            PubSubState conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            PubSubState wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            PubSubState dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Disabled, Is.True);
            Assert.That(conState == PubSubState.Disabled, Is.True);
            Assert.That(wgState == PubSubState.Disabled, Is.True);
            Assert.That(dswState == PubSubState.Disabled, Is.True);

            // Bring PubSub to Enabled
            configurator.Enable(pubSub);
            psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Operational, Is.True);
            Assert.That(conState == PubSubState.Disabled, Is.True);
            Assert.That(wgState == PubSubState.Disabled, Is.True);
            Assert.That(dswState == PubSubState.Disabled, Is.True);

            // Bring publisherConnection to Enabled
            configurator.Enable(publisherConnection);
            psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Operational, Is.True);
            Assert.That(conState == PubSubState.Operational, Is.True);
            Assert.That(wgState == PubSubState.Disabled, Is.True);
            Assert.That(dswState == PubSubState.Disabled, Is.True);

            // Bring writerGroup to Enabled
            configurator.Enable(writerGroup);
            psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Operational, Is.True);
            Assert.That(conState == PubSubState.Operational, Is.True);
            Assert.That(wgState == PubSubState.Operational, Is.True);
            Assert.That(dswState == PubSubState.Disabled, Is.True);

            // Bring datasetWriter to Enabled
            configurator.Enable(datasetWriter);
            psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Operational, Is.True);
            Assert.That(conState == PubSubState.Operational, Is.True);
            Assert.That(wgState == PubSubState.Operational, Is.True);
            Assert.That(dswState == PubSubState.Operational, Is.True);
        }

        [Test(Description = "Validate transition of state Paused_1 to Disabled_0 on Publisher")]
        public void ValidatePaused_1ToDisabled_0_Publisher()
        {
            UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(PublisherConfigurationFileName);

            UaPubSubConfigurator configurator = uaPubSubApplication.UaPubSubConfigurator;

            // The hierarchy PubSub -> PubSubConnection -> PubSubWriterGroup -> DataSetWriter brought to [Disabled, Paused, Paused, Paused]
            PubSubConfigurationDataType pubSub = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration;
            PubSubConnectionDataType publisherConnection = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration.Connections[0];
            WriterGroupDataType writerGroup = publisherConnection.WriterGroups[0];
            DataSetWriterDataType datasetWriter = writerGroup.DataSetWriters[0];

            configurator.Disable(pubSub);
            configurator.Disable(publisherConnection);
            configurator.Disable(writerGroup);
            configurator.Disable(datasetWriter);

            configurator.Enable(publisherConnection);
            configurator.Enable(writerGroup);
            configurator.Enable(datasetWriter);

            PubSubState psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            PubSubState conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            PubSubState wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            PubSubState dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Disabled, Is.True);
            Assert.That(conState == PubSubState.Paused, Is.True);
            Assert.That(wgState == PubSubState.Paused, Is.True);
            Assert.That(dswState == PubSubState.Paused, Is.True);

            // Bring Connection to Disabled
            configurator.Disable(publisherConnection);

            psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Disabled, Is.True);
            Assert.That(conState == PubSubState.Disabled, Is.True);
            Assert.That(wgState == PubSubState.Paused, Is.True);
            Assert.That(dswState == PubSubState.Paused, Is.True);

            // Bring writerGroup to Disabled
            configurator.Disable(writerGroup);

            psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Disabled, Is.True);
            Assert.That(conState == PubSubState.Disabled, Is.True);
            Assert.That(wgState == PubSubState.Disabled, Is.True);
            Assert.That(dswState == PubSubState.Paused, Is.True);

            // Bring datasetWriter to Disabled
            configurator.Disable(datasetWriter);

            psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Disabled, Is.True);
            Assert.That(conState == PubSubState.Disabled, Is.True);
            Assert.That(wgState == PubSubState.Disabled, Is.True);
            Assert.That(dswState == PubSubState.Disabled, Is.True);
        }

        [Test(Description = "Validate transition of state Paused_1 to Operational_2 on Publisher")]
        public void ValidatePaused_1ToOperational_2_Publisher()
        {
            UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(PublisherConfigurationFileName);

            UaPubSubConfigurator configurator = uaPubSubApplication.UaPubSubConfigurator;

            // The hierarchy PubSub -> PubSubConnection -> PubSubWriterGroup -> DataSetWriter brought to [Disabled, Paused, Paused, Paused]

            PubSubConfigurationDataType pubSub = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration;
            PubSubConnectionDataType publisherConnection = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration.Connections[0];
            WriterGroupDataType writerGroup = publisherConnection.WriterGroups[0];
            DataSetWriterDataType datasetWriter = writerGroup.DataSetWriters[0];

            configurator.Disable(pubSub);
            configurator.Disable(publisherConnection);
            configurator.Disable(writerGroup);
            configurator.Disable(datasetWriter);

            configurator.Enable(publisherConnection);
            configurator.Enable(writerGroup);
            configurator.Enable(datasetWriter);

            PubSubState psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            PubSubState conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            PubSubState wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            PubSubState dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Disabled, Is.True);
            Assert.That(conState == PubSubState.Paused, Is.True);
            Assert.That(wgState == PubSubState.Paused, Is.True);
            Assert.That(dswState == PubSubState.Paused, Is.True);

            // Bring pubSub to Enabled
            configurator.Enable(pubSub);
            psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Operational, Is.True);
            Assert.That(conState == PubSubState.Operational, Is.True);
            Assert.That(wgState == PubSubState.Operational, Is.True);
            Assert.That(dswState == PubSubState.Operational, Is.True);

        }

        [Test(Description = "Validate transition of state Operational_2 to Disabled_0 on Publisher")]
        public void ValidateOperational_2ToDisabled_0_Publisher()
        {
            UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(PublisherConfigurationFileName);

            UaPubSubConfigurator configurator = uaPubSubApplication.UaPubSubConfigurator;

            // The hierarchy PubSub -> PubSubConnection -> PubSubWriterGroup -> DataSetWriter brought to [Disabled, Disabled, Disabled, Disabled]
            PubSubConfigurationDataType pubSub = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration;
            PubSubConnectionDataType publisherConnection = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration.Connections[0];
            WriterGroupDataType writerGroup = publisherConnection.WriterGroups[0];
            DataSetWriterDataType datasetWriter = writerGroup.DataSetWriters[0];

            configurator.Disable(pubSub);
            configurator.Disable(publisherConnection);
            configurator.Disable(writerGroup);
            configurator.Disable(datasetWriter);

            PubSubState psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            PubSubState conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            PubSubState wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            PubSubState dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Disabled, Is.True);
            Assert.That(conState == PubSubState.Disabled, Is.True);
            Assert.That(wgState == PubSubState.Disabled, Is.True);
            Assert.That(dswState == PubSubState.Disabled, Is.True);

            // The hierarchy PubSub -> PubSubConnection -> PubSubWriterGroup -> DataSetWriter brought to [Operational, Operational, Operational, Operational]
            configurator.Enable(pubSub);
            configurator.Enable(publisherConnection);
            configurator.Enable(writerGroup);
            configurator.Enable(datasetWriter);

            psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Operational, Is.True);
            Assert.That(conState == PubSubState.Operational, Is.True);
            Assert.That(wgState == PubSubState.Operational, Is.True);
            Assert.That(dswState == PubSubState.Operational, Is.True);

            // The hierarchy PubSub -> PubSubConnection -> PubSubWriterGroup -> DataSetWriter brought to [Disabled, Disabled, Disabled, Disabled]
            configurator.Disable(pubSub);
            configurator.Disable(publisherConnection);
            configurator.Disable(writerGroup);
            configurator.Disable(datasetWriter);

            psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Disabled, Is.True);
            Assert.That(conState == PubSubState.Disabled, Is.True);
            Assert.That(wgState == PubSubState.Disabled, Is.True);
            Assert.That(dswState == PubSubState.Disabled, Is.True);
        }

        [Test(Description = "Validate transition of state Operational_2 to Paused_1 on Publisher")]
        public void ValidateOperational_2ToPaused_1_Publisher()
        {
            UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(PublisherConfigurationFileName);

            UaPubSubConfigurator configurator = uaPubSubApplication.UaPubSubConfigurator;

            // The hierarchy PubSub -> PubSubConnection -> PubSubWriterGroup -> DataSetWriter brought to [Disabled, Disabled, Disabled, Disabled]

            PubSubConfigurationDataType pubSub = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration;
            PubSubConnectionDataType publisherConnection = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration.Connections[0];
            WriterGroupDataType writerGroup = publisherConnection.WriterGroups[0];
            DataSetWriterDataType datasetWriter = writerGroup.DataSetWriters[0];

            configurator.Disable(pubSub);
            configurator.Disable(publisherConnection);
            configurator.Disable(writerGroup);
            configurator.Disable(datasetWriter);

            PubSubState psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            PubSubState conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            PubSubState wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            PubSubState dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Disabled, Is.True);
            Assert.That(conState == PubSubState.Disabled, Is.True);
            Assert.That(wgState == PubSubState.Disabled, Is.True);
            Assert.That(dswState == PubSubState.Disabled, Is.True);

            // The hierarchy PubSub -> PubSubConnection -> PubSubWriterGroup -> DataSetWriter brought to [Operational, Operational, Operational, Operational]
            configurator.Enable(pubSub);
            configurator.Enable(publisherConnection);
            configurator.Enable(writerGroup);
            configurator.Enable(datasetWriter);

            psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Operational, Is.True);
            Assert.That(conState == PubSubState.Operational, Is.True);
            Assert.That(wgState == PubSubState.Operational, Is.True);
            Assert.That(dswState == PubSubState.Operational, Is.True);

            // The hierarchy PubSub -> PubSubConnection -> PubSubWriterGroup -> DataSetWriter brought to [Disabled, Pause, Pause, Pause]
            configurator.Disable(pubSub);
            psState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(pubSub);
            conState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(publisherConnection);
            wgState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(writerGroup);
            dswState = uaPubSubApplication.UaPubSubConfigurator.FindStateForObject(datasetWriter);
            Assert.That(psState == PubSubState.Disabled, Is.True);
            Assert.That(conState == PubSubState.Paused, Is.True);
            Assert.That(wgState == PubSubState.Paused, Is.True);
            Assert.That(dswState == PubSubState.Paused, Is.True);
        }
    }
}
