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

using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Opc.Ua.MigrationAnalyzer.Analyzers;

namespace Opc.Ua.MigrationAnalyzer.Tests.Analyzers
{
    /// <summary>
    /// Tests for UA0025 (ILocalNode.DataLock removed).
    /// </summary>
    /// <remarks>
    /// Each source declares the 1.5.378 shape of the interface. The member is gone from the
    /// 2.0 assemblies, so the rule has to fire on sources that still compile against the old
    /// surface - which is exactly the migration path it exists for.
    /// </remarks>
    [TestFixture]
    public class UA0025Tests
    {
        private const string NodeShim = """
            namespace Opc.Ua
            {
                public interface INode
                {
                }

                public interface ILocalNode : INode
                {
                    object DataLock { get; }
                }

                public class Node : ILocalNode
                {
                    public object DataLock => this;
                }
            }
            """;

        [Test]
        public async Task ReportsOnLocalNodeDataLockAsync()
        {
            string source = NodeShim + """

                class C
                {
                    static object M(Opc.Ua.ILocalNode node) => node.DataLock;
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("ILocalNode.DataLock"));
        }

        [Test]
        public async Task ReportsOnNodeClassDataLockAsync()
        {
            string source = NodeShim + """

                class C
                {
                    static object M(Opc.Ua.Node node) => node.DataLock;
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("Node.DataLock"));
        }

        [Test]
        public async Task ReportsInsideALockStatementAsync()
        {
            // The shape a consumer actually wrote, and the reason the rule cannot auto-fix:
            // what the body needs to stay atomic with is not visible from the lock keyword.
            string source = NodeShim + """

                class C
                {
                    static void M(Opc.Ua.ILocalNode node)
                    {
                        lock (node.DataLock)
                        {
                        }
                    }
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(diagnostic.Id, Is.EqualTo("UA0025"));
        }

        [Test]
        public async Task DoesNotReportOnAnUnrelatedTypeAsync()
        {
            const string source = """
                class Unrelated
                {
                    public object DataLock { get; } = new object();
                }

                class C
                {
                    static object M(Unrelated u) => u.DataLock;
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0025RemovedNodeDataLockAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d => d.Id == "UA0025"),
                Is.False,
                "a member of the same name on an unrelated type must not fire the rule.");
        }

        [Test]
        public async Task DoesNotReportOnNodeBrowserDataLockAsync()
        {
            // NodeBrowser still has a DataLock; only the node one was removed.
            const string source = """
                namespace Opc.Ua
                {
                    public class NodeBrowser
                    {
                        protected object DataLock { get; } = new object();
                    }
                }

                class C : Opc.Ua.NodeBrowser
                {
                    object M() => this.DataLock;
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0025RemovedNodeDataLockAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diagnostics.Any(d => d.Id == "UA0025"), Is.False);
        }

        private static async Task<Diagnostic> SingleAsync(string source)
        {
            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0025RemovedNodeDataLockAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? diagnostic = diagnostics.SingleOrDefault(d => d.Id == "UA0025");
            Assert.That(diagnostic, Is.Not.Null, "expected UA0025 to fire.");
            return diagnostic!;
        }
    }
}
