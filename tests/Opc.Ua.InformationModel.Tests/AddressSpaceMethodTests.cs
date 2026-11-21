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
    /// compliance tests for Method node attributes and structure.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AddressSpaceMethod")]
    public class AddressSpaceMethodTests : TestFixture
    {
        [OneTimeSetUp]
        public void InitializeMethodNodeIds()
        {
            m_methodsFolderId = ToNodeId(Constants.MethodsFolder);
            m_addMethodId = ToNodeId(
                new ExpandedNodeId("Methods_Add", Constants.ReferenceServerNamespaceUri));
        }

        [Test]
        public async Task MethodNodeHasExecutableAttributeAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = m_addMethodId, AttributeId = Attributes.Executable }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].WrappedValue.TryGetValue(out bool _), Is.True);
        }

        [Test]
        public async Task MethodNodeHasUserExecutableAttributeAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = m_addMethodId, AttributeId = Attributes.UserExecutable }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].WrappedValue.TryGetValue(out bool _), Is.True);
        }

        [Test]
        public async Task MethodNodeHasInputArgumentsAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = m_addMethodId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasProperty,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            var propertyNames = new List<string>();
            foreach (ReferenceDescription r in response.Results[0].References)
            {
                propertyNames.Add(r.BrowseName.Name!);
            }
            Assert.That(propertyNames, Does.Contain("InputArguments"));
        }

        [Test]
        public async Task MethodNodeHasOutputArgumentsAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = m_addMethodId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasProperty,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            var propertyNames = new List<string>();
            foreach (ReferenceDescription r in response.Results[0].References)
            {
                propertyNames.Add(r.BrowseName.Name!);
            }
            Assert.That(propertyNames, Does.Contain("OutputArguments"));
        }

        [Test]
        public async Task MethodInputArgumentsHaveCorrectDataTypeAsync()
        {
            // Browse for InputArguments property node
            BrowseResponse browseResponse = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = m_addMethodId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasProperty,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            ReferenceDescription? inputArgsRef = null;
            foreach (ReferenceDescription r in browseResponse.Results[0].References)
            {
                if (r.BrowseName.Name == "InputArguments")
                {
                    inputArgsRef = r;
                    break;
                }
            }
            Assert.That(inputArgsRef, Is.Not.Null, "InputArguments property must exist.");

            var inputArgsId = ExpandedNodeId.ToNodeId(inputArgsRef.NodeId, Session.NamespaceUris);

            // Read the DataType of InputArguments variable
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = inputArgsId, AttributeId = Attributes.DataType }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
            NodeId dataType = readResponse.Results[0].GetValue<NodeId>(default);
            Assert.That(dataType, Is.EqualTo(DataTypeIds.Argument));
        }

        [Test]
        public async Task MethodHasComponentReferenceFromParentAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = m_methodsFolderId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)NodeClass.Method,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0),
                "Methods folder should contain Method nodes via HasComponent.");
        }

        [Test]
        public async Task MethodNodeClassIsMethodAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = m_addMethodId, AttributeId = Attributes.NodeClass }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].GetValue<int>(default), Is.EqualTo((int)NodeClass.Method));
        }

        [Test]
        public async Task MethodExecutableIsTrueAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = m_addMethodId, AttributeId = Attributes.Executable }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].GetValue<bool>(default), Is.True,
                "Methods_Add should be executable.");
        }

        private NodeId m_methodsFolderId;
        private NodeId m_addMethodId;
    }
}
