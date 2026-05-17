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
using System.Threading.Channels;
using System.Threading.Tasks;
using UaLens.Subscriptions;

namespace UaLens;

/// <summary>
/// Headless validator for the bounded-channel DropOldest semantics used
/// by <c>ChannelV2EngineAdapter</c> and <c>ClassicEngineAdapter</c>.
/// The adapters' <c>WriteEventOrCount</c> helper relies on:
/// <list type="number">
///   <item><see cref="ChannelReader{T}.CanCount"/> + <see cref="ChannelReader{T}.Count"/>
///         returning a usable count on the SDK's bounded channels.</item>
///   <item>Each over-capacity write evicting exactly one (the OLDEST) event
///         so the surviving N items are always the most-recent N.</item>
/// </list>
/// We can't test the live adapter without a server, but the channel
/// configuration is identical so this probe verifies the contract that
/// adapter code depends on.
/// </summary>
internal static class ChannelDropProbe
{
    public static int Run()
    {
        Console.WriteLine("== Channel drop probe ==");
        int rc = 0;
        const int kCapacity = 8192;

        var channel = Channel.CreateBounded<NotificationEvent>(new BoundedChannelOptions(kCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });

        // CanCount must be supported on this channel.
        rc |= AssertEqual("Reader.CanCount", true, channel.Reader.CanCount);

        // Write kCapacity + extra; expect `extra` drops.
        const int extra = 137;
        long droppedHeuristic = 0;
        for (int i = 1; i <= kCapacity + extra; i++)
        {
            // Mimic the adapter's WriteEventOrCount logic exactly.
            if (channel.Reader.CanCount && channel.Reader.Count >= kCapacity)
            {
                Interlocked.Increment(ref droppedHeuristic);
            }
            bool ok = channel.Writer.TryWrite(new NotificationEvent(
                NotificationKind.DataChange,
                ItemId: 1,
                ValueCount: 1,
                SequenceNumber: (uint)i,
                ReceivedAtUtc: DateTime.UtcNow,
                Value: i));
            if (!ok)
            {
                Console.WriteLine($"  FAIL  TryWrite returned false at seq={i}");
                rc = 1;
            }
        }

        rc |= AssertEqual("Buffer size at capacity", kCapacity, channel.Reader.Count);
        rc |= AssertEqual("DroppedHeuristic == extra", (long)extra, droppedHeuristic);

        // Drain and verify the surviving range is [extra+1 .. capacity+extra].
        // (Since BoundedChannelFullMode.DropOldest evicts the front when a new
        // item arrives, the oldest `extra` items should be gone.)
        int firstSeq = -1;
        int lastSeq = -1;
        int count = 0;
        while (channel.Reader.TryRead(out NotificationEvent ne))
        {
            if (firstSeq < 0)
            {
                firstSeq = (int)ne.SequenceNumber;
            }

            lastSeq = (int)ne.SequenceNumber;
            count++;
        }
        rc |= AssertEqual("Drained count == capacity", kCapacity, count);
        rc |= AssertEqual("First surviving seq", extra + 1, firstSeq);
        rc |= AssertEqual("Last surviving seq", kCapacity + extra, lastSeq);

        Console.WriteLine(rc == 0 ? "CHANNEL DROP PROBE PASS" : "CHANNEL DROP PROBE FAIL");
        return rc;
    }

    private static int AssertEqual<T>(string what, T expected, T actual)
    {
        bool ok = System.Collections.Generic.EqualityComparer<T>.Default.Equals(expected, actual);
        Console.WriteLine(ok ? $"  PASS  {what}" : $"  FAIL  {what}: expected={expected}, actual={actual}");
        return ok ? 0 : 1;
    }
}
