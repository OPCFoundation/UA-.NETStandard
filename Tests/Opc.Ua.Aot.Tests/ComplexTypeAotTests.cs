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

using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// The test data namespace URI for custom types from the reference server.
    /// </summary>
    internal static class TestDataConstants
    {
        public const string TestDataNamespaceUri = "http://test.org/UA/Data/";
    }

    /// <summary>
    /// AOT integration tests for the DefaultComplexTypeSystem.
    /// Validates that complex type loading, browsing, and reading
    /// works correctly when published as NativeAOT (no Reflection.Emit).
    /// Uses the TestData node manager types which are not pre-generated
    /// and must be loaded dynamically via the complex type system.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class ComplexTypeAotTests(AotTestFixture fixture)
    {
        /// <summary>
        /// Load the complex type system using DefaultComplexTypeFactory
        /// and verify that custom types from the TestData namespace
        /// are loaded into the session factory.
        /// </summary>
        [Test]
        public async Task LoadComplexTypeSystemAsync()
        {
            var complexTypeSystem = new ComplexTypeSystem(
                fixture.Session, fixture.Telemetry);

            bool success = await complexTypeSystem
                .LoadAsync(false, true, CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(success).IsTrue();

            int testNsIndex = fixture.Session.NamespaceUris
                .GetIndex(TestDataConstants.TestDataNamespaceUri);
            await Assert.That(testNsIndex).IsGreaterThan(0)
                .Because("TestData namespace must be registered in the server");

            // Verify the session factory has encodeable types registered
            // for the TestData namespace. Use TryGetEncodeableType to check
            // that at least one DataType from the test namespace is resolvable.
            // We browse for all DataType nodes in the server and check which
            // ones from the test namespace have been registered.
            ArrayOf<ReferenceDescription> allRefs =
                await AotClientSamples.BrowseFullAddressSpaceAsync(
                    fixture.Session, ObjectIds.RootFolder)
                .ConfigureAwait(false);

            int testDataTypesFound = 0;
            List<ReferenceDescription> dataTypeRefs = allRefs
                .Filter(r => r.NodeClass == NodeClass.DataType)
                .ToList();

            foreach (ReferenceDescription dtRef in dataTypeRefs)
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(
                    dtRef.NodeId, fixture.Session.NamespaceUris);
                if (nodeId.NamespaceIndex == testNsIndex)
                {
                    ExpandedNodeId expandedId = NodeId.ToExpandedNodeId(
                        nodeId, fixture.Session.NamespaceUris);
                    Type systemType = fixture.Session.Factory.GetSystemType(expandedId);
                    if (systemType != null)
                    {
                        testDataTypesFound++;
                    }
                }
            }

            await Assert.That(testDataTypesFound).IsGreaterThan(0)
                .Because(
                    "At least one TestData structure type should be " +
                    "registered in the session factory after loading");
        }

        /// <summary>
        /// Load complex types, browse the full address space, read all
        /// variable values, and verify no decoding errors occur.
        /// Mirrors BrowseComplexTypesServerAsync from the integration tests.
        /// </summary>
        [Test]
        public async Task BrowseAndReadComplexTypesAsync()
        {
            var complexTypeSystem = new ComplexTypeSystem(
                fixture.Session, fixture.Telemetry);

            await complexTypeSystem
                .LoadAsync(false, true, CancellationToken.None)
                .ConfigureAwait(false);

            ArrayOf<ReferenceDescription> referenceDescriptions =
                await AotClientSamples.BrowseFullAddressSpaceAsync(
                    fixture.Session, ObjectIds.RootFolder)
                .ConfigureAwait(false);

            await Assert.That(referenceDescriptions.Count).IsGreaterThan(0);

            ArrayOf<NodeId> variableIds =
                referenceDescriptions
                    .Filter(r => r.NodeClass == NodeClass.Variable)
                    .ConvertAll(r => ExpandedNodeId.ToNodeId(
                        r.NodeId, fixture.Session.NamespaceUris));

            await Assert.That(variableIds.Count).IsGreaterThan(0);

            // Read variables in batches to avoid exceeding message size limits
            const int batchSize = 500;
            for (int batch = 0; batch < variableIds.Count; batch += batchSize)
            {
                int count = Math.Min(batchSize, variableIds.Count - batch);
                ArrayOf<NodeId> batchIds = variableIds[batch..(batch + count)];

                (ArrayOf<DataValue> values, ArrayOf<ServiceResult> serviceResults) =
                    await fixture.Session.ReadValuesAsync(
                        batchIds, CancellationToken.None)
                    .ConfigureAwait(false);

                for (int ii = 0; ii < serviceResults.Count; ii++)
                {
                    ServiceResult serviceResult = serviceResults[ii];
                    await Assert.That(
                        ServiceResult.IsGood(serviceResult) ||
                        serviceResult.StatusCode == StatusCodes.BadNotReadable ||
                        serviceResult.StatusCode == StatusCodes.BadUserAccessDenied ||
                        serviceResult.StatusCode == StatusCodes.BadSecurityModeInsufficient)
                        .IsTrue()
                        .Because($"Variable {batchIds[ii]} returned {serviceResult}");
                }
            }
        }

        /// <summary>
        /// Load complex types, read a TestData structure variable, and
        /// verify the decoded value implements IStructure with accessible
        /// properties. Uses a variable from the TestData node manager
        /// whose type requires the complex type system to decode.
        /// </summary>
        [Test]
        public async Task ReadTestDataComplexTypeVariableAsync()
        {
            var complexTypeSystem = new ComplexTypeSystem(
                fixture.Session, fixture.Telemetry);

            await complexTypeSystem
                .LoadAsync(false, true, CancellationToken.None)
                .ConfigureAwait(false);

            int testNsIndex = fixture.Session.NamespaceUris
                .GetIndex(TestDataConstants.TestDataNamespaceUri);
            await Assert.That(testNsIndex).IsGreaterThan(0);

            // Browse all variables in the address space
            ArrayOf<ReferenceDescription> refs =
                await AotClientSamples.BrowseFullAddressSpaceAsync(
                    fixture.Session, ObjectIds.RootFolder)
                .ConfigureAwait(false);

            // Collect variable NodeIds in the test namespace
            List<NodeId> candidateVarIds = refs
                .Filter(r =>
                    r.NodeClass == NodeClass.Variable &&
                    ExpandedNodeId.ToNodeId(
                        r.NodeId, fixture.Session.NamespaceUris)
                        .NamespaceIndex == testNsIndex)
                .ConvertAll(r => ExpandedNodeId.ToNodeId(
                    r.NodeId, fixture.Session.NamespaceUris))
                .ToList();

            // Read all candidate variables and find one that decodes
            // as an IStructure (skip access-restricted nodes)
            NodeId structureVariableId = default;
            foreach (NodeId varNodeId in candidateVarIds)
            {
                try
                {
                    DataValue dv = await fixture.Session.ReadValueAsync(
                        varNodeId, CancellationToken.None).ConfigureAwait(false);
                    if (StatusCode.IsGood(dv.StatusCode) &&
                        dv.WrappedValue.TryGetValue(out ExtensionObject eo) &&
                        eo.TryGetValue(out IEncodeable _))
                    {
                        structureVariableId = varNodeId;
                        break;
                    }
                }
                catch (ServiceResultException)
                {
                    // skip inaccessible nodes
                }
            }

            await Assert.That(structureVariableId.IsNull).IsFalse()
                .Because(
                    "Should find a variable in the TestData namespace " +
                    "that decodes as a complex type");

            // Read again and fully validate
            DataValue dataValue = await fixture.Session.ReadValueAsync(
                structureVariableId, CancellationToken.None).ConfigureAwait(false);

            await Assert.That(StatusCode.IsGood(dataValue.StatusCode)).IsTrue();

            bool hasExtensionObject = dataValue.WrappedValue
                .TryGetValue(out ExtensionObject extensionObject);
            await Assert.That(hasExtensionObject).IsTrue()
                .Because("TestData structure variable should decode as ExtensionObject");

            bool hasEncodeable = extensionObject
                .TryGetValue(out IEncodeable encodeable);
            await Assert.That(hasEncodeable).IsTrue()
                .Because(
                    "ExtensionObject should contain a decoded IEncodeable, " +
                    "not raw bytes");

            IStructure complexType = encodeable as IStructure;
            await Assert.That(complexType).IsNotNull()
                .Because("Decoded type should implement IStructure");
            await Assert.That(complexType.GetFields().Count).IsGreaterThan(0);
        }
    }
}
