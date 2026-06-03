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
using NUnit.Framework;
using Opc.Ua.Identity;

namespace Opc.Ua.Core.Tests.Security.Identity
{
    /// <summary>
    /// Tests for <see cref="AccessToken"/>: ownership, dispose semantics,
    /// argument validation.
    /// </summary>
    [TestFixture]
    [Category("Identity")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class AccessTokenTests
    {
        [Test]
        public void CtorRejectsNullProfileUri()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new AccessToken(null!, [1, 2], DateTime.UtcNow, "user"))!;
            Assert.That(ex.ParamName, Is.EqualTo("profileUri"));
        }

        [Test]
        public void CtorRejectsNullTokenData()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new AccessToken(Profiles.JwtUserToken, null!, DateTime.UtcNow, "user"))!;
            Assert.That(ex.ParamName, Is.EqualTo("tokenData"));
        }

        [Test]
        public void TokenDataExposesPayload()
        {
            byte[] payload = [0x10, 0x20, 0x30];

            using var token = new AccessToken(
                Profiles.JwtUserToken,
                payload,
                DateTime.UtcNow.AddMinutes(30),
                "alice");

            Assert.That(token.TokenData.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public void DisposeClearsTokenBytes()
        {
            byte[] payload = [0x10, 0x20, 0x30];
            var token = new AccessToken(
                Profiles.JwtUserToken,
                payload,
                DateTime.UtcNow.AddMinutes(30),
                "alice");

            token.Dispose();

            // The original buffer (now zeroed) is owned by the token.
            Assert.That(payload, Is.EqualTo(new byte[] { 0, 0, 0 }));
        }

        [Test]
        public void TokenDataAccessAfterDisposeThrows()
        {
            var token = new AccessToken(
                Profiles.JwtUserToken,
                [0xAA],
                DateTime.UtcNow,
                "alice");
            token.Dispose();

            Assert.That(
                () =>
                {
                    ReadOnlySpan<byte> _ = token.TokenData;
                },
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            var token = new AccessToken(
                Profiles.JwtUserToken,
                [0xAA],
                DateTime.UtcNow,
                "alice");

            token.Dispose();
            Assert.That(token.Dispose, Throws.Nothing);
        }

        [Test]
        public void NullDisplayNameDefaultsToEmpty()
        {
            using var token = new AccessToken(
                Profiles.JwtUserToken,
                [0x01],
                DateTime.MaxValue,
                null!);
            Assert.That(token.DisplayName, Is.EqualTo(string.Empty));
        }

        [Test]
        public void NullGrantedScopesDefaultsToEmptyArray()
        {
            using var token = new AccessToken(
                Profiles.JwtUserToken,
                [0x01],
                DateTime.MaxValue,
                "alice");
            Assert.That(token.GrantedScopes, Is.Empty);
        }
    }
}
