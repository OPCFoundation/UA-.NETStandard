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
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Security.Pkcs11.Tests
{
    /// <summary>
    /// Tests that the default loader pins each module path exactly once.
    /// </summary>
    [TestFixture]
    [Category("Pkcs11")]
    [Parallelizable(ParallelScope.All)]
    public class DefaultPkcs11LibraryLoaderTests
    {
        [Test]
        public void PinTakesOneReferencePerModulePath()
        {
            var pinned = new List<string>();
            var loader = new DefaultPkcs11LibraryLoader(path =>
            {
                pinned.Add(path);
                return true;
            });

            bool first = loader.Pin("/opt/lib/libsofthsm2.so");
            bool second = loader.Pin("/opt/lib/libsofthsm2.so");
            bool other = loader.Pin("/opt/lib/other.so");

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.True);
                Assert.That(second, Is.False);
                Assert.That(other, Is.True);
                Assert.That(pinned, Has.Count.EqualTo(2));
                Assert.That(pinned[0], Is.EqualTo("/opt/lib/libsofthsm2.so"));
                Assert.That(pinned[1], Is.EqualTo("/opt/lib/other.so"));
            });
        }

        [Test]
        public void PinDoesNotRetryAfterFailedPin()
        {
            int attempts = 0;
            var loader = new DefaultPkcs11LibraryLoader(_ =>
            {
                attempts++;
                return false;
            });

            loader.Pin("missing.so");
            bool again = loader.Pin("missing.so");

            Assert.Multiple(() =>
            {
                Assert.That(again, Is.False);
                Assert.That(attempts, Is.EqualTo(1));
            });
        }

        [Test]
        public void ConcurrentPinsOfSamePathPinOnce()
        {
            int attempts = 0;
            var loader = new DefaultPkcs11LibraryLoader(_ =>
            {
                System.Threading.Interlocked.Increment(ref attempts);
                return true;
            });

            Parallel.For(0, 64, _ => loader.Pin("/opt/lib/libsofthsm2.so"));

            Assert.That(attempts, Is.EqualTo(1));
        }

        [Test]
        public void ConstructorRejectsNullPinDelegate()
        {
            Assert.That(
                () => new DefaultPkcs11LibraryLoader(null!),
                Throws.ArgumentNullException);
        }
    }
}