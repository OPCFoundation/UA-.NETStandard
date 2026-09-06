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
 *
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
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Opc.Ua.WotCon.Tests.Samples
{
    internal static partial class WotAggregationDocumentGenerator
    {
        /// <summary>
        /// Rebuilds the second pump and the sample-owned declarations in the
        /// authoritative NodeSet, without replacing Pump1's companion hierarchy.
        /// </summary>
        public static ByteString GeneratePumpNodeSetXml(string sourcePath)
        {
            var document = XDocument.Load(sourcePath);
            XElement root = document.Root
                ?? throw new InvalidOperationException("The pump NodeSet has no root.");
            XNamespace ns = root.Name.Namespace;
            XElement pump = FindSourceNode(root, "ns=1;s=Pump1");
            pump.SetAttributeValue("EventNotifier", "1");

            AddSourceAlias(root, "HasSubtype", "i=45");
            AddSourceAlias(root, "GeneratesEvent", "i=41");
            AddSourceAlias(root, "Argument", "i=296");

            foreach (string source in s_sourceNames)
            {
                string runningName = source + "Running";
                string runningId = "ns=1;s=Pump1." + runningName;
                AddSourceReference(pump, "HasComponent", runningId);
                SetSourceNode(root, new XElement(ns + "UAVariable",
                    new XAttribute("NodeId", runningId),
                    new XAttribute("BrowseName", "1:" + runningName),
                    new XAttribute("ParentNodeId", "ns=1;s=Pump1"),
                    new XAttribute("DataType", "Boolean"),
                    new XAttribute("AccessLevel", "1"),
                    new XElement(ns + "DisplayName", runningName),
                    new XElement(ns + "References",
                        SourceReference(ns, "HasComponent", "ns=1;s=Pump1", isForward: false),
                        SourceReference(ns, "HasTypeDefinition", "i=63"))));

                foreach (string operation in s_controlOperations)
                {
                    AddSourceMethod(root, pump, source + operation, null);
                }
            }

            foreach ((string alarm, string source) in s_alarmSources)
            {
                // Live source values must not become read-only TD const
                // constraints that forbid an alarm trip or a Start operation.
                string signalPath = AlarmConditionPath(alarm)[..^".Alarm".Length];
                FindSourceNode(root, "ns=1;s=Pump1." + signalPath).Element(ns + "Value")?.Remove();
                string eventId = "ns=1;s=Pump1." + alarm + "Alarm";
                AddSourceReference(pump, "GeneratesEvent", eventId);
                SetSourceNode(root, new XElement(ns + "UAObjectType",
                    new XAttribute("NodeId", eventId),
                    new XAttribute("BrowseName", "1:" + alarm + "Alarm"),
                    new XElement(ns + "DisplayName", alarm + "Alarm"),
                    new XElement(ns + "Description",
                        $"{source}-owned {alarm} notifications for Pump1."),
                    new XElement(ns + "References",
                        SourceReference(ns, "HasSubtype", "i=2915", isForward: false))));

                AddSourceMethod(root, pump, alarm + "Acknowledge", "i=9111");
                AddSourceMethod(root, pump, alarm + "Confirm", "i=9113");
            }

            // Pump2 is a deterministic instance of the same source hierarchy,
            // not an affordance overlay that only appears in a projection.
            root.Elements()
                .Where(element => IsPumpSourceNode(element, "Pump2"))
                .Remove();
            XElement[] pump1Nodes = [.. root.Elements()
                .Where(element => IsPumpSourceNode(element, "Pump1"))];
            foreach (XElement original in pump1Nodes)
            {
                var clone = new XElement(original);
                foreach (XAttribute attribute in clone.DescendantsAndSelf().Attributes())
                {
                    attribute.Value = ReplacePumpIdentity(attribute.Value);
                }
                foreach (XText text in clone.DescendantNodes().OfType<XText>())
                {
                    text.Value = ReplacePumpIdentity(text.Value);
                }
                root.Add(clone);
            }

            using var stream = new MemoryStream();
            using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\r\n",
                NewLineHandling = NewLineHandling.Replace
            }))
            {
                document.Save(writer);
            }
            stream.WriteByte(13);
            stream.WriteByte(10);
            return ByteString.From(stream.ToArray());
        }

        private static void AddSourceMethod(
            XElement root,
            XElement pump,
            string name,
            string? declarationId)
        {
            XNamespace ns = root.Name.Namespace;
            string nodeId = "ns=1;s=Pump1." + name;
            AddSourceReference(pump, "HasComponent", nodeId);
            var method = new XElement(ns + "UAMethod",
                new XAttribute("NodeId", nodeId),
                new XAttribute("BrowseName", "1:" + name),
                new XAttribute("ParentNodeId", "ns=1;s=Pump1"),
                new XAttribute("Executable", "true"),
                new XAttribute("UserExecutable", "true"),
                new XElement(ns + "DisplayName", name),
                new XElement(ns + "References",
                    SourceReference(ns, "HasComponent", "ns=1;s=Pump1", isForward: false)));
            if (declarationId is not null)
            {
                method.SetAttributeValue("MethodDeclarationId", declarationId);
                string argumentsId = nodeId + ".InputArguments";
                AddSourceReference(method, "HasProperty", argumentsId);
                SetSourceNode(root, new XElement(ns + "UAVariable",
                    new XAttribute("NodeId", argumentsId),
                    new XAttribute("BrowseName", "InputArguments"),
                    new XAttribute("ParentNodeId", nodeId),
                    new XAttribute("DataType", "Argument"),
                    new XAttribute("ValueRank", "1"),
                    new XAttribute("ArrayDimensions", "2"),
                    new XElement(ns + "DisplayName", "InputArguments"),
                    new XElement(ns + "References",
                        SourceReference(ns, "HasProperty", nodeId, isForward: false),
                        SourceReference(ns, "HasTypeDefinition", "i=68")),
                    new XElement(ns + "Value",
                        new XElement(s_typesNamespace + "ListOfExtensionObject",
                            CreateSourceArgument("EventId", "i=15"),
                            CreateSourceArgument("Comment", "i=21")))));
            }
            SetSourceNode(root, method);
        }

        private static XElement CreateSourceArgument(string name, string dataType)
        {
            XNamespace ns = s_typesNamespace;
            return new XElement(ns + "ExtensionObject",
                new XElement(ns + "TypeId", new XElement(ns + "Identifier", "i=297")),
                new XElement(ns + "Body",
                    new XElement(ns + "Argument",
                        new XElement(ns + "Name", name),
                        new XElement(ns + "DataType", new XElement(ns + "Identifier", dataType)),
                        new XElement(ns + "ValueRank", "-1"),
                        new XElement(ns + "ArrayDimensions"),
                        new XElement(ns + "Description",
                            new XElement(ns + "Text", name == "EventId"
                                ? "The occurrence EventId returned by this source's alarm."
                                : "Optional operator comment.")))));
        }

        private static XElement FindSourceNode(XElement root, string nodeId)
        {
            return root.Elements().Single(
                element => string.Equals(
                    (string?)element.Attribute("NodeId"), nodeId, StringComparison.Ordinal));
        }

        private static bool IsPumpSourceNode(XElement element, string pumpName)
        {
            string? nodeId = (string?)element.Attribute("NodeId");
            string prefix = "ns=1;s=" + pumpName;
            return string.Equals(nodeId, prefix, StringComparison.Ordinal) ||
                (nodeId?.StartsWith(prefix + ".", StringComparison.Ordinal) ?? false);
        }

        private static void SetSourceNode(XElement root, XElement node)
        {
            string nodeId = (string)node.Attribute("NodeId")!;
            XElement? existing = root.Elements().SingleOrDefault(
                element => string.Equals(
                    (string?)element.Attribute("NodeId"), nodeId, StringComparison.Ordinal));
            if (existing is null)
            {
                root.Add(node);
            }
            else
            {
                existing.ReplaceWith(node);
            }
        }

        private static void AddSourceAlias(XElement root, string alias, string nodeId)
        {
            XNamespace ns = root.Name.Namespace;
            XElement aliases = root.Element(ns + "Aliases")
                ?? throw new InvalidOperationException("The pump NodeSet has no alias table.");
            XElement? existing = aliases.Elements(ns + "Alias").SingleOrDefault(
                element => string.Equals(
                    (string?)element.Attribute("Alias"), alias, StringComparison.Ordinal));
            if (existing is null)
            {
                aliases.Add(new XElement(ns + "Alias", new XAttribute("Alias", alias), nodeId));
            }
            else if (!string.Equals(existing.Value, nodeId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Alias '{alias}' does not resolve to '{nodeId}'.");
            }
        }

        private static void AddSourceReference(XElement node, string referenceType, string target)
        {
            XNamespace ns = node.Name.Namespace;
            XElement references = node.Element(ns + "References")
                ?? throw new InvalidOperationException("A sample node has no references.");
            if (!references.Elements(ns + "Reference").Any(reference =>
                string.Equals((string?)reference.Attribute("ReferenceType"), referenceType,
                    StringComparison.Ordinal) &&
                !string.Equals((string?)reference.Attribute("IsForward"), "false",
                    StringComparison.Ordinal) &&
                string.Equals(reference.Value, target, StringComparison.Ordinal)))
            {
                references.Add(SourceReference(ns, referenceType, target));
            }
        }

        private static XElement SourceReference(
            XNamespace ns,
            string referenceType,
            string target,
            bool isForward = true)
        {
            var reference = new XElement(ns + "Reference",
                new XAttribute("ReferenceType", referenceType), target);
            if (!isForward)
            {
                reference.SetAttributeValue("IsForward", "false");
            }
            return reference;
        }

        private static string ReplacePumpIdentity(string value)
        {
            return value.Replace("Pump1", "Pump2", StringComparison.Ordinal)
                .Replace("Pump_1", "Pump_2", StringComparison.Ordinal)
                .Replace("Pump #1", "Pump #2", StringComparison.Ordinal)
                .Replace("SN-001", "SN-002", StringComparison.Ordinal);
        }

        private static readonly XNamespace s_typesNamespace =
            "http://opcfoundation.org/UA/2008/02/Types.xsd";

        private static readonly string[] s_sourceNames = ["SourceA", "SourceB"];
        private static readonly string[] s_controlOperations = ["Start", "Stop", "Reset"];
        private static readonly (string Alarm, string Source)[] s_alarmSources =
        [
            ("Cavitation", "SourceA"),
            ("MotorOverheat", "SourceB")
        ];
    }
}
