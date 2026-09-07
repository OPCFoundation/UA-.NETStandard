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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using NUnit.Framework;

namespace Opc.Ua.ISA95.Tests
{
    [TestFixture]
    public class ModelAssetTests
    {
        [TestCase(
            "Opc.ISA95.NodeSet2.xml",
            "http://www.OPCFoundation.org/UA/2013/01/ISA95",
            "1.00",
            390)]
        [TestCase(
            "Opc.Ua.ISA95.JobControl.V1.NodeSet2.xml",
            "http://opcfoundation.org/UA/ISA95-JOBCONTROL",
            "1.0.0",
            91)]
        [TestCase(
            "Opc.Ua.ISA95.JobControl.V2.NodeSet2.xml",
            "http://opcfoundation.org/UA/ISA95-JOBCONTROL_V2/",
            "2.0.0",
            258)]
        public void CanonicalNodeSetHasExpectedIdentity(
            string resourceName,
            string modelUri,
            string version,
            int expectedNodeCount)
        {
            XDocument document = LoadXml(resourceName);
            XNamespace ua = NodeSetNamespace;
            XElement model = document.Root!
                .Element(ua + "Models")!
                .Elements(ua + "Model")
                .Single();

            Assert.That(model.Attribute("ModelUri")!.Value, Is.EqualTo(modelUri));
            Assert.That(model.Attribute("Version")!.Value, Is.EqualTo(version));
            Assert.That(
                document.Root.Elements().Count(element =>
                    element.Name.LocalName.StartsWith("UA", StringComparison.Ordinal)),
                Is.EqualTo(expectedNodeCount));
        }

        [TestCase(
            "Opc.ISA95.NodeSet2.xml",
            "Opc.ISA95.NodeIds.csv")]
        [TestCase(
            "Opc.Ua.ISA95.JobControl.V1.NodeSet2.xml",
            "Opc.Ua.ISA95.JobControl.V1.NodeIds.csv")]
        [TestCase(
            "Opc.Ua.ISA95.JobControl.V2.NodeSet2.xml",
            "Opc.Ua.ISA95.JobControl.V2.NodeIds.csv")]
        public void IdentifierCsvMatchesNodeSetIdsAndClasses(string nodeSetResource, string csvResource)
        {
            XDocument document = LoadXml(nodeSetResource);
            var nodes = document.Root!
                .Elements()
                .Where(element => element.Name.LocalName.StartsWith("UA", StringComparison.Ordinal))
                .Select(element => new
                {
                    Element = element,
                    Id = ParseNumericId(element.Attribute("NodeId")?.Value)
                })
                .Where(item => item.Id.HasValue)
                .ToDictionary(item => item.Id!.Value);

            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            var seenIds = new HashSet<uint>();
            string[] lines = LoadText(csvResource)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            Assert.That(lines[0], Is.EqualTo("SymbolicName,NodeId,NodeClass"));
            foreach (string line in lines.Skip(1))
            {
                string[] fields = line.Split(',');
                Assert.That(fields, Has.Length.EqualTo(3), line);
                Assert.That(seenNames.Add(fields[0]), Is.True, $"Duplicate name {fields[0]}.");

                uint id = uint.Parse(fields[1], CultureInfo.InvariantCulture);
                Assert.That(seenIds.Add(id), Is.True, $"Duplicate id {id}.");
                Assert.That(nodes.TryGetValue(id, out var node), Is.True, $"NodeId i={id} is missing.");
                Assert.That(ToCsvNodeClass(node!.Element.Name.LocalName), Is.EqualTo(fields[2]));
            }

            Assert.That(seenIds, Is.EquivalentTo(nodes.Keys));
        }

        [Test]
        public void CommonModelContainsAllPrimaryTypesAndReferences()
        {
            XDocument document = LoadXml("Opc.ISA95.NodeSet2.xml");
            foreach ((uint id, string nodeClass) in CommonPrimaryNodes)
            {
                XElement node = FindNode(document, id);
                Assert.That(node.Name.LocalName, Is.EqualTo($"UA{nodeClass}"), $"i={id}");
            }
        }

        [Test]
        public void CommonModelContainsNormativeMaterialReferenceRepairs()
        {
            XDocument document = LoadXml("Opc.ISA95.NodeSet2.xml");
            Assert.That(
                HasReference(
                    FindNode(document, 5219),
                    "ns=1;i=5300",
                    "ns=1;i=5224"),
                Is.True);
            Assert.That(
                HasReference(
                    FindNode(document, 5232),
                    "ns=1;i=5333",
                    "ns=1;i=5295"),
                Is.True);
            Assert.That(
                HasReference(
                    FindNode(document, 5259),
                    "ns=1;i=5333",
                    "ns=1;i=5278"),
                Is.True);
        }

        [Test]
        public void JobControlV1MethodsAreMandatory()
        {
            XDocument document = LoadXml("Opc.Ua.ISA95.JobControl.V1.NodeSet2.xml");
            foreach (uint id in JobControlV1Methods)
            {
                Assert.That(GetModellingRule(FindNode(document, id)), Is.EqualTo("i=78"), $"i={id}");
            }
        }

