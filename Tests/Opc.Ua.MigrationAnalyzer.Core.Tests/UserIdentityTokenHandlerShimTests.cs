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

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.MigrationAnalyzer.Core.Tests
{
    /// <summary>
    /// Runtime tests for <see cref="UserIdentityTokenHandlerShim"/>.
    /// Verifies the synchronous shim methods forward to the async
    /// counterparts on <see cref="IUserIdentityTokenHandler"/>.
    /// </summary>
    [TestFixture]
    [Category("Shim")]
    public class UserIdentityTokenHandlerShimTests
    {
        /// <summary>
        /// The sync <c>Encrypt</c> shim must invoke
        /// <see cref="IUserIdentityTokenHandler.EncryptAsync"/> on the
        /// underlying handler.
        /// </summary>
        [Test]
        public Task EncryptCallsEncryptAsyncAsync()
        {
            var mock = new Mock<IUserIdentityTokenHandler>(MockBehavior.Strict);
            mock.Setup(h => h.EncryptAsync(
                    It.IsAny<Certificate>(),
                    It.IsAny<byte[]>(),
                    It.IsAny<string>(),
                    It.IsAny<IServiceMessageContext>(),
                    It.IsAny<Nonce?>(),
                    It.IsAny<Certificate?>(),
                    It.IsAny<CertificateCollection?>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());

            byte[] nonce = [1, 2, 3, 4];
            const string policy = "http://opcfoundation.org/UA/SecurityPolicy#None";
#pragma warning disable CS0618 // Sync Encrypt is an intentional shim call; GlobalContext is a stable test stand-in.
            IServiceMessageContext ctx = ServiceMessageContext.GlobalContext;

            mock.Object.Encrypt(null!, nonce, policy, ctx);
#pragma warning restore CS0618

            mock.Verify(h => h.EncryptAsync(
                    null!,
                    nonce,
                    policy,
                    ctx,
                    null,
                    null,
                    null,
                    false,
                    default),
                Times.Once);
            return Task.CompletedTask;
        }

        /// <summary>
        /// The sync <c>Sign</c> shim must return the same
        /// <see cref="SignatureData"/> the async path produced.
        /// </summary>
        [Test]
        public Task SignReturnsAsyncResultAsync()
        {
            var expected = new SignatureData
            {
                Algorithm = "http://opcfoundation.org/UA/test-sign",
                Signature = [9, 8, 7, 6]
            };
            var mock = new Mock<IUserIdentityTokenHandler>(MockBehavior.Strict);
            mock.Setup(h => h.SignAsync(
                    It.IsAny<byte[]>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<SignatureData>(expected));

            byte[] payload = [1, 2, 3];
            const string policy = "http://opcfoundation.org/UA/SecurityPolicy#None";

#pragma warning disable CS0618 // Sync Sign is an intentional shim call.
            SignatureData actual = mock.Object.Sign(payload, policy);
#pragma warning restore CS0618

            Assert.That(actual, Is.SameAs(expected));
            mock.Verify(h => h.SignAsync(payload, policy, default), Times.Once);
            return Task.CompletedTask;
        }

        /// <summary>
        /// The sync <c>Verify</c> shim must propagate the boolean result
        /// from the async path.
        /// </summary>
        [Test]
        public Task VerifyReturnsAsyncResultAsync()
        {
            var mock = new Mock<IUserIdentityTokenHandler>(MockBehavior.Strict);
            mock.Setup(h => h.VerifyAsync(
                    It.IsAny<byte[]>(),
                    It.IsAny<SignatureData>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(true));

            byte[] data = [1, 2, 3];
            var sig = new SignatureData { Algorithm = "alg", Signature = [9] };
            const string policy = "http://opcfoundation.org/UA/SecurityPolicy#None";

#pragma warning disable CS0618 // Sync Verify is an intentional shim call.
            bool ok = mock.Object.Verify(data, sig, policy);
#pragma warning restore CS0618

            Assert.That(ok, Is.True);
            mock.Verify(h => h.VerifyAsync(data, sig, policy, default), Times.Once);
            return Task.CompletedTask;
        }
    }
}
