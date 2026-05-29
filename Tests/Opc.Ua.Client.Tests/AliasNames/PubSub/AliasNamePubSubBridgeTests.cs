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

        private static NamespaceTable BuildNamespaceTable()
        {
            var ns = new NamespaceTable();
            ns.Append("http://test.example/");
            return ns;
        }
    }
}
