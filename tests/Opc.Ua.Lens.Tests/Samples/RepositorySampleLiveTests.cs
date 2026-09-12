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

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Samples;

namespace UaLens.Tests.Samples
{
    [TestFixture]
    [NonParallelizable]
    public sealed class RepositorySampleLiveTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [Explicit(
            "Requires UALENS_SAMPLE_SOURCE_ROOT naming a trusted checkout with both Release/net10 samples built.")]
        [Category("RepositorySampleProbe")]
        public async Task BuiltManagedSampleAdvertisesOwnedIdentityAndCleansUp(int sampleId)
        {
            string sourceRoot = Environment.GetEnvironmentVariable("UALENS_SAMPLE_SOURCE_ROOT") ??
                throw new InvalidOperationException("Set UALENS_SAMPLE_SOURCE_ROOT to the trusted built checkout.");
            string runParent = Path.Combine(Path.GetTempPath(), "UaLens-live-sample-" + Guid.NewGuid().ToString("N"));
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            var service = new RepositorySampleService(
                new RepositorySampleFiles(runParent, LocalFileSystem.Instance),
                new RepositorySamplePortAllocator(),
                new RepositorySampleRuntime(),
                new RepositorySampleDiscoveryProbe(telemetry),
                TimeProvider.System);
            await using (service.ConfigureAwait(false))
            {
                try
                {
                    RepositorySampleSnapshot setup = await service.ConfigureAsync(
                        (RepositorySampleId)sampleId,
                        new RepositorySampleSource(sourceRoot, RepositorySampleBuildConfiguration.Release,
                            RepositorySampleFramework.Net10)).ConfigureAwait(false);
                    Assert.That(setup.Phase, Is.EqualTo(RepositorySamplePhase.Configured), setup.Message);
                    RepositorySampleSnapshot ready = await service.StartAsync(
                        new RepositorySampleRunOptions { RunSeconds = 10 }).ConfigureAwait(false);
                    Assert.That(ready.Phase, Is.EqualTo(RepositorySamplePhase.AdvertisedReady));
                    Assert.That(ready.ProcessId, Is.GreaterThan(0));
                    Assert.That(ready.OwnsResources, Is.True);
                    Assert.That(ready.Evidence, Is.Not.Null);
                    Assert.That(ready.Evidence!.ApplicationName,
                        Is.EqualTo(RepositorySampleCatalog.Get((RepositorySampleId)sampleId).ApplicationName));
                    Assert.That(ready.Evidence.CertificateSha256.Length, Is.EqualTo(32));
                    Assert.That(ready.Evidence.Endpoint.IsLoopback, Is.True);
                    Assert.That(ready.SecureConnectAuthorized, Is.False);

                    RepositorySampleSnapshot completed = await service.Completion.WaitAsync(TimeSpan.FromSeconds(50))
                        .ConfigureAwait(false);

                    Assert.That(completed.Phase, Is.EqualTo(RepositorySamplePhase.Stopped), completed.Message);
                    Assert.That(completed.ExitCode, Is.Zero);
                    Assert.That(completed.ForcedTermination, Is.False);
                    Assert.That(completed.OwnsResources, Is.False);
                    Assert.That(Directory.EnumerateFileSystemEntries(runParent), Is.Empty);
                }
                catch (RepositorySampleException error)
                {
                    RepositorySampleSnapshot snapshot = service.Snapshot;
                    string output = string.Join(
                        Environment.NewLine,
                        snapshot.Output.ToArray()?.Select(line => line.Text)
                            ?? []);
                    throw new AssertionException(
                        $"{snapshot.Phase}/{snapshot.Failure}; exit {snapshot.ExitCode}: {snapshot.Message}\n{output}",
                        error);
                }
                finally
                {
                    await service.StopAsync().ConfigureAwait(false);
                    if (Directory.Exists(runParent) && !Directory.EnumerateFileSystemEntries(runParent).Any())
                    {
                        Directory.Delete(runParent, recursive: false);
                    }
                }
            }
        }
    }
}
