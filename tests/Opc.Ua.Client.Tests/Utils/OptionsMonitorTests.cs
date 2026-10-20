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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Unit tests for the in-memory <see cref="OptionsMonitor{T}"/>.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    public sealed class OptionsMonitorTests
    {
        private static readonly string[] s_expectedNotifications = ["1:b", "2:b", "1:bc", "2:bc"];

        [Test]
        public void ListenersAreNotifiedInRegistrationOrder()
        {
            var monitor = new OptionsMonitor<string>("a");
            var calls = new List<string>();
            using IDisposable? first = monitor.OnChange((v, _) => calls.Add("1:" + v));
            using IDisposable? second = monitor.OnChange((v, _) => calls.Add("2:" + v));

            monitor.CurrentValue = "b";
            monitor.Configure(v => v + "c");

            Assert.That(calls, Is.EqualTo(s_expectedNotifications));
            Assert.That(monitor.Get(null), Is.EqualTo("bc"));
        }

        [Test]
        public void DisposedListenerIsRemovedAndDoubleDisposeIsNoOp()
        {
            var monitor = new OptionsMonitor<int>(0);
            int first = 0;
            int second = 0;
            int third = 0;
            IDisposable? r1 = monitor.OnChange((_, _) => first++);
            IDisposable? r2 = monitor.OnChange((_, _) => second++);
            IDisposable? r3 = monitor.OnChange((_, _) => third++);

            r2!.Dispose();
            r2.Dispose();
            monitor.CurrentValue = 1;
            Assert.That((first, second, third), Is.EqualTo((1, 0, 1)));

            r1!.Dispose();
            r3!.Dispose();
            monitor.CurrentValue = 2;
            Assert.That((first, second, third), Is.EqualTo((1, 0, 1)));
        }

        [Test]
        public void SameCallbackRegisteredTwiceIsCalledTwice()
        {
            var monitor = new OptionsMonitor<int>(0);
            int calls = 0;
            void Callback(int value, string? name) => calls++;
            IDisposable? r1 = monitor.OnChange(Callback);
            using IDisposable? r2 = monitor.OnChange(Callback);

            monitor.CurrentValue = 1;
            Assert.That(calls, Is.EqualTo(2));

            r1!.Dispose();
            monitor.CurrentValue = 2;
            Assert.That(calls, Is.EqualTo(3));
        }

        [Test]
        public async Task ConcurrentRegistrationAndDisposalKeepsListenersConsistentAsync()
        {
            var monitor = new OptionsMonitor<int>(0);
            int stable = 0;
            int leaked = 0;
            using IDisposable? keep = monitor.OnChange((_, _) => Interlocked.Increment(ref stable));

            var tasks = new Task[Environment.ProcessorCount * 2];
            for (int t = 0; t < tasks.Length; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        IDisposable? registration = monitor.OnChange(
                            (_, _) => Interlocked.Increment(ref leaked));
                        registration!.Dispose();
                    }
                });
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Only the stable listener is left, no removal was lost
            monitor.CurrentValue = 1;
            Assert.That((stable, leaked), Is.EqualTo((1, 0)));
            int transient = 0;
            using IDisposable? probe = monitor.OnChange((_, _) => transient++);
            monitor.CurrentValue = 2;
            Assert.That((stable, transient), Is.EqualTo((2, 1)));
        }
    }
}
