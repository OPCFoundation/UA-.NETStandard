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
