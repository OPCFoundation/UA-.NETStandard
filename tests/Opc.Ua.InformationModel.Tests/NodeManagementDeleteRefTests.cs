// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for Node Management Delete Ref.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("NodeManagement")]
    public class NodeManagementDeleteRefTests : TestFixture
    {
        [Description("Attempt to delete a reference that does not exist and verify the result is reported per item.")]
        [Test]
        public async Task DeleteNonExistentReferenceReportsResultAsync()
        {
            try
            {
                DeleteReferencesResponse response =
                    await Session.DeleteReferencesAsync(
                        null,
                        new DeleteReferencesItem[]
                        {
                            new() {
                                SourceNodeId = ObjectIds.ObjectsFolder,
                                ReferenceTypeId = ReferenceTypeIds.Organizes,
                                IsForward = true,
                                TargetNodeId =
                                    new ExpandedNodeId(Constants.InvalidNodeId),
                                DeleteBidirectional = false
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
            }
            catch (ServiceResultException ex)
                when (ex.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore(
                    "DeleteReferences service not supported by ReferenceServer.");
            }
        }
    }
}
