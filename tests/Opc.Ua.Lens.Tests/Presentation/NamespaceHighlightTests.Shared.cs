/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.NodeSets;
using UaLens.Tests.NodeSets;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.Presentation
{
    public sealed partial class NamespaceHighlightTests
    {
        [OneTimeSetUp]
        public async Task CreateGraphAsync()
        {
            m_graph = await NodeSetAddressSpaceTests.CreateAsync().ConfigureAwait(false);
        }

        internal static Mock<ISession> Session(params ArrayOf<string> namespaces)
        {
            var table = new NamespaceTable();
            foreach (string uri in namespaces)
            {
                table.Append(uri);
            }
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(value => value.NamespaceUris).Returns(table);
            session.Setup(value => value.BrowseAsync(
                null, null, 0, It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse { Results = [new BrowseResult()] });
            return session;
        }

        internal static BrowserViewModel Browser(System.Func<ISession?> session)
        {
            return new BrowserViewModel(
                NodeSetAddressSpaceTests.Telemetry(), session, InlineWorkspaceDispatcher.Instance)
            {
                CurrentViewKind = BrowseViewKind.ObjectTypes
            };
        }

        private NodeSetAddressSpace m_graph = null!;
    }
}
