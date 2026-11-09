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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.NodeSets;
using UaLens.NodeSets.Loading;

namespace UaLens.Tests.NodeSets
{
    [TestFixture]
    [Explicit("Downloads the DI model from the public OPCFoundation/UA-Nodeset repository.")]
    [Category("LensNodeSetsNetwork")]
    public sealed class NodeSetNetworkTests
    {
        [Test]
        public async Task DownloadsAnOfficialCompanionAndBuildsItsAddressSpace()
        {
            string directory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, "nodeset-network-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "machine.xml");
                await File.WriteAllTextAsync(path, """
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                  <NamespaceUris><Uri>urn:lens:network-test</Uri><Uri>http://opcfoundation.org/UA/DI/</Uri></NamespaceUris>
                  <Models><Model ModelUri="urn:lens:network-test">
                    <RequiredModel ModelUri="http://opcfoundation.org/UA/DI/" />
                  </Model></Models>
                  <UAObject NodeId="ns=1;i=1" BrowseName="1:Machine">
                    <DisplayName>Machine</DisplayName>
                    <References>
                      <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
                      <Reference ReferenceType="i=40">ns=2;i=1002</Reference>
                    </References>
                  </UAObject>
                </UANodeSet>
                """).ConfigureAwait(false);
                using var repository = new UaNodeSetRepository();
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                ArrayOf<NodeSetDocument> documents = await new NodeSetLoader().LoadAsync(
                    [path], new RepositoryResolver(repository), cancellation.Token).ConfigureAwait(false);
                NodeSetDocument downloaded = documents.ToList().Single(document =>
                    document.NodeSet.Models?.Any(model => model.ModelUri == "http://opcfoundation.org/UA/DI/") == true);
                Assert.That(downloaded.Source, Does.StartWith(
                    "https://raw.githubusercontent.com/OPCFoundation/UA-Nodeset/"));
                NodeSetAddressSpace graph = await new NodeSetAddressSpaceFactory(NodeSetAddressSpaceTests.Telemetry())
                    .CreateAsync(documents, cancellation.Token).ConfigureAwait(false);
                Assert.That(graph.Nodes.Find(node => node.BrowseName.Name == "DeviceType"), Is.Not.Null);
                ushort ns = graph.NamespaceUris.GetIndexOrAppend("urn:lens:network-test");
                BrowseResponse result = await graph.BrowseAsync(
                    [new BrowseDescription
                    {
                        NodeId = new NodeId(1, ns),
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition
                    }], cancellation.Token).ConfigureAwait(false);
                Assert.That(result.Results[0].References[0].BrowseName.Name, Is.EqualTo("DeviceType"));
                TestContext.Progress.WriteLine(
                    $"Imported {graph.Nodes.Count} nodes from {documents.Count} documents; DI source: {downloaded.Source}");
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private sealed class RepositoryResolver(INodeSetRepository repository) : INodeSetDependencyResolver
        {
            public Task<NodeSetDocument?> ResolveAsync(NodeSetRequirement requirement, CancellationToken cancellationToken)
            {
                return repository.FindAsync(requirement, cancellationToken);
            }
        }
    }
}
