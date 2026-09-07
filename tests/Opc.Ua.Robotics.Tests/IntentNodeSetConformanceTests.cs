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
using System.Reflection;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.RobotIntent;
using Opc.Ua.Tests;
using RiDataTypeIds = Opc.Ua.RobotIntent.DataTypeIds;
using RiMethodIds = Opc.Ua.RobotIntent.MethodIds;
using RiObjectIds = Opc.Ua.RobotIntent.ObjectIds;
using RiObjectTypeIds = Opc.Ua.RobotIntent.ObjectTypeIds;
using RiVariableIds = Opc.Ua.RobotIntent.VariableIds;

namespace Opc.Ua.Robotics.Server.Tests
{
    /// <summary>
    /// Verifies that the Robot Intent NodeSet keeps the draft specification's structural contract.
    /// </summary>
    [TestFixture]
    [Category("Robotics")]
    public sealed class IntentNodeSetConformanceTests
    {
        [TestCaseSource(nameof(EnumerationCases))]
        public void EnumerationValuesMatchClauseFiveEight(EnumerationCase testCase)
        {
            Assert.Multiple(() =>
            {
                foreach (ExpectedMember member in testCase.Members)
                {
                    FieldInfo? field = testCase.EnumType.GetField(member.Name);
                    Assert.That(field, Is.Not.Null, $"{testCase.EnumType.Name}.{member.Name} exists.");
                    Assert.That(
                        Convert.ToInt32(field!.GetValue(null), System.Globalization.CultureInfo.InvariantCulture),
                        Is.EqualTo(member.Value),
                        $"{testCase.EnumType.Name}.{member.Name} has the normative value.");
                }

                string[] actualNames = Enum.GetNames(testCase.EnumType);
                Assert.That(
                    actualNames,
                    Is.EquivalentTo(testCase.Members.Select(member => member.Name)),
                    $"{testCase.EnumType.Name} declares only the clause 5.8 literals.");
            });
        }

        [Test]
        public void GeneratedRobotIntentPredefinedNodesContainNormativeIds()
        {
            SystemContext context = CreateSystemContext();
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaRobotIntent(context);

            NodeId[] expectedIds =
            [
                ExpandedNodeId.ToNodeId(RiObjectTypeIds.RobotIntentRootType, context.NamespaceUris),
                ExpandedNodeId.ToNodeId(RiObjectTypeIds.IntentControllerType, context.NamespaceUris),
                ExpandedNodeId.ToNodeId(RiObjectTypeIds.IntentOperationType, context.NamespaceUris),
                ExpandedNodeId.ToNodeId(RiObjectTypeIds.MissionType, context.NamespaceUris),
                ExpandedNodeId.ToNodeId(RiDataTypeIds.IntentDataType, context.NamespaceUris),
                ExpandedNodeId.ToNodeId(RiDataTypeIds.MotionIntentDataType, context.NamespaceUris),
                ExpandedNodeId.ToNodeId(RiDataTypeIds.ProcessIntentDataType, context.NamespaceUris),
                ExpandedNodeId.ToNodeId(RiMethodIds.IntentControllerType_SubmitIntent, context.NamespaceUris),
                ExpandedNodeId.ToNodeId(
                    RiVariableIds.IntentControllerType_SubmitIntent_OutputArguments,
                    context.NamespaceUris),
                ExpandedNodeId.ToNodeId(RiObjectIds.RobotIntent, context.NamespaceUris),
                ExpandedNodeId.ToNodeId(RiObjectIds.RobotIntent_Controllers, context.NamespaceUris)
            ];
            NodeId[] recursiveIds = [.. nodes.SelectMany(node => FlattenNodeIds(context, node))];

            Assert.Multiple(() =>
            {
                foreach (NodeId expectedId in expectedIds)
                {
                    Assert.That(
                        recursiveIds.Any(nodeId => nodeId == expectedId),
                        Is.True,
                        $"Generated AddOpcUaRobotIntent() emits predefined node {expectedId} in the type tree.");
                }
            });
        }

