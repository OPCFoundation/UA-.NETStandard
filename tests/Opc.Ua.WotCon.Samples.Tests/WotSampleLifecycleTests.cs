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
using System.Threading;
using System.Threading.Tasks;
using AggregationClient;
using NUnit.Framework;

namespace Opc.Ua.WotCon.Samples.Tests
{
    /// <summary>
    /// Verifies the hosted samples release their server resources during shutdown.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Category("Integration")]
    [Category("Samples")]
    [NonParallelizable]
    public sealed class WotSampleLifecycleTests
    {
        /// <summary>
        /// Server disposal after host shutdown must not access released session or role managers.
        /// </summary>
        [Test]
        public async Task MaterializedSamplesStopAndDisposeAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            WotSampleEnvironment environment = await WotSampleEnvironment
                .StartAsync(timeout.Token).ConfigureAwait(false);

            try
            {
                _ = await AggregationClientRunner.RunAsync(environment.ClientOptions, timeout.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                Assert.That(
                    async () => await environment.DisposeAsync().ConfigureAwait(false),
                    Throws.Nothing);
            }
        }
    }
}
