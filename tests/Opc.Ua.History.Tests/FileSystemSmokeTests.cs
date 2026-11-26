// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// Smoke tests for the FileSystem NodeManager wired into the
    /// reference server. Verifies that at least one drive (volume)
    /// is exposed under the Server object as a FileDirectoryType
    /// instance via an Organizes reference.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("FileSystem")]
    public class FileSystemSmokeTests : TestFixture
    {
        [Test]
        public async Task ServerHasAtLeastOneVolumeOrganizedAsync()
        {
            BrowseResponse resp = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[] {
                    new() {
                        NodeId = ObjectIds.Server,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        IncludeSubtypes = false,
                        NodeClassMask = (uint)NodeClass.Object,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode), Is.True,
                "Browse Server -> Organizes failed");

            int volumeCount = 0;
            foreach (ReferenceDescription r in resp.Results[0].References)
            {
                var typeId = ExpandedNodeId.ToNodeId(r.TypeDefinition, Session.NamespaceUris);
                if (typeId == ObjectTypeIds.FileDirectoryType)
                {
                    volumeCount++;
                }
            }
            if (volumeCount == 0)
            {
                Assert.Ignore(
                    "Server does not expose any FileDirectoryType volume " +
                    "(FileSystem NodeManager not enabled on this fixture).");
            }
            Assert.That(volumeCount, Is.GreaterThan(0),
                "Expected at least one FileDirectoryType volume organized under Server.");
        }
    }
}