        [Test]
        public void IntentOperationPromotesPartTenMembersExactly()
        {
            XDocument nodeSet = LoadNodeSet();
            XElement intentOperationType = RequiredNode(nodeSet, "UAObjectType", "IntentOperationType");
            XElement finalResultData = RequiredChild(nodeSet, intentOperationType, "FinalResultData");
            XElement programDiagnostic = RequiredChild(nodeSet, intentOperationType, "ProgramDiagnostic");
            XElement missionType = RequiredNode(nodeSet, "UAObjectType", "MissionType");

            Assert.Multiple(() =>
            {
                AssertHasReference(intentOperationType, "HasSubtype", "i=2391", false, "IntentOperationType base type");
                AssertHasReference(missionType, "HasSubtype", "i=2391", false, "MissionType base type");
                Assert.That(finalResultData.Name.LocalName, Is.EqualTo("UAObject"));
                Assert.That(finalResultData.Attribute("BrowseName")?.Value, Is.EqualTo("FinalResultData"));
                AssertHasReference(finalResultData, "HasComponent", "ns=1;i=1003", false, "FinalResultData parent");
                AssertHasReference(finalResultData, "HasTypeDefinition", "i=58", true, "FinalResultData type");
                AssertHasReference(finalResultData, "HasModellingRule", "i=78", true, "FinalResultData rule");
                Assert.That(programDiagnostic.Name.LocalName, Is.EqualTo("UAVariable"));
                Assert.That(programDiagnostic.Attribute("BrowseName")?.Value, Is.EqualTo("ProgramDiagnostic"));
                Assert.That(NormalizeNodeId(programDiagnostic.Attribute("DataType")?.Value), Is.EqualTo("i=24033"));
                AssertHasReference(programDiagnostic, "HasComponent", "ns=1;i=1003", false, "ProgramDiagnostic parent");
                AssertHasReference(programDiagnostic, "HasTypeDefinition", "i=15383", true, "ProgramDiagnostic type");
                AssertHasReference(programDiagnostic, "HasModellingRule", "i=78", true, "ProgramDiagnostic rule");
            });
        }

        [Test]
        public void IntentControllerDeclaresTheNormativeMethodSurface()
        {
            XDocument nodeSet = LoadNodeSet();
            XElement controller = RequiredNode(nodeSet, "UAObjectType", "IntentControllerType");
            string[] expectedMethods =
            [
                "RequestControl",
                "ReleaseControl",
                "SubmitIntent",
                "CancelIntent",
                "CancelAll",
                "Pause",
                "Resume",
                "Retry",
                "SubmitMission",
                "UpdateMission",
                "CancelMission",
                "OpenRealTimeChannel",
                "CloseRealTimeChannel"
            ];

            string[] actualMethods = [.. nodeSet.Descendants(s_ua + "UAMethod")
                .Where(node => node.Attribute("ParentNodeId")?.Value == controller.Attribute("NodeId")?.Value)
                .Select(BrowseName)
                .OrderBy(name => Array.IndexOf(expectedMethods, name))];

            Assert.That(actualMethods, Is.EqualTo(expectedMethods));
        }

        [TestCaseSource(nameof(MethodArgumentCases))]
        public void IntentControllerMethodArgumentsMatchAnnexA(MethodArgumentsCase methodCase)
        {
            XDocument nodeSet = LoadNodeSet();
            XElement method = RequiredChild(
                nodeSet,
                RequiredNode(nodeSet, "UAObjectType", "IntentControllerType"),
                methodCase.Name);

            Assert.Multiple(() =>
            {
                AssertArguments(nodeSet, method, "InputArguments", methodCase.InputArguments);
                AssertArguments(nodeSet, method, "OutputArguments", methodCase.OutputArguments);
            });
        }

