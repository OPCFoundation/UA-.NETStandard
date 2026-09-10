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
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    public sealed class WotConditionDeclarationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task ACompanionConditionUsesOneVerifiedTypeForItsSupertypeAndActionAsync(bool pinned)
        {
            using WotDocument document = ConditionDocument(pinned, withAction: true);
            var resolver = new Mock<IWotNodeResolver>();
            resolver.Setup(value => value.ResolveByBrowseNameAsync(
                    "urn:test:condition", "CustomAlarmType", WotExpectedNodeClass.ObjectType,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ArrayOf<WotResolvedNode>(
                    [new WotResolvedNode(ConditionIdentity, WotExpectedNodeClass.ObjectType)]));
            resolver.Setup(value => value.ResolveByNodeIdAsync(ConditionIdentity, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WotResolvedNode(ConditionIdentity, WotExpectedNodeClass.ObjectType)
                {
                    SupertypeNodeIds = [IntermediateIdentity]
                });
            resolver.Setup(value => value.ResolveByNodeIdAsync(IntermediateIdentity, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WotResolvedNode(IntermediateIdentity, WotExpectedNodeClass.ObjectType)
                {
                    SupertypeNodeIds = ["i=2955"]
                });

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, null, null, resolver.Object).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Errors(result));
            UAObjectType eventType = result.Value.Items.OfType<UAObjectType>().Single();
            Assert.That(eventType.References.Single(reference =>
                !reference.IsForward && reference.ReferenceType == "HasSubtype").Value,
                Is.EqualTo("ns=2;i=7001"));
            UAMethod method = result.Value.Items.OfType<UAMethod>().Single();
            Assert.That(method.MethodDeclarationId, Is.EqualTo("i=9111"));
            Assert.That(method.ParentNodeId, Is.EqualTo(eventType.NodeId));
            Assert.That(method.BrowseName, Is.EqualTo("Acknowledge"));
            Assert.That(eventType.References.Any(reference =>
                reference.IsForward &&
                reference.ReferenceType == "HasComponent" &&
                reference.Value == method.NodeId), Is.True);
            UAVariable arguments = result.Value.Items.OfType<UAVariable>()
                .Single(variable => variable.BrowseName == "InputArguments");
            Assert.That(arguments.DataType, Is.EqualTo("i=296"));
            Assert.That(arguments.ValueRank, Is.EqualTo(1));
            Assert.That(arguments.Value.GetElementsByTagName("Name", Namespaces.OpcUaXsd).Cast<System.Xml.XmlElement>()
                .Select(element => element.InnerText), Is.EqualTo(s_argumentNames));
            Assert.That(arguments.Value.GetElementsByTagName("Identifier", Namespaces.OpcUaXsd)
                .Cast<System.Xml.XmlElement>().Select(element => element.InnerText), Is.EqualTo(s_argumentIdentities));
        }

        [TestCase("missing")]
        [TestCase("substituted")]
        [TestCase("wrongClass")]
        [TestCase("nonCondition")]
        [TestCase("selfCycle")]
        [TestCase("twoNodeCycle")]
        [TestCase("missingAncestor")]
        [TestCase("cycleAfterCondition")]
        [TestCase("duplicateAncestors")]
        public async Task AConditionPinRequiresExactResolvedObjectTypeAncestryAsync(string failure)
        {
            using WotDocument document = ConditionDocument(pinned: true);
            var resolver = new Mock<IWotNodeResolver>();
            WotResolvedNode? resolved = failure == "missing"
                ? null
                : new WotResolvedNode(
                    failure == "substituted" ? IntermediateIdentity : ConditionIdentity,
                    failure == "wrongClass" ? WotExpectedNodeClass.VariableType : WotExpectedNodeClass.ObjectType)
                {
                    SupertypeNodeIds = failure == "cycleAfterCondition"
                        ? ["i=2782", ConditionIdentity]
                        : failure == "duplicateAncestors"
                            ? ["i=2782", "i=2782"]
                            : [failure switch
                            {
                                "nonCondition" => "i=58",
                                "selfCycle" => ConditionIdentity,
                                "twoNodeCycle" or "missingAncestor" => IntermediateIdentity,
                                _ => "i=2782"
                            }]
                };
            resolver.Setup(value => value.ResolveByNodeIdAsync(ConditionIdentity, It.IsAny<CancellationToken>()))
                .ReturnsAsync(resolved);
            if (failure == "twoNodeCycle")
            {
                resolver.Setup(value => value.ResolveByNodeIdAsync(
                        IntermediateIdentity, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new WotResolvedNode(IntermediateIdentity, WotExpectedNodeClass.ObjectType)
                    {
                        SupertypeNodeIds = [ConditionIdentity]
                    });
            }

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, null, null, resolver.Object).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Location?.JsonPointer == "/events/alarm/uav:conditionTypeId"), Is.True, Errors(result));
        }

        [TestCase(100, true)]
        [TestCase(101, false)]
        public async Task ConditionAncestryHonorsItsExactDepthBoundaryAsync(int depth, bool accepted)
        {
            using WotDocument document = ConditionDocument(pinned: true);
            var resolver = new Mock<IWotNodeResolver>();
            for (int index = 0; index < depth; index++)
            {
                string identity = ChainIdentity(index);
                string parent = index == depth - 1 ? "i=2782" : ChainIdentity(index + 1);
                resolver.Setup(value => value.ResolveByNodeIdAsync(identity, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new WotResolvedNode(identity, WotExpectedNodeClass.ObjectType)
                    {
                        SupertypeNodeIds = [parent]
                    });
            }

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, null, null, resolver.Object).ConfigureAwait(false);

            Assert.That(result.Success, Is.EqualTo(accepted), Errors(result));
            if (accepted)
            {
                Assert.That(result.Value.Items.OfType<UAObjectType>().Single()
                    .References.Single(reference => reference.ReferenceType == "HasSubtype").Value,
                    Is.EqualTo("ns=2;i=7001"));
            }
            else
            {
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Location?.JsonPointer == "/events/alarm/uav:conditionTypeId"), Is.True);
            }
            resolver.Verify(value => value.ResolveByNodeIdAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtMost(100));
        }

        [Test]
        public async Task CancellationDuringConditionResolutionStopsBeforeSynthesisAsync()
        {
            using WotDocument document = ConditionDocument(pinned: true);
            using var cancellation = new CancellationTokenSource();
            var resolver = new Mock<IWotNodeResolver>();
            resolver.Setup(value => value.ResolveByNodeIdAsync(ConditionIdentity, cancellation.Token))
                .Returns(() =>
                {
                    cancellation.Cancel();
                    return new ValueTask<WotResolvedNode?>(
                        new WotResolvedNode(ConditionIdentity, WotExpectedNodeClass.ObjectType)
                        {
                            SupertypeNodeIds = [IntermediateIdentity]
                        });
                });

            await Assert.ThatAsync(async () =>
                await WotNodeSetConverter.ToNodeSetResultAsync(
                    document, null, null, null, resolver.Object, cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            resolver.Verify(value => value.ResolveByNodeIdAsync(
                IntermediateIdentity, It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AStandardPinCannotOverrideAResolvedCompanionHintAsync(bool hintResolves)
        {
            using WotDocument original = ConditionDocument(pinned: true);
            JsonObject json = JsonNode.Parse(original.Utf8Json.Span).AsObject();
            json["events"]["alarm"]["uav:conditionTypeId"] = "i=2782";
            using var document = WotDocument.Parse(WotTestData.Utf8(json.ToJsonString()));
            var resolver = new Mock<IWotNodeResolver>();
            resolver.Setup(value => value.ResolveByBrowseNameAsync(
                    "urn:test:condition", "CustomAlarmType", WotExpectedNodeClass.ObjectType,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(hintResolves
                    ? new ArrayOf<WotResolvedNode>(
                        [new WotResolvedNode(ConditionIdentity, WotExpectedNodeClass.ObjectType)])
                    : []);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, null, null, resolver.Object).ConfigureAwait(false);

            Assert.That(result.Success, Is.EqualTo(!hintResolves), Errors(result));
            if (hintResolves)
            {
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ConditionTypeConflict &&
                    diagnostic.Location?.JsonPointer == "/events/alarm/uav:conditionTypeId"), Is.True);
            }
        }

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public void RestoredConditionClaimsUseActualNativeAncestryWithoutDemandingMissingData(
            bool archive, bool companionPin, bool conditionAncestor)
        {
            UANodeSet source = NativeCondition(conditionAncestor);
            JsonObject root = NativeDocument(source, archive, companionPin);
            using var document = WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.EqualTo(conditionAncestor), Errors(result));
            if (!conditionAncestor)
            {
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict &&
                    diagnostic.Location?.JsonPointer == "/events/alarm/uav:conditionTypeId"), Is.True);
            }
            Assert.That(result.Value.Items, Has.Length.EqualTo(3));
            UAObjectType eventType = result.Value.Items.OfType<UAObjectType>()
                .Single(type => type.NodeId == "ns=1;i=5002");
            Assert.That(eventType.References.Single().Value, Is.EqualTo("ns=2;i=7001"));
            UAObjectType companion = result.Value.Items.OfType<UAObjectType>()
                .Single(type => type.NodeId == "ns=2;i=7001");
            Assert.That(companion.References.Single().Value, Is.EqualTo(conditionAncestor ? "i=2782" : "i=58"));
            Assert.That(result.Value.Items.OfType<UAVariable>(), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeConditionAncestryAcceptsAForwardHasSubtypeDeclaration(bool archive)
        {
            UANodeSet source = NativeCondition(true);
            source.Items.Single(node => node.NodeId == "ns=2;i=7001").References = [];
            source.Items =
            [
                .. source.Items,
                new UAObjectType
                {
                    NodeId = "i=2782",
                    BrowseName = "ConditionType",
                    References =
                    [
                        new Reference { ReferenceType = "HasSubtype", IsForward = true, Value = "ns=2;i=7001" }
                    ]
                }
            ];
            JsonObject root = NativeDocument(source, archive, companionPin: true);
            using var document = WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Errors(result));
            Assert.That(result.Value.Items, Has.Length.EqualTo(4));
            Assert.That(result.Value.Items.Single(node => node.NodeId == "ns=2;i=7001").References ?? [], Is.Empty);
            Assert.That(result.Value.Items.Single(node => node.NodeId == "i=2782").References.Single().Value,
                Is.EqualTo("ns=2;i=7001"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeConditionPinsDoNotHideMalformedReadableHints(bool archive)
        {
            JsonObject root = NativeDocument(NativeCondition(true), archive, companionPin: false);
            root["events"]["alarm"]["uav:conditionType"] = 42;
            using var document = WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict &&
                diagnostic.Location?.JsonPointer == "/events/alarm/uav:conditionTypeId"), Is.True);
            Assert.That(result.Value.Items, Has.Length.EqualTo(3));
        }

        private static JsonObject NativeDocument(UANodeSet source, bool archive, bool companionPin)
        {
            using WotDocument projected = WotNodeSetConverter.FromNodeSet(
                source,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Always
                });
            JsonObject root = JsonNode.Parse(projected.Utf8Json.Span).AsObject();
            root.Remove(archive ? "uav:nodes" : "uav:nodeSet");
            root["events"] = new JsonObject
            {
                ["alarm"] = new JsonObject
                {
                    ["@type"] = "uav:eventType",
                    ["uav:id"] = "nsu=urn:test:pump;i=5002",
                    ["uav:conditionTypeId"] = companionPin ? ConditionIdentity : "i=2782"
                }
            };
            return root;
        }

        private static UANodeSet NativeCondition(bool conditionAncestor)
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:test:pump", "urn:test:condition"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:pump" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=5001",
                        BrowseName = "1:PumpType",
                        References =
                        [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=58" },
                            new Reference
                            {
                                ReferenceType = "GeneratesEvent", IsForward = true, Value = "ns=1;i=5002"
                            }
                        ]
                    },
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=5002",
                        BrowseName = "1:AlarmType",
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype", IsForward = false, Value = "ns=2;i=7001"
                            }
                        ]
                    },
                    new UAObjectType
                    {
                        NodeId = "ns=2;i=7001",
                        BrowseName = "2:CustomAlarmType",
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype",
                                IsForward = false,
                                Value = conditionAncestor ? "i=2782" : "i=58"
                            }
                        ]
                    }
                ]
            };
        }

        private static WotDocument ConditionDocument(bool pinned, bool withAction = false)
        {
            string pin = pinned ? "\"uav:conditionTypeId\":\"" + ConditionIdentity + "\"," : string.Empty;
            string action = withAction
                ? """
                  ,"actions": {
                    "ack": {
                      "uav:conditionAction": "Acknowledge", "uav:actsOn": "alarm",
                      "input": {
                        "type": "object",
                        "properties": {
                          "EventId": { "type": "string", "contentEncoding": "base64" },
                          "Comment": { "type": "string", "uav:mapToType": "i=21" }
                        }
                      }
                    }
                  }
                  """
                : string.Empty;
            return WotDocument.Parse(WotTestData.Utf8(
                """
                {
                  "@context": {
                    "ua": "http://opcfoundation.org/UA/",
                    "vendor": "urn:test:condition"
                  },
                  "@type": "uav:object",
                  "uav:id": "nsu=urn:test:pump;i=5001",
                  "uav:browseName": "nsu=urn:test:pump;Pump",
                  "events": {
                    "alarm": {
                      "@type": "uav:eventType",
                """ +
                pin +
                """
                      "uav:conditionType": "vendor:CustomAlarmType",
                      "data": {
                        "type": "object",
                        "properties": { "EventId": { "type": "string", "contentEncoding": "base64" } }
                      }
                    }
                  }
                """ +
                action +
                "}"));
        }

        private static string ChainIdentity(int index)
        {
            return "nsu=urn:test:condition;i=" + (7001 + index).ToString(CultureInfo.InvariantCulture);
        }

        private static string Errors(WotConversionResult<UANodeSet> result)
        {
            return string.Join("; ", result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.Message));
        }

        private const string ConditionIdentity = "nsu=urn:test:condition;i=7001";
        private const string IntermediateIdentity = "nsu=urn:test:condition;i=7002";
        private static readonly string[] s_argumentNames = ["EventId", "Comment"];
        private static readonly string[] s_argumentIdentities = ["i=297", "i=15", "i=297", "i=21"];
    }
}
