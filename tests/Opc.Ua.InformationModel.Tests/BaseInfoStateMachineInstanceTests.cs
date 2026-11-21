// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// Compliance tests for Base Information state machine instances.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("BaseInfoStateMachineInstance")]
    public class BaseInfoStateMachineInstanceTests : TestFixture
    {
        [Test]
        public async Task StateMachineTypeHasGeneratesEventReferenceAsync()
        {
            // Issue #3720: CTT walks the StateMachineType hierarchy for a
            // forward GeneratesEvent reference with IncludeSubtypes=true.
            foreach (NodeId typeId in (NodeId[])[ObjectTypeIds.StateMachineType, ObjectTypeIds.FiniteStateMachineType])
            {
                List<ReferenceDescription> references = await BrowseReferencesAsync(
                    typeId,
                    ReferenceTypeIds.GeneratesEvent,
                    BrowseDirection.Forward,
                    includeSubtypes: true,
                    (uint)NodeClass.ObjectType).ConfigureAwait(false);

                Assert.That(
                    references,
                    Is.Not.Empty,
                    $"Type {typeId} should declare at least one GeneratesEvent forward reference per " +
                    "Part 16 §4.4.5.");

                foreach (ReferenceDescription reference in references)
                {
                    var targetId = ExpandedNodeId.ToNodeId(
                        reference.NodeId,
                        Session.NamespaceUris);
                    Assert.That(targetId.IsNull, Is.False,
                        $"GeneratesEvent target for {typeId} should resolve to a local event type.");
                    Assert.That(
                        await IsSubtypeOfAsync(
                            targetId,
                            ObjectTypeIds.BaseEventType).ConfigureAwait(false),
                        Is.True,
                        $"GeneratesEvent target {targetId} for {typeId} should be BaseEventType or a subtype.");
                }
            }
        }

        private async Task<List<ReferenceDescription>> BrowseReferencesAsync(
            NodeId nodeId,
            NodeId referenceTypeId,
            BrowseDirection direction,
            bool includeSubtypes,
            uint nodeClassMask)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = direction,
                        ReferenceTypeId = referenceTypeId,
                        IncludeSubtypes = includeSubtypes,
                        NodeClassMask = nodeClassMask,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            var refs = new List<ReferenceDescription>();
            if (response.Results[0].References != default!)
            {
                foreach (ReferenceDescription reference in response.Results[0].References)
                {
                    refs.Add(reference);
                }
            }

            return refs;
        }

        private async Task<bool> IsSubtypeOfAsync(NodeId typeId, NodeId baseTypeId)
        {
            if (typeId == baseTypeId)
            {
                return true;
            }

            NodeId currentTypeId = typeId;
            const int maxDepth = 32;

            for (int ii = 0; ii < maxDepth; ii++)
            {
                List<ReferenceDescription> parents = await BrowseReferencesAsync(
                    currentTypeId,
                    ReferenceTypeIds.HasSubtype,
                    BrowseDirection.Inverse,
                    includeSubtypes: false,
                    (uint)NodeClass.ObjectType).ConfigureAwait(false);

                if (parents.Count == 0)
                {
                    return false;
                }

                currentTypeId = ExpandedNodeId.ToNodeId(parents[0].NodeId, Session.NamespaceUris);
                if (currentTypeId == baseTypeId)
                {
                    return true;
                }

                if (currentTypeId.IsNull)
                {
                    return false;
                }
            }

            return false;
        }
    }
}