        [Test]
        public void WellKnownRobotIntentInstanceIsExposedUnderServer()
        {
            XDocument nodeSet = LoadNodeSet();
            XElement robotIntent = RequiredNode(nodeSet, "UAObject", "RobotIntent");
            XElement robotIntentRootType = RequiredNode(nodeSet, "UAObjectType", "RobotIntentRootType");
            XElement controllers = RequiredChild(nodeSet, robotIntentRootType, "Controllers");
            XElement specificationVersion = RequiredChild(nodeSet, robotIntentRootType, "SpecificationVersion");

            Assert.Multiple(() =>
            {
                Assert.That(robotIntent.Attribute("NodeId")?.Value, Is.EqualTo("ns=1;i=7001"));
                Assert.That(robotIntent.Attribute("BrowseName")?.Value, Is.EqualTo("1:RobotIntent"));
                Assert.That(robotIntent.Attribute("ParentNodeId")?.Value, Is.EqualTo("i=2253"));
                AssertHasReference(robotIntent, "HasComponent", "i=2253", false, "RobotIntent parent");
                AssertHasReference(robotIntent, "HasTypeDefinition", "ns=1;i=1001", true, "RobotIntent type");
                Assert.That(RiObjectIds.GetIdentifier("RobotIntent"), Is.EqualTo(RiObjectIds.RobotIntent));
                Assert.That(controllers.Name.LocalName, Is.EqualTo("UAObject"));
                Assert.That(controllers.Attribute("NodeId")?.Value, Is.EqualTo("ns=1;i=6001"));
                AssertHasReference(controllers, "HasComponent", "ns=1;i=1001", false, "Controllers parent");
                AssertHasReference(controllers, "HasTypeDefinition", "i=61", true, "Controllers folder type");
                AssertHasReference(controllers, "HasModellingRule", "i=78", true, "Controllers rule");
                Assert.That(specificationVersion.Name.LocalName, Is.EqualTo("UAVariable"));
                Assert.That(specificationVersion.Attribute("NodeId")?.Value, Is.EqualTo("ns=1;i=6002"));
                Assert.That(
                    NormalizeNodeId(specificationVersion.Attribute("DataType")?.Value),
                    Is.EqualTo("i=12"));
                AssertHasReference(
                    specificationVersion,
                    "HasProperty",
                    "ns=1;i=1001",
                    false,
                    "SpecificationVersion parent");
                AssertHasReference(specificationVersion, "HasModellingRule", "i=78", true, "SpecificationVersion rule");
            });
        }

        [TestCaseSource(nameof(ObjectTypeHierarchyCases))]
        public void ObjectTypeHierarchyMatchesClauseFiveOne(SubtypeCase subtypeCase)
        {
            XDocument nodeSet = LoadNodeSet();
            XElement node = RequiredNode(nodeSet, "UAObjectType", subtypeCase.Name);

            AssertHasReference(node, "HasSubtype", subtypeCase.BaseTypeId, false, $"{subtypeCase.Name} base type");
        }

        [Test]
        public void IntentDataTypeHierarchyAndAbstractnessMatchClauseFiveThree()
        {
            XDocument nodeSet = LoadNodeSet();
            Assert.Multiple(() =>
            {
                AssertDataTypeSubtype(nodeSet, "IntentDataType", "i=22");
                AssertDataTypeSubtype(nodeSet, "MotionIntentDataType", "ns=1;i=3053");
                AssertDataTypeSubtype(nodeSet, "ProcessIntentDataType", "ns=1;i=3054");
                Assert.That(
                    RequiredNode(nodeSet, "UADataType", "IntentDataType").Attribute("IsAbstract")?.Value,
                    Is.EqualTo("true"));
                Assert.That(
                    RequiredNode(nodeSet, "UADataType", "MotionIntentDataType").Attribute("IsAbstract")?.Value,
                    Is.EqualTo("true"));
                Assert.That(
                    RequiredNode(nodeSet, "UADataType", "ProcessIntentDataType").Attribute("IsAbstract")?.Value,
                    Is.EqualTo("true"));

                foreach (string name in MotionIntentNames)
                {
                    AssertDataTypeSubtype(nodeSet, name, "ns=1;i=3054");
                }

                foreach (string name in ProcessIntentNames)
                {
                    AssertDataTypeSubtype(nodeSet, name, "ns=1;i=3076");
                }

                foreach (string name in DirectIntentNames)
                {
                    AssertDataTypeSubtype(nodeSet, name, "ns=1;i=3053");
                }
            });
        }

