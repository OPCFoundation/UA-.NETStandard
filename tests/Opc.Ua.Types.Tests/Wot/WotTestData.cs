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
using System.Text;
using System.Xml;
using Opc.Ua.Export;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Shared NodeSet2 fixtures for the WoT conversion tests.
    /// </summary>
    internal static class WotTestData
    {
        /// <summary>
        /// Builds a NodeSet exercising several NodeClasses, references,
        /// modelling rules, a NodeSet-level extension and a node-level extension.
        /// </summary>
        public static UANodeSet CreateRichNodeSet()
        {
            var xml = new XmlDocument();
            System.Xml.XmlElement modelExtension = xml.CreateElement("test", "Metadata", "urn:test");
            modelExtension.SetAttribute("key", "value");
            modelExtension.InnerText = "payload";

            var nodeExtensionDoc = new XmlDocument();
            System.Xml.XmlElement nodeExtension = nodeExtensionDoc.CreateElement("vendor", "Note", "urn:vendor");
            nodeExtension.InnerText = "annotation";

            return new UANodeSet
            {
                NamespaceUris = ["urn:test:model"],
                Models =
                [
                    new ModelTableEntry
                    {
                        ModelUri = "urn:test:model",
                        Version = "1.0.0",
                        PublicationDate = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
                        PublicationDateSpecified = true,
                        RequiredModel =
                        [
                            new ModelTableEntry
                            {
                                ModelUri = "http://opcfoundation.org/UA/",
                                Version = "1.05.03",
                                PublicationDate = new DateTime(2023, 12, 15, 0, 0, 0, DateTimeKind.Utc),
                                PublicationDateSpecified = true
                            }
                        ]
                    }
                ],
                Extensions = [modelExtension],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1001",
                        BrowseName = "1:MachineType",
                        SymbolicName = "MachineType",
                        DisplayName = [new Opc.Ua.Export.LocalizedText { Value = "MachineType" }],
                        Description = [new Opc.Ua.Export.LocalizedText { Locale = "en", Value = "A test type." }],
                        Extensions = [nodeExtension],
                        References =
                        [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=58" },
                            new Reference { ReferenceType = "HasComponent", IsForward = true, Value = "ns=1;i=6001" },
                            new Reference { ReferenceType = "HasComponent", IsForward = true, Value = "ns=1;i=7001" },
                            new Reference { ReferenceType = "GeneratesEvent", IsForward = true, Value = "ns=1;i=1002" }
                        ]
                    },
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1002",
                        BrowseName = "1:OverTemperatureEventType",
                        DisplayName = [new Opc.Ua.Export.LocalizedText { Value = "OverTemperatureEventType" }],
                        References =
                        [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=2041" }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=6001",
                        BrowseName = "1:Speed",
                        DisplayName = [new Opc.Ua.Export.LocalizedText { Value = "Speed" }],
                        DataType = "Double",
                        AccessLevel = 3,
                        ParentNodeId = "ns=1;i=1001",
                        References =
                        [
                            new Reference { ReferenceType = "HasTypeDefinition", IsForward = true, Value = "i=63" },
                            new Reference { ReferenceType = "HasModellingRule", IsForward = true, Value = "i=78" },
                            new Reference { ReferenceType = "HasComponent", IsForward = false, Value = "ns=1;i=1001" }
                        ]
                    },
                    new UAMethod
                    {
                        NodeId = "ns=1;i=7001",
                        BrowseName = "1:Reset",
                        DisplayName = [new Opc.Ua.Export.LocalizedText { Value = "Reset" }],
                        ParentNodeId = "ns=1;i=1001",
                        References =
                        [
                            new Reference { ReferenceType = "HasModellingRule", IsForward = true, Value = "i=80" },
                            new Reference { ReferenceType = "HasComponent", IsForward = false, Value = "ns=1;i=1001" }
                        ]
                    },
                    new UAObject
                    {
                        NodeId = "ns=1;i=5001",
                        BrowseName = "1:Machine",
                        DisplayName = [new Opc.Ua.Export.LocalizedText { Value = "Machine" }],
                        EventNotifier = 1,
                        References =
                        [
                            new Reference { ReferenceType = "HasTypeDefinition", IsForward = true, Value = "ns=1;i=1001" }
                        ]
                    },
                    new UAReferenceType
                    {
                        NodeId = "ns=1;i=4001",
                        BrowseName = "1:Controls",
                        DisplayName = [new Opc.Ua.Export.LocalizedText { Value = "Controls" }],
                        InverseName = [new Opc.Ua.Export.LocalizedText { Value = "IsControlledBy" }],
                        Symmetric = false,
                        References =
                        [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=47" }
                        ]
                    },
                    new UAView
                    {
                        NodeId = "ns=1;i=8001",
                        BrowseName = "1:PlantView",
                        DisplayName = [new Opc.Ua.Export.LocalizedText { Value = "PlantView" }],
                        ContainsNoLoops = true,
                        EventNotifier = 1
                    }
                ]
            };
        }

        /// <summary>
        /// Builds a compact NodeSet with a single ObjectType, one variable and
        /// one method used by the native-projection reconstruction tests.
        /// </summary>
        public static UANodeSet CreateReconstructableNodeSet()
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:test:model"],
                Models =
                [
                    new ModelTableEntry
                    {
                        ModelUri = "urn:test:model",
                        Version = "2.0.0",
                        PublicationDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                        PublicationDateSpecified = true
                    }
                ],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1001",
                        BrowseName = "1:PumpType",
                        DisplayName = [new Opc.Ua.Export.LocalizedText { Value = "PumpType" }],
                        References =
                        [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=58" },
                            new Reference { ReferenceType = "HasComponent", IsForward = true, Value = "ns=1;i=6001" },
                            new Reference { ReferenceType = "HasComponent", IsForward = true, Value = "ns=1;i=7001" }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=6001",
                        BrowseName = "1:PumpSpeed",
                        DisplayName = [new Opc.Ua.Export.LocalizedText { Value = "PumpSpeed" }],
                        DataType = "Double",
                        AccessLevel = 3,
                        ParentNodeId = "ns=1;i=1001",
                        References =
                        [
                            new Reference { ReferenceType = "HasTypeDefinition", IsForward = true, Value = "i=63" },
                            new Reference { ReferenceType = "HasModellingRule", IsForward = true, Value = "i=78" },
                            new Reference { ReferenceType = "HasComponent", IsForward = false, Value = "ns=1;i=1001" }
                        ]
                    },
                    new UAMethod
                    {
                        NodeId = "ns=1;i=7001",
                        BrowseName = "1:Reset",
                        DisplayName = [new Opc.Ua.Export.LocalizedText { Value = "Reset" }],
                        ParentNodeId = "ns=1;i=1001",
                        References =
                        [
                            new Reference { ReferenceType = "HasModellingRule", IsForward = true, Value = "i=80" },
                            new Reference { ReferenceType = "HasComponent", IsForward = false, Value = "ns=1;i=1001" }
                        ]
                    }
                ]
            };
        }

        public static byte[] Serialize(UANodeSet nodeSet)
        {
            using var stream = new MemoryStream();
            nodeSet.Write(stream);
            return stream.ToArray();
        }

        public static byte[] Utf8(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }
    }
}
