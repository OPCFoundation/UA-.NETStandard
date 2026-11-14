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
 *
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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Wot.Exporter.Tests
{
    /// <summary>
    /// Tests command input, file output, and fault reports.
    /// </summary>
    [TestFixture]
    public sealed class ExportCommandTests
    {
        /// <summary>
        /// Creates isolated input and output folders.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_folder = Path.Combine(Path.GetTempPath(), "wot-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_folder);
            m_output = new StringWriter(CultureInfo.InvariantCulture);
            m_error = new StringWriter(CultureInfo.InvariantCulture);
            m_command = new ExportCommand(m_output, m_error);
        }

        /// <summary>
        /// Removes files made by this test only.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            m_output.Dispose();
            m_error.Dispose();
            Directory.Delete(m_folder, recursive: true);
        }

        /// <summary>
        /// Rejects missing or extra arguments.
        /// </summary>
        [Test]
        public async Task InvalidArgumentsReturnUsageErrorAsync()
        {
            foreach (string[] args in (string[][])[[], ["one", "two", "three"], ["--unknown"]])
            {
                int code = await m_command.RunAsync(args).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(2));
            }
            Assert.That(m_error.ToString(), Does.Contain("ERROR:"));
        }

        /// <summary>
        /// Shows help without a required source.
        /// </summary>
        [Test]
        public async Task HelpReturnsSuccessAsync()
        {
            int code = await m_command.RunAsync(["--help"]).ConfigureAwait(false);
            Assert.That(code, Is.Zero);
            Assert.That(m_output.ToString(), Does.Contain("Usage:"));
            Assert.That(Directory.EnumerateFileSystemEntries(m_folder), Is.Empty);
        }

        /// <summary>
        /// Rejects a missing path and a folder with no input files.
        /// </summary>
        [Test]
        public async Task MissingSourceAndEmptySourceReturnInputErrorAsync()
        {
            int missing = await m_command.RunAsync([Path.Combine(m_folder, "missing")]).ConfigureAwait(false);
            Assert.That(missing, Is.EqualTo(2));
            Assert.That(await m_command.RunAsync([m_folder]).ConfigureAwait(false), Is.EqualTo(2));
        }

        /// <summary>
        /// Writes each model, reports progress, and does not read the output tree.
        /// </summary>
        [Test]
        public async Task RecursiveExportUsesDefaultOutputAndIsStableAsync()
        {
            string child = Path.Combine(m_folder, "child");
            Directory.CreateDirectory(child);
            await File.WriteAllTextAsync(Path.Combine(child, "Test.NodeSet2.xml"), k_nodeSet).ConfigureAwait(false);
            string excluded = Path.Combine(m_folder, "out");
            Directory.CreateDirectory(excluded);
            await File.WriteAllTextAsync(Path.Combine(excluded, "Bad.NodeSet2.xml"), "invalid").ConfigureAwait(false);

            Assert.That(await m_command.RunAsync([m_folder]).ConfigureAwait(false), Is.Zero, m_error.ToString());
            string target = Path.Combine(excluded, "child", "Test.NodeSet2.xml");
            string[] paths = Directory.GetFiles(target);
            Assert.That(paths, Has.Length.EqualTo(2));
            string[] first = await Task.WhenAll(paths.Select(path => File.ReadAllTextAsync(path)))
                .ConfigureAwait(false);
            foreach (string json in first)
            {
                using var document = JsonDocument.Parse(json);
                Assert.That(document.RootElement.GetProperty("@type").EnumerateArray()
                    .Any(type => type.GetString() == "tm:ThingModel"), Is.True);
            }
            Assert.That(m_output.ToString(), Does.Contain("[1/1]").And.Contain("Wrote 2 Thing Models"));
            Assert.That(await m_command.RunAsync([m_folder]).ConfigureAwait(false), Is.Zero, m_error.ToString());
            string[] second = await Task.WhenAll(paths.Select(path => File.ReadAllTextAsync(path)))
                .ConfigureAwait(false);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(Directory.GetFiles(target, "*.tmp"), Is.Empty);
        }

        /// <summary>
        /// Keeps processing after a malformed source and returns a failing exit code.
        /// </summary>
        [Test]
        public async Task InvalidFileDoesNotStopOtherFilesAsync()
        {
            await File.WriteAllTextAsync(Path.Combine(m_folder, "A.NodeSet2.xml"), "<broken>").ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(m_folder, "B.NodeSet2.xml"), k_nodeSet).ConfigureAwait(false);
            Assert.That(await m_command.RunAsync(
                [Path.Combine(m_folder, "A.NodeSet2.xml"), Path.GetPathRoot(m_folder)!]).ConfigureAwait(false),
                Is.EqualTo(2));
            string target = Path.Combine(m_folder, "result");
            int code = await m_command.RunAsync([m_folder, target]).ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(1));
            Assert.That(m_error.ToString(), Does.Contain("A.NodeSet2.xml").And.Contain("ERROR:"));
            Assert.That(Directory.GetFiles(target, "*", SearchOption.AllDirectories), Has.Length.EqualTo(2));
        }

        /// <summary>
        /// Accepts an individual file and rejects output that contains the source.
        /// </summary>
        [Test]
        public async Task FileSourceUsesItsParentForDefaultOutputAsync()
        {
            string source = Path.Combine(m_folder, "Test.NodeSet2.xml");
            await File.WriteAllTextAsync(source, k_nodeSet).ConfigureAwait(false);
            Assert.That(await m_command.RunAsync([source, m_folder]).ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(await m_command.RunAsync([source]).ConfigureAwait(false), Is.Zero, m_error.ToString());
            Assert.That(Directory.GetFiles(Path.Combine(m_folder, "out"), "*", SearchOption.AllDirectories),
                Has.Length.EqualTo(2));
        }

        /// <summary>
        /// Stops before output when the caller cancels.
        /// </summary>
        [Test]
        public async Task CancellationReturnsDistinctExitCodeAsync()
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            int code = await m_command.RunAsync([m_folder], cancellation.Token).ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(130));
            Assert.That(m_error.ToString(), Does.Contain("stopped"));
            Assert.That(Directory.EnumerateFileSystemEntries(m_folder), Is.Empty);
        }

        /// <summary>
        /// Stops discovery when cancellation arrives after enumeration has begun.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationInterruptsDiscoveryAsync(bool nested)
        {
            await File.WriteAllTextAsync(Path.Combine(m_folder, "A.NodeSet2.xml"), k_nodeSet).ConfigureAwait(false);
            string remaining = nested ? Path.Combine(m_folder, "child") : m_folder;
            Directory.CreateDirectory(remaining);
            await File.WriteAllTextAsync(Path.Combine(remaining, "B.NodeSet2.xml"), k_nodeSet).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            using IEnumerator<string> files = ExportCommand.FindNodeSets(
                m_folder, Path.Combine(m_folder, "out"), cancellation.Token).GetEnumerator();
            Assert.That(files.MoveNext(), Is.True);

            await cancellation.CancelAsync().ConfigureAwait(false);

            Assert.That(() => files.MoveNext(), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(Directory.Exists(Path.Combine(m_folder, "out")), Is.False);
        }

        /// <summary>Contains two independent type roots.</summary>
        private const string k_nodeSet = """
        <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
          <NamespaceUris><Uri>urn:export:test</Uri></NamespaceUris>
          <Models><Model ModelUri="urn:export:test" Version="1.0" /></Models>
          <UAObjectType NodeId="ns=1;i=1" BrowseName="1:PumpType">
            <DisplayName>PumpType</DisplayName>
            <References><Reference ReferenceType="i=45" IsForward="false">i=58</Reference></References>
          </UAObjectType>
          <UAObjectType NodeId="ns=1;i=2" BrowseName="1:ValveType">
            <DisplayName>ValveType</DisplayName>
            <References><Reference ReferenceType="i=45" IsForward="false">i=58</Reference></References>
          </UAObjectType>
        </UANodeSet>
        """;

        /// <summary>Stores this test's files.</summary>
        private string m_folder = null!;

        /// <summary>Captures progress messages.</summary>
        private StringWriter m_output = null!;

        /// <summary>Captures diagnostics.</summary>
        private StringWriter m_error = null!;

        /// <summary>Runs this test's command.</summary>
        private ExportCommand m_command = null!;
    }
}