        [Test]
        public void AbstractIntentFieldsAllowSubtypes()
        {
            XDocument nodeSet = LoadNodeSet();
            XElement missionStep = RequiredNode(nodeSet, "UADataType", "MissionStepDataType");
            XElement intentField = RequiredField(missionStep, "Intent");

            Assert.Multiple(() =>
            {
                Assert.That(NormalizeNodeId(intentField.Attribute("DataType")?.Value), Is.EqualTo("ns=1;i=3053"));
                Assert.That(intentField.Attribute("AllowSubTypes")?.Value, Is.EqualTo("true"));
            });
        }

        [Test]
        public void NodeSetDeclaresOnlyTheBaseOpcUaRequiredModel()
        {
            XDocument nodeSet = LoadNodeSet();
            XElement[] requiredModels = [.. nodeSet.Descendants(s_ua + "RequiredModel")];

            Assert.Multiple(() =>
            {
                Assert.That(requiredModels, Has.Length.EqualTo(1));
                Assert.That(requiredModels[0].Attribute("ModelUri")?.Value, Is.EqualTo(OpcUaNamespace));
            });
        }

        [Test]
        public void StructureFieldShapesMatchNormativePins()
        {
            XDocument nodeSet = LoadNodeSet();
            XElement pose = RequiredNode(nodeSet, "UADataType", "Pose3DDataType");
            XElement jointMove = RequiredNode(nodeSet, "UADataType", "JointMoveIntentDataType");

            Assert.Multiple(() =>
            {
                AssertField(pose, "FrameId", "i=12", null, null, null);
                AssertField(pose, "Position", "i=11", "1", "3", null);
                AssertField(pose, "Orientation", "i=11", "1", "4", null);
                AssertField(jointMove, "HasJointTargets", "i=1", null, null, null);
                AssertField(jointMove, "JointTargets", "i=11", "1", null, null);
                AssertField(jointMove, "TargetPose", "ns=1;i=3050", null, null, null);
            });
        }