        [Test]
        public void JobControlV2MethodRulesMatchPublishedModel()
        {
            XDocument document = LoadXml("Opc.Ua.ISA95.JobControl.V2.NodeSet2.xml");
            foreach (uint id in JobControlV2OptionalReceiverMethods)
            {
                Assert.That(GetModellingRule(FindNode(document, id)), Is.EqualTo("i=80"), $"i={id}");
            }
            foreach (uint id in JobControlV2MandatoryResponseMethods)
            {
                Assert.That(GetModellingRule(FindNode(document, id)), Is.EqualTo("i=78"), $"i={id}");
            }
        }

        [Test]
        public void JobControlV2StateMachineHasPublishedCauses()
        {
            XDocument document = LoadXml("Opc.Ua.ISA95.JobControl.V2.NodeSet2.xml");
            foreach ((uint transitionId, uint methodId) in JobControlV2TransitionCauses)
            {
                XElement transition = FindNode(document, transitionId);
                Assert.That(
                    HasReference(transition, "HasCause", $"ns=1;i={methodId}"),
                    Is.True,
                    $"Transition i={transitionId} must have cause i={methodId}.");
            }
        }

        [Test]
        public void JobControlV2StatusEventHasRequiredShape()
        {
            XDocument document = LoadXml("Opc.Ua.ISA95.JobControl.V2.NodeSet2.xml");
            XElement eventType = FindNode(document, 1006);
            Assert.That(eventType.Attribute("IsAbstract")?.Value, Is.EqualTo("true"));
            Assert.That(HasReference(eventType, "HasSubtype", "i=2041", isForward: false), Is.True);

            foreach (uint propertyId in new uint[] { 6047, 6048, 6049 })
            {
                Assert.That(GetModellingRule(FindNode(document, propertyId)), Is.EqualTo("i=78"));
            }

            XElement provider = FindNode(document, 1003);
            Assert.That(HasReference(provider, "GeneratesEvent", "ns=1;i=1006"), Is.True);
        }

        private static XDocument LoadXml(string name)
        {
            using Stream stream = OpenResource(name);
            return XDocument.Load(stream);
        }

        private static string LoadText(string name)
        {
            using Stream stream = OpenResource(name);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static Stream OpenResource(string name)
        {
            string fullName = $"Opc.Ua.ISA95.Tests.Assets.{name}";
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(fullName)
                ?? throw new InvalidOperationException($"Embedded resource {fullName} was not found.");
        }

        private static uint? ParseNumericId(string? nodeId)
        {
            const string marker = ";i=";
            int index = nodeId?.LastIndexOf(marker, StringComparison.Ordinal) ?? -1;
            string? identifier = index < 0
                ? null
                : nodeId![(index + marker.Length)..];
            if (index < 0 ||
                !uint.TryParse(
                    identifier,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out uint id))
            {
                return null;
            }
            return id;
        }

        private static XElement FindNode(XDocument document, uint id)
        {
            string nodeId = $"ns=1;i={id}";
            return document.Root!
                .Elements()
                .Single(element => element.Attribute("NodeId")?.Value == nodeId);
        }

        private static string? GetModellingRule(XElement node)
        {
            return node
                .Element(NodeSetNamespace + "References")?
                .Elements(NodeSetNamespace + "Reference")
                .Single(reference => reference.Attribute("ReferenceType")?.Value == "HasModellingRule")
                .Value;
        }

        private static bool HasReference(
            XElement node,
            string referenceType,
            string target,
            bool isForward = true)
        {
            return node
                .Element(NodeSetNamespace + "References")!
                .Elements(NodeSetNamespace + "Reference")
                .Any(reference =>
                    reference.Attribute("ReferenceType")?.Value == referenceType &&
                    reference.Attribute("IsForward")?.Value != "false" == isForward &&
                    reference.Value == target);
        }

        private static string ToCsvNodeClass(string elementName)
        {
            return elementName.StartsWith("UA", StringComparison.Ordinal)
                ? elementName[2..]
                : elementName;
        }

        private static readonly XNamespace NodeSetNamespace =
            "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";

        private static readonly (uint Id, string NodeClass)[] CommonPrimaryNodes =
        [
            (4714, "ReferenceType"),
            (4910, "ReferenceType"),
            (2009, "ReferenceType"),
            (5114, "ReferenceType"),
            (5300, "ReferenceType"),
            (5333, "ReferenceType"),
            (4957, "ObjectType"),
            (4958, "ObjectType"),
            (4996, "ObjectType"),
            (5131, "ObjectType"),
            (5034, "ObjectType"),
            (5040, "ObjectType"),
            (5078, "ObjectType"),
            (5085, "ObjectType"),
            (5209, "ObjectType"),
            (5219, "ObjectType"),
            (5232, "ObjectType"),
            (5259, "ObjectType"),
            (5048, "VariableType")
        ];

        private static readonly uint[] JobControlV1Methods = [7001, 7002, 7003];

        private static readonly uint[] JobControlV2OptionalReceiverMethods =
            [7001, 7004, 7005, 7013, 7007, 7008, 7009, 7010, 7006, 7011, 7012];

        private static readonly uint[] JobControlV2MandatoryResponseMethods = [7002, 7014, 7003];

        private static readonly (uint TransitionId, uint MethodId)[] JobControlV2TransitionCauses =
        [
            (5042, 7005),
            (5043, 7013),
            (5041, 7009),
            (5044, 7009),
            (5046, 7007),
            (5050, 7008),
            (5047, 7006),
            (5051, 7006),
            (5048, 7010),
            (5049, 7010),
            (5084, 7010),
            (5085, 7010)
        ];
    }
}
