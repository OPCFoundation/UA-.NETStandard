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
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Exercises bounded configuration and generated-model selection guards through their public entry points.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    public sealed class GmsSystemGuardTests
    {
        /// <summary>
        /// Native workflow bounds reject invalid element, reference and payload limits without service requests.
        /// </summary>
        [TestCase(0, 1, 1)]
        [TestCase(4097, 1, 1)]
        [TestCase(1, 0, 1)]
        [TestCase(1, 100001, 1)]
        [TestCase(1, 1, 0)]
        [TestCase(1, 1, 1048577)]
        public void ClientRejectsInvalidReadBounds(int elements, int references, int payload)
        {
            var options = new GmsClientOptions
            {
                MaximumElements = elements, MaximumReferences = references, MaximumPayloadBytes = payload
            };
            Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
        }

        /// <summary>
        /// Runtime limits cannot be disabled by zero or exceed the documented finite aggregate ceiling.
        /// </summary>
        [TestCase(0, 128, 32)]
        [TestCase(4097, 128, 32)]
        [TestCase(128, 0, 32)]
        [TestCase(128, 4097, 32)]
        [TestCase(128, 128, 0)]
        [TestCase(128, 128, 1025)]
        public void BuilderRejectsInvalidRuntimeBounds(int sensors, int equipment, int history)
        {
            var builder = new GmsBuilder(Mock.Of<IMachineryBuildContext>(), "Bounds");
            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                builder.WithOptions(new GmsSystemOptions
                {
                    MaximumSensors = sensors, MaximumEquipment = equipment, MaximumCalibrationHistory = history
                }))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        /// <summary>
        /// Standalone application nodes cannot claim a standard model namespace.
        /// </summary>
        [TestCase("")]
        [TestCase("relative")]
        [TestCase("http://opcfoundation.org/UA/GMS/")]
        [TestCase("http://opcfoundation.org/UA/MachineTool/")]
        public void HostingRejectsInvalidApplicationNamespaces(string uri)
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });
            Assert.Throws<ArgumentException>(() => builder.AddGms(options => options.InstanceNamespaceUri = uri));
        }

        /// <summary>
        /// In-memory providers refuse missing inherited observations before attempting revision assignment.
        /// </summary>
        [Test]
        public void ProviderRejectsMissingSnapshotsAndInheritedObservations()
        {
            Assert.Throws<ArgumentNullException>(() => new InMemoryGmsProvider(null!));
            Assert.Throws<ArgumentException>(() =>
                new InMemoryGmsProvider(new GmsSystemSnapshot { MachineTool = null! }));
            var provider = new InMemoryGmsProvider(new GmsSystemSnapshot());
            Assert.ThrowsAsync<ArgumentNullException>(() => provider.SetSnapshotAsync(null!).AsTask());
            Assert.ThrowsAsync<ArgumentException>(() =>
                provider.SetSnapshotAsync(new GmsSystemSnapshot { MachineTool = null! }).AsTask());
        }

        /// <summary>
        /// Native selection cannot silently accept a substituted Number-valued Qualified or missing ToolLife.
        /// </summary>
        [Test]
        public void QualifiedSelectionPreservesItsActualGeneratedDoubleDeclaration()
        {
            SystemContext context = ModelTestContext.CreateSystemContext();
            SensorState sensor = context.CreateInstanceOfSensorType();
            Assert.Throws<ArgumentNullException>(() => GmsEquipmentNodeExtensions.AddQualified(null!, context));
            Assert.Throws<ArgumentNullException>(() => sensor.AddQualified(null!));
            var name = new QualifiedName(BrowseNames.Qualified,
                (ushort)context.NamespaceUris.GetIndex(GmsModel.NamespaceUri));
            ToolLifeState replacement = context.CreateInstanceOfToolLifeType(sensor.ToolLife!, name);
            sensor.ToolLife!.AddChild(replacement);
            Assert.Throws<ServiceResultException>(() => sensor.AddQualified(context));
            sensor.ToolLife = null;
            Assert.Throws<ServiceResultException>(() => sensor.AddQualified(context));
        }
    }
}
