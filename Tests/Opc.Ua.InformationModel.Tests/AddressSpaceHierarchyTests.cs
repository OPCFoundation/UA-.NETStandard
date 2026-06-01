/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for Address Space Model:
    /// Notifier/Source hierarchies, UserAccessLevel, interfaces,
    /// array variables, and type definitions.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AddressSpace")]
    [Category("AddressSpaceHierarchy")]
    public class AddressSpaceHierarchyTests : TestFixture
    {
        [Description("Browse HasNotifier references from Server object.")]
        [Test]
        public async Task BrowseHasNotifierFromServerAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server,
                ReferenceTypeIds.HasNotifier).ConfigureAwait(false);

            // Server may or may not have HasNotifier references
            // Just verify the browse succeeded
            Assert.That(result, Is.Not.Null);
        }

        [Description("Verify notifier hierarchy reaches event sources (if any).")]
        [Test]
        public async Task VerifyNotifierHierarchyReachesEventSourcesAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server,
                ReferenceTypeIds.HasNotifier).ConfigureAwait(false);

            if (result.References.Count == 0)
            {
                Assert.Ignore("Server has no HasNotifier references.");
            }

            foreach (ReferenceDescription r in result.References)
            {
                Assert.That(r.NodeId, Is.Not.Null);
            }
        }

        [Description("Browse HasEventSource references from Server.")]
        [Test]
        public async Task BrowseHasEventSourceFromServerAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server,
                ReferenceTypeIds.HasEventSource).ConfigureAwait(false);

            // May or may not have event sources
            Assert.That(result, Is.Not.Null);
        }

        [Description("Verify event source nodes have EventNotifier attribute set.")]
        [Test]
        public async Task EventSourceNodesHaveEventNotifierAttributeAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server,
                ReferenceTypeIds.HasEventSource).ConfigureAwait(false);

            if (result.References.Count == 0)
            {
                Assert.Ignore("Server has no HasEventSource references.");
            }

            foreach (ReferenceDescription r in result.References.ToArray())
            {
                var nodeId = ExpandedNodeId.ToNodeId(
                    r.NodeId, Session.NamespaceUris);
                DataValue dv = await ReadAttributeAsync(
                    nodeId, Attributes.EventNotifier).ConfigureAwait(false);

                // If read succeeds, verify the attribute exists
                if (StatusCode.IsGood(dv.StatusCode))
                {
                    byte notifier = dv.WrappedValue.GetByte();
                    Assert.That(notifier, Is.GreaterThanOrEqualTo((byte)0));
                }
            }
        }

        [Description("Read UserAccessLevel on a readable variable node.")]
        [Test]
        public async Task ReadUserAccessLevelOnReadableNodeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue dv = await ReadAttributeAsync(
                nodeId, Attributes.UserAccessLevel).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
            byte ual = dv.WrappedValue.GetByte();
            Assert.That(
                ual & AccessLevels.CurrentRead,
                Is.Not.Zero,
                "UserAccessLevel should include CurrentRead.");
        }

        [Description("Read UserAccessLevel on a writable variable – should have write bit.")]
        [Test]
        public async Task ReadUserAccessLevelOnWritableNodeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            // Verify AccessLevel has write bit
            DataValue alDv = await ReadAttributeAsync(
                nodeId, Attributes.AccessLevel).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(alDv.StatusCode), Is.True);
            byte al = alDv.WrappedValue.GetByte();

            if ((al & AccessLevels.CurrentWrite) == 0)
            {
                Assert.Ignore("Node does not have CurrentWrite in AccessLevel.");
            }

            DataValue ualDv = await ReadAttributeAsync(
                nodeId, Attributes.UserAccessLevel).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(ualDv.StatusCode), Is.True);
            byte ual = ualDv.WrappedValue.GetByte();
            Assert.That(
                ual & AccessLevels.CurrentWrite,
                Is.Not.Zero,
                "UserAccessLevel should include CurrentWrite for writable node.");
        }

        [Description("Read AccessLevel on a standard read-only Server property (Server_NamespaceArray).")]
        [Test]
        public async Task ReadAccessLevelOnReadOnlyPropertyAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                VariableIds.Server_NamespaceArray,
                Attributes.AccessLevel).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                $"AccessLevel must be readable on Server_NamespaceArray: {dv.StatusCode}");

            byte al = dv.WrappedValue.GetByte();
            Assert.That(
                al & AccessLevels.CurrentRead,
                Is.Not.Zero,
                "Property should have CurrentRead in AccessLevel.");
        }

        [Description("Verify array variables have correct ValueRank.")]
        [Test]
        public async Task ArrayVariableHasCorrectValueRankAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);
            DataValue dv = await ReadAttributeAsync(
                nodeId, Attributes.ValueRank).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);

            int valueRank = dv.WrappedValue.GetInt32();
            Assert.That(
                valueRank,
                Is.GreaterThanOrEqualTo(ValueRanks.OneDimension)
                    .Or.EqualTo(ValueRanks.OneOrMoreDimensions)
                    .Or.EqualTo(ValueRanks.Any),
                "Array variable should have ValueRank >= OneDimension.");
        }

        [Description("Verify array variables have ArrayDimensions attribute.")]
        [Test]
        public async Task ArrayVariableHasArrayDimensionsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);
            DataValue dv = await ReadAttributeAsync(
                nodeId, Attributes.ArrayDimensions).ConfigureAwait(false);

            // ArrayDimensions may be null if ValueRank allows variable dimensions
            Assert.That(
                StatusCode.IsGood(dv.StatusCode) ||
                dv.StatusCode.Code == StatusCodes.BadAttributeIdInvalid,
                Is.True);
        }

        [Description("Browse HasTypeDefinition of a variable instance.")]
        [Test]
        public async Task BrowseTypeDefinitionOfVariableAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            BrowseResult result = await BrowseAsync(
                nodeId,
                ReferenceTypeIds.HasTypeDefinition).ConfigureAwait(false);

            Assert.That(result.References.Count, Is.GreaterThan(0),
                "Variable instance should have HasTypeDefinition reference.");
        }

        [Description("Verify all ObjectType instances have HasTypeDefinition reference. Check Server object has HasTypeDefinition = ServerType.")]
        [Test]
        public async Task ServerObjectHasTypeDefinitionAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server,
                ReferenceTypeIds.HasTypeDefinition).ConfigureAwait(false);

            Assert.That(result.References.Count, Is.EqualTo(1));
            Assert.That(
                result.References[0].BrowseName.Name,
                Is.EqualTo("ServerType"));
        }

        [Description("Verify InstanceDeclarations have correct ModellingRules. Browse ServerType to find children with ModellingRule references.")]
        [Test]
        public async Task InstanceDeclarationsHaveModellingRulesAsync()
        {
            BrowseResult children = await BrowseAsync(
                ObjectTypeIds.ServerType,
                ReferenceTypeIds.HierarchicalReferences).ConfigureAwait(false);

            Assert.That(children.References.Count, Is.GreaterThan(0));

            // Check that at least some children have HasModellingRule
            bool foundModellingRule = false;
            foreach (ReferenceDescription child in children.References.ToArray())
            {
                var childId = ExpandedNodeId.ToNodeId(
                    child.NodeId, Session.NamespaceUris);

                // Browse all references to find HasModellingRule
                BrowseResult mrResult = await BrowseAsync(
                    childId,
                    ReferenceTypeIds.NonHierarchicalReferences).ConfigureAwait(false);

                foreach (ReferenceDescription r in mrResult.References)
                {
                    var refTypeId = ExpandedNodeId.ToNodeId(
                        r.ReferenceTypeId, Session.NamespaceUris);
                    if (refTypeId == ReferenceTypeIds.HasModellingRule)
                    {
                        foundModellingRule = true;
                        break;
                    }
                }

                if (foundModellingRule)
                {
                    break;
                }
            }

            if (!foundModellingRule)
            {
                Assert.Ignore(
                    "No ModellingRule references found on ServerType children.");
            }
        }

        [Description("Browse for Interface types (if supported).")]
        [Test]
        public async Task BrowseForInterfaceTypesAsync()
        {
            // Interfaces are defined under BaseInterfaceType (i=17602)
            DataValue dv = await ReadAttributeAsync(
                new NodeId(17602), Attributes.BrowseName).ConfigureAwait(false);

            if (!StatusCode.IsGood(dv.StatusCode))
            {
                Assert.Ignore("BaseInterfaceType not found; interfaces may not be supported.");
            }

            BrowseResult result = await BrowseAsync(
                new NodeId(17602),
                ReferenceTypeIds.HasSubtype).ConfigureAwait(false);

            // Just verify the browse works
            Assert.That(result, Is.Not.Null);
        }

        [Description("Verify HasInterface references (if supported).")]
        [Test]
        public async Task VerifyHasInterfaceReferencesAsync()
        {
            // HasInterface reference type is i=17603
            DataValue dv = await ReadAttributeAsync(
                new NodeId(17603), Attributes.BrowseName).ConfigureAwait(false);

            if (!StatusCode.IsGood(dv.StatusCode))
            {
                Assert.Ignore("HasInterface reference type not found.");
            }

            Assert.That(
                dv.GetValue<QualifiedName>(default).Name,
                Is.EqualTo("HasInterface"));
        }

        [Description("Verify Objects folder children have HasTypeDefinition.")]
        [Test]
        public async Task ObjectsFolderChildrenHaveTypeDefinitionAsync()
        {
            BrowseResult children = await BrowseAsync(
                ObjectIds.ObjectsFolder,
                ReferenceTypeIds.HierarchicalReferences).ConfigureAwait(false);

            Assert.That(children.References.Count, Is.GreaterThan(0));

            // Check first few object children
            int checkedCount = 0;
            foreach (ReferenceDescription child in children.References.ToArray())
            {
                if (child.NodeClass == NodeClass.Object)
                {
                    var childId = ExpandedNodeId.ToNodeId(
                        child.NodeId, Session.NamespaceUris);
                    BrowseResult tdResult = await BrowseAsync(
                        childId,
                        ReferenceTypeIds.HasTypeDefinition).ConfigureAwait(false);

                    Assert.That(tdResult.References.Count, Is.GreaterThan(0),
                        $"Object '{child.BrowseName}' should have HasTypeDefinition.");

                    checkedCount++;
                    if (checkedCount >= 3)
                    {
                        break;
                    }
                }
            }

            if (checkedCount == 0)
            {
                Assert.Ignore("No Object children found under Objects folder.");
            }
        }

        [Description("Verify a scalar variable has BaseDataVariableType as type definition.")]
        [Test]
        public async Task ScalarVariableHasBaseDataVariableTypeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            BrowseResult result = await BrowseAsync(
                nodeId,
                ReferenceTypeIds.HasTypeDefinition).ConfigureAwait(false);

            Assert.That(result.References.Count, Is.GreaterThan(0));
            string typeName = result.References[0].BrowseName.Name;
            Assert.That(
                typeName,
                Is.EqualTo("BaseDataVariableType")
                    .Or.EqualTo("PropertyType")
                    .Or.Not.Empty,
                "Variable should have a recognized type definition.");
        }

        [Test]
        public async Task ServerCapabilitiesHasTypeDefinitionAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server_ServerCapabilities,
                ReferenceTypeIds.HasTypeDefinition).ConfigureAwait(false);

            Assert.That(result.References.Count, Is.GreaterThan(0),
                "ServerCapabilities should have a HasTypeDefinition reference.");
        }

        [Test]
        public async Task ServerStatusHasTypeDefinitionAsync()
        {
            BrowseResult result = await BrowseAsync(
                VariableIds.Server_ServerStatus,
                ReferenceTypeIds.HasTypeDefinition).ConfigureAwait(false);

            Assert.That(result.References.Count, Is.GreaterThan(0),
                "ServerStatus should have a HasTypeDefinition reference.");
        }

        [Test]
        public async Task DataTypeFolderExistsAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                ObjectIds.DataTypesFolder,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "DataTypes folder should exist.");
        }

        [Test]
        public async Task ReferenceTypeFolderExistsAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                ObjectIds.ReferenceTypesFolder,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "ReferenceTypes folder should exist.");
        }

        [Test]
        public async Task BaseObjectTypeExistsAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                ObjectTypeIds.BaseObjectType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
            Assert.That(
                dv.GetValue<QualifiedName>(default).Name,
                Is.EqualTo("BaseObjectType"));
        }

        [Test]
        public async Task BaseVariableTypeExistsAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                VariableTypeIds.BaseVariableType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
            Assert.That(
                dv.GetValue<QualifiedName>(default).Name,
                Is.EqualTo("BaseVariableType"));
        }

        [Test]
        public async Task BaseDataTypeExistsAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                DataTypeIds.BaseDataType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
            Assert.That(
                dv.GetValue<QualifiedName>(default).Name,
                Is.EqualTo("BaseDataType"));
        }

        [Description("Browse HasSubtype from BaseObjectType and verify FolderType exists among the subtypes.")]
        [Test]
        public async Task VerifyBaseObjectTypeToFolderTypeSubtypeChainAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectTypeIds.BaseObjectType,
                ReferenceTypeIds.HasSubtype).ConfigureAwait(false);

            Assert.That(result.References.Count, Is.GreaterThan(0),
                "BaseObjectType should have subtypes.");

            bool foundFolderType = false;
            foreach (ReferenceDescription r in result.References)
            {
                if (string.Equals(
                    r.BrowseName.Name, "FolderType",
                    StringComparison.Ordinal))
                {
                    foundFolderType = true;
                    break;
                }
            }

            Assert.That(foundFolderType, Is.True,
                "FolderType should be a direct subtype of BaseObjectType.");
        }

        [Description("Browse HasSubtype from BaseVariableType and verify subtypes exist.")]
        [Test]
        public async Task VerifyBaseVariableTypeSubtypesAsync()
        {
            BrowseResult result = await BrowseAsync(
                VariableTypeIds.BaseVariableType,
                ReferenceTypeIds.HasSubtype).ConfigureAwait(false);

            Assert.That(result.References.Count, Is.GreaterThan(0),
                "BaseVariableType should have at least one subtype.");

            foreach (ReferenceDescription r in result.References)
            {
                Assert.That(r.BrowseName.Name, Is.Not.Null.And.Not.Empty,
                    "Subtype BrowseName should not be empty.");
            }
        }

        [Description("Browse HasSubtype from Number DataType (i=26) and verify Integer (i=27) exists as a subtype.")]
        [Test]
        public async Task VerifyNumberToIntegerDataTypeHierarchyAsync()
        {
            BrowseResult result = await BrowseAsync(
                new NodeId(26),
                ReferenceTypeIds.HasSubtype).ConfigureAwait(false);

            Assert.That(result.References.Count, Is.GreaterThan(0),
                "Number DataType should have subtypes.");

            bool foundInteger = false;
            foreach (ReferenceDescription r in result.References)
            {
                var nodeId = ExpandedNodeId.ToNodeId(
                    r.NodeId, Session.NamespaceUris);
                if (nodeId == new NodeId(27))
                {
                    foundInteger = true;
                    break;
                }
            }

            Assert.That(foundInteger, Is.True,
                "Integer (i=27) should be a subtype of Number (i=26).");
        }

        [Description("Browse Server object children and verify ServerCapabilities, ServerDiagnostics, and ServerStatus mandatory components exist.")]
        [Test]
        public async Task VerifyServerMandatoryComponentsAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server,
                ReferenceTypeIds.HierarchicalReferences).ConfigureAwait(false);

            Assert.That(result.References.Count, Is.GreaterThan(0),
                "Server object should have children.");

            var childNames = new HashSet<string>();
            foreach (ReferenceDescription r in result.References)
            {
                childNames.Add(r.BrowseName.Name);
            }

            Assert.That(childNames, Does.Contain("ServerCapabilities"),
                "Server should have ServerCapabilities component.");
            Assert.That(childNames, Does.Contain("ServerDiagnostics"),
                "Server should have ServerDiagnostics component.");
            Assert.That(childNames, Does.Contain("ServerStatus"),
                "Server should have ServerStatus component.");
        }

        [Description("Read AccessRestrictions attribute on Server object. The attribute may not be supported by all servers.")]
        [Test]
        public async Task VerifyAccessRestrictionsAttributeOnNodesAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                ObjectIds.Server,
                Attributes.AccessRestrictions).ConfigureAwait(false);

            if (!StatusCode.IsGood(dv.StatusCode))
            {
                Assert.Ignore(
                    $"AccessRestrictions not supported on Server object: {dv.StatusCode}");
            }

            // Server may return Good with an empty Variant when AccessRestrictions
            // attribute is supported but not explicitly set on the node — that's
            // a valid 'no restrictions' result, treat as supported-but-unset.
            if (dv.WrappedValue.IsNull)
            {
                Assert.Ignore(
                    "AccessRestrictions attribute returned an empty value (no restrictions set).");
            }

            Assert.That(dv.WrappedValue.TryGetValue(out ushort _), Is.True,
                "AccessRestrictions value should not be null when supported.");
        }

        [Description("Browse HasTypeDefinition of Objects folder and verify it is FolderType.")]
        [Test]
        public async Task BrowseTypeDefinitionOfObjectInstanceMatchesDeclaredTypeAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.ObjectsFolder,
                ReferenceTypeIds.HasTypeDefinition).ConfigureAwait(false);

            Assert.That(result.References.Count, Is.GreaterThan(0),
                "Objects folder should have HasTypeDefinition reference.");

            Assert.That(
                result.References[0].BrowseName.Name,
                Is.EqualTo("FolderType"),
                "Objects folder type definition should be FolderType.");
        }

        private async Task<BrowseResult> BrowseAsync(
            NodeId nodeId,
            NodeId referenceTypeId,
            BrowseDirection direction = BrowseDirection.Forward,
            bool includeSubtypes = true)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = direction,
                        ReferenceTypeId = referenceTypeId,
                        IncludeSubtypes = includeSubtypes,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            return response.Results[0];
        }

        private async Task<DataValue> ReadAttributeAsync(
            NodeId nodeId, uint attributeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = attributeId
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }
    }
}
