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
namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests for batch and service-level operations.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class BatchOperationsAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task BrowseBatchAsync()
        {
            var browseTemplate = new BrowseDescription
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };

            ArrayOf<BrowseDescription> browseDescriptions =
            [
                new BrowseDescription
                {
                    NodeId = ObjectIds.RootFolder,
                    BrowseDirection = browseTemplate.BrowseDirection,
                    ReferenceTypeId = browseTemplate.ReferenceTypeId,
                    IncludeSubtypes = browseTemplate.IncludeSubtypes,
                    NodeClassMask = browseTemplate.NodeClassMask,
                    ResultMask = browseTemplate.ResultMask
                },
                new BrowseDescription
                {
                    NodeId = ObjectIds.ObjectsFolder,
                    BrowseDirection = browseTemplate.BrowseDirection,
                    ReferenceTypeId = browseTemplate.ReferenceTypeId,
                    IncludeSubtypes = browseTemplate.IncludeSubtypes,
                    NodeClassMask = browseTemplate.NodeClassMask,
                    ResultMask = browseTemplate.ResultMask
                },
                new BrowseDescription
                {
                    NodeId = ObjectIds.TypesFolder,
                    BrowseDirection = browseTemplate.BrowseDirection,
                    ReferenceTypeId = browseTemplate.ReferenceTypeId,
                    IncludeSubtypes = browseTemplate.IncludeSubtypes,
                    NodeClassMask = browseTemplate.NodeClassMask,
                    ResultMask = browseTemplate.ResultMask
                }
            ];

            BrowseResponse response = await fixture.Session!.BrowseAsync(
                null, null, 100, browseDescriptions,
                CancellationToken.None).ConfigureAwait(false);

            await Assert.That(response.Results.Count)
                .IsEqualTo(browseDescriptions.Count);

            foreach (BrowseResult result in response.Results.ToList())
            {
                await Assert.That(StatusCode.IsGood(result.StatusCode))
                    .IsTrue();
                await Assert.That(result.References.Count)
                    .IsGreaterThan(0);
            }
        }

        [Test]
        public async Task TranslateBrowsePathsAsync()
        {
            var browsePaths = new List<BrowsePath>
            {
                new() {
                    StartingNode = ObjectIds.RootFolder,
                    RelativePath = new RelativePath(
                        QualifiedName.From("Objects"))
                },
                new() {
                    StartingNode = ObjectIds.RootFolder,
                    RelativePath = new RelativePath(
                        QualifiedName.From("Types"))
                },
                new() {
                    StartingNode = ObjectIds.RootFolder,
                    RelativePath = new RelativePath(
                        QualifiedName.From("Views"))
                }
            };

            TranslateBrowsePathsToNodeIdsResponse response =
                await fixture.Session!.TranslateBrowsePathsToNodeIdsAsync(
                    null, browsePaths.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            await Assert.That(response.Results.Count)
                .IsEqualTo(browsePaths.Count);

            foreach (BrowsePathResult result in response.Results.ToList())
            {
                await Assert.That(StatusCode.IsGood(result.StatusCode))
                    .IsTrue();
                await Assert.That(result.Targets.Count)
                    .IsGreaterThan(0);
            }
        }

        [Test]
        public async Task ReadBatchAsync()
        {
            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId
                {
                    NodeId = VariableIds.Server_ServerStatus,
                    AttributeId = Attributes.Value
                },
                new ReadValueId
                {
                    NodeId = VariableIds.Server_ServerStatus_State,
                    AttributeId = Attributes.Value
                },
                new ReadValueId
                {
                    NodeId = VariableIds.Server_ServerStatus_StartTime,
                    AttributeId = Attributes.Value
                },
                new ReadValueId
                {
                    NodeId = VariableIds.Server_NamespaceArray,
                    AttributeId = Attributes.Value
                }
            ];

            ReadResponse response = await fixture.Session!.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                nodesToRead, CancellationToken.None).ConfigureAwait(false);

            await Assert.That(response.Results.Count)
                .IsEqualTo(nodesToRead.Count);

            foreach (DataValue result in response.Results.ToList())
            {
                await Assert.That(StatusCode.IsGood(result.StatusCode))
                    .IsTrue();
            }
        }
    }
}
