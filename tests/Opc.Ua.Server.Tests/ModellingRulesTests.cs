/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for ModellingRules in ServerCapabilities.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ModellingRulesTests
    {
        private ServerFixture<ReferenceServer> m_fixture;
        private ReferenceServer m_server;
        private RequestHeader m_requestHeader;
        private SecureChannelContext m_secureChannelContext;

        /// <summary>
        /// Set up a Server fixture.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AllNodeManagers = true,
                OperationLimits = true
            };
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Tear down the server fixture.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Create a session for a test.
        /// </summary>
        [SetUp]
        public async Task SetUpAsync()
        {
            (m_requestHeader, m_secureChannelContext) =
                await m_server.CreateAndActivateSessionAsync(TestContext.CurrentContext.Test.Name).ConfigureAwait(false);
        }

        /// <summary>
        /// Close the session for a test.
        /// </summary>
        [TearDown]
        public async Task TearDownAsync()
        {
            await m_server.CloseSessionAsync(m_secureChannelContext, m_requestHeader, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Test that the ModellingRules folder is populated with the expected modelling rules.
        /// </summary>
        [Test]
        public async Task TestModellingRulesPopulatedAsync()
        {
            // Browse ServerCapabilities->ModellingRules
            NodeId modellingRulesNodeId = ObjectIds.Server_ServerCapabilities_ModellingRules;

            var browseRequest = new BrowseDescription
            {
                NodeId = modellingRulesNodeId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IncludeSubtypes = true,
                NodeClassMask = (uint)NodeClass.Object,
                ResultMask = (uint)BrowseResultMask.All
            };

            ArrayOf<BrowseDescription> browseDescriptions = [browseRequest];

            BrowseResponse browseResponse = await m_server.BrowseAsync(
                m_secureChannelContext,
                m_requestHeader,
                null,
                0,
                browseDescriptions, RequestLifetime.None).ConfigureAwait(false);

            ArrayOf<BrowseResult> results = browseResponse.Results;
            Assert.That(results.IsNull, Is.False);
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].References.Count, Is.GreaterThan(0), "ModellingRules folder should not be empty");

            // Check that expected modelling rules are present
            string[] expectedRules =
            [
                BrowseNames.ModellingRule_Mandatory,
                BrowseNames.ModellingRule_Optional,
                BrowseNames.ModellingRule_ExposesItsArray,
                BrowseNames.ModellingRule_OptionalPlaceholder,
                BrowseNames.ModellingRule_MandatoryPlaceholder
            ];

            foreach (string expectedRule in expectedRules)
            {
                bool found = false;
                foreach (ReferenceDescription reference in results[0].References)
                {
                    if (reference.BrowseName.Name == expectedRule)
                    {
                        found = true;
                        // Verify it's of the correct type
                        var expectedTypeDefinition = ExpandedNodeId.ToNodeId(
                            ObjectTypeIds.ModellingRuleType,
                            m_server.CurrentInstance.NamespaceUris);
                        Assert.That(reference.TypeDefinition, Is.EqualTo(expectedTypeDefinition));
                        break;
                    }
                }
                Assert.That(found, Is.True,
                    $"Expected modelling rule '{expectedRule}' not found in ServerCapabilities->ModellingRules");
            }
        }

        /// <summary>
        /// Test that all modelling rules have the correct type definition.
        /// </summary>
        [Test]
        public async Task TestModellingRulesHaveCorrectTypeAsync()
        {
            NodeId modellingRulesNodeId = ObjectIds.Server_ServerCapabilities_ModellingRules;

            var browseRequest = new BrowseDescription
            {
                NodeId = modellingRulesNodeId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IncludeSubtypes = true,
                NodeClassMask = (uint)NodeClass.Object,
                ResultMask = (uint)BrowseResultMask.All
            };

            ArrayOf<BrowseDescription> browseDescriptions = [browseRequest];

            BrowseResponse browseResponse = await m_server.BrowseAsync(
                m_secureChannelContext,
                m_requestHeader,
                null,
                0,
                browseDescriptions, RequestLifetime.None).ConfigureAwait(false);

            ArrayOf<BrowseResult> results = browseResponse.Results;
            Assert.That(results.IsNull, Is.False);
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].References.Count, Is.GreaterThan(0));

            // All references should be of type ModellingRuleType
            var expectedTypeDefinition = ExpandedNodeId.ToNodeId(
                ObjectTypeIds.ModellingRuleType,
                m_server.CurrentInstance.NamespaceUris);

            foreach (ReferenceDescription reference in results[0].References)
            {
                Assert.That(reference.NodeClass, Is.EqualTo(NodeClass.Object));
                Assert.That(reference.TypeDefinition, Is.EqualTo(expectedTypeDefinition),
                    $"Modelling rule '{reference.BrowseName.Name}' does not have the correct type definition");
            }
        }
    }
}
