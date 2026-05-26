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

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di.Server.SoftwareUpdate;

namespace Opc.Ua.Pumps.Tests
{
    /// <summary>
    /// Tests for <c>PumpSoftwareUpdateSeeder.SeedAsync</c> — the demo
    /// seeder that populates a <see cref="MemoryPackageStore"/> with
    /// two sample firmware packages so the MinimalPumpServer can
    /// demonstrate the OPC 10000-100 §10.3 software-update path. The
    /// seeder is <c>internal static</c> with no
    /// <c>InternalsVisibleTo</c> declaration on MinimalPumpServer, so
    /// we invoke it via reflection.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    [Category("SoftwareUpdate")]
    public sealed class PumpSoftwareUpdateSeederTests
    {
        [Test]
        public async Task SeedAsyncAddsTwoPackagesToStore()
        {
            var store = new MemoryPackageStore();

            await InvokeSeedAsync(store).ConfigureAwait(false);

            List<SoftwarePackage> packages = await CollectAsync(store)
                .ConfigureAwait(false);
            Assert.That(packages, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task SeedAsyncRegistersExpectedPackageIds()
        {
            var store = new MemoryPackageStore();

            await InvokeSeedAsync(store).ConfigureAwait(false);

            List<SoftwarePackage> packages = await CollectAsync(store)
                .ConfigureAwait(false);
            IEnumerable<string> ids = packages.Select(p => p.Id);
            Assert.That(ids, Is.EquivalentTo(s_expectedIds));
        }

        private static readonly string[] s_expectedIds =
        {
            "pump-firmware-1.0.0",
            "pump-firmware-1.0.1-rc"
        };

        [Test]
        public async Task SeedAsyncAssignsVendorAndNonZeroSize()
        {
            var store = new MemoryPackageStore();

            await InvokeSeedAsync(store).ConfigureAwait(false);

            List<SoftwarePackage> packages = await CollectAsync(store)
                .ConfigureAwait(false);
            foreach (SoftwarePackage pkg in packages)
            {
                Assert.That(pkg.Vendor, Is.EqualTo("SimPump Corp"));
                Assert.That(pkg.SizeBytes, Is.GreaterThan(0),
                    "MemoryPackageStore.AddAsync should record payload byte count.");
            }
        }

        private static async Task<List<SoftwarePackage>> CollectAsync(
            MemoryPackageStore store)
        {
            var list = new List<SoftwarePackage>();
            await foreach (SoftwarePackage pkg in store.ListAsync())
            {
                list.Add(pkg);
            }
            return list;
        }

        // PumpSoftwareUpdateSeeder is declared `internal static` in
        // MinimalPumpServer; that assembly does not expose its
        // internals to this test project, so we invoke SeedAsync via
        // reflection through the Program type's owning assembly.
        private static async Task InvokeSeedAsync(MemoryPackageStore store)
        {
            Assembly pumpsAssembly = typeof(global::Pumps.PumpNodeManagerFactory).Assembly;
            System.Type? seederType = pumpsAssembly.GetType(
                "Pumps.SoftwareUpdate.PumpSoftwareUpdateSeeder",
                throwOnError: true);
            MethodInfo? method = seederType!.GetMethod(
                "SeedAsync",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null,
                "PumpSoftwareUpdateSeeder.SeedAsync must exist.");

            object? result = method!.Invoke(null, new object[] { store });
            Assert.That(result, Is.InstanceOf<Task>());
            await ((Task)result!).ConfigureAwait(false);
        }
    }
}
