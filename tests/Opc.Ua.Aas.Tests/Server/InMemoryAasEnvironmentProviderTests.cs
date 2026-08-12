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

using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Aas.Server;

namespace Opc.Ua.Aas.Tests.Server
{
    /// <summary>
    /// Exercises the in-memory environment source the hosting extensions install by default.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class InMemoryAasEnvironmentProviderTests
    {
        /// <summary>
        /// The single-environment convenience overload has to yield exactly that environment, since
        /// it is the shape every hosting sample uses.
        /// </summary>
        [Test]
        public async Task SingleEnvironmentIsYieldedOnceAsync()
        {
            AasEnvironment environment = AasServerTestData.CreateEnvironment();
            var provider = new InMemoryAasEnvironmentProvider(environment);

            List<AasEnvironment> environments = await CollectAsync(provider).ConfigureAwait(false);

            Assert.That(environments, Is.EqualTo(new[] { environment }));
        }

        /// <summary>
        /// Order matters because the projection host materializes in enumeration order and later
        /// environments may reference earlier ones.
        /// </summary>
        [Test]
        public async Task EnvironmentsAreYieldedInTheOrderTheyWereSuppliedAsync()
        {
            AasEnvironment first = AasServerTestData.CreateEnvironment();
            AasEnvironment second = AasServerTestData.CreateEnvironment();
            AasEnvironment third = AasServerTestData.CreateEnvironment();
            var provider = new InMemoryAasEnvironmentProvider(
                new ArrayOf<AasEnvironment>(new[] { first, second, third }));

            List<AasEnvironment> environments = await CollectAsync(provider).ConfigureAwait(false);

            Assert.That(environments, Is.EqualTo(new[] { first, second, third }));
        }

        /// <summary>
        /// A default-constructed <see cref="ArrayOf{T}"/> is null rather than empty, so it has to be
        /// normalized instead of faulting the enumeration.
        /// </summary>
        [Test]
        public async Task ANullCollectionIsTreatedAsEmptyAsync()
        {
            var provider = new InMemoryAasEnvironmentProvider(default(ArrayOf<AasEnvironment>));

            List<AasEnvironment> environments = await CollectAsync(provider).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(default(ArrayOf<AasEnvironment>).IsNull, Is.True);
                Assert.That(environments, Is.Empty);
            });
        }

        /// <summary>
        /// Cancellation has to be observed between environments so a shutdown during address space
        /// creation does not have to wait for the whole collection to project.
        /// </summary>
        [Test]
        public void EnumerationStopsWhenCancellationIsRequested()
        {
            var provider = new InMemoryAasEnvironmentProvider(
                new ArrayOf<AasEnvironment>(new[]
                {
                    AasServerTestData.CreateEnvironment(),
                    AasServerTestData.CreateEnvironment()
                }));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.That(
                () => CollectAsync(provider, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
        }

        /// <summary>
        /// A null entry would surface much later as a null reference deep inside materialization, so
        /// it is rejected where it can still be attributed to the provider.
        /// </summary>
        [Test]
        public void ANullEntryIsRejectedWithADiagnosticMessage()
        {
            var provider = new InMemoryAasEnvironmentProvider(
                new ArrayOf<AasEnvironment>(new[]
                {
                    AasServerTestData.CreateEnvironment(),
                    null!
                }));

            Assert.That(
                () => CollectAsync(provider),
                Throws.InvalidOperationException.With.Message
                    .EqualTo("An in-memory AAS environment entry is null."));
        }

        private static async Task<List<AasEnvironment>> CollectAsync(
            InMemoryAasEnvironmentProvider provider,
            CancellationToken cancellationToken = default)
        {
            var environments = new List<AasEnvironment>();
            await foreach (AasEnvironment environment in provider
                .GetEnvironmentsAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                environments.Add(environment);
            }
            return environments;
        }
    }
}
