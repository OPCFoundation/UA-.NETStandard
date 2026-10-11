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

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Di;
using Opc.Ua.IA;
using Opc.Ua.Machinery;
using Opc.Ua.Tests;
using Opc.Ua.Vision.MachineVision;
using Opc.Ua.Vision.MachineVision.AMCM;
using Opc.Ua.Vision.Perception;
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Pins the independent released models and their generated runtime contracts.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed class MachineVisionModelTests
    {
        /// <summary>
        /// Published model data is retained byte-for-byte, including upstream identifiers.
        /// </summary>
        [TestCase("Opc.Ua.MachineVision.NodeSet2.xml", MachineVisionModels.ControlNodeSetSha256)]
        [TestCase("Opc.Ua.MachineVision.AMCM.NodeSet2.xml", MachineVisionModels.AssetNodeSetSha256)]
        public void PublishedNodeSetIsUnmodified(string file, string expectedHash)
        {
            using var hash = SHA256.Create();
            using FileStream stream = File.OpenRead(
                Path.Combine(TestContext.CurrentContext.TestDirectory, "Model", file));
            Assert.That(
                CoreUtils.ToHexString(hash.ComputeHash(stream)),
                Is.EqualTo(expectedHash));
        }

        /// <summary>
        /// Part 2 is an independent, observational model, not a Part 1 extension.
        /// </summary>
        [Test]
        public void AssetModelHasNoMethodsAndRequiresMachineryNotControl()
        {
            XDocument model = ReadModel("Opc.Ua.MachineVision.AMCM.NodeSet2.xml");
            Assert.That(model.Descendants(s_ns + "UAMethod"), Is.Empty);
            string?[] dependencies =
                [.. model.Descendants(s_ns + "RequiredModel").Select(element => (string?)element.Attribute("ModelUri"))];
            Assert.That(dependencies, Does.Contain(Machinery.Namespaces.Machinery));
            Assert.That(dependencies, Does.Not.Contain(MachineVisionModels.ControlNamespaceUri));
            XElement placeholder = model.Root!.Elements().Single(
                element => (string?)element.Attribute("NodeId") == "ns=1;i=5004");
            Assert.That(
                placeholder.Descendants(s_ns + "Reference").Single(
                    reference => (string?)reference.Attribute("ReferenceType") == "HasModellingRule").Value,
                Is.EqualTo("i=11510"));
        }

        /// <summary>
        /// All model loaders can coexist and preserve the distinct namespace identities.
        /// </summary>
        [Test]
        public void GeneratedModelsLoadTogetherAndCreateTypedInstances()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.Create(telemetry);
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory
            };
            foreach (string uri in new VisionModelProvider(MachineVisionParts.All).NamespaceUris)
            {
                context.NamespaceUris.GetIndexOrAppend(uri);
            }
            foreach (string uri in new VisionMachineryStandaloneModelProvider().NamespaceUris)
            {
                context.NamespaceUris.GetIndexOrAppend(uri);
            }
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            nodes.AddOpcUaIA(context);
            nodes.AddOpcUaMachinery(context);
            nodes.AddOpcUaVisionPerception(context);
            Assert.That(nodes.AddOpcUaMachineVision(context), Is.SameAs(nodes));
            Assert.That(nodes.AddOpcUaMachineVisionAMCM(context), Is.SameAs(nodes));

            VisionSystemState control = context.CreateInstanceOfVisionSystemType();
            VisionSystemAssetState asset = context.CreateInstanceOfVisionSystemAssetType();
            Assert.Multiple(() =>
            {
                Assert.That(control.VisionStateMachine, Is.Not.Null);
                Assert.That(asset.Identification, Is.Not.Null);
                Assert.That(
                    control.TypeDefinitionId.NamespaceIndex,
                    Is.Not.EqualTo(asset.TypeDefinitionId.NamespaceIndex));
                Assert.That(context.NamespaceUris.GetIndex(MachineVisionModels.ControlNamespaceUri), Is.GreaterThan(0));
                Assert.That(context.NamespaceUris.GetIndex(MachineVisionModels.AssetNamespaceUri), Is.GreaterThan(0));
            });
        }

        /// <summary>
        /// The short loader names register both encodeable and node state factories without changing CLR namespaces.
        /// </summary>
        [Test]
        public void ModelLoaderNamesRegisterTypedFactories()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var message = ServiceMessageContext.CreateEmpty(telemetry);
            IEncodeableFactoryBuilder encodeables = message.Factory.Builder;
            Assert.That(encodeables.AddOpcUaMachineVision(), Is.SameAs(encodeables));
            Assert.That(encodeables.AddOpcUaMachineVisionAMCM(), Is.SameAs(encodeables));
            encodeables.Commit();
            Assert.That(message.Factory.ContainsEncodeableType(MachineVision.DataTypeIds.ResultDataType), Is.True);
            Assert.That(message.Factory.ContainsEncodeableType(
                MachineVision.AMCM.DataTypeIds.SEMI_E10SystemStateInfoDataType), Is.True);

            var context = new SystemContext(telemetry) { NamespaceUris = message.NamespaceUris };
            context.NamespaceUris.GetIndexOrAppend(MachineVisionModels.ControlNamespaceUri);
            context.NamespaceUris.GetIndexOrAppend(MachineVisionModels.AssetNamespaceUri);
            var factory = new NodeStateFactory();
            Assert.That(factory.AddOpcUaMachineVision(), Is.SameAs(factory));
            Assert.That(factory.AddOpcUaMachineVisionAMCM(), Is.SameAs(factory));
            Assert.That(factory.CreateInstance(
                context, null, NodeClass.Object, new QualifiedName("Control"), ReferenceTypeIds.HasComponent,
                ExpandedNodeId.ToNodeId(MachineVision.ObjectTypeIds.VisionSystemType, context.NamespaceUris)),
                Is.TypeOf<VisionSystemState>());
            Assert.That(factory.CreateInstance(
                context, null, NodeClass.Object, new QualifiedName("Asset"), ReferenceTypeIds.HasComponent,
                ExpandedNodeId.ToNodeId(
                    MachineVision.AMCM.ObjectTypeIds.VisionSystemAssetType, context.NamespaceUris)),
                Is.TypeOf<VisionSystemAssetState>());
        }

        /// <summary>
        /// Both models reject missing loader arguments at their public entry points.
        /// </summary>
        [Test]
        public void ModelLoaderNamesRejectNullArguments()
        {
            var context = new SystemContext(NUnitTelemetryContext.Create());
            var nodes = new NodeStateCollection();
            Assert.Multiple(() =>
            {
                Assert.That(Assert.Throws<ArgumentNullException>(() =>
                    ((NodeStateCollection)null!).AddOpcUaMachineVision(context))!.ParamName, Is.EqualTo("nodes"));
                Assert.That(Assert.Throws<ArgumentNullException>(() =>
                    ((NodeStateCollection)null!).AddOpcUaMachineVisionAMCM(context))!.ParamName, Is.EqualTo("nodes"));
                Assert.That(Assert.Throws<ArgumentNullException>(() =>
                    nodes.AddOpcUaMachineVision(null!))!.ParamName, Is.EqualTo("context"));
                Assert.That(Assert.Throws<ArgumentNullException>(() =>
                    nodes.AddOpcUaMachineVisionAMCM(null!))!.ParamName, Is.EqualTo("context"));
                Assert.That(Assert.Throws<ArgumentNullException>(() =>
                    ((IEncodeableFactoryBuilder)null!).AddOpcUaMachineVision())!.ParamName, Is.EqualTo("builder"));
                Assert.That(Assert.Throws<ArgumentNullException>(() =>
                    ((IEncodeableFactoryBuilder)null!).AddOpcUaMachineVisionAMCM())!.ParamName, Is.EqualTo("builder"));
                Assert.That(Assert.Throws<ArgumentNullException>(() =>
                    ((INodeStateFactoryBuilder)null!).AddOpcUaMachineVision())!.ParamName, Is.EqualTo("builder"));
                Assert.That(Assert.Throws<ArgumentNullException>(() =>
                    ((INodeStateFactoryBuilder)null!).AddOpcUaMachineVisionAMCM())!.ParamName, Is.EqualTo("builder"));
            });
        }

        /// <summary>
        /// Existing hosting remains opt-in and invalid model selections are rejected.
        /// </summary>
        [Test]
        public void ReleasedModelsAreOptInAndModelNamespacesCannotOwnInstances()
        {
            var options = new VisionServerOptions();
            Assert.That(options.MachineVisionParts, Is.EqualTo(MachineVisionParts.None));
            options.MachineVisionParts = (MachineVisionParts)8;
            Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
            options.MachineVisionParts = MachineVisionParts.All;
            options.InstanceNamespaceUri = MachineVisionModels.AssetNamespaceUri;
            Assert.Throws<ServiceResultException>(options.Validate);
        }

        private static XDocument ReadModel(string file)
        {
            return XDocument.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "Model", file));
        }

        private static readonly XNamespace s_ns = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
    }
}
