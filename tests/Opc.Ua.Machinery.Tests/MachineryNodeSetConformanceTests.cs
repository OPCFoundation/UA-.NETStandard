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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Asserts the structural facts of the vendored OPC 40001 NodeSets that the
    /// server layer is built on.
    /// </summary>
    /// <remarks>
    /// The identifier drift guard already fails the build when a symbol moves.
    /// These tests pin the shape instead — the things a reader would otherwise
    /// have to take on trust, and several of which contradict the obvious
    /// assumption.
    /// </remarks>
    [TestFixture]
    [Category("Machinery")]
    public sealed class MachineryNodeSetConformanceTests
    {
        private const string UaNodeSetNamespace =
            "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";

        [Test]
        public void MachinesFolderIsOrganizedByTheObjectsFolder()
        {
            XElement machines = FindNode(Machinery(), "ns=1;i=1001");

            Assert.That(machines.Attribute("BrowseName")!.Value, Is.EqualTo("1:Machines"));
            Assert.That(
                TypeDefinitionOf(machines),
                Is.EqualTo("i=61"),
                "The Machines folder is a FolderType.");
            Assert.That(
                References(machines).Any(reference =>
                    reference.Type == "Organizes" &&
                    !reference.IsForward &&
                    reference.Target == "i=85"),
                Is.True,
                "OPC 40001-1 organizes Machines from Objects (i=85), not from the " +
                "Device Integration DeviceSet.");
        }

        [Test]
        public void ThereIsNoMachineryItemsFolderAndNoBuildingBlocksType()
        {
            IEnumerable<string> browseNames = Machinery()
                .Root!
                .Elements()
                .Where(element => element.Name.LocalName.StartsWith("UA", StringComparison.Ordinal))
                .Select(element => element.Attribute("BrowseName")?.Value ?? string.Empty);

            Assert.That(browseNames, Has.No.Member("1:MachineryItems"));
            Assert.That(browseNames, Has.No.Member("1:MachineryBuildingBlocksType"));
        }

        [Test]
        public void TheMachineryModelDeclaresNoMethodAtAll()
        {
            IEnumerable<XElement> methods = Machinery()
                .Root!
                .Elements()
                .Where(element => element.Name.LocalName == "UAMethod");

            Assert.That(
                methods,
                Is.Empty,
                "Neither OPC 40001-1 state machine declares a cause, so state " +
                "changes always come from the server.");
        }

        [Test]
        public void BothStateMachinesDeclareFourStatesAndSixteenTransitions()
        {
            AssertStateMachine("ns=1;i=1002", s_itemStates);
            AssertStateMachine("ns=1;i=1008", s_operationModes);
        }

        [Test]
        public void IdentificationAndOperationCounterAreFunctionalGroups()
        {
            const string diFunctionalGroupType = "ns=2;i=1005";
            Assert.That(SuperTypeOf(FindNode(Machinery(), "ns=1;i=1004")),
                Is.EqualTo(diFunctionalGroupType),
                "MachineryItemIdentificationType derives from the DI FunctionalGroupType.");
            Assert.That(SuperTypeOf(FindNode(Machinery(), "ns=1;i=1009")),
                Is.EqualTo(diFunctionalGroupType),
                "MachineryOperationCounterType derives from the DI FunctionalGroupType.");
        }

        [Test]
        public void ComponentIdentificationIsAttachedWithHasAddIn()
        {
            XElement identification = FindNode(Machinery(), "ns=1;i=5003");

            Assert.That(
                References(identification).Any(reference =>
                    reference.Type == "HasAddIn" &&
                    !reference.IsForward &&
                    reference.Target == "ns=1;i=5002"),
                Is.True,
                "OPC 40001-1 attaches the building blocks with HasAddIn.");
        }

        [Test]
        public void JobsNeedsNoDeviceIntegration()
        {
            string[] namespaceUris = NamespaceUris(Jobs());

            Assert.That(
                namespaceUris,
                Is.EqualTo(s_jobsNamespaces),
                "OPC 40001-3 composes Job Control V2 and nothing else.");
        }

        [Test]
        public void ResultNeedsNothingButUaCore()
        {
            string[] namespaceUris = NamespaceUris(ResultModel());

            Assert.That(
                namespaceUris,
                Is.EqualTo(s_resultNamespaces),
                "OPC 40001-101 carries neither Device Integration nor the machine model.");
        }

        [Test]
        public void JobManagementHasExactlyTwoMandatoryChildren()
        {
            XDocument jobs = Jobs();
            XElement[] children = ChildrenOf(jobs, "ns=1;i=1003");

            XElement[] modelled = children
                .Where(child =>
                    child.Attribute("BrowseName")!.Value != "DefaultInstanceBrowseName")
                .ToArray();

            Assert.That(
                modelled.Select(child => child.Attribute("BrowseName")!.Value),
                Is.EquivalentTo(s_jobManagementChildren));
            foreach (XElement child in modelled)
            {
                Assert.That(
                    ModellingRuleOf(child),
                    Is.EqualTo("i=78"),
                    $"{child.Attribute("BrowseName")!.Value} is mandatory.");
            }
        }

        [Test]
        public void ResultTransferDerivesFromTemporaryFileTransfer()
        {
            Assert.That(
                SuperTypeOf(FindNode(ResultModel(), "ns=1;i=1003")),
                Is.EqualTo("i=15744"),
                "ResultTransferType derives from TemporaryFileTransferType, which is " +
                "why the download is the standard OPC 10000-5 sequence.");
        }

        [Test]
        public void ResultManagementDeclaresFiveOptionalMethods()
        {
            XElement[] methods = ChildrenOf(ResultModel(), "ns=1;i=1004")
                .Where(child => child.Name.LocalName == "UAMethod")
                .ToArray();

            Assert.That(
                methods.Select(method => method.Attribute("BrowseName")!.Value),
                Is.EquivalentTo(s_resultManagementMethods));
            foreach (XElement method in methods)
            {
                Assert.That(
                    ModellingRuleOf(method),
                    Is.EqualTo("i=80"),
                    $"{method.Attribute("BrowseName")!.Value} is optional.");
            }
        }

        [Test]
        public void EnergyDeclaresTheContainsReferenceType()
        {
            XElement[] referenceTypes = Energy()
                .Root!
                .Elements()
                .Where(element => element.Name.LocalName == "UAReferenceType")
                .ToArray();

            Assert.That(referenceTypes, Has.Length.EqualTo(1));
            Assert.That(
                referenceTypes[0].Attribute("BrowseName")!.Value,
                Is.EqualTo("1:Contains"));
        }

        [Test]
        public void EnergyFlowMembersUseEcmQualifiedBrowseNames()
        {
            // The Machinery.Server energy builder spells these names out because
            // neither model's generated BrowseNames class carries them: they are
            // members of the Energy model that borrow the ECM namespace.
            IEnumerable<string> actual = Energy()
                .Root!
                .Elements()
                .Where(element => element.Name.LocalName == "UAVariable")
                .Select(element => element.Attribute("BrowseName")!.Value);

            foreach (string name in s_energyFlowMembers)
            {
                Assert.That(actual, Contains.Item(name));
            }
        }

        [Test]
        public void ProcessValueVariableTypeIsASubtypeOfTheDeclaredAnalogSignalType()
        {
            const string padimAnalogSignalVariableType = "ns=2;i=1111";
            XDocument processValues = ProcessValues();
            XElement analogSignal = ChildrenOf(processValues, "ns=1;i=1003")
                .Single(child => child.Attribute("BrowseName")!.Value == "2:AnalogSignal");

            Assert.That(
                ModellingRuleOf(analogSignal),
                Is.EqualTo("i=78"),
                "AnalogSignal is mandatory on ProcessValueType.");
            Assert.That(
                TypeDefinitionOf(analogSignal),
                Is.EqualTo(padimAnalogSignalVariableType),
                "The type declaration keeps PADIM's AnalogSignalVariableType - " +
                "OPC 40001-2 does not narrow it.");
            Assert.That(
                SuperTypeOf(FindNode(processValues, "ns=1;i=2002")),
                Is.EqualTo(padimAnalogSignalVariableType),
                "ProcessValueVariableType derives from it, which is what lets an " +
                "instance use the richer type in that slot and so reach the " +
                "OPC 40001-2 limits.");
        }

        [Test]
        public void ProcessValueSetpointUsesTheSetpointVariableType()
        {
            XElement setpoint = ChildrenOf(ProcessValues(), "ns=1;i=1003")
                .Single(child => child.Attribute("BrowseName")!.Value == "1:ProcessValueSetpoint");

            Assert.That(ModellingRuleOf(setpoint), Is.EqualTo("i=80"));
            Assert.That(TypeDefinitionOf(setpoint), Is.EqualTo("ns=1;i=2003"));
        }

        private static readonly string[] s_itemStates =
            ["Executing", "NotAvailable", "NotExecuting", "OutOfService"];

        private static readonly string[] s_operationModes =
            ["Maintenance", "None", "Processing", "Setup"];

        private static readonly string[] s_jobManagementChildren =
            ["1:JobOrderControl", "1:JobOrderResults"];

        private static readonly string[] s_resultManagementMethods =
        [
            "1:GetLatestResult",
            "1:GetResultById",
            "1:GetResultIdListFiltered",
            "1:AcknowledgeResults",
            "1:ReleaseResultHandle"
        ];

        private static readonly string[] s_jobsNamespaces =
        [
            "http://opcfoundation.org/UA/Machinery/Jobs/",
            "http://opcfoundation.org/UA/ISA95-JOBCONTROL_V2/"
        ];

        private static readonly string[] s_resultNamespaces =
            ["http://opcfoundation.org/UA/Machinery/Result/"];

        private static readonly string[] s_energyFlowMembers =
        [
            "2:Pressure",
            "2:Temperature",
            "2:Volume",
            "2:VolumeFlowRate",
            "2:Mass",
            "2:MassFlowRate"
        ];

        private static void AssertStateMachine(string nodeId, string[] states)
        {
            XDocument machinery = Machinery();
            XElement[] children = ChildrenOf(machinery, nodeId);
            string[] childNames = children
                .Select(child => child.Attribute("BrowseName")!.Value)
                .Where(name => name != "DefaultInstanceBrowseName")
                .ToArray();

            foreach (string state in states)
            {
                Assert.That(childNames, Contains.Item($"1:{state}"));
            }

            var expectedTransitions = new List<string>();
            foreach (string from in states)
            {
                foreach (string to in states)
                {
                    expectedTransitions.Add($"1:From{from}To{to}");
                }
            }

            Assert.That(
                expectedTransitions,
                Has.Count.EqualTo(16),
                "Four states produce sixteen transitions, self-transitions included.");
            foreach (string transition in expectedTransitions)
            {
                Assert.That(childNames, Contains.Item(transition));
            }
        }

        [Test]
        public void TheDefaultInstanceBrowseNamesAreWhatTheBuilderPlaces()
        {
            XDocument machinery = Machinery();
            string[] namespaceUris = NamespaceUris(machinery);
            Dictionary<string, string> declared = DefaultInstanceBrowseNames(machinery);

            // Every OPC 40001-1 conformance unit is worded "using the
            // DefaultInstanceBrowseName", so the namespace a block's browse
            // name sits in is load-bearing rather than cosmetic — and it is
            // not always the Machinery one. OperationCounters in particular
            // reuses the Device Integration functional group so a client
            // written against OPC 10000-100 finds it.
            Assert.That(
                declared["1:MachineryOperationCounterType"],
                Is.EqualTo("2:OperationCounters"));
            Assert.That(
                declared["1:MachineIdentificationType"],
                Is.EqualTo("2:Identification"));
            Assert.That(
                declared["1:MachineryComponentIdentificationType"],
                Is.EqualTo("2:Identification"));
            Assert.That(declared["1:MonitoringType"], Is.EqualTo("1:Monitoring"));
            Assert.That(declared["1:MachineComponentsType"], Is.EqualTo("1:Components"));
            Assert.That(
                declared["1:MachineryLifetimeCounterType"],
                Is.EqualTo("1:LifetimeCounters"));
            Assert.That(
                declared["1:MachineryEquipmentFolderType"],
                Is.EqualTo("1:MachineryEquipment"));
            Assert.That(declared["1:NotificationsType"], Is.EqualTo("1:Notifications"));
            Assert.That(
                declared["1:MachineryItemState_StateMachineType"],
                Is.EqualTo("1:MachineryItemState"));
            Assert.That(
                declared["1:MachineryOperationModeStateMachineType"],
                Is.EqualTo("1:MachineryOperationMode"));

            Assert.That(namespaceUris[0], Is.EqualTo("http://opcfoundation.org/UA/Machinery/"));
            Assert.That(
                namespaceUris[1],
                Is.EqualTo("http://opcfoundation.org/UA/DI/"),
                "Index 2 in a browse name above refers to this URI.");
        }

        /// <summary>
        /// Maps every ObjectType that declares a
        /// <c>DefaultInstanceBrowseName</c> to the value it declares.
        /// </summary>
        private static Dictionary<string, string> DefaultInstanceBrowseNames(
            XDocument document)
        {
            var declared = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (XElement variable in document.Root!.Elements())
            {
                if (variable.Name.LocalName != "UAVariable" ||
                    variable.Attribute("BrowseName")?.Value != "DefaultInstanceBrowseName")
                {
                    continue;
                }
                string? parentNodeId = variable.Attribute("ParentNodeId")?.Value;
                if (parentNodeId == null)
                {
                    continue;
                }
                XElement? owner = document.Root.Elements().FirstOrDefault(element =>
                    element.Attribute("NodeId")?.Value == parentNodeId);
                XElement? value = variable
                    .Element(XName.Get("Value", UaNodeSetNamespace))?
                    .Elements()
                    .FirstOrDefault();
                if (owner == null || value == null)
                {
                    continue;
                }
                string index = value
                    .Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "NamespaceIndex")?
                    .Value ?? "0";
                string name = value
                    .Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "Name")?
                    .Value ?? string.Empty;
                declared[owner.Attribute("BrowseName")!.Value] = $"{index}:{name}";
            }
            return declared;
        }

        private static XDocument Machinery()
        {
            return Load("Opc.Ua.Machinery.NodeSet2.xml");
        }

        private static XDocument Jobs()
        {
            return Load("Opc.Ua.Machinery.Jobs.NodeSet2.xml");
        }

        private static XDocument ResultModel()
        {
            return Load("Opc.Ua.Machinery.Result.NodeSet2.xml");
        }

        private static XDocument Energy()
        {
            return Load("Opc.Ua.Machinery.Energy.NodeSet2.xml");
        }

        private static XDocument ProcessValues()
        {
            return Load("Opc.Ua.Machinery.ProcessValues.NodeSet2.xml");
        }

        private static XDocument Load(string fileName)
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Model", fileName);
            Assert.That(File.Exists(path), Is.True, $"'{path}' was not copied to the output.");
            return XDocument.Load(path);
        }

        private static string[] NamespaceUris(XDocument document)
        {
            XElement? namespaceUris = document.Root!
                .Element(XName.Get("NamespaceUris", UaNodeSetNamespace));
            return namespaceUris == null
                ? []
                : namespaceUris.Elements().Select(element => element.Value).ToArray();
        }

        private static XElement FindNode(XDocument document, string nodeId)
        {
            XElement? node = document.Root!
                .Elements()
                .FirstOrDefault(element => element.Attribute("NodeId")?.Value == nodeId);
            Assert.That(node, Is.Not.Null, $"'{nodeId}' is missing from the NodeSet.");
            return node!;
        }

        private static XElement[] ChildrenOf(XDocument document, string parentNodeId)
        {
            return document.Root!
                .Elements()
                .Where(element => element.Attribute("ParentNodeId")?.Value == parentNodeId)
                .ToArray();
        }

        private static string? TypeDefinitionOf(XElement node)
        {
            return References(node)
                .FirstOrDefault(reference =>
                    reference.Type == "HasTypeDefinition" && reference.IsForward)
                .Target;
        }

        private static string? SuperTypeOf(XElement node)
        {
            return References(node)
                .FirstOrDefault(reference =>
                    reference.Type == "HasSubtype" && !reference.IsForward)
                .Target;
        }

        private static string? ModellingRuleOf(XElement node)
        {
            return References(node)
                .FirstOrDefault(reference =>
                    reference.Type == "HasModellingRule" && reference.IsForward)
                .Target;
        }

        private static IEnumerable<(string Type, bool IsForward, string Target)> References(
            XElement node)
        {
            XElement? references = node.Element(XName.Get("References", UaNodeSetNamespace));
            if (references == null)
            {
                yield break;
            }
            foreach (XElement reference in references.Elements())
            {
                yield return (
                    reference.Attribute("ReferenceType")?.Value ?? string.Empty,
                    reference.Attribute("IsForward")?.Value != "false",
                    reference.Value);
            }
        }
    }
}
