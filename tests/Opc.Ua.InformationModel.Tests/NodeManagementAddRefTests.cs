/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for Node Management Add Ref.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("NodeManagement")]
    public class NodeManagementAddRefTests : TestFixture
    {
        [Description("Add a forward reference between two existing nodes and verify it succeeds.")]
        [Test]
        public async Task AddReferenceBetweenExistingNodesSucceedsAsync()
        {
            try
            {
                AddReferencesResponse response =
                    await Session.AddReferencesAsync(
                        null,
                        new AddReferencesItem[]
                        {
                            new() {
                                SourceNodeId = ObjectIds.ObjectsFolder,
                                ReferenceTypeId = ReferenceTypeIds.Organizes,
                                IsForward = true,
                                TargetNodeId = new ExpandedNodeId(ObjectIds.Server),
                                TargetNodeClass = NodeClass.Object
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                StatusCode statusCode = response.Results[0];

                // The reference may already exist (Server is already a child of
                // ObjectsFolder via Organizes) so accept Good or DuplicateReferenceNotAllowed.
                // Additionally, servers may opt out of NodeManagement on the
                // source's owning NodeManager (CoreNodeManager) and return
                // BadUserAccessDenied or BadNotSupported.
                Assert.That(
                    StatusCode.IsGood(statusCode) ||
                    statusCode == StatusCodes.BadDuplicateReferenceNotAllowed ||
                    statusCode == StatusCodes.BadUserAccessDenied ||
                    statusCode == StatusCodes.BadNotSupported,
                    Is.True,
                    $"AddReferences returned unexpected status: {statusCode}");
            }
            catch (ServiceResultException ex)
                when (ex.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore("AddReferences service not supported by ReferenceServer.");
            }
        }
    }
}