        private static IEnumerable<TestCaseData> EnumerationCases()
        {
            yield return EnumCase<ExecutionStateEnum>(
                ("Accepted", 0),
                ("Queued", 1),
                ("Executing", 2),
                ("Suspended", 3),
                ("Cancelling", 4),
                ("Succeeded", 5),
                ("Failed", 6),
                ("Cancelled", 7),
                ("Retriable", 8));
            yield return EnumCase<BufferModeEnum>(
                ("Aborting", 0),
                ("Buffered", 1),
                ("BlendingLow", 2),
                ("BlendingPrevious", 3),
                ("BlendingNext", 4),
                ("BlendingHigh", 5));
            yield return EnumCase<BlockingModeEnum>(("None", 0), ("Soft", 1), ("Single", 2), ("Hard", 3));
            yield return EnumCase<TerminationModeEnum>(("Exact", 0), ("Blend", 1));
            yield return EnumCase<ReleaseModeEnum>(("Drop", 0), ("Place", 1), ("Handover", 2));
            yield return EnumCase<ApproachModeEnum>(("Default", 0), ("ToolZ", 1), ("Top", 2), ("Side", 3));
            yield return EnumCase<FrameRoleEnum>(
                ("World", 0),
                ("Base", 1),
                ("MechanicalInterface", 2),
                ("Tool", 3),
                ("Object", 4),
                ("Other", 5));
            yield return EnumCase<OperationalModeEnum>(
                ("Other", 0),
                ("ManualReducedSpeed", 1),
                ("ManualHighSpeed", 2),
                ("Automatic", 3),
                ("AutomaticExternal", 4));
            yield return EnumCase<IntentFailureEnum>(
                ("None", 0),
                ("Unreachable", 1),
                ("Kinematics", 2),
                ("Collision", 3),
                ("JointLimit", 4),
                ("SpeedLimit", 5),
                ("ToolMissing", 6),
                ("ObjectNotFound", 7),
                ("GraspFailed", 8),
                ("Timeout", 9),
                ("NotPermittedInMode", 10),
                ("ControlNotOwned", 11),
                ("CapabilityNotSupported", 12),
                ("ParameterInvalid", 13),
                ("QueueFull", 14),
                ("Superseded", 15),
                ("HardwareFault", 16),
                ("SafetyStop", 17),
                ("Other", 18),
                ("SafetyLimitExceeded", 19),
                ("NoTransition", 20));
            yield return EnumCase<StopModeEnum>(
                ("OnPath", 1),
                ("EndOfCycle", 2),
                ("ProcessStop", 3),
                ("QuickStop", 4),
                ("EndOfInstruction", 5));
            yield return EnumCase<AxisKindEnum>(("Revolute", 0), ("Prismatic", 1));
            yield return EnumCase<MissionUpdateResultEnum>(
                ("Accepted", 0),
                ("Outdated", 1),
                ("BaseConflict", 2),
                ("UnknownMission", 3),
                ("Rejected", 4));
            yield return EnumCase<SafeMotionFunctionEnum>(
                ("None", 0),
                ("Sto", 1),
                ("Ss1", 2),
                ("Ss2", 3),
                ("Sos", 4),
                ("Sls", 5),
                ("Slp", 6),
                ("Sdi", 7),
                ("Sbc", 8));
            yield return EnumCase<RealTimeTransportEnum>(
                ("Rtde", 0),
                ("Egm", 1),
                ("Fri", 2),
                ("Rsi", 3),
                ("MotoRos2", 4),
                ("OpcUaFx", 5),
                ("Other", 6));
            yield return EnumCase<ChannelInitiatorEnum>(("Server", 0), ("Client", 1));
            yield return EnumCase<ErrorPolicyEnum>(
                ("Abort", 0),
                ("Retry", 1),
                ("Skip", 2),
                ("Fallback", 3),
                ("Compensate", 4));
            yield return EnumCase<DivergenceKindEnum>(("Alternative", 0), ("Parallel", 1));
            yield return EnumCase<WeaveShapeEnum>(("None", 0), ("Sine", 1), ("Zigzag", 2), ("Trapezoid", 3));
        }

        private static IEnumerable<TestCaseData> MethodArgumentCases()
        {
            yield return MethodCase("RequestControl", [], [Arg("Granted", "i=1"), Arg("CurrentOwner", "i=17")]);
            yield return MethodCase("ReleaseControl", [], []);
            yield return MethodCase(
                "SubmitIntent",
                [Arg("Intent", "ns=1;i=3053")],
                [
                    Arg("Accepted", "i=1"),
                    Arg("IntentId", "i=12"),
                    Arg("Operation", "i=17"),
                    Arg("Failure", "ns=1;i=3009"),
                    Arg("Message", "i=21")
                ]);
            yield return MethodCase(
                "CancelIntent",
                [Arg("IntentId", "i=12"), Arg("StopMode", "ns=1;i=3010")],
                [Arg("Accepted", "i=1")]);
            yield return MethodCase("CancelAll", [Arg("StopMode", "ns=1;i=3010")], [Arg("Cancelled", "i=7")]);
            yield return MethodCase("Pause", [], [Arg("Accepted", "i=1")]);
            yield return MethodCase("Resume", [], [Arg("Accepted", "i=1")]);
            yield return MethodCase(
                "Retry",
                [Arg("IntentId", "i=12")],
                [
                    Arg("Accepted", "i=1"),
                    Arg("Operation", "i=17"),
                    Arg("Failure", "ns=1;i=3009"),
                    Arg("Message", "i=21")
                ]);
            yield return MethodCase(
                "SubmitMission",
                [Arg("Mission", "ns=1;i=3068")],
                [
                    Arg("Accepted", "i=1"),
                    Arg("MissionId", "i=12"),
                    Arg("Operation", "i=17"),
                    Arg("Failure", "ns=1;i=3009"),
                    Arg("Message", "i=21")
                ]);
            yield return MethodCase(
                "UpdateMission",
                [Arg("MissionId", "i=12"), Arg("MissionUpdateId", "i=7"), Arg("Steps", "ns=1;i=3067", 1)],
                [Arg("Result", "ns=1;i=3012"), Arg("Message", "i=21")]);
            yield return MethodCase(
                "CancelMission",
                [Arg("MissionId", "i=12"), Arg("StopMode", "ns=1;i=3010")],
                [Arg("Accepted", "i=1")]);
            yield return MethodCase(
                "OpenRealTimeChannel",
                [Arg("ChannelId", "i=12"), Arg("RequestedLease", "i=290")],
                [
                    Arg("Granted", "i=1"),
                    Arg("EndpointUrl", "i=12"),
                    Arg("PayloadDescriptor", "i=12"),
                    Arg("LeaseExpiry", "i=294"),
                    Arg("Message", "i=21")
                ]);
            yield return MethodCase("CloseRealTimeChannel", [Arg("ChannelId", "i=12")], [Arg("Released", "i=1")]);
        }

