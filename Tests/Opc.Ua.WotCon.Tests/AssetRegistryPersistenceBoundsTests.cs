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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Assets;
using Opc.Ua.WotCon.Server.ThingDescriptions;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Defence-in-depth bounds on the persisted-TD loader:
    /// <see cref="AssetRegistry.EnumeratePersistedAsync"/> must skip
    /// oversize files, over-deep JSON, and stop after
    /// <see cref="WotConnectivityServerOptions.MaxPersistedThingDescriptionFiles"/>
    /// so a malicious or corrupted persistence directory cannot wedge
    /// server startup through CPU/memory/stack exhaustion.
    /// </summary>
    /// <remarks>
    /// These tests construct the registry directly with a null node
    /// manager because <c>EnumeratePersistedAsync</c> only reads
    /// <c>m_options</c> and <c>m_logger</c> — it never dereferences the
    /// manager. Pinning that contract is the point of a dedicated test
    /// fixture; a future change that adds manager-bound work to the
    /// load path will surface here as a <c>NullReferenceException</c>
    /// and force the contributor to revisit the bounds.
    /// </remarks>
    [TestFixture]
    [Category("WotCon")]
    public sealed class AssetRegistryPersistenceBoundsTests
    {
        private string _tempFolder = null!;

        [SetUp]
        public void SetUp()
        {
            _tempFolder = Path.Combine(
                Path.GetTempPath(),
                "wotcon-bounds-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempFolder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempFolder))
            {
                try
                {
                    Directory.Delete(_tempFolder, recursive: true);
                }
                catch
                { /* swallow */
                }
            }
        }

        [Test]
        public async Task WellFormedTdIsRestoredAndCountsAgainstBudget()
        {
            // Happy path — establishes the baseline: a normal TD inside
            // the limits is loaded and yielded exactly once.
            const string assetName = "asset-001";
            WriteValidTd(assetName);

            var options = MakeOptions();
            var entries = new List<LogEntry>();
            await using AssetRegistry registry = MakeRegistry(options, entries);

            List<(string Name, ThingDescription Description)> results =
                await EnumerateAsync(registry).ConfigureAwait(false);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Name, Is.EqualTo(assetName));
            Assert.That(results[0].Description.Name, Is.EqualTo(assetName));
            Assert.That(entries.Any(e => e.Level >= LogLevel.Warning), Is.False,
                "Happy-path enumeration must not emit any warnings.");
        }

        [Test]
        public async Task OversizeFileIsSkippedWithWarning()
        {
            // The size limit is enforced on the read side (the spec
            // existed but was only checked at write time before this
            // hardening). Verify a file beyond the cap is skipped and a
            // warning is emitted referencing the size.
            const string goodName = "asset-good";
            const string oversizeName = "asset-toobig";
            const int sizeLimit = 4 * 1024;
            WriteValidTd(goodName);
            WriteOversizeJson(oversizeName, sizeLimit * 2);

            var options = MakeOptions();
            options.MaxThingDescriptionSize = sizeLimit;
            var entries = new List<LogEntry>();
            await using AssetRegistry registry = MakeRegistry(options, entries);

            List<(string Name, ThingDescription _)> results =
                await EnumerateAsync(registry).ConfigureAwait(false);

            Assert.That(results.Select(r => r.Name), Is.EquivalentTo([goodName]));

            LogEntry? warning = entries.FirstOrDefault(e =>
                e.Level == LogLevel.Warning &&
                e.Message.Contains("exceeds", StringComparison.Ordinal) &&
                e.Message.Contains(oversizeName, StringComparison.Ordinal));
            Assert.That(warning, Is.Not.Null,
                "Expected a size-limit warning that names the oversize file.");
        }

        [Test]
        public async Task FileCountLimitIsEnforcedExactly()
        {
            // Spec: exactly MaxPersistedThingDescriptionFiles entries are
            // yielded when the directory contains MaxPersisted... + 5.
            const int limit = 7;
            for (int i = 0; i < limit + 5; i++)
            {
                WriteValidTd($"asset-{i:D3}");
            }

            var options = MakeOptions();
            options.MaxPersistedThingDescriptionFiles = limit;
            var entries = new List<LogEntry>();
            await using AssetRegistry registry = MakeRegistry(options, entries);

            List<(string Name, ThingDescription _)> results =
                await EnumerateAsync(registry).ConfigureAwait(false);

            Assert.That(results, Has.Count.EqualTo(limit));
            Assert.That(entries.Any(e =>
                e.Level == LogLevel.Warning &&
                e.Message.Contains("MaxPersistedThingDescriptionFiles", StringComparison.Ordinal)),
                Is.True, "Expected a budget-exhaustion warning.");
        }

        [Test]
        public async Task FileCountLimitOfZeroSkipsEverything()
        {
            // A file-count limit of 0 means 'do not load any persisted TDs'.
            // Verifies a hard kill switch: an operator can drop the
            // directory-load behaviour without removing the folder.
            WriteValidTd("asset-001");

            var options = MakeOptions();
            options.MaxPersistedThingDescriptionFiles = 0;
            var entries = new List<LogEntry>();
            await using AssetRegistry registry = MakeRegistry(options, entries);

            List<(string Name, ThingDescription _)> results =
                await EnumerateAsync(registry).ConfigureAwait(false);

            Assert.That(results, Is.Empty);
        }

        [Test]
        public async Task OverDeepJsonIsSkippedWithWarning_NotThrown()
        {
            // Build a JSON document whose nesting exceeds MaxDepth via an
            // 'invalid' nested-arrays payload that the JsonSerializer
            // would still try to read into the ThingDescription type.
            const string overDeepName = "asset-deep";
            const string okName = "asset-ok";
            const int depthLimit = 8;
            WriteValidTd(okName);
            WriteOverDeepJson(overDeepName, depthLimit + 16);

            var options = MakeOptions();
            options.MaxThingDescriptionJsonDepth = depthLimit;
            var entries = new List<LogEntry>();
            await using AssetRegistry registry = MakeRegistry(options, entries);

            List<(string Name, ThingDescription _)> results =
                await EnumerateAsync(registry).ConfigureAwait(false);

            // The well-formed TD survives; the deep one is skipped.
            Assert.That(results.Select(r => r.Name), Is.EquivalentTo([okName]));

            LogEntry? warning = entries.FirstOrDefault(e =>
                e.Level == LogLevel.Warning &&
                e.Message.Contains(overDeepName, StringComparison.Ordinal));
            Assert.That(warning, Is.Not.Null,
                "Expected a deserialization warning for the over-deep TD.");
            Assert.That(warning!.Message, Does.Contain("JSON deserialization failed"));
        }

        [Test]
        public async Task MalformedJsonIsSkippedNotThrown()
        {
            // Defence-in-depth: arbitrary malformed JSON (not just
            // depth-related) must not propagate out of the loader.
            const string badName = "asset-malformed";
            const string okName = "asset-ok";
            WriteValidTd(okName);
            File.WriteAllText(
                Path.Combine(_tempFolder, badName + ".jsonld"),
                "{ \"this is\": not-valid-json");

            var options = MakeOptions();
            var entries = new List<LogEntry>();
            await using AssetRegistry registry = MakeRegistry(options, entries);

            List<(string Name, ThingDescription _)> results =
                await EnumerateAsync(registry).ConfigureAwait(false);

            Assert.That(results.Select(r => r.Name), Is.EquivalentTo([okName]));
            Assert.That(entries.Any(e =>
                e.Level == LogLevel.Warning &&
                e.Message.Contains(badName, StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public async Task CancellationIsPropagated()
        {
            // OperationCanceledException must escape unmodified — the
            // catch filter in EnumeratePersistedAsync explicitly
            // rethrows on cancellation per the task spec.
            var options = MakeOptions();
            var entries = new List<LogEntry>();
            await using AssetRegistry registry = MakeRegistry(options, entries);

            // Seed at least one file so the foreach actually runs.
            WriteValidTd("asset-001");

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in registry.EnumeratePersistedAsync(cts.Token)
                    .ConfigureAwait(false))
                {
                    // unreachable — cancellation must fire on first iteration.
                }
            });
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private WotConnectivityServerOptions MakeOptions()
        {
            return new()
            {
                ThingDescriptionStorageFolder = _tempFolder
            };
        }

        private AssetRegistry MakeRegistry(
            WotConnectivityServerOptions options,
            List<LogEntry> entries)
        {
            // EnumeratePersistedAsync does not dereference m_manager;
            // passing null keeps the test focused on the bounds-checking
            // contract. A NullReferenceException here in a future
            // refactor is the desired signal that the load path now
            // touches the manager and must be re-tested at that layer.
            ILogger logger = new CapturingLogger(entries);
            return new AssetRegistry(manager: null!, options, logger);
        }

        private void WriteValidTd(string assetName)
        {
            string path = Path.Combine(_tempFolder, assetName + ".jsonld");
            File.WriteAllText(path,
                $$"""
                {
                  "name": "{{assetName}}",
                  "base": "sim://opcua.test/wot/{{assetName}}",
                  "properties": {
                    "Voltage": { "type": "number" }
                  }
                }
                """);
        }

        private void WriteOversizeJson(string assetName, int bytes)
        {
            // Build a valid JSON document with a long description field
            // so the file is well-formed (will deserialize fine if the
            // size check is skipped) and the only reason to reject it is
            // the size bound.
            var builder = new StringBuilder(bytes + 256);
            builder.Append("{ \"name\": \"")
                .Append(assetName)
                .Append("\", \"description\": \"");
            int payloadLen = bytes - builder.Length - 8;
            builder.Append('A', Math.Max(payloadLen, 0))
                .Append("\" }");
            string path = Path.Combine(_tempFolder, assetName + ".jsonld");
            File.WriteAllText(path, builder.ToString());
        }

        private void WriteOverDeepJson(string assetName, int depth)
        {
            // Build {"a":{"a":{"a":...}}} nested to depth `depth`. The
            // JsonSerializer treats each nested object as one level, so
            // a depth of MaxDepth+1 triggers JsonException via the
            // bounded JsonSerializerOptions.MaxDepth.
            var builder = new StringBuilder((depth * 6) + 16);
            for (int i = 0; i < depth; i++)
            {
                builder.Append("{\"a\":");
            }
            builder.Append('1');
            for (int i = 0; i < depth; i++)
            {
                builder.Append('}');
            }
            string path = Path.Combine(_tempFolder, assetName + ".jsonld");
            File.WriteAllText(path, builder.ToString());
        }

        private static async Task<List<(string Name, ThingDescription Description)>> EnumerateAsync(
            AssetRegistry registry)
        {
            var results = new List<(string, ThingDescription)>();
            await foreach (var entry in registry.EnumeratePersistedAsync(CancellationToken.None)
                .ConfigureAwait(false))
            {
                results.Add(entry);
            }
            return results;
        }

        private sealed record LogEntry(LogLevel Level, EventId EventId, string Message);

        private sealed class CapturingLogger : ILogger
        {
            public CapturingLogger(List<LogEntry> entries)
            {
                m_entries = entries;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                return NullScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                m_entries.Add(new LogEntry(
                    logLevel, eventId, formatter(state, exception)));
            }

            private readonly List<LogEntry> m_entries;

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose()
                {
                }
            }
        }
    }
}
