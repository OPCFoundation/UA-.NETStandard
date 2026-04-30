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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// Unit tests for <see cref="DefaultServerRedundancyHandler"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ServerRedundancy")]
    public sealed class ServerRedundancyHandlerTests
    {
        private DefaultServerRedundancyHandler m_handler;

        [SetUp]
        public void SetUp()
        {
            m_handler = new DefaultServerRedundancyHandler();
        }

        [Test]
        public void SelectFailoverTargetReturnsNullForNoneMode()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancyMode.None,
                ServiceLevel = 200,
                RedundantServers = [],
            };

            ConfiguredEndpoint? result = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(result, Is.Null);
        }

        [Test]
        public void SelectFailoverTargetReturnsNullForTransparentMode()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancyMode.Transparent,
                ServiceLevel = 200,
                RedundantServers = new List<RedundantServer>
                {
                    CreateServerInfo("urn:backup", 200, ServerState.Running),
                },
            };

            ConfiguredEndpoint? result = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(result, Is.Null);
        }

        [Test]
        public void SelectFailoverTargetSelectsHighestServiceLevel()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancyMode.Hot,
                ServiceLevel = 200,
                RedundantServers = new List<RedundantServer>
                {
                    CreateServerInfo("urn:server-low", 100, ServerState.Running),
                    CreateServerInfo("urn:server-high", 250, ServerState.Running),
                    CreateServerInfo("urn:server-mid", 180, ServerState.Running),
                },
            };

            ConfiguredEndpoint? result = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(result, Is.Not.Null);
            Assert.That(
                result!.Description.Server.ApplicationUri,
                Is.EqualTo("urn:server-high"));
        }

        [Test]
        public void SelectFailoverTargetSkipsCurrentEndpoint()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancyMode.Hot,
                ServiceLevel = 200,
                RedundantServers = new List<RedundantServer>
                {
                    CreateServerInfo("urn:current", 255, ServerState.Running),
                    CreateServerInfo("urn:backup", 100, ServerState.Running),
                },
            };

            ConfiguredEndpoint? result = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(result, Is.Not.Null);
            Assert.That(
                result!.Description.Server.ApplicationUri,
                Is.EqualTo("urn:backup"));
        }

        [Test]
        public void SelectFailoverTargetSkipsNonRunningServers()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancyMode.Hot,
                ServiceLevel = 200,
                RedundantServers = new List<RedundantServer>
                {
                    CreateServerInfo("urn:suspended", 255, ServerState.Suspended),
                    CreateServerInfo("urn:shutdown", 240, ServerState.Shutdown),
                    CreateServerInfo("urn:running", 100, ServerState.Running),
                },
            };

            ConfiguredEndpoint? result = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(result, Is.Not.Null);
            Assert.That(
                result!.Description.Server.ApplicationUri,
                Is.EqualTo("urn:running"));
        }

        [Test]
        public void SelectFailoverTargetReturnsNullWhenNoViableServers()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancyMode.Hot,
                ServiceLevel = 200,
                RedundantServers = new List<RedundantServer>
                {
                    CreateServerInfo("urn:current", 200, ServerState.Running),
                    CreateServerInfo("urn:down", 100, ServerState.Shutdown),
                    CreateServerInfo("urn:failed", 50, ServerState.Failed),
                },
            };

            ConfiguredEndpoint? result = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(result, Is.Null);
        }

        [Test]
        public void SelectFailoverTargetWorksForColdMode()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancyMode.Cold,
                ServiceLevel = 200,
                RedundantServers = new List<RedundantServer>
                {
                    CreateServerInfo("urn:backup", 150, ServerState.Running),
                },
            };

            ConfiguredEndpoint? result = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(result, Is.Not.Null);
            Assert.That(
                result!.Description.Server.ApplicationUri,
                Is.EqualTo("urn:backup"));
        }

        [Test]
        public void SelectFailoverTargetWorksForWarmMode()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancyMode.Warm,
                ServiceLevel = 200,
                RedundantServers = new List<RedundantServer>
                {
                    CreateServerInfo("urn:backup", 150, ServerState.Running),
                },
            };

            ConfiguredEndpoint? result = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(result, Is.Not.Null);
            Assert.That(
                result!.Description.Server.ApplicationUri,
                Is.EqualTo("urn:backup"));
        }

        [Test]
        public void SelectFailoverTargetWorksForHotMode()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancyMode.Hot,
                ServiceLevel = 200,
                RedundantServers = new List<RedundantServer>
                {
                    CreateServerInfo("urn:backup", 150, ServerState.Running),
                },
            };

            ConfiguredEndpoint? result = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(result, Is.Not.Null);
            Assert.That(
                result!.Description.Server.ApplicationUri,
                Is.EqualTo("urn:backup"));
        }

        [Test]
        public void SelectFailoverTargetWorksForHotAndMirroredMode()
        {
            var info = new ServerRedundancyInfo
            {
                Mode = RedundancyMode.HotAndMirrored,
                ServiceLevel = 200,
                RedundantServers = new List<RedundantServer>
                {
                    CreateServerInfo("urn:backup", 150, ServerState.Running),
                },
            };

            ConfiguredEndpoint? result = m_handler.SelectFailoverTarget(
                info, CreateCurrentEndpoint("urn:current"));

            Assert.That(result, Is.Not.Null);
            Assert.That(
                result!.Description.Server.ApplicationUri,
                Is.EqualTo("urn:backup"));
        }

        [Test]
        public async Task FetchRedundancyInfoReturnsNoneWhenNotSupportedAsync()
        {
            Mock<ISession> mockSession = CreateMockSession(
                redundancySupport: (int)RedundancySupport.None,
                serviceLevel: (byte)200);

            ServerRedundancyInfo info = await m_handler.FetchRedundancyInfoAsync(
                mockSession.Object).ConfigureAwait(false);

            Assert.That(info.Mode, Is.EqualTo(RedundancyMode.None));
            Assert.That(info.RedundantServers, Is.Empty);
        }

        [Test]
        public async Task FetchRedundancyInfoReadsServiceLevelAsync()
        {
            Mock<ISession> mockSession = CreateMockSession(
                redundancySupport: (int)RedundancySupport.None,
                serviceLevel: (byte)175);

            ServerRedundancyInfo info = await m_handler.FetchRedundancyInfoAsync(
                mockSession.Object).ConfigureAwait(false);

            Assert.That(info.ServiceLevel, Is.EqualTo(175));
        }

        [Test]
        public async Task FetchRedundancyInfoReadsRedundantServerArrayAsync()
        {
            var serverData1 = new RedundantServerDataType
            {
                ServerId = "urn:server1",
                ServiceLevel = 200,
                ServerState = ServerState.Running,
            };
            var serverData2 = new RedundantServerDataType
            {
                ServerId = "urn:server2",
                ServiceLevel = 150,
                ServerState = ServerState.Suspended,
            };

            Mock<ISession> mockSession = CreateMockSession(
                redundancySupport: (int)RedundancySupport.Hot,
                serviceLevel: (byte)200,
                redundantServers: [serverData1, serverData2]);

            ServerRedundancyInfo info = await m_handler.FetchRedundancyInfoAsync(
                mockSession.Object).ConfigureAwait(false);

            Assert.That(info.Mode, Is.EqualTo(RedundancyMode.Hot));
            Assert.That(info.RedundantServers, Has.Count.EqualTo(2));
            Assert.That(
                info.RedundantServers[0].ServerUri,
                Is.EqualTo("urn:server1"));
            Assert.That(info.RedundantServers[0].ServiceLevel, Is.EqualTo(200));
            Assert.That(
                info.RedundantServers[0].ServerState,
                Is.EqualTo(ServerState.Running));
            Assert.That(
                info.RedundantServers[1].ServerUri,
                Is.EqualTo("urn:server2"));
            Assert.That(
                info.RedundantServers[1].ServerState,
                Is.EqualTo(ServerState.Suspended));
        }

        [Test]
        public async Task FetchRedundancyInfoHandlesReadErrorsAsync()
        {
            Mock<ISession> mockSession = CreateMockSessionWithBadStatus();

            ServerRedundancyInfo info = await m_handler.FetchRedundancyInfoAsync(
                mockSession.Object).ConfigureAwait(false);

            Assert.That(info.Mode, Is.EqualTo(RedundancyMode.None));
            Assert.That(info.ServiceLevel, Is.Zero);
            Assert.That(info.RedundantServers, Is.Empty);
        }

        private static RedundantServer CreateServerInfo(
            string uri, byte serviceLevel, ServerState state)
        {
            return new RedundantServer
            {
                ServerUri = uri,
                ServiceLevel = serviceLevel,
                ServerState = state,
            };
        }

        private static ConfiguredEndpoint CreateCurrentEndpoint(string applicationUri)
        {
            var description = new EndpointDescription
            {
                EndpointUrl = applicationUri,
                Server = new ApplicationDescription
                {
                    ApplicationUri = applicationUri,
                },
            };

            return new ConfiguredEndpoint(null, description);
        }

        /// <summary>
        /// Creates a mock <see cref="ISession"/> that returns the given
        /// redundancy support and service level from <c>ReadAsync</c>,
        /// and optionally returns a redundant server array from
        /// <c>ReadValueAsync</c>.
        /// </summary>
        private static Mock<ISession> CreateMockSession(
            int redundancySupport,
            byte serviceLevel,
            RedundantServerDataType[]? redundantServers = null)
        {
            var mock = new Mock<ISession>();

            // ReadValuesAsync reads two nodes via ReadAsync: RedundancySupport + ServiceLevel
            mock.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.Is<ArrayOf<ReadValueId>>(r => r.Count == 2),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new DataValue(new Variant(redundancySupport), StatusCodes.Good),
                        new DataValue(new Variant(serviceLevel), StatusCodes.Good),
                    ],
                    DiagnosticInfos = [],
                });

            // ReadValueAsync for the redundant server array (single-node read)
            if (redundantServers != null)
            {
                ArrayOf<ExtensionObject> extensionObjects =
                    Array.ConvertAll(redundantServers, s => new ExtensionObject(s));

                mock.Setup(s => s.ReadAsync(
                        It.IsAny<RequestHeader>(),
                        It.IsAny<double>(),
                        It.IsAny<TimestampsToReturn>(),
                        It.Is<ArrayOf<ReadValueId>>(r => r.Count == 1),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new ReadResponse
                    {
                        Results =
                        [
                            new DataValue(
                                new Variant(extensionObjects),
                                StatusCodes.Good),
                        ],
                        DiagnosticInfos = [],
                    });
            }

            return mock;
        }

        /// <summary>
        /// Creates a mock session where all reads return Bad status.
        /// </summary>
        private static Mock<ISession> CreateMockSessionWithBadStatus()
        {
            var mock = new Mock<ISession>();

            mock.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.Is<ArrayOf<ReadValueId>>(r => r.Count == 2),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown),
                        new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown),
                    ],
                    DiagnosticInfos = [],
                });

            return mock;
        }
    }
}
