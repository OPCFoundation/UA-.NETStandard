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
using System;

namespace Opc.Ua.PubSub.Tests
{
    partial class PubSubStateMachineTests
    {
        [Test(Description = "Validate Call Enable on Disabled object")]
        public void ValidateEnableOnDisabled()
        {
            UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(PublisherConfigurationFileName);
            UaPubSubConfigurator configurator = uaPubSubApplication.UaPubSubConfigurator;
            PubSubConfigurationDataType pubSub = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration;
            configurator.Disable(pubSub);
            Assert.AreEqual((StatusCode)StatusCodes.Good, configurator.Enable(pubSub));
        }

        [Test(Description = "Validate Call Enable on Enabled object")]
        public void ValidateEnableOnOperational()
        {
            UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(PublisherConfigurationFileName);
            UaPubSubConfigurator configurator = uaPubSubApplication.UaPubSubConfigurator;
            PubSubConfigurationDataType pubSub = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration;
            configurator.Enable(pubSub);
            Assert.AreEqual((StatusCode)StatusCodes.BadInvalidState, configurator.Enable(pubSub));
        }

        [Test(Description = "Validate Call Disable on Enabled object")]
        public void ValidateDisableOnEnabled()
        {
            UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(PublisherConfigurationFileName);
            UaPubSubConfigurator configurator = uaPubSubApplication.UaPubSubConfigurator;
            PubSubConfigurationDataType pubSub = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration;
            configurator.Enable(pubSub);
            Assert.AreEqual((StatusCode)StatusCodes.Good, configurator.Disable(pubSub));
        }

        [Test(Description = "Validate Call Disable on Disabled object")]
        public void ValidateDisableOnDisabled()
        {
            UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(PublisherConfigurationFileName);
            UaPubSubConfigurator configurator = uaPubSubApplication.UaPubSubConfigurator;
            PubSubConfigurationDataType pubSub = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration;
            configurator.Disable(pubSub);
            Assert.AreEqual((StatusCode)StatusCodes.BadInvalidState, configurator.Disable(pubSub));
        }

        [Test(Description = "Validate Call Enable on null object")]
        public void ValidateEnableOnNUll()
        {
            UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(PublisherConfigurationFileName);
            UaPubSubConfigurator configurator = uaPubSubApplication.UaPubSubConfigurator;
            Assert.Throws<ArgumentException>(() => configurator.Enable(null), "The Enable method does not throw exception when called with null parameter.");
        }

        [Test(Description = "Validate Call Disable on null object")]
        public void ValidateDisableOnNUll()
        {
            UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(PublisherConfigurationFileName);
            UaPubSubConfigurator configurator = uaPubSubApplication.UaPubSubConfigurator;
            Assert.Throws<ArgumentException>(() => configurator.Disable(null), "The Disable method does not throw exception when called with null parameter.");
        }

        [Test(Description = "Validate Call Enable on non existing object")]
        public void ValidateEnableOnNonExisting()
        {
            UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(PublisherConfigurationFileName);
            UaPubSubConfigurator configurator = uaPubSubApplication.UaPubSubConfigurator;
            PubSubConfigurationDataType nonExisting = new PubSubConfigurationDataType();
            Assert.Throws<ArgumentException>(() => configurator.Enable(nonExisting), "The Enable method does not throw exception when called with non existing parameter.");
        }

        [Test(Description = "Validate Call Disable on non existing object")]
        public void ValidateDisableOnNonExisting()
        {
            UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(PublisherConfigurationFileName);
            UaPubSubConfigurator configurator = uaPubSubApplication.UaPubSubConfigurator;
            PubSubConfigurationDataType nonExisting = new PubSubConfigurationDataType();
            Assert.Throws<ArgumentException>(() => configurator.Disable(nonExisting), "The Disable method does not throw exception when called with non existing parameter.");
        }

    }
}
