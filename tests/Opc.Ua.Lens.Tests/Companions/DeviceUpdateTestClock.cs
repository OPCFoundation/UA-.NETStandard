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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using Di = Opc.Ua.Di;
using static UaLens.Tests.Companions.IndustrialCompanionTestSession;

namespace UaLens.Tests.Companions;

internal sealed class DeviceUpdateTestClock
{
    public DeviceUpdateTestClock(bool autoAdvance = true)
    {
        Provider.SetupGet(value => value.TimestampFrequency).Returns(TimeSpan.TicksPerSecond);
        Provider.Setup(value => value.GetTimestamp()).Returns(() => m_timestamp);
        Provider.Setup(value => value.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns((TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            {
                Assert.That(dueTime, Is.EqualTo(TimeSpan.FromSeconds(1)));
                Assert.That(period, Is.EqualTo(Timeout.InfiniteTimeSpan));
                var timer = new Mock<ITimer>(MockBehavior.Strict);
                timer.Setup(value => value.Dispose());
                timer.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
                m_timers.Add(timer);
                TimerCreated.TrySetResult();
                if (autoAdvance)
                {
                    m_timestamp += dueTime.Ticks;
                    Tick?.Invoke(m_timers.Count);
                    callback(state);
                }
                return timer.Object;
            });
    }

    public Mock<TimeProvider> Provider { get; } = new(MockBehavior.Strict);

    public TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int TimerCount => m_timers.Count;

    public Action<int>? Tick { get; set; }

    public void VerifyDisposedTimers()
    {
        foreach (Mock<ITimer> timer in m_timers)
        {
            timer.Verify(value => value.Dispose(), Times.Once);
        }
    }

    private readonly List<Mock<ITimer>> m_timers = [];
    private long m_timestamp;
}
