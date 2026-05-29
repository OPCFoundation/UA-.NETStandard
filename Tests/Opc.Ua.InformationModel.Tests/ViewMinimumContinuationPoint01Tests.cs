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

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for View Minimum Continuation Point 01.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("ViewServices")]
    public class ViewMinimumContinuationPoint01Tests : TestFixture
    {
        [Description("Given one node to browse And the node exists And the node has at least three forward references And RequestedMaxReferencesPerNode is 1 And Browse has been called When BrowseNext is")]
        [Test]
        public async Task BrowseNextWithLowMaxRefsReturnsContinuationPointAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true, NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("Test 5.7.2-8 prepared by Dale Pope dale.pope@matrikon.com Description: Given one node to browse And the node exists And the node has at least three references of the same Reference")]
        [Test]
        public async Task BrowseNextWithSameReferenceTypeUsesContinuationPointAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true, NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("Test 5.7.2-9 prepared by Dale Pope dale.pope@matrikon.com Description: Given one node to browse And the node exists And the node has at least three references of the same Reference")]
        [Test]
        public async Task BrowseNextWithSameReferenceTypeReturnsRemainingReferencesAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true, NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("Browse a (valid) node, specifying a nodeClassMask (other than all or View), requestedMaxReferencesPerNode = 1, and BrowseDirection = Both. The node must have at least two reference")]
        [Test]
        public async Task BrowseWithNodeClassMaskAndBothDirectionUsesContinuationPointAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true, NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given one node to browse And the node exists And the node has at least three references And RequestedMaxReferencesPerNode is 1 And ResultMask is set to include one result field And")]
        [Test]
        public async Task BrowseWithSelectiveResultMaskUsesContinuationPointAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true, NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given one node to browse And the node exists And the node has at least two View references And RequestedMaxReferencesPerNode is 1 And NodeClassMask is set to 128 (View) And Browse")]
        [Test]
        public async Task BrowseWithViewNodeClassMaskUsesContinuationPointAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true, NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given a continuation point And the continuation point does not exist And diagnostic info is requested When BrowseNext is called Then the server returns specified operation diagnost")]
        [Test]
        public async Task BrowseNextWithUnknownContinuationPointReturnsDiagnosticInfoAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true, NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given a continuation point And the continuation point does not exist And diagnostic info is not requested When Browse is called Then the server returns no diagnostic info. */ inclu")]
        [Test]
        public async Task BrowseWithUnknownContinuationPointOmitsDiagnosticInfoAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true, NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given multiple nodes to browse And the nodes exist And the nodes have at least one forward reference And the server limits the maximum number of continuation points And the number")]
        [Test]
        public async Task BrowseNextRejectsContinuationPointWhenServerLimitExceededAsync()
        {
            BrowseNextResponse response = await Session.BrowseNextAsync(
                null, false, new ByteString[] { (ByteString)new byte[] { 0xFF, 0xFE } }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given a node to browse And the node exists And the requestedMaxReferencesPerNode is 1 And the node has at least two references When Browse is called And the session is disconnected")]
        [Test]
        public async Task BrowseNextAfterSessionDisconnectFailsAsync()
        {
            BrowseNextResponse response = await Session.BrowseNextAsync(
                null, false, new ByteString[] { (ByteString)new byte[] { 0xFF, 0xFE } }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given an empty/null authenticationToken When BrowseNext is called Then the server returns service error Bad_SecurityChecksFailed.")]
        [Test]
        public async Task BrowseNextWithEmptyAuthenticationTokenFailsAsync()
        {
            BrowseNextResponse response = await Session.BrowseNextAsync(
                null, false, new ByteString[] { (ByteString)new byte[] { 0xFF, 0xFE } }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given a non-existent authenticationToken When BrowseNext is called Then the server returns service error Bad_SecurityChecksFailed.")]
        [Test]
        public async Task BrowseNextWithNonExistentAuthenticationTokenFailsAsync()
        {
            BrowseNextResponse response = await Session.BrowseNextAsync(
                null, false, new ByteString[] { (ByteString)new byte[] { 0xFF, 0xFE } }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given a RequestHeader.Timestamp of 0 When BrowseNext is called Then the server returns service error Bad_InvalidTimestamp.")]
        [Test]
        public async Task BrowseNextWithZeroTimestampFailsAsync()
        {
            BrowseNextResponse response = await Session.BrowseNextAsync(
                null, false, new ByteString[] { (ByteString)new byte[] { 0xFF, 0xFE } }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }
    }
}
