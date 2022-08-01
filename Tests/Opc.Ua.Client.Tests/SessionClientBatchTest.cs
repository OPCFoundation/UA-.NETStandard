/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Client tests.
    /// </summary>
    [TestFixture, Category("Client"), Category("SessionClient")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [TestFixtureSource(nameof(FixtureArgs))]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class SessionClientBatchTest : ClientTestFramework
    {
        public const uint kOperationLimit = 5;
        public SessionClientBatchTest(string uriScheme = Utils.UriSchemeOpcTcp) :
            base(uriScheme)
        {
        }

        #region Test Setup
        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public new async Task OneTimeSetUp()
        {
            SupportsExternalServerUrl = true;
            await base.OneTimeSetUp();
            Session.OperationLimits = new OperationLimits() {
                MaxMonitoredItemsPerCall = kOperationLimit,
                MaxNodesPerBrowse = kOperationLimit,
                MaxNodesPerHistoryReadData = kOperationLimit,
                MaxNodesPerHistoryReadEvents = kOperationLimit,
                MaxNodesPerHistoryUpdateData = kOperationLimit,
                MaxNodesPerHistoryUpdateEvents = kOperationLimit,
                MaxNodesPerMethodCall = kOperationLimit,
                MaxNodesPerNodeManagement = kOperationLimit,
                MaxNodesPerRead = kOperationLimit,
                MaxNodesPerRegisterNodes = kOperationLimit,
                MaxNodesPerTranslateBrowsePathsToNodeIds = kOperationLimit,
                MaxNodesPerWrite = kOperationLimit
            };
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public new Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public new async Task SetUp()
        {
            await base.SetUp().ConfigureAwait(false);
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public new Task TearDown()
        {
            return base.TearDown();
        }
        #endregion

        #region Test Methods
        [Test]
        public void AddNodes()
        {
            var nodesToAdd = new AddNodesItemCollection();
            var addNodesItem = new AddNodesItem() { };
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                nodesToAdd.Add(addNodesItem);
            }

            var requestHeader = new RequestHeader();
            var sre = Assert.Throws<ServiceResultException>(() => {
                var responseHeader = Session.AddNodes(requestHeader,
                    nodesToAdd,
                    out AddNodesResultCollection results,
                    out DiagnosticInfoCollection diagnosticInfos);

                Assert.NotNull(responseHeader);
                Assert.AreEqual(nodesToAdd.Count, results.Count);
                Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
            });

            Assert.AreEqual(StatusCodes.BadServiceUnsupported, sre.StatusCode);
        }

#if (CLIENT_ASYNC)
        [Test]
        public void AddNodesAsync()
        {
            var nodesToAdd = new AddNodesItemCollection();
            var addNodesItem = new AddNodesItem() { };
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                nodesToAdd.Add(addNodesItem);
            }

            var requestHeader = new RequestHeader();
            var sre = Assert.ThrowsAsync<ServiceResultException>(async () => {
                var response = await Session.AddNodesAsync(requestHeader,
                    nodesToAdd, CancellationToken.None).ConfigureAwait(false); ;

                Assert.NotNull(response);
                AddNodesResultCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                Assert.AreEqual(nodesToAdd.Count, results.Count);
                Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
            });

            Assert.AreEqual(StatusCodes.BadServiceUnsupported, sre.StatusCode);
        }

#endif

        [Test]
        public void AddReferences()
        {
            var referencesToAdd = new AddReferencesItemCollection();
            var addReferencesItem = new AddReferencesItem() { };
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                referencesToAdd.Add(addReferencesItem);
            }

            var requestHeader = new RequestHeader();
            var sre = Assert.Throws<ServiceResultException>(() => {
                var responseHeader = Session.AddReferences(requestHeader,
                    referencesToAdd,
                    out StatusCodeCollection results,
                    out DiagnosticInfoCollection diagnosticInfos);

                Assert.NotNull(responseHeader);
                Assert.AreEqual(referencesToAdd.Count, results.Count);
                Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
            });

            Assert.AreEqual(StatusCodes.BadServiceUnsupported, sre.StatusCode);
        }

#if (CLIENT_ASYNC)
        [Test]
        public void AddReferencesAsync()
        {
            var referencesToAdd = new AddReferencesItemCollection();
            var addReferencesItem = new AddReferencesItem() { };
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                referencesToAdd.Add(addReferencesItem);
            }

            var requestHeader = new RequestHeader();
            var sre = Assert.ThrowsAsync<ServiceResultException>(async () => {
                var response = await Session.AddReferencesAsync(requestHeader,
                    referencesToAdd, CancellationToken.None).ConfigureAwait(false); ;

                Assert.NotNull(response);
                StatusCodeCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                Assert.AreEqual(referencesToAdd.Count, results.Count);
                Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
            });

            Assert.AreEqual(StatusCodes.BadServiceUnsupported, sre.StatusCode);
        }
