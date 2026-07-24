/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

#if NET10_0
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    [NonParallelizable]
    public sealed class NodeSetExportToolsTests
    {
        private const string kNodeSetSessionName = "mcp-nodeset-export";

        private string? m_originalExportRoot;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            _ = await McpTestEnvironment.SessionManager.ConnectAsync(
                kNodeSetSessionName,
                McpTestEnvironment.ServerUrl,
                "None",
                "None",
                "Anonymous",
                null,
                null,
                true,
                CancellationToken.None).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            _ = await McpTestEnvironment.SessionManager.DisconnectAsync(kNodeSetSessionName)
                .ConfigureAwait(false);
        }

        [SetUp]
        public void SetUp()
        {
            m_originalExportRoot = Environment.GetEnvironmentVariable(
                NodeSetExportTools.ExportRootEnvironmentVariable);
            Environment.SetEnvironmentVariable(
                NodeSetExportTools.ExportRootEnvironmentVariable,
                null);
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable(
                NodeSetExportTools.ExportRootEnvironmentVariable,
                m_originalExportRoot);
        }

        [Test]
        public void ResolveExportRootHonorsDiEnvironmentAndDefaultPrecedence()
        {
            string diRoot = CreateExportRoot();
            string environmentRoot = CreateExportRoot();
            Environment.SetEnvironmentVariable(
                NodeSetExportTools.ExportRootEnvironmentVariable,
                environmentRoot);

            using ServiceProvider diProvider = CreateProvider(diRoot);
            using ServiceProvider emptyProvider =
                new ServiceCollection().BuildServiceProvider();

            Assert.That(
                NodeSetExportTools.ResolveExportRoot(diProvider),
                Is.EqualTo(Path.GetFullPath(diRoot)));
            Assert.That(
                NodeSetExportTools.ResolveExportRoot(emptyProvider),
                Is.EqualTo(Path.GetFullPath(environmentRoot)));

            Environment.SetEnvironmentVariable(
                NodeSetExportTools.ExportRootEnvironmentVariable,
                null);
            Assert.That(
                NodeSetExportTools.ResolveExportRoot(emptyProvider),
                Is.EqualTo(Path.GetFullPath(
                    Path.Combine(Path.GetTempPath(), "Opc.Ua.Mcp", "exports"))));
        }

        [Test]
        public void ResolveExportPathAcceptsContainedPathsAndRejectsEscapes()
        {
            string exportRoot = CreateExportRoot();
            using ServiceProvider provider = CreateProvider(exportRoot);
            string relative = Path.Combine("nested", "nodeset.xml");
            string absolute = Path.Combine(exportRoot, "absolute.xml");

            Assert.That(
                NodeSetExportTools.ResolveExportPath(
                    provider,
                    relative,
                    "filePath"),
                Is.EqualTo(Path.GetFullPath(Path.Combine(exportRoot, relative))));
            Assert.That(
                NodeSetExportTools.ResolveExportPath(
                    provider,
                    absolute,
                    "filePath"),
                Is.EqualTo(Path.GetFullPath(absolute)));
            Assert.That(
                () => NodeSetExportTools.ResolveExportPath(
                    provider,
                    Path.Combine("..", "escape.xml"),
                    "filePath"),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => NodeSetExportTools.ResolveExportPath(
                    provider,
                    " ",
                    "filePath"),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task ExportNodeSetAsyncWritesCompleteFilteredExportAsync()
        {
            string exportRoot = CreateExportRoot();
            using ServiceProvider provider = CreateProvider(exportRoot);
            string relativePath = Path.Combine("single", "nodeset.xml");

            try
            {
                string json = await NodeSetExportTools.ExportNodeSetAsync(
                    provider,
                    McpTestEnvironment.SessionManager,
                    relativePath,
                    startingNodeId: "i=85",
                    exportMode: "Complete",
                    includeStartNode: true,
                    filterBaseTypes: true,
                    sessionName: kNodeSetSessionName)
                    .ConfigureAwait(false);

                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                string filePath = GetRequiredProperty(root, "filePath")
                    .GetString()!;

                Assert.That(GetRequiredProperty(root, "success").GetBoolean(), Is.True);
                Assert.That(GetRequiredProperty(root, "nodeCount").GetInt32(), Is.Positive);
                Assert.That(GetRequiredProperty(root, "fileSizeBytes").GetInt64(), Is.Positive);
                Assert.That(GetRequiredProperty(root, "exportMode").GetString(), Is.EqualTo("Complete"));
                Assert.That(File.Exists(filePath), Is.True);
            }
            finally
            {
                DeleteDirectory(exportRoot);
            }
        }

        [Test]
        public async Task ExportNodeSetPerNamespaceAsyncWritesCustomNamespacesAsync()
        {
            string exportRoot = CreateExportRoot();
            using ServiceProvider provider = CreateProvider(exportRoot);

            try
            {
                string json = await NodeSetExportTools.ExportNodeSetPerNamespaceAsync(
                    provider,
                    McpTestEnvironment.SessionManager,
                    "per-namespace",
                    sessionName: kNodeSetSessionName)
                    .ConfigureAwait(false);

                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                JsonElement files = GetRequiredProperty(root, "files");

                Assert.That(GetRequiredProperty(root, "success").GetBoolean(), Is.True);
                Assert.That(GetRequiredProperty(root, "totalNodeCount").GetInt32(), Is.Positive);
                Assert.That(
                    GetRequiredProperty(root, "exportedNamespaces").GetInt32(),
                    Is.GreaterThan(0));
                Assert.That(files.GetArrayLength(), Is.GreaterThan(0));
                Assert.That(
                    files.EnumerateArray().All(file =>
                        File.Exists(file.GetProperty("filePath").GetString())),
                    Is.True);
            }
            finally
            {
                DeleteDirectory(exportRoot);
            }
        }

        private static ServiceProvider CreateProvider(string exportRoot)
        {
            var services = new ServiceCollection();
            services.AddSingleton(new McpServerOptions
            {
                NodeSetExportRoot = exportRoot
            });
            return services.BuildServiceProvider();
        }

        private static string CreateExportRoot()
        {
            return Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "mcp-nodeset-tests",
                Guid.NewGuid().ToString("N"));
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        private static JsonElement GetRequiredProperty(
            JsonElement element,
            string propertyName)
        {
            Assert.That(
                element.TryGetProperty(propertyName, out JsonElement property),
                Is.True);
            return property;
        }
    }
}
#endif