        private static IEnumerable<TestCaseData> ObjectTypeHierarchyCases()
        {
            yield return TypeCase("RobotIntentRootType", "i=58");
            yield return TypeCase("IntentControllerType", "i=58");
            yield return TypeCase("IntentOperationType", "i=2391");
            yield return TypeCase("MissionType", "i=2391");
            yield return TypeCase("IntentCapabilitiesType", "i=58");
            yield return TypeCase("CoordinateFrameType", "i=58");
            yield return TypeCase("ToolType", "i=58");
            yield return TypeCase("LocationType", "i=58");
            yield return TypeCase("AxisType", "i=58");
            yield return TypeCase("OutputSignalType", "i=58");
            yield return TypeCase("ProgramType", "i=58");
            yield return TypeCase("SafetyStateType", "i=58");
            yield return TypeCase("RealTimeChannelType", "i=58");
            yield return TypeCase("RobotDescriptionType", "i=58");
        }

        private static TestCaseData EnumCase<TEnum>(params (string Name, int Value)[] members)
            where TEnum : struct, Enum
        {
            var testCase = new EnumerationCase(
                typeof(TEnum),
                [.. members.Select(member => new ExpectedMember(member.Name, member.Value))]);
            return new TestCaseData(testCase).SetName($"{typeof(TEnum).Name}ValuesMatchClauseFiveEight");
        }

        private static TestCaseData MethodCase(
            string name,
            ExpectedArgument[] inputArguments,
            ExpectedArgument[] outputArguments)
        {
            var testCase = new MethodArgumentsCase(
                name,
                inputArguments,
                outputArguments);
            return new TestCaseData(testCase).SetName($"{name}ArgumentsMatchAnnexA");
        }

        private static ExpectedArgument Arg(string name, string dataType, int valueRank = -1)
        {
            return new ExpectedArgument(name, dataType, valueRank);
        }

        private static TestCaseData TypeCase(string name, string baseTypeId)
        {
            return new TestCaseData(new SubtypeCase(name, baseTypeId)).SetName($"{name}BaseTypeMatchesClauseFiveOne");
        }

        private static SystemContext CreateSystemContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(true);
            var messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(RobotIntentNamespace);

            return new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        private static XDocument LoadNodeSet()
        {
            return XDocument.Load(NodeSetPath());
        }

        private static IEnumerable<NodeId> FlattenNodeIds(ISystemContext context, NodeState node)
        {
            yield return node.NodeId;

            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                foreach (NodeId nodeId in FlattenNodeIds(context, child))
                {
                    yield return nodeId;
                }
            }
        }

