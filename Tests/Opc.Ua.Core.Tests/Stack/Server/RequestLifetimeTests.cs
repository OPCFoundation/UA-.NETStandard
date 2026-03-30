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
using System.Threading;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class RequestLifetimeTests
    {
        [Test]
        public void Constructor_Default_CreatesInstance()
        {
            using var lifetime = new RequestLifetime();

            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void Constructor_WithExternalToken_CreatesLinkedTokenSource()
        {
            using var cts = new CancellationTokenSource();
            using var lifetime = new RequestLifetime(cts.Token);

            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.False);
            
            cts.Cancel();

            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void TryCancel_WhenNotCancelled_ReturnsTrue_SetsStatusCode_CancelsToken()
        {
            using var lifetime = new RequestLifetime();

            bool result = lifetime.TryCancel(StatusCodes.BadTimeout);

            Assert.That(result, Is.True);
            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
        }

        [Test]
        public void TryCancel_WhenAlreadyCancelled_ReturnsFalse_LeavesStatusCode()
        {
            using var lifetime = new RequestLifetime();

            bool firstResult = lifetime.TryCancel(StatusCodes.BadTimeout);
            bool secondResult = lifetime.TryCancel(StatusCodes.BadUnexpectedError);

            Assert.That(firstResult, Is.True);
            Assert.That(secondResult, Is.False);
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.True);
        }

        [Test]
        public void TryCancel_AfterDispose_ReturnsFalse()
        {
            var lifetime = new RequestLifetime();
            lifetime.Dispose();

            bool result = lifetime.TryCancel(StatusCodes.BadTimeout);

            Assert.That(result, Is.False);
            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.False);
        }

        [Test]
        public void MarkCompleted_DisposesToken_CannotCancel()
        {
            var lifetime = new RequestLifetime();
            lifetime.MarkCompleted();

            bool result = lifetime.TryCancel(StatusCodes.BadTimeout);

            Assert.That(result, Is.False);

            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.False);
        }

        [Test]
        public void None_Property_ReturnsCompletedInstance()
        {
            var lifetime = RequestLifetime.None;

            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.Good));

            bool result = lifetime.TryCancel(StatusCodes.BadTimeout);

            Assert.That(result, Is.False);

            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.False);
        }
    }
}
