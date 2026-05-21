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

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    [TestFixture]
    [Category("TransportChannelTests")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ChannelAsyncOperationTests
    {
        [Test]
        public async Task EndAsyncReturnsCompletedResponse()
        {
            using var operation = new ChannelAsyncOperation<int>(
                int.MaxValue,
                null,
                null,
                NullLogger.Instance);

            _ = Task.Run(async () =>
            {
                await Task.Delay(50).ConfigureAwait(false);
                operation.Complete(123);
            });

            int result = await operation.EndAsync(int.MaxValue).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(123));
            Assert.That(operation.Error.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void EndAsyncThrowsBadRequestInterruptedWhenCanceled()
        {
            using var operation = new ChannelAsyncOperation<int>(
                int.MaxValue,
                null,
                null,
                NullLogger.Instance);
            using var cancellationTokenSource = new CancellationTokenSource();

            Task<int> task = operation.EndAsync(int.MaxValue, ct: cancellationTokenSource.Token);
            cancellationTokenSource.Cancel();

            Assert.That(
                async () => await task.ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadRequestInterrupted));
        }

        [Test]
        public void EndAsyncThrowsBadRequestTimeoutWhenOperationTimesOut()
        {
            using var operation = new ChannelAsyncOperation<int>(
                50,
                null,
                null,
                NullLogger.Instance);

            Assert.That(
                async () => await operation.EndAsync(int.MaxValue).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadRequestTimeout));
        }
    }
}