        private static string NodeSetPath()
        {
            DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(
                    directory.FullName,
                    "src",
                    "Opc.Ua.Robotics",
                    "Model",
                    "Opc.Ua.RobotIntent.NodeSet2.xml");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                "Could not locate Opc.Ua.RobotIntent.NodeSet2.xml from the test directory.");
        }

        private static XElement RequiredNode(XDocument nodeSet, string nodeClass, string browseName)
        {
            XElement? node = nodeSet.Descendants(s_ua + nodeClass).SingleOrDefault(
                candidate => BrowseName(candidate) == browseName);
            Assert.That(node, Is.Not.Null, $"{nodeClass} {browseName} exists.");
            return node!;
        }

        private static XElement RequiredChild(XDocument nodeSet, XElement parent, string browseName)
        {
            string? parentNodeId = parent.Attribute("NodeId")?.Value;
            XElement? child = nodeSet.Descendants()
                .Where(candidate => candidate.Name.Namespace == s_ua)
                .SingleOrDefault(candidate =>
                    candidate.Attribute("ParentNodeId")?.Value == parentNodeId &&
                    BrowseName(candidate) == browseName);
            Assert.That(child, Is.Not.Null, $"{browseName} is declared below {BrowseName(parent)}.");
            return child!;
        }

        private static void AssertArguments(
            XDocument nodeSet,
            XElement method,
            string browseName,
            IReadOnlyList<ExpectedArgument> expectedArguments)
        {
            XElement? variable = nodeSet.Descendants(s_ua + "UAVariable").SingleOrDefault(candidate =>
                candidate.Attribute("ParentNodeId")?.Value == method.Attribute("NodeId")?.Value &&
                BrowseName(candidate) == browseName);
            if (expectedArguments.Count == 0)
            {
                Assert.That(variable, Is.Null, $"{BrowseName(method)} has no {browseName} node.");
                return;
            }

            Assert.That(variable, Is.Not.Null, $"{BrowseName(method)} declares {browseName}.");
            ExpectedArgument[] actualArguments = [.. variable!.Descendants(s_uax + "Argument")
                .Select(argument => new ExpectedArgument(
                    RequiredElementValue(argument, "Name"),
                    NormalizeNodeId(RequiredElementValue(argument.Element(s_uax + "DataType")!, "Identifier")),
                    int.Parse(
                        RequiredElementValue(argument, "ValueRank"),
                        System.Globalization.CultureInfo.InvariantCulture)))];

            Assert.That(
                actualArguments,
                Is.EqualTo(expectedArguments),
                $"{BrowseName(method)} {browseName} names, order, DataTypes and ValueRanks match Annex A.");
        }

        private static string RequiredElementValue(XElement element, string localName)
        {
            XElement? child = element.Element(s_uax + localName);
            Assert.That(child, Is.Not.Null, $"{element.Name.LocalName} contains {localName}.");
            return child!.Value;
        }

        private static void AssertDataTypeSubtype(XDocument nodeSet, string name, string baseTypeId)
        {
            XElement node = RequiredNode(nodeSet, "UADataType", name);
            AssertHasReference(node, "HasSubtype", baseTypeId, false, $"{name} base type");
        }

        private static void AssertField(
            XElement dataType,
            string name,
            string dataTypeId,
            string? valueRank,
            string? arrayDimensions,
            string? allowSubTypes)
        {
            XElement field = RequiredField(dataType, name);

            Assert.Multiple(() =>
            {
                Assert.That(
                    NormalizeNodeId(field.Attribute("DataType")?.Value),
                    Is.EqualTo(dataTypeId),
                    $"{name} DataType");
                Assert.That(field.Attribute("ValueRank")?.Value, Is.EqualTo(valueRank), $"{name} ValueRank");
                Assert.That(
                    field.Attribute("ArrayDimensions")?.Value,
                    Is.EqualTo(arrayDimensions),
                    $"{name} ArrayDimensions");
                Assert.That(
                    field.Attribute("AllowSubTypes")?.Value,
                    Is.EqualTo(allowSubTypes),
                    $"{name} AllowSubTypes");
            });
        }

        private static XElement RequiredField(XElement dataType, string name)
        {
            XElement? field = dataType.Element(s_ua + "Definition")?
                .Elements(s_ua + "Field")
                .SingleOrDefault(candidate => candidate.Attribute("Name")?.Value == name);
            Assert.That(field, Is.Not.Null, $"{BrowseName(dataType)} field {name} exists.");
            return field!;
        }

        private static void AssertHasReference(
            XElement node,
            string referenceType,
            string targetId,
            bool isForward,
            string because)
        {
            XElement? reference = node.Element(s_ua + "References")?
                .Elements(s_ua + "Reference")
                .SingleOrDefault(candidate =>
                    candidate.Attribute("ReferenceType")?.Value == referenceType &&
                    IsForward(candidate) == isForward &&
                    NormalizeNodeId(candidate.Value) == targetId);
            Assert.That(reference, Is.Not.Null, $"{because} uses {referenceType} to {targetId}.");
        }

        private static bool IsForward(XElement reference)
        {
            string? value = reference.Attribute("IsForward")?.Value;
            return value == null || bool.Parse(value);
        }

        private static string BrowseName(XElement node)
        {
            string value = node.Attribute("BrowseName")?.Value ?? string.Empty;
            int separator = value.IndexOf(':', StringComparison.Ordinal);
            return separator < 0 ? value : value[(separator + 1)..];
        }

        private static string NormalizeNodeId(string? value)
        {
            return value switch
            {
                "Boolean" => "i=1",
                "Double" => "i=11",
                "String" => "i=12",
                "NodeId" => "i=17",
                "LocalizedText" => "i=21",
                "Structure" => "i=22",
                "Argument" => "i=296",
                "Duration" => "i=290",
                "UtcTime" => "i=294",
                "UInt32" => "i=7",
                null => string.Empty,
                _ => value
            };
        }

        private const string RobotIntentNamespace = "http://opcfoundation.org/UA/RobotIntent/";
        private const string OpcUaNamespace = "http://opcfoundation.org/UA/";
        private const string UaNodeSetNamespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
        private const string UaTypesNamespace = "http://opcfoundation.org/UA/2008/02/Types.xsd";

        private static readonly XNamespace s_ua = UaNodeSetNamespace;
        private static readonly XNamespace s_uax = UaTypesNamespace;

        private static readonly string[] MotionIntentNames =
        [
            "JointMoveIntentDataType",
            "LinearMoveIntentDataType",
            "CircularMoveIntentDataType",
            "TrajectoryIntentDataType",
            "CartesianPathIntentDataType",
            "ForceIntentDataType"
        ];

        private static readonly string[] ProcessIntentNames =
        [
            "ArcWeldIntentDataType",
            "SpotWeldIntentDataType",
            "DispenseIntentDataType",
            "FastenIntentDataType",
            "PalletiseIntentDataType",
            "SurfaceFinishIntentDataType"
        ];

        private static readonly string[] DirectIntentNames =
        [
            "GraspIntentDataType",
            "ReleaseIntentDataType",
            "PickIntentDataType",
            "PlaceIntentDataType",
            "ToolChangeIntentDataType",
            "SetOutputIntentDataType",
            "CallProgramIntentDataType",
            "WaitIntentDataType"
        ];

        public sealed record ExpectedMember(string Name, int Value);

        public sealed record EnumerationCase(Type EnumType, IReadOnlyList<ExpectedMember> Members);

        public sealed record ExpectedArgument(string Name, string DataType, int ValueRank = -1);

        public sealed record MethodArgumentsCase(
            string Name,
            IReadOnlyList<ExpectedArgument> InputArguments,
            IReadOnlyList<ExpectedArgument> OutputArguments);

        public sealed record SubtypeCase(string Name, string BaseTypeId);
    }
}
