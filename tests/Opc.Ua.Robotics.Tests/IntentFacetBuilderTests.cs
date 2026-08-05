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
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.RobotIntent;
using Opc.Ua.Tests;
using RiAxisState = Opc.Ua.RobotIntent.AxisState;
using RiDataTypes = Opc.Ua.RobotIntent.DataTypes;
using RiNamespaces = Opc.Ua.RobotIntent.Namespaces;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Tests Robot Intent facet computation.
    /// </summary>
    [TestFixture]
    public class IntentFacetBuilderTests
    {
        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(true);
            var messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.Append(RiNamespaces.RobotIntent);
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory
            };
            m_controller = new IntentControllerState(null);
            m_controller.Create(
                m_context,
                new NodeId("Controller", 1),
                new QualifiedName("Controller", 1),
                new LocalizedText("Controller"),
                true);
        }

        [Test]
        public void ComputeFacetsForMinimalControllerClaimsBaseOnly()
        {
            m_controller.Capabilities!.SupportedIntents!.Value = new[]
            {
                Capability(RiDataTypes.WaitIntentDataType)
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Contain("RI-Base"));
            Assert.That(facets, Does.Contain("RI-Wait"));
            Assert.That(facets, Does.Not.Contain("RI-Trajectory"));
        }

        [Test]
        public void ComputeFacetsRequiresCapabilityAndAddressSpaceEvidence()
        {
            AddAxis("Axis0", 0);
            m_controller.Capabilities!.AxisCount!.Value = 1;
            m_controller.Capabilities.TrajectorySupported!.Value = true;
            m_controller.Capabilities.SupportedIntents!.Value = new[]
            {
                Capability(RiDataTypes.JointMoveIntentDataType),
                Capability(RiDataTypes.TrajectoryIntentDataType)
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Contain("RI-Motion-Joint"));
            Assert.That(facets, Does.Contain("RI-Trajectory"));
            Assert.That(facets, Does.Not.Contain("RI-Motion-Linear"));
        }

        [Test]
        public void ComputeFacetsClaimsOnlySatisfiedProcessFacets()
        {
            AddTool("Tool0");
            AddTool("Tool1");
            AddLocation("Location0");
            m_controller.AddOutputs(m_context);
            m_controller.AddPrograms(m_context);
            m_controller.AddDescription(m_context);
            m_controller.AddRealTimeChannels(m_context);
            m_controller.Capabilities!.RealTimeChannelsSupported!.Value = true;
            m_controller.Capabilities.SupportedIntents!.Value = new[]
            {
                Capability(RiDataTypes.GraspIntentDataType),
                Capability(RiDataTypes.ReleaseIntentDataType),
                Capability(RiDataTypes.PickIntentDataType),
                Capability(RiDataTypes.PlaceIntentDataType),
                Capability(RiDataTypes.ToolChangeIntentDataType),
                Capability(RiDataTypes.SetOutputIntentDataType),
                Capability(RiDataTypes.CallProgramIntentDataType)
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.Multiple(() =>
            {
                Assert.That(facets, Does.Contain("RI-Grasp"));
                Assert.That(facets, Does.Contain("RI-PickPlace"));
                Assert.That(facets, Does.Contain("RI-ToolChange"));
                Assert.That(facets, Does.Contain("RI-Output"));
                Assert.That(facets, Does.Contain("RI-Program"));
                Assert.That(facets, Does.Contain("RI-Safety"));
                Assert.That(facets, Does.Contain("RI-Description"));
                Assert.That(facets, Does.Contain("RI-RealTimeChannel"));
            });
        }

        [Test]
        public void ComputeFacetsRequiresEveryIntentNamedByFacet()
        {
            AddTool("Tool0");
            AddLocation("Location0");
            m_controller.Capabilities!.SupportedIntents!.Value = new[]
            {
                Capability(RiDataTypes.GraspIntentDataType),
                Capability(RiDataTypes.PickIntentDataType)
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.Multiple(() =>
            {
                Assert.That(facets, Does.Not.Contain("RI-Grasp"));
                Assert.That(facets, Does.Not.Contain("RI-PickPlace"));
            });
        }

        [Test]
        public void ComputeFacetsRejectsDeclaredCapabilityWithoutRequiredEvidence()
        {
            m_controller.Capabilities!.TrajectorySupported!.Value = true;
            m_controller.Capabilities.AxisCount!.Value = 2;
            AddAxis("Axis0", 0);
            m_controller.Capabilities.SupportedIntents!.Value = new[]
            {
                Capability(RiDataTypes.JointMoveIntentDataType),
                Capability(RiDataTypes.TrajectoryIntentDataType),
                Capability(RiDataTypes.ForceIntentDataType)
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.Multiple(() =>
            {
                Assert.That(facets, Does.Not.Contain("RI-Motion-Joint"));
                Assert.That(facets, Does.Contain("RI-Trajectory"));
                Assert.That(facets, Does.Not.Contain("RI-Force"));
            });
        }

        private void AddAxis(string browseName, uint index)
        {
            RiAxisState axis = OpcUaRobotIntentExtensions.CreateInstanceOfAxisType(
                m_context,
                m_controller.Axes!,
                new QualifiedName(browseName, 1));
            axis.Index!.Value = index;
            axis.Kind!.Value = AxisKindEnum.Revolute;
            m_controller.Axes!.AddChild(axis);
        }

        private void AddTool(string browseName)
        {
            ToolState tool = OpcUaRobotIntentExtensions.CreateInstanceOfToolType(
                m_context,
                m_controller.Tools!,
                new QualifiedName(browseName, 1));
            m_controller.Tools!.AddChild(tool);
        }

        private void AddLocation(string browseName)
        {
            LocationState location = OpcUaRobotIntentExtensions.CreateInstanceOfLocationType(
                m_context,
                m_controller.Locations!,
                new QualifiedName(browseName, 1));
            m_controller.Locations!.AddChild(location);
        }

        private IntentCapabilityDataType Capability(uint dataType)
        {
            return new IntentCapabilityDataType
            {
                IntentType = NodeId.Create(dataType, RiNamespaces.RobotIntent, m_context.NamespaceUris),
                SupportedBufferModes = new[] { BufferModeEnum.Aborting }.ToArrayOf()
            };
        }

        private SystemContext m_context = null!;
        private IntentControllerState m_controller = null!;
    }
}
