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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// What WoT Binding Section 5.2.1's declaration merge does with the
    /// documents it is not written for: a member that names nothing resolvable,
    /// a member that claims the root's own identity, a Node no declaration
    /// could describe, and a declaration whose NodeClass, DataType or type
    /// definition the member cannot take.
    /// </summary>
    /// <remarks>
    /// The merge rewrites Nodes in place, so its guards decide what is left
    /// alone rather than what is reported. Each one is exercised with a
    /// document that trips it, and the assertion is on the Node that was - or
    /// was not - rewritten, because a guard that only ever sees well-formed
    /// input proves the input is well formed and nothing else.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotDeclarationMergeBoundaryTests
    {
        private const string PumpNamespace = "urn:test:merge";
        private const string TankTypeId = "nsu=urn:test:merge;i=1042";
        private const string RootNodeId = "nsu=urn:test:merge;i=5001";

        /// <summary>
        /// The merge walks every Node the synthesis produced, and a Node that
        /// is not one of the four NodeClasses an instance declaration can
        /// describe - the DataType a document defines for its own members, for
        /// one - is not a member at all. It is left exactly as synthesized.
        /// </summary>
        [Test]
        public async Task ANodeOfAnUndeclarableNodeClassIsLeftAloneAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"Speed\":{\"type\":\"number\",\"uav:mapToType\":\"i=11\"}," +
                "\"Payload\":{\"type\":\"object\",\"uav:dataTypeDefinition\":" +
                "{\"@id\":\"urn:test:merge#Payload\"," +
                "\"@type\":\"uav:StructureDefinition\"," +
                "\"uav:dataTypeName\":\"pump:Payload\"," +
                "\"uav:structureType\":\"Structure\"," +
                "\"uav:fields\":[{\"@type\":\"uav:StructureField\"," +
                "\"uav:fieldName\":\"Sample\"," +
                "\"uav:fieldDataTypeName\":\"ua:Double\"," +
                "\"uav:fieldDataTypeId\":\"i=11\"}]}}",
                Declaration("Speed", WotDeclarationKind.Variable)).ConfigureAwait(false);

            UANodeSet nodeSet = Succeeded(result);

            Assert.Multiple(() =>
            {
                Assert.That(
                    nodeSet.Items!.OfType<UADataType>().Any(),
                    Is.True,
                    "The document defined a DataType, so the merge saw a Node no " +
                    "declaration describes.");
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationMismatch),
                    Is.False,
                    "A Node that is not a member is skipped, not reported.");
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationPopulated),
                    Is.True);
            });
        }

        /// <summary>
        /// A member that authors the projection root's own identity is the root
        /// rather than a member of it, so it populates nothing: rewriting it
        /// against a declaration would apply a member's declaration to the
        /// object that carries the members.
        /// </summary>
        [Test]
        public async Task AMemberClaimingTheRootsIdentityPopulatesNothingAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"Speed\":{\"type\":\"number\",\"uav:id\":\"" + RootNodeId + "\"}",
                Declaration("Speed", WotDeclarationKind.Variable)).ConfigureAwait(false);

            Succeeded(result);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.DeclarationPopulated),
                Is.False,
                "The only member names the root, so no declaration was populated.");
        }

        /// <summary>
        /// A member is matched by its qualified BrowseName, so a BrowseName
        /// that resolves to no qualified name at all matches nothing. The
        /// numeric forms below are refused by Section 5.1.3 in the first place;
        /// the point here is that the merge does not go on to guess a namespace
        /// for them.
        /// </summary>
        [TestCase("", TestName = "NoName")]
        [TestCase("nsu=urn:x", TestName = "MalformedQualifier")]
        [TestCase("99:Speed", TestName = "IndexPastTheTable")]
        public async Task AMemberWhoseBrowseNameResolvesToNothingPopulatesNothingAsync(
            string browseName)
        {
            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"Speed\":{\"type\":\"number\",\"uav:browseName\":\"" + browseName + "\"}",
                Declaration("Speed", WotDeclarationKind.Variable)).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.DeclarationPopulated),
                Is.False);
        }

        /// <summary>
        /// Index zero is the base OPC UA namespace, so a member written
        /// <c>0:Name</c> is a member of that namespace and matches a
        /// declaration there - and only there.
        /// </summary>
        [Test]
        public async Task IndexZeroIsTheBaseNamespaceAsync()
        {
            WotConversionResult<UANodeSet> matching = await ConvertAsync(
                "\"Speed\":{\"type\":\"number\",\"uav:browseName\":\"0:Speed\"}",
                Declaration(
                    "Speed",
                    WotDeclarationKind.Variable,
                    namespaceUri: WotVocabulary.OpcUaNamespace)).ConfigureAwait(false);
            WotConversionResult<UANodeSet> mismatching = await ConvertAsync(
                "\"Speed\":{\"type\":\"number\",\"uav:browseName\":\"0:Speed\"}",
                Declaration("Speed", WotDeclarationKind.Variable)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    matching.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationPopulated),
                    Is.True);
                Assert.That(
                    mismatching.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationPopulated),
                    Is.False,
                    "The declaration is in the model's own namespace, not namespace zero.");
            });
        }

        /// <summary>
        /// A declaration the member cannot be an instance of is reported with
        /// the NodeClass the member actually has, so an author can see which of
        /// the two to change.
        /// </summary>
        [TestCase(
            "\"Speed\":{\"type\":\"number\"}", "Speed", WotDeclarationKind.Method, "Variable")]
        [TestCase("\"Speed\":{}", "Speed", WotDeclarationKind.Variable, "Method")]
        [TestCase(
            "\"Speed\":{\"data\":{\"type\":\"object\",\"properties\":{}}}",
            "Speed",
            WotDeclarationKind.Variable,
            "ObjectType")]
        public async Task ADeclarationTheMemberCannotBeIsReportedWithItsNodeClassAsync(
            string affordance, string name, WotDeclarationKind kind, string nodeClass)
        {
            string? properties = nodeClass == "Variable" ? affordance : null;
            string? actions = nodeClass == "Method" ? affordance : null;
            string? events = nodeClass == "ObjectType" ? affordance : null;

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                properties,
                Declaration(name, kind),
                actions,
                events).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.DeclarationMismatch &&
                        d.Severity == WotDiagnosticSeverity.Error &&
                        d.Message.Contains(
                            "projected as a " + nodeClass, StringComparison.Ordinal) &&
                        d.Message.Contains(
                            "declares it as a " + kind.ToString(), StringComparison.Ordinal)),
                Is.True,
                "The report names the NodeClass the member has and the kind declared.");
        }

        /// <summary>
        /// A declaration whose kind this Binding does not model - one an
        /// AddressSpace reported for a NodeClass outside the four - matches no
        /// member, because nothing is known about what populating it would
        /// mean.
        /// </summary>
        [TestCase(WotDeclarationKind.Unknown)]
        [TestCase(WotDeclarationKind.Object)]
        public async Task ADeclarationOfAKindNoMemberCanBeIsReportedAsync(
            WotDeclarationKind kind)
        {
            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"Speed\":{\"type\":\"number\"}",
                Declaration("Speed", kind)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationMismatch),
                    Is.True);
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationPopulated),
                    Is.False);
            });
        }

        /// <summary>
        /// A declaration that states no DataType and no type definition still
        /// places the member - it says which ReferenceType reaches it - but it
        /// contradicts nothing the member said about what it holds, so the
        /// member keeps its own DataType and its own type definition.
        /// </summary>
        [Test]
        public async Task ADeclarationThatStatesNoTypeLeavesTheMembersOwnAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"Speed\":{\"type\":\"number\",\"uav:mapToType\":\"i=11\"}",
                new WotTypeDeclaration
                {
                    NamespaceUri = PumpNamespace,
                    BrowseName = "Speed",
                    Kind = WotDeclarationKind.Variable,
                    DeclaringTypeNodeId = TankTypeId,
                    NodeId = "nsu=urn:test:merge;i=6001",
                    ReferenceTypeName = "HasOrderedComponent",
                    DataType = string.Empty,
                    TypeDefinitionNodeId = string.Empty,
                    ValueRank = ValueRanks.Scalar,
                    ModellingRule = WotModellingRule.Optional,
                    IsInherited = true
                }).ConfigureAwait(false);

            UANodeSet nodeSet = Succeeded(result);
            UAVariable speed = nodeSet.Items!.OfType<UAVariable>()
                .Single(v => v.BrowseName!.EndsWith(":Speed", StringComparison.Ordinal));

            Assert.Multiple(() =>
            {
                Assert.That(
                    speed.DataType,
                    Is.EqualTo("i=11"),
                    "The declaration states no DataType, so it contradicts none.");
                Assert.That(
                    speed.References!.Single(r =>
                        string.Equals(
                            r.ReferenceType, "HasTypeDefinition", StringComparison.Ordinal))
                        .Value,
                    Is.EqualTo(WotVocabulary.BaseDataVariableType));
                Assert.That(
                    speed.References!.Single(r => !r.IsForward).ReferenceType,
                    Is.EqualTo("HasOrderedComponent"),
                    "The declaration does state which ReferenceType reaches the member.");
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationPopulated &&
                            d.Message.Contains("inherited ", StringComparison.Ordinal) &&
                            !d.Message.Contains("mandatory ", StringComparison.Ordinal)),
                    Is.True,
                    "An inherited, optional declaration is reported as exactly that.");
            });
        }

        /// <summary>
        /// A member whose authored BrowseName is not a QualifiedName at all is
        /// matched by the name the projected Node actually carries, and the
        /// document's affordance is keyed by the name it was written under. The
        /// two are then different names, so the member states nothing the
        /// declaration could contradict and takes what the declaration says.
        /// </summary>
        [Test]
        public async Task AMemberMatchedUnderAnotherNameStatesNothingDefinitiveAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"Speed\":{\"type\":\"string\",\"uav:browseName\":\"nsu=abc\"}",
                new WotTypeDeclaration
                {
                    NamespaceUri = WotVocabulary.OpcUaNamespace,
                    BrowseName = "nsu=abc",
                    Kind = WotDeclarationKind.Variable,
                    DeclaringTypeNodeId = TankTypeId,
                    NodeId = "nsu=urn:test:merge;i=6003",
                    ReferenceTypeName = "HasProperty",
                    TypeDefinitionNodeId = WotVocabulary.PropertyType,
                    DataType = "i=11",
                    ValueRank = ValueRanks.Scalar,
                    ModellingRule = WotModellingRule.Mandatory
                }).ConfigureAwait(false);

            UANodeSet nodeSet = result.Value!;
            UAVariable speed = nodeSet.Items!.OfType<UAVariable>()
                .Single(v => string.Equals(v.BrowseName, "nsu=abc", StringComparison.Ordinal));

            Assert.Multiple(() =>
            {
                Assert.That(
                    speed.DataType,
                    Is.EqualTo("i=11"),
                    "The member states no definitive DataType, so it takes the declaration's.");
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationPopulated),
                    Is.True);
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationMismatch),
                    Is.False);
            });
        }

        /// <summary>
        /// A declared Method keeps the References that carry its signature: the
        /// merge rewrites the one that places the member and the one that names
        /// its type definition, and leaves everything else the synthesis
        /// produced alone.
        /// </summary>
        [Test]
        public async Task ADeclaredMethodKeepsItsArgumentReferencesAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertAsync(
                properties: null,
                new WotTypeDeclaration
                {
                    NamespaceUri = PumpNamespace,
                    BrowseName = "Reset",
                    Kind = WotDeclarationKind.Method,
                    DeclaringTypeNodeId = TankTypeId,
                    NodeId = "nsu=urn:test:merge;i=6002",
                    MethodDeclarationNodeId = "nsu=urn:test:merge;i=6002",
                    ReferenceTypeName = "HasComponent",
                    TypeDefinitionNodeId = WotVocabulary.PropertyType,
                    ModellingRule = WotModellingRule.Mandatory
                },
                actions: "\"Reset\":{\"input\":{\"type\":\"number\"}}").ConfigureAwait(false);

            UANodeSet nodeSet = Succeeded(result);
            UAMethod reset = nodeSet.Items!.OfType<UAMethod>().Single();

            Assert.Multiple(() =>
            {
                Assert.That(reset.MethodDeclarationId, Is.EqualTo("ns=1;i=6002"));
                Assert.That(
                    reset.References!.Any(r =>
                        r.IsForward &&
                        string.Equals(
                            r.ReferenceType, "HasProperty", StringComparison.Ordinal)),
                    Is.True,
                    "The Method still owns its InputArguments Property.");
                Assert.That(
                    reset.References!.Any(r =>
                        string.Equals(
                            r.ReferenceType, "HasTypeDefinition", StringComparison.Ordinal)),
                    Is.False,
                    "A Method has no type definition for the declaration to rewrite.");
            });
        }

        /// <summary>
        /// Every declared member is reached by the ReferenceType its own
        /// declaration states, and rewriting one member's placement never
        /// disturbs another's - which is what makes the rewrite a replacement
        /// rather than an addition.
        /// </summary>
        [Test]
        public async Task EachMemberIsReachedByItsOwnDeclaredReferenceTypeAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"Speed\":{\"type\":\"number\",\"uav:mapToType\":\"i=11\"}," +
                "\"Serial\":{\"type\":\"number\",\"uav:mapToType\":\"i=11\"}," +
                "\"Model\":{\"type\":\"number\",\"uav:mapToType\":\"i=11\"}",
                Placed("Speed", "HasOrderedComponent"),
                declarations:
                [
                    Placed("Serial", "HasProperty"),
                    Placed("Model", "HasComponent")
                ]).ConfigureAwait(false);

            UANodeSet nodeSet = Succeeded(result);
            UANode root = nodeSet.Items!.First(i => i is UAObject);

            Assert.Multiple(() =>
            {
                Assert.That(ReferenceTypeTo(nodeSet, root, "Speed"),
                    Is.EqualTo("HasOrderedComponent"));
                Assert.That(ReferenceTypeTo(nodeSet, root, "Serial"),
                    Is.EqualTo("HasProperty"));
                Assert.That(ReferenceTypeTo(nodeSet, root, "Model"),
                    Is.EqualTo("HasComponent"));
            });
        }

        private static string ReferenceTypeTo(
            UANodeSet nodeSet, UANode root, string browseName)
        {
            string nodeId = nodeSet.Items!
                .Single(i => string.Equals(
                    i.BrowseName, "1:" + browseName, StringComparison.Ordinal))
                .NodeId!;
            return root.References!
                .Single(r => r.IsForward &&
                    string.Equals(r.Value, nodeId, StringComparison.Ordinal))
                .ReferenceType!;
        }

        private static WotTypeDeclaration Placed(string browseName, string referenceTypeName)
        {
            return new WotTypeDeclaration
            {
                NamespaceUri = PumpNamespace,
                BrowseName = browseName,
                Kind = WotDeclarationKind.Variable,
                DeclaringTypeNodeId = TankTypeId,
                NodeId = "nsu=urn:test:merge;s=" + browseName,
                ReferenceTypeName = referenceTypeName,
                TypeDefinitionNodeId = WotVocabulary.BaseDataVariableType,
                DataType = "i=11",
                ValueRank = ValueRanks.Scalar,
                ModellingRule = WotModellingRule.Mandatory
            };
        }

        private static UANodeSet Succeeded(WotConversionResult<UANodeSet> result)
        {
            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);
            Assert.That(result.Value, Is.Not.Null);
            return result.Value!;
        }

        private static WotTypeDeclaration Declaration(
            string browseName,
            WotDeclarationKind kind,
            string? namespaceUri = null)
        {
            return new WotTypeDeclaration
            {
                NamespaceUri = namespaceUri ?? PumpNamespace,
                BrowseName = browseName,
                Kind = kind,
                DeclaringTypeNodeId = TankTypeId,
                NodeId = "nsu=urn:test:merge;i=6001",
                ReferenceTypeName = "HasProperty",
                TypeDefinitionNodeId = WotVocabulary.PropertyType,
                DataType = "i=11",
                ValueRank = ValueRanks.Scalar,
                ModellingRule = WotModellingRule.Mandatory
            };
        }

        private static async Task<WotConversionResult<UANodeSet>> ConvertAsync(
            string? properties,
            WotTypeDeclaration declaration,
            string? actions = null,
            string? events = null,
            WotTypeDeclaration[]? declarations = null)
        {
            var all = new List<WotTypeDeclaration> { declaration };
            if (declarations is not null)
            {
                all.AddRange(declarations);
            }
            using WotDocument document = WotDocument.Parse(
                WotTestData.Utf8(Instance(properties, actions, events)));
            return await WotNodeSetConverter.ToNodeSetResultAsync(
                document,
                null,
                null,
                null,
                new ScriptedDeclarationResolver([.. all])).ConfigureAwait(false);
        }

        private static string Instance(string? properties, string? actions, string? events)
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"tm\":\"https://www.w3.org/2019/wot/tm#\"," +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"" + PumpNamespace + "\"}]," +
                "\"@type\":[\"Thing\",\"uav:object\",\"pump:TankType\"]," +
                "\"title\":\"Tank\",\"uav:browseName\":\"pump:Tank\"," +
                "\"uav:id\":\"" + RootNodeId + "\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}" +
                (properties is null ? string.Empty : ",\"properties\":{" + properties + "}") +
                (actions is null ? string.Empty : ",\"actions\":{" + actions + "}") +
                (events is null ? string.Empty : ",\"events\":{" + events + "}") +
                "}";
        }

        /// <summary>
        /// A local context that holds exactly one type and reports exactly the
        /// declarations a test hands it, so the merge can be driven with
        /// declarations no document could state.
        /// </summary>
        private sealed class ScriptedDeclarationResolver
            : IWotNodeResolver, IWotTypeDeclarationResolver
        {
            public ScriptedDeclarationResolver(params WotTypeDeclaration[] declarations)
            {
                m_declarations = declarations;
            }

            public ValueTask<bool> HoldsNamespaceAsync(
                string namespaceUri, CancellationToken cancellationToken = default)
            {
                return new ValueTask<bool>(
                    string.Equals(namespaceUri, PumpNamespace, StringComparison.Ordinal));
            }

            public ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
                string namespaceUri,
                string browseName,
                WotExpectedNodeClass expected,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ArrayOf<WotResolvedNode>>(
                    string.Equals(browseName, "TankType", StringComparison.Ordinal)
                        ? new ArrayOf<WotResolvedNode>(
                            [new WotResolvedNode(TankTypeId, WotExpectedNodeClass.ObjectType)])
                        : ArrayOf<WotResolvedNode>.Empty);
            }

            public ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
                string expandedNodeId, CancellationToken cancellationToken = default)
            {
                return new ValueTask<WotResolvedNode?>(
                    string.Equals(expandedNodeId, TankTypeId, StringComparison.Ordinal)
                        ? new WotResolvedNode(TankTypeId, WotExpectedNodeClass.ObjectType)
                        : null);
            }

            public ValueTask<WotTypeDeclarationSet?> ResolveDeclarationsAsync(
                string typeNodeId,
                WotDeclarationScope scope,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<WotTypeDeclarationSet?>(
                    string.Equals(typeNodeId, TankTypeId, StringComparison.Ordinal)
                        ? new WotTypeDeclarationSet
                        {
                            TypeNodeId = TankTypeId,
                            Declarations = m_declarations.ToArrayOf(),
                            Supertypes = ArrayOf<string>.Empty
                        }
                        : null);
            }

            private readonly IReadOnlyList<WotTypeDeclaration> m_declarations;
        }
    }
}