#endif

        [Test]
        public void DeleteNodes()
        {
            var nodesTDelete = new DeleteNodesItemCollection();
            var deleteNodesItem = new DeleteNodesItem() { };
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                nodesTDelete.Add(deleteNodesItem);
            }

            var requestHeader = new RequestHeader();
            var sre = Assert.Throws<ServiceResultException>(() => {
                var responseHeader = Session.DeleteNodes(requestHeader,
                    nodesTDelete,
                    out StatusCodeCollection results,
                    out DiagnosticInfoCollection diagnosticInfos);

                Assert.NotNull(responseHeader);
                Assert.AreEqual(nodesTDelete.Count, results.Count);
                Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
            });

            Assert.AreEqual(StatusCodes.BadServiceUnsupported, sre.StatusCode);
        }

#if (CLIENT_ASYNC)
        [Test]
        public void DeleteNodesAsync()
        {
            var nodesTDelete = new DeleteNodesItemCollection();
            var deleteNodesItem = new DeleteNodesItem() { };
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                nodesTDelete.Add(deleteNodesItem);
            }

            var requestHeader = new RequestHeader();
            var sre = Assert.ThrowsAsync<ServiceResultException>(async () => {
                var response = await Session.DeleteNodesAsync(requestHeader,
                    nodesTDelete, CancellationToken.None).ConfigureAwait(false);

                StatusCodeCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                Assert.NotNull(response.ResponseHeader);
                Assert.AreEqual(nodesTDelete.Count, results.Count);
                Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
            });

            Assert.AreEqual(StatusCodes.BadServiceUnsupported, sre.StatusCode);
        }
#endif

        [Test]
        public void DeleteReferences()
        {
            var referencesToDelete = new DeleteReferencesItemCollection();
            var deleteReferencesItem = new DeleteReferencesItem() { };
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                referencesToDelete.Add(deleteReferencesItem);
            }

            var requestHeader = new RequestHeader();
            var sre = Assert.Throws<ServiceResultException>(() => {
                var responseHeader = Session.DeleteReferences(requestHeader,
                    referencesToDelete,
                    out StatusCodeCollection results,
                    out DiagnosticInfoCollection diagnosticInfos);

                Assert.NotNull(responseHeader);
                Assert.AreEqual(referencesToDelete.Count, results.Count);
                Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
            });

            Assert.AreEqual(StatusCodes.BadServiceUnsupported, sre.StatusCode);
        }

#if (CLIENT_ASYNC)
        [Test]
        public void DeleteReferencesAsync()
        {
            var referencesToDelete = new DeleteReferencesItemCollection();
            var deleteReferencesItem = new DeleteReferencesItem() { };
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                referencesToDelete.Add(deleteReferencesItem);
            }

            var requestHeader = new RequestHeader();
            var sre = Assert.ThrowsAsync<ServiceResultException>(async () => {
                var response = await Session.DeleteReferencesAsync(requestHeader,
                    referencesToDelete, CancellationToken.None).ConfigureAwait(false);

                StatusCodeCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                Assert.NotNull(response.ResponseHeader);
                Assert.AreEqual(referencesToDelete.Count, results.Count);
                Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
            });

            Assert.AreEqual(StatusCodes.BadServiceUnsupported, sre.StatusCode);
        }
#endif


        #endregion

        #region Benchmarks
        #endregion

        #region Private Methods
        #endregion
    }
}
