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
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Core.Diagnostics.Bindings;

using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Diagnostics.Tests.Bindings
{
    [TestFixture]
    public sealed class ChannelCaptureRegistryTests
    {
        [Test]
        public void CurrentObserverIsNullByDefault()
        {
            var registry = new ChannelCaptureRegistry();

            Assert.That(registry.CurrentObserver, Is.Null);
        }

        [Test]
        public void SetObserverReturnsNullPreviousWhenEmpty()
        {
            var registry = new ChannelCaptureRegistry();
            IFrameCaptureSink first = Mock.Of<IFrameCaptureSink>();

            IFrameCaptureSink? previous = registry.SetObserver(first);

            Assert.That(previous, Is.Null);
            Assert.That(registry.CurrentObserver, Is.SameAs(first));
        }

        [Test]
        public void SetObserverReturnsPreviouslyInstalledObserver()
        {
            var registry = new ChannelCaptureRegistry();
            IFrameCaptureSink first = Mock.Of<IFrameCaptureSink>();
            IFrameCaptureSink second = Mock.Of<IFrameCaptureSink>();
            registry.SetObserver(first);

            IFrameCaptureSink? previous = registry.SetObserver(second);

            Assert.That(previous, Is.SameAs(first));
            Assert.That(registry.CurrentObserver, Is.SameAs(second));
        }

        [Test]
        public void SetObserverNullClearsCurrentObserverAndReturnsPrevious()
        {
            var registry = new ChannelCaptureRegistry();
            IFrameCaptureSink first = Mock.Of<IFrameCaptureSink>();
            registry.SetObserver(first);

            IFrameCaptureSink? previous = registry.SetObserver(null);

            Assert.That(previous, Is.SameAs(first));
            Assert.That(registry.CurrentObserver, Is.Null);
        }

        [Test]
        public void TryClearObserverSucceedsWhenReferenceMatches()
        {
            var registry = new ChannelCaptureRegistry();
            IFrameCaptureSink observer = Mock.Of<IFrameCaptureSink>();
            registry.SetObserver(observer);

            bool cleared = registry.TryClearObserver(observer);

            Assert.That(cleared, Is.True);
            Assert.That(registry.CurrentObserver, Is.Null);
        }

        [Test]
        public void TryClearObserverFailsWhenReferenceDiffers()
        {
            var registry = new ChannelCaptureRegistry();
            IFrameCaptureSink installed = Mock.Of<IFrameCaptureSink>();
            IFrameCaptureSink other = Mock.Of<IFrameCaptureSink>();
            registry.SetObserver(installed);

            bool cleared = registry.TryClearObserver(other);

            Assert.That(cleared, Is.False);
            Assert.That(registry.CurrentObserver, Is.SameAs(installed),
                "Failed compare-and-swap must leave the installed observer in place.");
        }

        [Test]
        public void TryClearObserverFailsWhenNoObserverIsInstalled()
        {
            var registry = new ChannelCaptureRegistry();
            IFrameCaptureSink observer = Mock.Of<IFrameCaptureSink>();

            bool cleared = registry.TryClearObserver(observer);

            Assert.That(cleared, Is.False);
            Assert.That(registry.CurrentObserver, Is.Null);
        }

        [Test]
        public void RepeatedSetObserverWithSameInstanceReturnsItself()
        {
            // Setting the same observer twice is idempotent for the visible
            // state; the second call returns the same reference because the
            // CAS reads the value back out.
            var registry = new ChannelCaptureRegistry();
            IFrameCaptureSink observer = Mock.Of<IFrameCaptureSink>();
            registry.SetObserver(observer);

            IFrameCaptureSink? previous = registry.SetObserver(observer);

            Assert.That(previous, Is.SameAs(observer));
            Assert.That(registry.CurrentObserver, Is.SameAs(observer));
        }

        [Test]
        public void ConcurrentSetObserversNeverCorruptCurrentObserver()
        {
            // Stress check: many threads racing SetObserver(x) must always
            // land on one of the supplied observers, never a stale/null value.
            var registry = new ChannelCaptureRegistry();
            var observers = new IFrameCaptureSink[8];
            for (int i = 0; i < observers.Length; i++)
            {
                observers[i] = Mock.Of<IFrameCaptureSink>();
            }

            const int iterations = 200;
            Parallel.For(0, iterations * observers.Length, i =>
            {
                IFrameCaptureSink chosen = observers[i % observers.Length];
                registry.SetObserver(chosen);
            });

            IFrameCaptureSink? final = registry.CurrentObserver;
            Assert.That(final, Is.Not.Null);
            Assert.That(Array.IndexOf(observers, final), Is.GreaterThanOrEqualTo(0),
                "Final observer must be one of the values written by some thread.");
        }

        [Test]
        public void InterfaceContractAllowsPolymorphicUse()
        {
            // Smoke test that the concrete type can be passed via the
            // IChannelCaptureRegistry interface and behaves identically. The
            // explicit interface variable is deliberate (we are validating
            // the contract surface, not the implementation type).
#pragma warning disable CA1859
            IChannelCaptureRegistry registry = new ChannelCaptureRegistry();
#pragma warning restore CA1859
            IFrameCaptureSink observer = Mock.Of<IFrameCaptureSink>();

            IFrameCaptureSink? previous = registry.SetObserver(observer);

            Assert.That(previous, Is.Null);
            Assert.That(registry.CurrentObserver, Is.SameAs(observer));
            Assert.That(registry.TryClearObserver(observer), Is.True);
            Assert.That(registry.CurrentObserver, Is.Null);
        }
    }
}
