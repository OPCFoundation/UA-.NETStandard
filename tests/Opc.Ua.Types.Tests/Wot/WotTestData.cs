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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Opc.Ua.Export;
using Opc.Ua.Wot;

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

            var valueDocument = new XmlDocument();
            System.Xml.XmlElement variableValue = valueDocument.CreateElement(
                "uax",
                "Double",
                Namespaces.OpcUaXsd);
            variableValue.InnerText = "42.5";

            var variableTypeValueDocument = new XmlDocument();
            System.Xml.XmlElement variableTypeValue = variableTypeValueDocument.CreateElement(
                "uax",
                "String",
                Namespaces.OpcUaXsd);
            variableTypeValue.InnerText = "default";

            return new UANodeSet
            {
                NamespaceUris = ["urn:test:model"],
                ServerUris = ["urn:test:server"],
                Models =
                [
                    new ModelTableEntry
                    {
                        ModelUri = "urn:test:model",
                        XmlSchemaUri = "urn:test:model:schema",
                        Version = "1.0.0",
                        ModelVersion = "1.0.0+build.7",
                        AccessRestrictions = 3,
                        PublicationDate = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
                        PublicationDateSpecified = true,
                        RolePermissions =
                        [
                            new RolePermission { Value = "i=15644", Permissions = 65 }
                        ],
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
                    },
                    new ModelTableEntry
                    {
                        ModelUri = "urn:test:model:secondary",
                        Version = "1.0.0"
                    }
                ],
                Aliases =
                [
                    // A NodeSet2 document may only use a name where a NodeId is
                    // expected if it declares that name here, so a fixture that
                    // exercises the round trip declares every name it uses; an
                    // undeclared one cannot be imported at all.
                    new NodeIdAlias { Alias = "MachineTypeAlias", Value = "ns=1;i=1001" },
                    new NodeIdAlias { Alias = "Double", Value = "i=11" },
                    new NodeIdAlias { Alias = "GeneratesEvent", Value = "i=41" },
                    new NodeIdAlias { Alias = "HasComponent", Value = "i=47" },
                    new NodeIdAlias { Alias = "HasModellingRule", Value = "i=37" },
                    new NodeIdAlias { Alias = "HasProperty", Value = "i=46" },
                    new NodeIdAlias { Alias = "HasSubtype", Value = "i=45" },
                    new NodeIdAlias { Alias = "HasTypeDefinition", Value = "i=40" },
                    new NodeIdAlias { Alias = "String", Value = "i=12" }
                ],
                Extensions = [modelExtension],
                LastModified = new DateTime(2026, 7, 21, 12, 34, 56, DateTimeKind.Utc),
                LastModifiedSpecified = true,
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1001",
                        BrowseName = "1:MachineType",
                        SymbolicName = "MachineType",
                        DisplayName = [new Opc.Ua.Export.LocalizedText { Value = "MachineType" }],
                        Description = [new Opc.Ua.Export.LocalizedText { Locale = "en", Value = "A test type." }],
                        Category = ["Test", "Machine"],
                        Documentation = "https://example.test/MachineType",
                        WriteMask = 1,
                        UserWriteMask = 2,
                        AccessRestrictions = 3,
                        AccessRestrictionsSpecified = true,
                        RolePermissions =
                        [
                            new RolePermission { Value = "i=15644", Permissions = 1 }
                        ],
                        ReleaseStatus = ReleaseStatus.Draft,
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
                        UserAccessLevel = 2,
                        MinimumSamplingInterval = 125.5,
                        Historizing = true,
                        DesignToolOnly = true,
                        Value = variableValue,
                        Translation =
                        [
                            new TranslationType
                            {
                                Items =
                                [
                                    new Opc.Ua.Export.LocalizedText
                                    {
                                        Locale = "en",
                                        Value = "Speed"
                                    },
                                    new StructureTranslationType
                                    {
                                        Name = "Value",
                                        Text =
                                        [
                                            new Opc.Ua.Export.LocalizedText
                                            {
                                                Locale = "de",
                                                Value = "Drehzahl"
                                            }
                                        ]
                                    }
                                ]
                            }
                        ],
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
                        Executable = false,
                        UserExecutable = false,
                        MethodDeclarationId = "ns=1;i=7000",
                        ArgumentDescription =
                        [
                            new UAMethodArgument
                            {
                                Name = "Reason",
                                Description =
                                [
                                    new Opc.Ua.Export.LocalizedText
                                    {
                                        Locale = "en",
                                        Value = "Reset reason"
                                    }
                                ]
                            }
                        ],
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
                    new UAVariableType
                    {
                        NodeId = "ns=1;i=3001",
                        BrowseName = "1:ConfiguredStringType",
                        DisplayName =
                        [
                            new Opc.Ua.Export.LocalizedText { Value = "ConfiguredStringType" }
                        ],
                        IsAbstract = true,
                        DataType = "String",
                        ValueRank = 1,
                        ArrayDimensions = "4",
                        Value = variableTypeValue,
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype",
                                IsForward = false,
                                Value = "i=62"
                            }
                        ]
                    },
                    new UADataType
                    {
                        NodeId = "ns=1;i=3002",
                        BrowseName = "1:MachineMode",
                        DisplayName =
                        [
                            new Opc.Ua.Export.LocalizedText { Value = "MachineMode" }
                        ],
                        Purpose = DataTypePurpose.CodeGenerator,
                        Definition = new Opc.Ua.Export.DataTypeDefinition
                        {
                            Name = "1:MachineMode",
                            SymbolicName = "MachineMode",
                            IsOptionSet = true,
                            Field =
                            [
                                new Opc.Ua.Export.DataTypeField
                                {
                                    Name = "Stopped",
                                    SymbolicName = "Stopped",
                                    Value = 0,
                                    DisplayName =
                                    [
                                        new Opc.Ua.Export.LocalizedText
                                        {
                                            Locale = "en",
                                            Value = "Stopped"
                                        }
                                    ]
                                },
                                new Opc.Ua.Export.DataTypeField
                                {
                                    Name = "Running",
                                    SymbolicName = "Running",
                                    Value = 1,
                                    IsOptional = true,
                                    AllowSubTypes = true,
                                    DataType = "i=6",
                                    ValueRank = 1,
                                    ArrayDimensions = "2",
                                    MaxStringLength = 32
                                }
                            ]
                        },
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype",
                                IsForward = false,
                                Value = "i=29"
                            }
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
                Aliases =
                [
                    // Declared for the same reason as in CreateRichNodeSet: a
                    // name used where a NodeId is expected has to be declared
                    // or the document cannot be imported.
                    new NodeIdAlias { Alias = "Double", Value = "i=11" },
                    new NodeIdAlias { Alias = "HasComponent", Value = "i=47" },
                    new NodeIdAlias { Alias = "HasModellingRule", Value = "i=37" },
                    new NodeIdAlias { Alias = "HasSubtype", Value = "i=45" },
                    new NodeIdAlias { Alias = "HasTypeDefinition", Value = "i=40" }
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

        /// <summary>
        /// Serves the sibling EventType definitions the event-selection
        /// fixtures name, so a document that states a selection of WoT Binding
        /// Section 6.1 converts through the asynchronous path that resolves it.
        /// </summary>
        public static IWotThingResolver EventTypeDocuments()
        {
            return new SiblingDocumentResolver(
                ("./base-event.tm.jsonld", BaseEventTypeDocument),
                ("./condition.tm.jsonld", ConditionTypeDocument));
        }

        public static byte[] Utf8(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        /// <summary>
        /// Parses a NodeSet value fragment without admitting a DTD.
        /// </summary>
        /// <remarks>
        /// A value fixture is authored as text because that is how it reads,
        /// but <c>LoadXml</c> resolves an external DTD, so the reader states
        /// explicitly that there is none to resolve.
        /// </remarks>
        public static System.Xml.XmlElement ParseValue(string xml)
        {
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(
                new StringReader(xml),
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                }))
            {
                document.Load(reader);
            }
            return document.DocumentElement;
        }

        /// <summary>
        /// The <c>BaseEventType</c> definition the fixtures link to: the fields
        /// a clause names plus the identity every clause taken from it carries
        /// (WoT Binding Section 6.1).
        /// </summary>
        private const string BaseEventTypeDocument =
            "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
            "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
            "\"pump\":\"urn:test:pump\"}]," +
            "\"@type\":[\"tm:ThingModel\",\"uav:eventType\"]," +
            "\"title\":\"BaseEventType\",\"uav:id\":\"i=2041\"," +
            "\"data\":{\"type\":\"object\"," +
            "\"uav:fieldOrder\":[\"EventId\",\"Severity\",\"Trace\"]," +
            "\"properties\":{\"EventId\":{\"type\":\"string\"}," +
            "\"Severity\":{\"type\":\"integer\"}," +
            "\"Trace\":{\"uav:browseName\":\"pump:Trace\",\"type\":\"string\"}}}}";

        /// <summary>
        /// The <c>ConditionType</c> definition the fixtures link to.
        /// </summary>
        private const string ConditionTypeDocument =
            "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
            "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
            "\"@type\":[\"tm:ThingModel\",\"uav:eventType\"]," +
            "\"title\":\"ConditionType\",\"uav:id\":\"i=2782\"," +
            "\"data\":{\"type\":\"object\"," +
            "\"uav:fieldOrder\":[\"ConditionId\",\"Severity\",\"LastSeverity\"]," +
            "\"properties\":{\"ConditionId\":{\"type\":\"string\"}," +
            "\"Severity\":{\"type\":\"integer\"}," +
            "\"LastSeverity\":{\"type\":\"integer\"}}}}";

        /// <summary>
        /// Serves a fixed set of sibling documents by href, and nothing else:
        /// a reference resolves through the local document context of WoT
        /// Binding Section 5.1.5 and is never dereferenced over the network.
        /// </summary>
        private sealed class SiblingDocumentResolver : IWotThingResolver
        {
            public SiblingDocumentResolver(params (string Href, string Json)[] documents)
            {
                foreach ((string href, string json) in documents)
                {
                    m_documents[href] = json;
                }
            }

            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<WotResolverResult>(
                    m_documents.TryGetValue(reference, out string json)
                        ? WotResolverResult.FromBytes(Utf8(json))
                        : WotResolverResult.NotFound);
            }

            private readonly Dictionary<string, string> m_documents = new(StringComparer.Ordinal);
        }
    }
}
