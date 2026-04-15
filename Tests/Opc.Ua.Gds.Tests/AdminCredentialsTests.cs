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
using NUnit.Framework;
using Opc.Ua.Gds.Client;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class AdminCredentialsTests
    {
        [Test]
        public void ConstructorCreatesInstance()
        {
            var args = new AdminCredentialsRequiredEventArgs();
            Assert.That(args, Is.Not.Null);
        }

        [Test]
        public void InheritsFromEventArgs()
        {
            var args = new AdminCredentialsRequiredEventArgs();
            Assert.That(args, Is.InstanceOf<EventArgs>());
        }

        [Test]
        public void CredentialsDefaultsToNull()
        {
            var args = new AdminCredentialsRequiredEventArgs();
            Assert.That(args.Credentials, Is.Null);
        }

        [Test]
        public void CredentialsRoundTrip()
        {
            var args = new AdminCredentialsRequiredEventArgs();
            using var identity = new UserIdentity();
            args.Credentials = identity;
            Assert.That(args.Credentials, Is.SameAs(identity));
        }

        [Test]
        public void CacheCredentialsDefaultsToFalse()
        {
            var args = new AdminCredentialsRequiredEventArgs();
            Assert.That(args.CacheCredentials, Is.False);
        }

        [Test]
        public void CacheCredentialsRoundTrip()
        {
            var args = new AdminCredentialsRequiredEventArgs
            {
                CacheCredentials = true
            };
            Assert.That(args.CacheCredentials, Is.True);
        }
    }
}
