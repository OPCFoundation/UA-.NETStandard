// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

#pragma warning disable CA2007

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.AliasNames;
using Opc.Ua.Client.AliasNames.PubSub;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Client.Tests.AliasNames.PubSub
{
    /// <summary>
    /// Coverage tests for the client-side Part 17 Annex D bridge:
    /// reader filtering and the
    /// <see cref="AliasNamePubSubRefreshStrategy"/> behaviour.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public class AliasNamePubSubBridgeTests
    {
        [Test]
        public void ReaderFiltersByExpectedApplicationUri()
        {
            var reader = new AliasNamePubSubReader(
                new AliasNamePubSubReaderOptions
                {
                    ExpectedApplicationUri = "urn:expected"
                });
            int received = 0;
            reader.AliasUpdateReceived += (_, _) => received++;

            bool accepted = reader.Submit(new AliasUpdateDataType
            {
                ApplicationUri = "urn:other"
            });
            Assert.That(accepted, Is.False);
            Assert.That(received, Is.Zero);

            accepted = reader.Submit(new AliasUpdateDataType
            {
                ApplicationUri = "urn:expected"
            });
            Assert.That(accepted, Is.True);
            Assert.That(received, Is.EqualTo(1));
        }

        [Test]
        public async Task StrategyInvalidatesOnMatchingCategoryUpdateAsync()
        {
            var reader = new AliasNamePubSubReader();
            var strategy = new AliasNamePubSubRefreshStrategy(reader);

            // Build a session harness so we can construct an
            // AliasNameClient pointing at a custom category.
            var harness = AliasNameSessionHarness.Create();
            harness.SessionMock.SetupGet(s => s.NamespaceUris)
                .Returns(BuildNamespaceTable());

            var categoryId = new NodeId("MyCategory", 1);
            var client = new AliasNameClient(harness.Session, categoryId);

            int invalidations = 0;
            await strategy.StartAsync(
                client, () => invalidations++, CancellationToken.None).ConfigureAwait(false);
            try
            {
                // Matching update — must fire.
                reader.Submit(new AliasUpdateDataType
                {
                    ApplicationUri = "urn:p",
                    Categories = new[]
                    {
                        new AliasCategoryUpdateDataType
                        {
                            Category = new PortableNodeId
                            {
                                NamespaceUri = "http://test.example/",
                                Identifier = NodeId.Parse("s=MyCategory")
                            },
                            LastChange = 1
                        }
                    }.ToArrayOf()
                });
                Assert.That(invalidations, Is.EqualTo(1));

                // Same LastChange — must NOT fire again.
                reader.Submit(new AliasUpdateDataType
                {
                    ApplicationUri = "urn:p",
                    Categories = new[]
                    {
                        new AliasCategoryUpdateDataType
                        {
                            Category = new PortableNodeId
                            {
                                NamespaceUri = "http://test.example/",
                                Identifier = NodeId.Parse("s=MyCategory")
                            },
                            LastChange = 1
                        }
                    }.ToArrayOf()
                });
                Assert.That(invalidations, Is.EqualTo(1));

                // Wraparound (1 -> 0) — must fire (inequality check).
                reader.Submit(new AliasUpdateDataType
                {
                    ApplicationUri = "urn:p",
                    Categories = new[]
                    {
                        new AliasCategoryUpdateDataType
                        {
                            Category = new PortableNodeId
                            {
                                NamespaceUri = "http://test.example/",
                                Identifier = NodeId.Parse("s=MyCategory")
                            },
                            LastChange = 0
                        }
                    }.ToArrayOf()
                });
                Assert.That(invalidations, Is.EqualTo(2));

                // Non-matching category — must NOT fire.
                reader.Submit(new AliasUpdateDataType
                {
                    ApplicationUri = "urn:p",
                    Categories = new[]
                    {
                        new AliasCategoryUpdateDataType
                        {
                            Category = new PortableNodeId
                            {
                                NamespaceUri = "http://other.example/",
                                Identifier = NodeId.Parse("s=MyCategory")
                            },
                            LastChange = 99
                        }
                    }.ToArrayOf()
                });
                Assert.That(invalidations, Is.EqualTo(2));
            }
            finally
            {
                await strategy.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task StrategyMatchesStringIdentifiersContainingSemicolonsAsync()
        {
            var reader = new AliasNamePubSubReader();
            var strategy = new AliasNamePubSubRefreshStrategy(reader);
            var harness = AliasNameSessionHarness.Create();
            harness.SessionMock.SetupGet(s => s.NamespaceUris)
                .Returns(BuildNamespaceTable());

            var categoryId = new NodeId("A;B", 1);
            var client = new AliasNameClient(harness.Session, categoryId);

            int invalidations = 0;
            await strategy.StartAsync(
                client, () => invalidations++, CancellationToken.None).ConfigureAwait(false);
            try
            {
                reader.Submit(new AliasUpdateDataType
                {
                    ApplicationUri = "urn:p",
                    Categories = new[]
                    {
                        new AliasCategoryUpdateDataType
                        {
                            Category = new PortableNodeId
                            {
                                NamespaceUri = "http://test.example/",
                                Identifier = NodeId.Parse("s=A;B")
                            },
                            LastChange = 7
                        }
                    }.ToArrayOf()
                });

                Assert.That(invalidations, Is.EqualTo(1));
            }
            finally
            {
                await strategy.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static NamespaceTable BuildNamespaceTable()
        {
            var ns = new NamespaceTable();
            ns.Append("http://test.example/");
            return ns;
        }
    }
}
