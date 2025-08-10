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
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Client.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.ComplexTypes.Tests
{
    /// <summary>
    /// Node cache resolver tests.
    /// </summary>
    [TestFixture, Category("Client")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [TestFixtureSource(nameof(FixtureArgs))]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class NodeCacheResolverTests : ClientTestFramework
    {
        public NodeCacheResolverTests()
            : base(Utils.UriSchemeOpcTcp) { }

        public NodeCacheResolverTests(string uriScheme = Utils.UriSchemeOpcTcp)
            : base(uriScheme) { }

        public static readonly NodeId[] TypeSystems =
        [
            ObjectIds.OPCBinarySchema_TypeSystem,
            ObjectIds.XmlSchema_TypeSystem,
        ];

        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public new Task OneTimeSetUp()
        {
            SupportsExternalServerUrl = true;
            return base.OneTimeSetUp();
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
        public new Task SetUp()
        {
            return base.SetUp();
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public new Task TearDown()
        {
            return base.TearDown();
        }

        [Test, Order(100)]
        public async Task LoadStandardDataTypeSystemAsync()
        {
            var nodeResolver = new NodeCacheResolver(Session);
            ServiceResultException sre = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                System.Collections.Generic.IReadOnlyDictionary<NodeId, DataDictionary> t = await nodeResolver
                    .LoadDataTypeSystem(ObjectIds.ObjectAttributes_Encoding_DefaultJson)
                    .ConfigureAwait(false);
            });
            Assert.AreEqual((StatusCode)StatusCodes.BadNodeIdInvalid, (StatusCode)sre.StatusCode);
            System.Collections.Generic.IReadOnlyDictionary<NodeId, DataDictionary> typeSystem = await nodeResolver
                .LoadDataTypeSystem()
                .ConfigureAwait(false);
            Assert.NotNull(typeSystem);
            typeSystem = await nodeResolver
                .LoadDataTypeSystem(ObjectIds.OPCBinarySchema_TypeSystem)
                .ConfigureAwait(false);
            Assert.NotNull(typeSystem);
            typeSystem = await nodeResolver.LoadDataTypeSystem(ObjectIds.XmlSchema_TypeSystem).ConfigureAwait(false);
            Assert.NotNull(typeSystem);
        }

        [Test, Order(110)]
        [TestCaseSource(nameof(TypeSystems))]
        public async Task LoadAllServerDataTypeSystemsAsync(NodeId dataTypeSystem)
        {
            // find the dictionary for the description.
            var browser = new Browser(Session)
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IncludeSubtypes = false,
                NodeClassMask = 0,
            };

            ReferenceDescriptionCollection references = browser.Browse(dataTypeSystem);
            Assert.NotNull(references);

            TestContext.Out.WriteLine("  Found {0} references", references.Count);

            // read all type dictionaries in the type system
            var nodeResolver = new NodeCacheResolver(Session);
            foreach (ReferenceDescription r in references)
            {
                var dictionaryId = ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris);
                TestContext.Out.WriteLine("  ReadDictionary {0} {1}", r.BrowseName.Name, dictionaryId);
                DataDictionary dictionaryToLoad = await nodeResolver
                    .LoadDictionaryAsync(dictionaryId, r.BrowseName.Name)
                    .ConfigureAwait(false);

                // internal API for testing only
                byte[] dictionary = await nodeResolver.ReadDictionaryAsync(dictionaryId).ConfigureAwait(false);
                // TODO: workaround known issues in the Xml type system.
                // https://mantis.opcfoundation.org/view.php?id=7393
                if (dataTypeSystem.Equals(ObjectIds.XmlSchema_TypeSystem))
                {
                    try
                    {
                        dictionaryToLoad.Validate(dictionary, true);
                    }
                    catch (Exception ex)
                    {
                        NUnit.Framework.Assert.Inconclusive(ex.Message);
                    }
                }
                else
                {
                    dictionaryToLoad.Validate(dictionary, true);
                }
            }
        }
    }
}
