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
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Server.Nodes;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.RuntimeNodeSet
{
    [TestFixture]
    [Category("RuntimeNodeSet")]
    [Category("NodeSource")]
    [Parallelizable]
    public sealed class NodeSetImporterTests
    {
        private const string kNamespaceUri =
            "urn:opcfoundation.org:Tests:TypedNodeSetImport";
        private const string kExternalNamespaceUri =
            "urn:opcfoundation.org:Tests:TypedNodeSetImport:External";

        [Test]
        public void FactoriesSelectTypedStatesByEachDiscriminator()
        {
            SystemContext context = CreateContext();
            context.NamespaceUris.Append(
                "urn:opcfoundation.org:Tests:TypedNodeSetImport:Preexisting");
            UANodeSet nodeSet = ReadNodeSet(
                """
                  <UAObjectType NodeId="ns=1;i=100" BrowseName="1:TypedObjectType" IsAbstract="true">
                    <DisplayName>TypedObjectType</DisplayName>
                    <References>
                      <Reference ReferenceType="i=45" IsForward="false">i=58</Reference>
                    </References>
                  </UAObjectType>
                  <UAVariableType NodeId="ns=1;i=101" BrowseName="1:TypedVariableType"
                                  DataType="i=11" ValueRank="1" ArrayDimensions="2">
                    <DisplayName>TypedVariableType</DisplayName>
                    <References>
                      <Reference ReferenceType="i=45" IsForward="false">i=63</Reference>
                    </References>
                  </UAVariableType>
                  <UADataType NodeId="ns=1;i=102" BrowseName="1:TypedDataType" IsAbstract="true">
                    <DisplayName>TypedDataType</DisplayName>
                    <References>
                      <Reference ReferenceType="i=45" IsForward="false">i=24</Reference>
                    </References>
                    <Definition Name="TypedDataType">
                      <Field Name="Code" DataType="i=6" />
                    </Definition>
                  </UADataType>
                  <UAReferenceType NodeId="ns=1;i=103" BrowseName="1:TypedReferenceType"
                                   IsAbstract="true" Symmetric="false">
                    <DisplayName>TypedReferenceType</DisplayName>
                    <InverseName>IsTypedBy</InverseName>
                    <References>
                      <Reference ReferenceType="i=45" IsForward="false">i=32</Reference>
                    </References>
                  </UAReferenceType>
                  <UAView NodeId="ns=1;i=104" BrowseName="1:TypedView"
                          ContainsNoLoops="true" EventNotifier="1">
                    <DisplayName>TypedView</DisplayName>
                  </UAView>
                  <UAObject NodeId="ns=1;i=200" BrowseName="1:TypedObject">
                    <DisplayName>TypedObject</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">ns=1;i=100</Reference>
                    </References>
                  </UAObject>
                  <UAVariable NodeId="ns=1;i=201" BrowseName="1:TypedVariable"
                              DataType="i=11" ValueRank="-1">
                    <DisplayName>TypedVariable</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">ns=1;i=101</Reference>
                    </References>
                  </UAVariable>
                  <UAMethod NodeId="ns=1;i=202" BrowseName="1:TypedMethod"
                            MethodDeclarationId="ns=1;i=105">
                    <DisplayName>TypedMethod</DisplayName>
                  </UAMethod>
                """);
            var importer = new NodeSetImporter(
                context,
                new ManualFactoryProvider(
                    Factory(NodeClass.Object, 100, static () => new TypedObjectState(null)),
                    Factory(NodeClass.Variable, 101, static () => new TypedVariableState(null)),
                    Factory(NodeClass.Method, 105, static () => new TypedMethodState(null)),
                    Factory(NodeClass.ObjectType, 100, static () => new TypedObjectTypeState()),
                    Factory(NodeClass.VariableType, 101, static () => new TypedVariableTypeState()),
                    Factory(NodeClass.DataType, 102, static () => new TypedDataTypeState()),
                    Factory(NodeClass.ReferenceType, 103, static () => new TypedReferenceTypeState()),
                    Factory(NodeClass.View, 104, static () => new TypedViewState())));

            importer.Import(nodeSet);
            importer.Complete();

            var objectType = (TypedObjectTypeState)Find(importer, 100);
            var variableType = (TypedVariableTypeState)Find(importer, 101);
            var dataType = (TypedDataTypeState)Find(importer, 102);
            var referenceType = (TypedReferenceTypeState)Find(importer, 103);
            var view = (TypedViewState)Find(importer, 104);
            Assert.Multiple(() =>
            {
                Assert.That(objectType, Is.TypeOf<TypedObjectTypeState>());
                Assert.That(objectType.IsAbstract, Is.True);
                Assert.That(variableType, Is.TypeOf<TypedVariableTypeState>());
                Assert.That(variableType.DataType, Is.EqualTo(DataTypeIds.Double));
                Assert.That(variableType.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                Assert.That(variableType.ArrayDimensions, Is.EqualTo(new uint[] { 2 }));
                Assert.That(dataType, Is.TypeOf<TypedDataTypeState>());
                Assert.That(
                    dataType.DataTypeDefinition.TryGetValue(
                        out DataTypeDefinition definition),
                    Is.True);
                Assert.That(definition, Is.Not.Null);
                Assert.That(referenceType, Is.TypeOf<TypedReferenceTypeState>());
                Assert.That(referenceType.IsAbstract, Is.True);
                Assert.That(referenceType.InverseName.Text, Is.EqualTo("IsTypedBy"));
                Assert.That(view, Is.TypeOf<TypedViewState>());
                Assert.That(view.ContainsNoLoops, Is.True);
                Assert.That(view.EventNotifier, Is.EqualTo((byte)1));
                Assert.That(Find(importer, 200), Is.TypeOf<TypedObjectState>());
                Assert.That(Find(importer, 201), Is.TypeOf<TypedVariableState>());
                Assert.That(Find(importer, 202), Is.TypeOf<TypedMethodState>());
            });
        }

        [Test]
        public void UnknownDiscriminatorsUseExactGenericFallbacks()
        {
            SystemContext context = CreateContext();
            UANodeSet nodeSet = ReadNodeSet(
                """
                  <UAObjectType NodeId="ns=1;i=100" BrowseName="1:GenericObjectType">
                    <DisplayName>GenericObjectType</DisplayName>
                  </UAObjectType>
                  <UAObject NodeId="ns=1;i=200" BrowseName="1:GenericObject">
                    <DisplayName>GenericObject</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">ns=1;i=100</Reference>
                    </References>
                  </UAObject>
                  <UAVariable NodeId="ns=1;i=201" BrowseName="1:GenericProperty"
                              DataType="i=12">
                    <DisplayName>GenericProperty</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=68</Reference>
                    </References>
                  </UAVariable>
                  <UAMethod NodeId="ns=1;i=202" BrowseName="1:GenericMethod"
                            MethodDeclarationId="ns=1;i=105">
                    <DisplayName>GenericMethod</DisplayName>
                  </UAMethod>
                """);
            var importer = new NodeSetImporter(context, factoryProvider: null);

            importer.Import(nodeSet);
            importer.Complete();

            Assert.Multiple(() =>
            {
                Assert.That(Find(importer, 100), Is.TypeOf<BaseObjectTypeState>());
                Assert.That(Find(importer, 200), Is.TypeOf<BaseObjectState>());
                Assert.That(Find(importer, 201), Is.TypeOf<PropertyState>());
                Assert.That(Find(importer, 202), Is.TypeOf<MethodState>());
            });
        }

        [Test]
        public void TypedImportMatchesGenericFieldsValuesAndReferences()
        {
            const string nodes =
                """
                  <UAObjectType NodeId="ns=1;i=100" BrowseName="1:TypedObjectType">
                    <DisplayName>TypedObjectType</DisplayName>
                  </UAObjectType>
                  <UAVariableType NodeId="ns=1;i=101" BrowseName="1:TypedVariableType"
                                  DataType="i=11">
                    <DisplayName>TypedVariableType</DisplayName>
                  </UAVariableType>
                  <UAObject NodeId="ns=1;i=200" BrowseName="1:TypedObject"
                            SymbolicName="TypedObjectSymbol" EventNotifier="1"
                            WriteMask="2097151" UserWriteMask="31"
                            AccessRestrictions="3" ReleaseStatus="Released"
                            DesignToolOnly="true">
                    <DisplayName Locale="en-US">Typed object</DisplayName>
                    <Description Locale="en-US">Typed object description</Description>
                    <Category>First</Category>
                    <Category>Second</Category>
                    <Documentation>https://example.test/object</Documentation>
                    <References>
                      <Reference ReferenceType="i=40">ns=1;i=100</Reference>
                      <Reference ReferenceType="i=37">i=78</Reference>
                      <Reference ReferenceType="i=47" IsForward="false">ns=2;i=900</Reference>
                    </References>
                    <RolePermissions>
                      <RolePermission Permissions="63">i=15644</RolePermission>
                    </RolePermissions>
                    <Extensions>
                      <Extension>
                        <test:Marker xmlns:test="urn:test:extension">object</test:Marker>
                      </Extension>
                    </Extensions>
                  </UAObject>
                  <UAVariable NodeId="ns=1;i=201" BrowseName="1:TypedVariable"
                              SymbolicName="TypedVariableSymbol" DataType="i=11"
                              ValueRank="1" ArrayDimensions="2" AccessLevel="3"
                              UserAccessLevel="1" MinimumSamplingInterval="12.5"
                              Historizing="true" AccessRestrictions="1"
                              ReleaseStatus="Draft" DesignToolOnly="true">
                    <DisplayName Locale="en-US">Typed variable</DisplayName>
                    <Description Locale="en-US">Typed variable description</Description>
                    <References>
                      <Reference ReferenceType="i=40">ns=1;i=101</Reference>
                      <Reference ReferenceType="i=47" IsForward="false">ns=1;i=200</Reference>
                    </References>
                    <RolePermissions>
                      <RolePermission Permissions="3">i=15644</RolePermission>
                    </RolePermissions>
                    <Value>
                      <uax:ListOfDouble>
                        <uax:Double>1.5</uax:Double>
                        <uax:Double>2.5</uax:Double>
                      </uax:ListOfDouble>
                    </Value>
                  </UAVariable>
                  <UAMethod NodeId="ns=1;i=202" BrowseName="1:TypedMethod"
                            SymbolicName="TypedMethodSymbol" Executable="true"
                            UserExecutable="false" MethodDeclarationId="ns=1;i=105"
                            ReleaseStatus="Deprecated" DesignToolOnly="true">
                    <DisplayName Locale="en-US">Typed method</DisplayName>
                    <Description Locale="en-US">Typed method description</Description>
                    <References>
                      <Reference ReferenceType="i=47" IsForward="false">ns=1;i=200</Reference>
                    </References>
                  </UAMethod>
                """;
            UANodeSet genericNodeSet = ReadNodeSet(nodes);
            UANodeSet typedNodeSet = ReadNodeSet(nodes);
            SystemContext genericContext = CreateContext();
            SystemContext typedContext = CreateContext();
            var genericNodes = new NodeStateCollection();
            genericNodeSet.Import(genericContext, genericNodes, linkParentChild: true);
            var typedImporter = new NodeSetImporter(
                typedContext,
                new ManualFactoryProvider(
                    Factory(NodeClass.Object, 100, static () => new TypedObjectState(null)),
                    Factory(NodeClass.Variable, 101, static () => new TypedVariableState(null)),
                    Factory(NodeClass.Method, 105, static () => new TypedMethodState(null))));

            typedImporter.Import(typedNodeSet);
            typedImporter.Complete();

            AssertNodeParity(
                genericContext,
                genericNodes.Single(node => HasNumericIdentifier(node, 200)),
                typedContext,
                Find(typedImporter, 200));
            AssertNodeParity(
                genericContext,
                genericNodes.Single(node => HasNumericIdentifier(node, 201)),
                typedContext,
                Find(typedImporter, 201));
            AssertNodeParity(
                genericContext,
                genericNodes.Single(node => HasNumericIdentifier(node, 202)),
                typedContext,
                Find(typedImporter, 202));
            Assert.That(
                ((MethodState)Find(typedImporter, 202)).UserExecutable,
                Is.False);

            var genericVariable = (BaseVariableState)genericNodes.Single(
                node => HasNumericIdentifier(node, 201));
            var typedVariable = (BaseVariableState)Find(typedImporter, 201);
            Assert.Multiple(() =>
            {
                Assert.That(typedVariable.DataType, Is.EqualTo(genericVariable.DataType));
                Assert.That(typedVariable.ValueRank, Is.EqualTo(genericVariable.ValueRank));
                Assert.That(
                    typedVariable.ArrayDimensions,
                    Is.EqualTo(genericVariable.ArrayDimensions));
                Assert.That(typedVariable.AccessLevelEx, Is.EqualTo(genericVariable.AccessLevelEx));
                Assert.That(
                    typedVariable.UserAccessLevel,
                    Is.EqualTo(genericVariable.UserAccessLevel));
                Assert.That(
                    typedVariable.MinimumSamplingInterval,
                    Is.EqualTo(genericVariable.MinimumSamplingInterval));
                Assert.That(typedVariable.Historizing, Is.EqualTo(genericVariable.Historizing));
                Assert.That(typedVariable.Value, Is.EqualTo(genericVariable.Value));
                Assert.That(typedVariable.StatusCode, Is.EqualTo(genericVariable.StatusCode));
                Assert.That(typedVariable.Timestamp, Is.EqualTo(genericVariable.Timestamp));
            });
        }

        [Test]
        public void CompleteLinksAcrossDocumentsOnceAndPreservesUnresolvedParent()
        {
            SystemContext context = CreateContext();
            var importer = new NodeSetImporter(context, factoryProvider: null);
            UANodeSet children = ReadNodeSet(
                """
                  <UAObject NodeId="ns=1;i=2" BrowseName="1:Child"
                            ParentNodeId="ns=1;i=1">
                    <DisplayName>Child</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=58</Reference>
                      <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1</Reference>
                    </References>
                  </UAObject>
                  <UAObject NodeId="ns=1;i=3" BrowseName="1:ExternalChild"
                            ParentNodeId="ns=2;i=9">
                    <DisplayName>ExternalChild</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=58</Reference>
                      <Reference ReferenceType="i=47" IsForward="false">ns=2;i=9</Reference>
                    </References>
                  </UAObject>
                """,
                includeExternalNamespace: true);
            UANodeSet parent = ReadNodeSet(
                """
                  <UAObject NodeId="ns=1;i=1" BrowseName="1:Parent">
                    <DisplayName>Parent</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=58</Reference>
                    </References>
                  </UAObject>
                """);

            importer.Import(children);
            importer.Import(parent);
            importer.Complete();
            importer.Complete();

            NodeState parentState = Find(importer, 1);
            var childState = (BaseInstanceState)Find(importer, 2);
            var externalChild = (BaseInstanceState)Find(importer, 3);
            var linkedChildren = new List<BaseInstanceState>();
            parentState.GetChildren(context, linkedChildren);
            var externalNamespaceIndex =
                (ushort)context.NamespaceUris.GetIndex(kExternalNamespaceUri);

            Assert.Multiple(() =>
            {
                Assert.That(childState.Parent, Is.SameAs(parentState));
                Assert.That(
                    childState.ReferenceTypeId,
                    Is.EqualTo(ReferenceTypeIds.HasComponent));
                Assert.That(linkedChildren, Is.EqualTo(new[] { childState }));
                Assert.That(
                    UANodeSet.TryGetUnresolvedParentNodeId(childState, out _),
                    Is.False);
                Assert.That(externalChild.Parent, Is.Null);
                Assert.That(
                    UANodeSet.TryGetUnresolvedParentNodeId(
                        externalChild,
                        out NodeId unresolvedParent),
                    Is.True);
                Assert.That(
                    unresolvedParent,
                    Is.EqualTo(new NodeId(9u, externalNamespaceIndex)));
            });
        }

        [Test]
        public void DeferredLinkingPreservesApplicationHandle()
        {
            SystemContext context = CreateContext();
            var importer = new NodeSetImporter(context, factoryProvider: null);
            UANodeSet nodeSet = ReadNodeSet(
                """
                  <UAObject NodeId="ns=1;i=1" BrowseName="1:Parent">
                    <DisplayName>Parent</DisplayName>
                  </UAObject>
                  <UAObject NodeId="ns=1;i=2" BrowseName="1:Child"
                            ParentNodeId="ns=1;i=1">
                    <DisplayName>Child</DisplayName>
                    <References>
                      <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1</Reference>
                    </References>
                  </UAObject>
                """);

            importer.Import(nodeSet);
            var child = (BaseInstanceState)Find(importer, 2);
            var applicationHandle = new object();
            child.Handle = applicationHandle;
            importer.Complete();

            Assert.Multiple(() =>
            {
                Assert.That(child.Parent, Is.SameAs(Find(importer, 1)));
                Assert.That(child.Handle, Is.SameAs(applicationHandle));
                Assert.That(
                    child.ReferenceTypeId,
                    Is.EqualTo(ReferenceTypeIds.HasComponent));
            });
        }

        [Test]
        public void SameBatchCustomHierarchyRetainsItsReferenceType()
        {
            SystemContext context = CreateContext();
            var importer = new NodeSetImporter(context, factoryProvider: null);
            UANodeSet nodeSet = ReadNodeSet(
                """
                  <UAReferenceType NodeId="ns=1;i=500" BrowseName="1:HasTypedChild">
                    <DisplayName>HasTypedChild</DisplayName>
                    <References>
                      <Reference ReferenceType="i=45" IsForward="false">i=33</Reference>
                    </References>
                  </UAReferenceType>
                  <UAObject NodeId="ns=1;i=1" BrowseName="1:Parent">
                    <DisplayName>Parent</DisplayName>
                  </UAObject>
                  <UAObject NodeId="ns=1;i=2" BrowseName="1:Child"
                            ParentNodeId="ns=1;i=1">
                    <DisplayName>Child</DisplayName>
                    <References>
                      <Reference ReferenceType="ns=1;i=500" IsForward="false">ns=1;i=1</Reference>
                    </References>
                  </UAObject>
                """);

            importer.Import(nodeSet);
            importer.Complete();

            var child = (BaseInstanceState)Find(importer, 2);
            Assert.That(child.ReferenceTypeId, Is.EqualTo(new NodeId(500u, 1)));
        }

        [Test]
        public void TypedGeneratedLikeStateUsesOnlyFlatImportedChildren()
        {
            SystemContext context = CreateContext();
            UANodeSet nodeSet = ReadNodeSet(
                """
                  <UAObject NodeId="ns=1;i=200" BrowseName="1:GeneratedLike">
                    <DisplayName>GeneratedLike</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">ns=1;i=100</Reference>
                      <Reference ReferenceType="i=47">ns=1;i=201</Reference>
                    </References>
                  </UAObject>
                  <UAVariable NodeId="ns=1;i=201" BrowseName="1:MandatoryValue"
                              ParentNodeId="ns=1;i=200" DataType="i=6">
                    <DisplayName>MandatoryValue</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=63</Reference>
                      <Reference ReferenceType="i=47" IsForward="false">ns=1;i=200</Reference>
                    </References>
                  </UAVariable>
                """);
            var importer = new NodeSetImporter(
                context,
                new ManualFactoryProvider(
                    Factory(
                        NodeClass.Object,
                        100,
                        static () => new GeneratedLikeObjectState(null)),
                    new ManualImportFactory(
                        NodeClass.Variable,
                        new ExpandedNodeId(201u, kNamespaceUri),
                        static () => new TypedVariableState(null),
                        NodeSetImportDiscriminator.NodeId)));

            importer.Import(nodeSet);
            importer.Complete();
            var parent = (GeneratedLikeObjectState)Find(importer, 200);
            NodeState importedChild = Find(importer, 201);
            parent.CreateAsPredefinedNode(context);
            var children = new List<BaseInstanceState>();
            parent.GetChildren(context, children);

            Assert.Multiple(() =>
            {
                Assert.That(parent.InitializeCount, Is.Zero);
                Assert.That(parent.MandatoryValue, Is.SameAs(importedChild));
                Assert.That(children, Is.EqualTo(new[] { importedChild }));
            });
        }

        [Test]
        public async Task ExactChildFactoriesPopulateTypedMethodArgumentsAsync()
        {
            SystemContext context = CreateContext();
            UANodeSet nodeSet = ReadNodeSet(
                """
                  <UAMethod NodeId="ns=1;i=202" BrowseName="1:Calculate"
                            MethodDeclarationId="ns=1;i=105">
                    <DisplayName>Calculate</DisplayName>
                  </UAMethod>
                  <UAVariable NodeId="ns=1;i=203" BrowseName="InputArguments"
                              ParentNodeId="ns=1;i=202" DataType="i=296"
                              ValueRank="1">
                    <DisplayName>InputArguments</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=68</Reference>
                      <Reference ReferenceType="i=46" IsForward="false">ns=1;i=202</Reference>
                    </References>
                  </UAVariable>
                  <UAVariable NodeId="ns=1;i=204" BrowseName="OutputArguments"
                              ParentNodeId="ns=1;i=202" DataType="i=296"
                              ValueRank="1">
                    <DisplayName>OutputArguments</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=68</Reference>
                      <Reference ReferenceType="i=46" IsForward="false">ns=1;i=202</Reference>
                    </References>
                  </UAVariable>
                """);
            var importer = new NodeSetImporter(
                context,
                new ManualFactoryProvider(
                    Factory(
                        NodeClass.Method,
                        105,
                        static () => new TypedMethodState(null)),
                    new ManualImportFactory(
                        NodeClass.Variable,
                        new ExpandedNodeId(203u, kNamespaceUri),
                        static () =>
                            PropertyState<ArrayOf<Argument>>
                                .With<StructureBuilder<Argument>>(null),
                        NodeSetImportDiscriminator.NodeId),
                    new ManualImportFactory(
                        NodeClass.Variable,
                        new ExpandedNodeId(204u, kNamespaceUri),
                        static () =>
                            PropertyState<ArrayOf<Argument>>
                                .With<StructureBuilder<Argument>>(null),
                        NodeSetImportDiscriminator.NodeId)));

            importer.Import(nodeSet);
            importer.Complete();

            var method = (TypedMethodState)Find(importer, 202);
            method.InputArguments!.Value =
            [
                new Argument
                {
                    Name = "Value",
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar
                }
            ];
            method.OutputArguments!.Value =
            [
                new Argument
                {
                    Name = "Result",
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar
                }
            ];
            method.OnCallMethod = (_, _, inputArguments, outputArguments) =>
            {
                Assert.That(inputArguments[0].TryGetValue(out int input), Is.True);
                outputArguments[0] = new Variant(input + 1);
                return ServiceResult.Good;
            };
            var argumentErrors = new List<ServiceResult>();
            var output = new List<Variant>();

            ServiceResult result = await method.CallAsync(
                context,
                NodeId.Null,
                [new Variant(41)],
                argumentErrors,
                output).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(method.InputArguments.NodeId, Is.EqualTo(new NodeId(203u, 1)));
                Assert.That(method.OutputArguments.NodeId, Is.EqualTo(new NodeId(204u, 1)));
                Assert.That(method.InputArguments.Parent, Is.SameAs(method));
                Assert.That(method.OutputArguments.Parent, Is.SameAs(method));
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(argumentErrors, Has.Count.EqualTo(1));
                Assert.That(ServiceResult.IsGood(argumentErrors[0]), Is.True);
                Assert.That(output, Has.Count.EqualTo(1));
                Assert.That(output[0].TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(42));
            });
        }

        [Test]
        public void DuplicateNodeIdsAcrossDocumentsAreRejected()
        {
            SystemContext context = CreateContext();
            var importer = new NodeSetImporter(context, factoryProvider: null);
            UANodeSet first = ReadNodeSet(
                """
                  <UAObject NodeId="ns=1;i=1" BrowseName="1:First">
                    <DisplayName>First</DisplayName>
                  </UAObject>
                """);
            UANodeSet second = ReadNodeSet(
                """
                  <UAObject NodeId="ns=1;i=1" BrowseName="1:Second">
                    <DisplayName>Second</DisplayName>
                  </UAObject>
                """);

            importer.Import(first);

            Assert.That(
                () => importer.Import(second),
                Throws.InvalidOperationException.With.Message.Contains("Duplicate NodeId"));
        }

        [Test]
        public void FactoryThatMaterializesChildrenIsRejected()
        {
            SystemContext context = CreateContext();
            UANodeSet nodeSet = ReadNodeSet(
                """
                  <UAObject NodeId="ns=1;i=200" BrowseName="1:GeneratedLike">
                    <DisplayName>GeneratedLike</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">ns=1;i=100</Reference>
                    </References>
                  </UAObject>
                """);
            var importer = new NodeSetImporter(
                context,
                new ManualFactoryProvider(
                    Factory(
                        NodeClass.Object,
                        100,
                        () =>
                        {
                            var state = new GeneratedLikeObjectState(null);
                            state.Create(
                                context,
                                NodeId.Null,
                                new QualifiedName("GeneratedLike", 1),
                                LocalizedText.Null,
                                assignNodeIds: false);
                            return state;
                        })));

            Assert.That(
                () => importer.Import(nodeSet),
                Throws.InvalidOperationException.With.Message.Contains("empty states"));
        }

        private static ManualImportFactory Factory(
            NodeClass nodeClass,
            uint discriminatorId,
            Func<NodeState> create)
        {
            return new ManualImportFactory(
                nodeClass,
                new ExpandedNodeId(discriminatorId, kNamespaceUri),
                create);
        }

        private static NodeState Find(NodeSetImporter importer, uint identifier)
        {
            return importer.ImportedNodes.Single(
                node => HasNumericIdentifier(node, identifier));
        }

        private static bool HasNumericIdentifier(
            NodeState node,
            uint identifier)
        {
            return node.NodeId.TryGetValue(out uint actual) &&
                actual == identifier;
        }

        private static SystemContext CreateContext()
        {
            var namespaceUris = new NamespaceTable();
            return new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = namespaceUris,
                ServerUris = new StringTable(),
                TypeTable = new TypeTable(namespaceUris),
                EncodeableFactory = EncodeableFactory.Create()
            };
        }

        private static UANodeSet ReadNodeSet(
            string nodes,
            bool includeExternalNamespace = false)
        {
            string externalNamespace = includeExternalNamespace
                ? $"    <Uri>{kExternalNamespaceUri}</Uri>\r\n"
                : string.Empty;
            string xml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\"\r\n" +
                "           xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">\r\n" +
                "  <NamespaceUris>\r\n" +
                $"    <Uri>{kNamespaceUri}</Uri>\r\n" +
                externalNamespace +
                "  </NamespaceUris>\r\n" +
                nodes +
                "\r\n</UANodeSet>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            return UANodeSet.Read(stream)!;
        }

        private static void AssertNodeParity(
            ISystemContext genericContext,
            NodeState generic,
            ISystemContext typedContext,
            NodeState typed)
        {
            var genericReferences = new List<IReference>();
            var typedReferences = new List<IReference>();
            generic.GetReferences(genericContext, genericReferences);
            typed.GetReferences(typedContext, typedReferences);
            string[] expectedReferences = genericReferences
                .Select(FormatReference)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            string[] actualReferences = typedReferences
                .Select(FormatReference)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(typed.NodeClass, Is.EqualTo(generic.NodeClass));
                Assert.That(typed.NodeId, Is.EqualTo(generic.NodeId));
                Assert.That(typed.BrowseName, Is.EqualTo(generic.BrowseName));
                Assert.That(typed.DisplayName, Is.EqualTo(generic.DisplayName));
                Assert.That(typed.Description, Is.EqualTo(generic.Description));
                Assert.That(typed.SymbolicName, Is.EqualTo(generic.SymbolicName));
                Assert.That(typed.WriteMask, Is.EqualTo(generic.WriteMask));
                Assert.That(typed.UserWriteMask, Is.EqualTo(generic.UserWriteMask));
                Assert.That(typed.AccessRestrictions, Is.EqualTo(generic.AccessRestrictions));
                Assert.That(typed.ReleaseStatus, Is.EqualTo(generic.ReleaseStatus));
                Assert.That(typed.DesignToolOnly, Is.EqualTo(generic.DesignToolOnly));
                Assert.That(typed.NodeSetDocumentation, Is.EqualTo(generic.NodeSetDocumentation));
                Assert.That(
                    typed.Categories?.ToArray(),
                    Is.EqualTo(generic.Categories?.ToArray()));
                Assert.That(
                    FormatExtensions(typed.Extensions),
                    Is.EqualTo(FormatExtensions(generic.Extensions)));
                Assert.That(
                    FormatRolePermissions(typed.RolePermissions),
                    Is.EqualTo(FormatRolePermissions(generic.RolePermissions)));
                Assert.That(actualReferences, Is.EqualTo(expectedReferences));
            });

            if (generic is BaseInstanceState genericInstance &&
                typed is BaseInstanceState typedInstance)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(
                        typedInstance.TypeDefinitionId,
                        Is.EqualTo(genericInstance.TypeDefinitionId));
                    Assert.That(
                        typedInstance.ModellingRuleId,
                        Is.EqualTo(genericInstance.ModellingRuleId));
                    Assert.That(
                        typedInstance.Parent?.NodeId,
                        Is.EqualTo(genericInstance.Parent?.NodeId));
                });
            }
            if (generic is BaseObjectState genericObject &&
                typed is BaseObjectState typedObject)
            {
                Assert.That(typedObject.EventNotifier, Is.EqualTo(genericObject.EventNotifier));
            }
            if (generic is MethodState genericMethod &&
                typed is MethodState typedMethod)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(typedMethod.Executable, Is.EqualTo(genericMethod.Executable));
                    Assert.That(
                        typedMethod.UserExecutable,
                        Is.EqualTo(genericMethod.UserExecutable));
                    Assert.That(
                        typedMethod.MethodDeclarationId,
                        Is.EqualTo(genericMethod.MethodDeclarationId));
                });
            }
        }

        private static string FormatReference(IReference reference)
        {
            return $"{reference.ReferenceTypeId}|{reference.IsInverse}|{reference.TargetId}";
        }

        private static string[] FormatExtensions(XmlElement[] extensions)
        {
            if (extensions is null)
            {
                return null;
            }

            var formatted = new string[extensions.Length];
            for (int i = 0; i < extensions.Length; i++)
            {
                formatted[i] = extensions[i].OuterXml;
            }
            return formatted;
        }

        private static string[] FormatRolePermissions(
            ArrayOf<RolePermissionType> permissions)
        {
            var formatted = new string[permissions.Count];
            for (int i = 0; i < permissions.Count; i++)
            {
                formatted[i] =
                    $"{permissions[i].RoleId}|{permissions[i].Permissions}";
            }
            return formatted;
        }

        private sealed class ManualFactoryProvider : INodeSetImportFactoryProvider
        {
            public ManualFactoryProvider(params INodeSetImportFactory[] factories)
            {
                m_factories = factories;
            }

            public ArrayOf<INodeSetImportFactory> GetNodeSetImportFactories()
            {
                return m_factories;
            }

            private readonly ArrayOf<INodeSetImportFactory> m_factories;
        }

        private sealed class ManualImportFactory : INodeSetImportFactory
        {
            public ManualImportFactory(
                NodeClass nodeClass,
                ExpandedNodeId discriminatorId,
                Func<NodeState> create,
                NodeSetImportDiscriminator? discriminator = null)
            {
                NodeClass = nodeClass;
                DiscriminatorId = discriminatorId;
                Discriminator = discriminator ??
                    nodeClass switch
                    {
                        NodeClass.Object or NodeClass.Variable =>
                            NodeSetImportDiscriminator.TypeDefinition,
                        NodeClass.Method =>
                            NodeSetImportDiscriminator.MethodDeclaration,
                        _ => NodeSetImportDiscriminator.NodeId
                    };
                m_create = create;
            }

            public NodeClass NodeClass { get; }

            public NodeSetImportDiscriminator Discriminator { get; }

            public ExpandedNodeId DiscriminatorId { get; }

            public NodeState CreateEmptyState()
            {
                return m_create();
            }

            private readonly Func<NodeState> m_create;
        }

        private sealed class TypedObjectState : BaseObjectState
        {
            public TypedObjectState(NodeState parent)
                : base(parent)
            {
            }
        }

        private sealed class TypedVariableState : BaseDataVariableState
        {
            public TypedVariableState(NodeState parent)
                : base(parent)
            {
            }
        }

        private sealed class TypedMethodState : MethodState
        {
            public TypedMethodState(NodeState parent)
                : base(parent)
            {
            }
        }

        private sealed class TypedObjectTypeState : BaseObjectTypeState;

        private sealed class TypedVariableTypeState : BaseDataVariableTypeState;

        private sealed class TypedDataTypeState : DataTypeState;

        private sealed class TypedReferenceTypeState : ReferenceTypeState;

        private sealed class TypedViewState : ViewState;

        private sealed class GeneratedLikeObjectState : BaseObjectState
        {
            public GeneratedLikeObjectState(NodeState parent)
                : base(parent)
            {
            }

            public BaseVariableState MandatoryValue { get; private set; }

            public int InitializeCount { get; private set; }

            protected override void Initialize(ISystemContext context)
            {
                InitializeCount++;
                base.Initialize(context);
                var child = new BaseDataVariableState(this)
                {
                    BrowseName = new QualifiedName("MandatoryValue", 1),
                    DisplayName = new LocalizedText("MandatoryValue"),
                    DataType = DataTypeIds.Int32
                };
                AddChild(child);
            }

            public override void GetChildren(
                ISystemContext context,
                IList<BaseInstanceState> children)
            {
                if (MandatoryValue is not null)
                {
                    children.Add(MandatoryValue);
                }
                base.GetChildren(context, children);
            }

            protected override BaseInstanceState FindChild(
                ISystemContext context,
                QualifiedName browseName,
                bool createOrReplace,
                BaseInstanceState replacement,
                bool assignInstanceNodeIds = true)
            {
                if (browseName.Name == "MandatoryValue")
                {
                    if (createOrReplace)
                    {
                        MandatoryValue = replacement as BaseVariableState ??
                            new BaseDataVariableState(this)
                            {
                                BrowseName = browseName,
                                DisplayName = new LocalizedText(browseName.Name),
                                DataType = DataTypeIds.Int32
                            };
                    }
                    return MandatoryValue;
                }

                return base.FindChild(
                    context,
                    browseName,
                    createOrReplace,
                    replacement,
                    assignInstanceNodeIds);
            }
        }
    }
}
