/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Schema
{
    /// <summary>
    /// Covers typed argument import together with the generic child-linking contract.
    /// </summary>
    [TestFixture]
    [Category("UANodeSet")]
    [Parallelizable]
    public sealed class UANodeSetMethodArgumentBindingTests
    {
        [TestCase(BrowseNames.InputArguments)]
        [TestCase(BrowseNames.OutputArguments)]
        public void ImportRejectsDuplicateStandardArgumentProperties(string browseName)
        {
            UANodeSet nodeSet = CreateNodeSet(browseName);
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable()
            };
            var nodes = new NodeStateCollection();

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => nodeSet.Import(context, nodes, linkParentChild: true));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(exception.Message, Does.Contain($"multiple {browseName}"));
        }

        [TestCase("1:InputArguments")]
        [TestCase("1:OutputArguments")]
        public void ImportKeepsCustomArgumentNamesAsOrdinaryChildren(string browseName)
        {
            UANodeSet nodeSet = CreateNodeSet(browseName);
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable()
            };
            var nodes = new NodeStateCollection();

            nodeSet.Import(context, nodes, linkParentChild: true);

            MethodState method = nodes.OfType<MethodState>().Single();
            var children = new List<BaseInstanceState>();
            method.GetChildren(context, children);
            Assert.Multiple(() =>
            {
                Assert.That(method.InputArguments, Is.Null);
                Assert.That(method.OutputArguments, Is.Null);
                Assert.That(children, Has.Count.EqualTo(2));
                Assert.That(children, Is.All.InstanceOf<PropertyState<ArrayOf<Argument>>>());
                Assert.That(children.Select(child => child.Parent), Is.All.SameAs(method));
                Assert.That(
                    children.Select(child => child.NodeId),
                    Is.EquivalentTo(nodes.OfType<BaseVariableState>().Select(node => node.NodeId)));
            });
        }

        private static UANodeSet CreateNodeSet(string browseName)
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:test:typed-method-argument-binding"],
                Items =
                [
                    CreateArgument("ns=1;i=1001", browseName),
                    CreateArgument("ns=1;i=1002", browseName),
                    new UAMethod
                    {
                        NodeId = "ns=1;i=1000",
                        BrowseName = "1:Load"
                    }
                ]
            };
        }

        private static UAVariable CreateArgument(string nodeId, string browseName)
        {
            return new UAVariable
            {
                NodeId = nodeId,
                BrowseName = browseName,
                ParentNodeId = "ns=1;i=1000",
                DataType = "i=296",
                ValueRank = ValueRanks.OneDimension,
                References =
                [
                    new Reference
                    {
                        ReferenceType = "i=40",
                        Value = "i=68"
                    },
                    new Reference
                    {
                        ReferenceType = "i=46",
                        IsForward = false,
                        Value = "ns=1;i=1000"
                    }
                ]
            };
        }
    }
}
