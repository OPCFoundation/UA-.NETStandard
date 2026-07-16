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
using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Tests for client-wide initial connect admission limiting.
    /// </summary>
    [TestFixture]
    [Category("RateLimiting")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ClientConnectRateLimiterTests
    {
        [Test]
        public async Task AcquireAsyncWaitsUntilPermitIsReleased()
        {
            using var gate = new RateLimiterClientConnectGate(maxConcurrency: 1);
            IDisposable first = await gate.AcquireAsync(CancellationToken.None)
                .ConfigureAwait(false);

            Task<IDisposable> secondAcquire = gate
                .AcquireAsync(CancellationToken.None)
                .AsTask();

            Assert.That(secondAcquire.IsCompleted, Is.False);

            first.Dispose();

            Task completed = await Task
                .WhenAny(secondAcquire, Task.Delay(TimeSpan.FromSeconds(5)))
                .ConfigureAwait(false);

            Assert.That(completed, Is.SameAs(secondAcquire));

            using IDisposable second = await secondAcquire.ConfigureAwait(false);
            Assert.That(second, Is.Not.Null);
        }

        [Test]
        public void ConstructorRejectsInvalidMaxConcurrency()
        {
            Assert.That(
                () => new RateLimiterClientConnectGate(0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
