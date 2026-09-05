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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Pins the model NodeSets this library generates from to the versions the
    /// specifications publish, and pins every NodeId they assign.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two NodeSets are adopted verbatim from the specification sources
    /// rather than maintained here, so the thing that can go wrong is not a
    /// hand edit but a re-sync that quietly moves an identifier. A NodeId is
    /// the one part of a companion model that a deployed Server, a persisted
    /// subscription and a stored configuration all depend on by value, so a
    /// re-sync that renumbers a Node breaks systems that never changed.
    /// </para>
    /// <para>
    /// The counts and version strings are pinned for the complementary reason:
    /// a re-sync that drops Nodes, or that lands the model without its version
    /// bump, is invisible in a diff of two large XML files and is exactly what
    /// this notices.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotConModelSourceParityTests
    {
        [Test]
        public void TheConnectivityModelIsTheAdoptedSpecificationVersion()
        {
            XElement model = ReadModel(ConnectivityNodeSet, ConnectivityNamespace);

            Assert.Multiple(() =>
            {
                Assert.That(
                    model.Attribute("Version")?.Value,
                    Is.EqualTo("1.1"),
                    "Draft iterations do not increment the information model version.");
                Assert.That(
                    model.Attribute("PublicationDate")?.Value,
                    Is.EqualTo("2026-09-02T00:00:00Z"));
            });
        }

        [Test]
        public void TheRegistryModelIsTheAdoptedSpecificationVersion()
        {
            XElement model = ReadModel(RegistryNodeSet, RegistryNamespace);

            Assert.Multiple(() =>
            {
                Assert.That(model.Attribute("Version")?.Value, Is.EqualTo("0.4.0"));
                Assert.That(
                    model.Attribute("PublicationDate")?.Value,
                    Is.EqualTo("2026-08-31T00:00:00Z"));
            });
        }

        /// <summary>
        /// The connectivity model requires the registry model, so the two have
        /// to be synced together: a connectivity NodeSet that requires a
        /// registry version this repository does not carry describes a model
        /// no Server here can load.
        /// </summary>
        [Test]
        public void TheConnectivityModelRequiresTheRegistryVersionThatIsCarried()
        {
            XElement connectivity = ReadModel(ConnectivityNodeSet, ConnectivityNamespace);
            XElement registry = ReadModel(RegistryNodeSet, RegistryNamespace);

            XElement required = connectivity
                .Elements(UaNodeSet + "RequiredModel")
                .Single(e => e.Attribute("ModelUri")?.Value == RegistryNamespace);

            Assert.Multiple(() =>
            {
                Assert.That(
                    required.Attribute("Version")?.Value,
                    Is.EqualTo(registry.Attribute("Version")?.Value));
                Assert.That(
                    required.Attribute("PublicationDate")?.Value,
                    Is.EqualTo(registry.Attribute("PublicationDate")?.Value));
            });
        }

        /// <summary>
        /// Every NodeId the connectivity model assigned before the re-sync is
        /// still assigned to a Node with the same BrowseName.
        /// </summary>
        [Test]
        public void TheConnectivityModelReassignsNoNodeId()
        {
            Dictionary<string, string> nodes = ReadNodes(ConnectivityNodeSet);

            Assert.Multiple(() =>
            {
                foreach ((string nodeId, string browseName) in s_pinnedConnectivityNodes)
                {
                    Assert.That(
                        nodes.TryGetValue(nodeId, out string? actual),
                        Is.True,
                        $"'{nodeId}' is no longer assigned.");
                    Assert.That(
                        actual,
                        Is.EqualTo(browseName),
                        $"'{nodeId}' now names a different Node.");
                }
                Assert.That(
                    nodes,
                    Has.Count.EqualTo(286),
                    "The connectivity model has 286 Nodes; a re-sync that drops one is " +
                    "invisible in a diff of two large NodeSets.");
            });
        }

        [Test]
        public void TheRegistryModelReassignsNoNodeId()
        {
            Dictionary<string, string> nodes = ReadNodes(RegistryNodeSet);

            Assert.Multiple(() =>
            {
                foreach ((string nodeId, string browseName) in s_pinnedRegistryNodes)
                {
                    Assert.That(
                        nodes.TryGetValue(nodeId, out string? actual),
                        Is.True,
                        $"'{nodeId}' is no longer assigned.");
                    Assert.That(
                        actual,
                        Is.EqualTo(browseName),
                        $"'{nodeId}' now names a different Node.");
                }
                Assert.That(nodes, Has.Count.EqualTo(71));
            });
        }

        /// <summary>
        /// OPC 10000-5 requires a Server to publish the metadata of every
        /// namespace it exposes, and the registry model now carries its own.
        /// </summary>
        [Test]
        public void TheRegistryModelPublishesItsOwnNamespaceMetadata()
        {
            XDocument document = XDocument.Load(FindModel(RegistryNodeSet));
            XElement metadata = document.Root!
                .Elements(UaNodeSet + "UAObject")
                .Single(e => e.Attribute("BrowseName")?.Value == "1:" + RegistryNamespace);

            string Property(string name)
            {
                string id = metadata
                    .Element(UaNodeSet + "References")!
                    .Elements(UaNodeSet + "Reference")
                    .Where(r => r.Attribute("ReferenceType")?.Value == "HasProperty")
                    .Select(r => r.Value)
                    .Single(v => document.Root!
                        .Elements(UaNodeSet + "UAVariable")
                        .Any(e => e.Attribute("NodeId")?.Value == v &&
                            e.Attribute("BrowseName")?.Value == "1:" + name));
                return document.Root!
                    .Elements(UaNodeSet + "UAVariable")
                    .Single(e => e.Attribute("NodeId")?.Value == id)
                    .Element(UaNodeSet + "Value")!
                    .Value;
            }

            Assert.Multiple(() =>
            {
                Assert.That(Property("NamespaceUri"), Is.EqualTo(RegistryNamespace));
                Assert.That(Property("NamespaceVersion"), Is.EqualTo("0.4.0"));
                Assert.That(Property("IsNamespaceSubset"), Is.EqualTo("false"));
            });
        }

        /// <summary>
        /// The generated NodeId table is the CSV beside the NodeSet, so the two
        /// have to assign the same identifiers: a CSV that has drifted
        /// generates public members for Nodes the model does not declare, or
        /// leaves declared Nodes with no generated identifier at all.
        /// </summary>
        [Test]
        public void TheGeneratedIdentifierTableCoversTheConnectivityModel()
        {
            Dictionary<string, string> nodes = ReadNodes(ConnectivityNodeSet);
            var modelled = new HashSet<string>(StringComparer.Ordinal);
            foreach (string nodeId in nodes.Keys)
            {
                if (nodeId.StartsWith("ns=2;i=", StringComparison.Ordinal))
                {
                    modelled.Add(nodeId.Substring("ns=2;i=".Length));
                }
            }

            var declared = new HashSet<string>(StringComparer.Ordinal);
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in File.ReadLines(FindModel(ConnectivityCsv)))
            {
                string[] fields = line.Split(',');
                if (fields.Length < 3)
                {
                    continue;
                }
                string identifier = fields[1].Trim();
                declared.Add(identifier);
                // 'Unspecified' reserves an identifier the incorporated v1.02
                // model once used and the combined model no longer declares a
                // Node for. Reserving it is what stops a later revision from
                // handing that number to something else.
                if (!string.Equals(fields[2].Trim(), "Unspecified", StringComparison.Ordinal))
                {
                    assigned.Add(identifier);
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    assigned.Except(modelled, StringComparer.Ordinal),
                    Is.Empty,
                    "The identifier table assigns identifiers the model does not declare.");
                Assert.That(
                    modelled.Except(declared, StringComparer.Ordinal),
                    Is.Empty,
                    "The model assigns identifiers the table does not declare, so those " +
                    "Nodes have no generated identifier.");
            });
        }

        private static XElement ReadModel(string fileName, string modelUri)
        {
            XDocument document = XDocument.Load(FindModel(fileName));
            return document.Root!
                .Element(UaNodeSet + "Models")!
                .Elements(UaNodeSet + "Model")
                .Single(e => e.Attribute("ModelUri")?.Value == modelUri);
        }

        private static Dictionary<string, string> ReadNodes(string fileName)
        {
            XDocument document = XDocument.Load(FindModel(fileName));
            var nodes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (XElement element in document.Root!.Elements())
            {
                string? nodeId = element.Attribute("NodeId")?.Value;
                string? browseName = element.Attribute("BrowseName")?.Value;
                if (nodeId is not null && browseName is not null)
                {
                    nodes[nodeId] = browseName;
                }
            }
            return nodes;
        }

        private static string FindModel(string fileName)
        {
            string? directory = Path.GetDirectoryName(
                typeof(WotConModelSourceParityTests).Assembly.Location);
            while (!string.IsNullOrEmpty(directory))
            {
                if (File.Exists(Path.Combine(directory, "UA.slnx")))
                {
                    foreach (string candidate in s_modelDirectories)
                    {
                        string path = Path.Combine(
                            directory,
                            candidate.Replace('/', Path.DirectorySeparatorChar),
                            fileName);
                        if (File.Exists(path))
                        {
                            return path;
                        }
                    }
                    break;
                }
                directory = Path.GetDirectoryName(directory);
            }
            throw new FileNotFoundException($"The model '{fileName}' was not found.");
        }

        private static readonly string[] s_modelDirectories =
        [
            "src/Opc.Ua.WotCon/Design",
            "src/Opc.Ua.XRegistry"
        ];

        private const string ConnectivityNodeSet = "Opc.Ua.WotCon.NodeSet2.xml";
        private const string ConnectivityCsv = "Opc.Ua.WotCon.NodeSet2.csv";
        private const string RegistryNodeSet = "Opc.Ua.XRegistry.NodeSet2.xml";
        private const string ConnectivityNamespace = "http://opcfoundation.org/UA/WoT-Con/";
        private const string RegistryNamespace = "http://opcfoundation.org/UA/xRegistry/";

        private static readonly XNamespace UaNodeSet =
            "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";

        /// <summary>
        /// A representative NodeId of every generation the connectivity model
        /// has: the OPC 10100-1 v1.02 surface it incorporates, the 1.1 registry
        /// surface, and the DataTypes both depend on.
        /// </summary>
        private static readonly (string NodeId, string BrowseName)[] s_pinnedConnectivityNodes =
        [
            ("ns=2;i=1", "2:WoTAssetConnectionManagementType"),
            ("ns=2;i=2", "2:<WoTAssetName>"),
            ("ns=2;i=110", "2:WoTAssetFileType"),
            ("ns=2;i=67", "2:http://opcfoundation.org/UA/WoT-Con/"),
            ("ns=2;i=64000", "2:WoTRegistryType"),
            ("ns=2;i=64010", "2:WoTResourceEventType"),
            ("ns=2;i=64100", "2:WoTRegistry")
        ];

        /// <summary>
        /// The registry Nodes a WoT Connectivity Server exposes by identifier.
        /// </summary>
        private static readonly (string NodeId, string BrowseName)[] s_pinnedRegistryNodes =
        [
            ("ns=1;i=63561", "1:http://opcfoundation.org/UA/xRegistry/"),
            ("ns=1;i=63562", "1:NamespaceUri"),
            ("ns=1;i=63563", "1:NamespaceVersion"),
            ("ns=1;i=63564", "1:NamespacePublicationDate"),
            ("ns=1;i=63565", "1:IsNamespaceSubset")
        ];
    }
}
