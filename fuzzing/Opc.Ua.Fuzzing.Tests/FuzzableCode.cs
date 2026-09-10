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
using System.IO;
using System.Text;
using System.Threading;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Supplies observable callbacks and deliberately invalid signatures for the linked harness.
    /// </summary>
    public sealed class FuzzableCode
    {
        internal static List<(string Target, byte[] Input)> Invocations { get; } = [];

        internal static int FuzzInfoCalls { get; private set; }

        internal static Exception InjectedFailure { get; set; } =
            new InvalidOperationException("Injected fuzz target failure.");

        internal static ReadOnlySpan<byte> HangingInput => "opcua-fuzz-watchdog-regression"u8;

        internal static ReadOnlySpan<byte> CompletedInput => "opcua-fuzz-watchdog-control"u8;

        internal static ReadOnlySpan<byte> LargeOutputInput => "opcua-fuzz-output-control"u8;

        internal static string[] InvalidTargetNames =>
        [
            nameof(FuzzInfo),
            nameof(ReturningStreamTarget),
            nameof(ReturningStringTarget),
            nameof(ReturningSpanTarget),
            nameof(GenericStreamTarget),
            nameof(InstanceStreamTarget),
            nameof(MultipleParametersTarget),
            nameof(ByteArrayTarget),
            nameof(WritableSpanTarget),
            nameof(DerivedStreamTarget),
            nameof(ByRefStringTarget),
            nameof(HiddenStreamTarget),
            nameof(InternalStreamTarget)
        ];

        public static void FuzzInfo()
        {
            FuzzInfoCalls++;
        }

        public static void StreamTarget(Stream input)
        {
            RecordStream(nameof(StreamTarget), input);
        }

        public static void StringTarget(string input)
        {
            Invocations.Add((nameof(StringTarget), Encoding.UTF8.GetBytes(input)));
        }

        public static void SpanTarget(ReadOnlySpan<byte> input)
        {
            Invocations.Add((nameof(SpanTarget), input.ToArray()));
        }

        public static void ThrowingStreamTarget(Stream input)
        {
            RecordStream(nameof(ThrowingStreamTarget), input);
            throw InjectedFailure;
        }

        public static void ThrowingStringTarget(string input)
        {
            Invocations.Add((nameof(ThrowingStringTarget), Encoding.UTF8.GetBytes(input)));
            throw InjectedFailure;
        }

        public static void ThrowingSpanTarget(ReadOnlySpan<byte> input)
        {
            Invocations.Add((nameof(ThrowingSpanTarget), input.ToArray()));
            throw InjectedFailure;
        }

        public static void HangingSpanTarget(ReadOnlySpan<byte> input)
        {
            Invocations.Add((nameof(HangingSpanTarget), input.ToArray()));
            if (input.SequenceEqual(HangingInput))
            {
                Console.WriteLine(HangingTargetEntered);
                Console.Out.Flush();
                Thread.Sleep(Timeout.Infinite);
            }
            else if (input.SequenceEqual(CompletedInput))
            {
                Console.WriteLine(TargetCompleted);
            }
            else if (input.SequenceEqual(LargeOutputInput))
            {
                Console.Out.Write(new string('o', OutputLength));
                Console.Error.Write(new string('e', OutputLength));
            }
        }

        public static int ReturningStreamTarget(Stream input)
        {
            throw new InvalidOperationException(kInvalidTargetMessage);
        }

        public static string ReturningStringTarget(string input)
        {
            throw new InvalidOperationException(kInvalidTargetMessage);
        }

        public static int ReturningSpanTarget(ReadOnlySpan<byte> input)
        {
            throw new InvalidOperationException(kInvalidTargetMessage);
        }

        public static void GenericStreamTarget<T>(Stream input)
        {
            throw new InvalidOperationException(kInvalidTargetMessage);
        }

        public void InstanceStreamTarget(Stream input)
        {
            throw new InvalidOperationException(kInvalidTargetMessage);
        }

        public static void MultipleParametersTarget(Stream input, string text)
        {
            throw new InvalidOperationException(kInvalidTargetMessage);
        }

        public static void ByteArrayTarget(byte[] input)
        {
            throw new InvalidOperationException(kInvalidTargetMessage);
        }

        public static void WritableSpanTarget(Span<byte> input)
        {
            throw new InvalidOperationException(kInvalidTargetMessage);
        }

        public static void DerivedStreamTarget(MemoryStream input)
        {
            throw new InvalidOperationException(kInvalidTargetMessage);
        }

        public static void ByRefStringTarget(ref string input)
        {
            throw new InvalidOperationException(kInvalidTargetMessage);
        }

        internal static void Reset()
        {
            Invocations.Clear();
            FuzzInfoCalls = 0;
            InjectedFailure = new InvalidOperationException("Injected fuzz target failure.");
        }

        internal static void InternalStreamTarget(Stream input)
        {
            throw new InvalidOperationException(kInvalidTargetMessage);
        }

        private static void HiddenStreamTarget(Stream input)
        {
            throw new InvalidOperationException(kInvalidTargetMessage);
        }

        private static void RecordStream(string target, Stream input)
        {
            using var copy = new MemoryStream();
            input.CopyTo(copy);
            Invocations.Add((target, copy.ToArray()));
        }

        internal const string HangingTargetEntered = "Injected hanging target entered.";
        internal const string TargetCompleted = "Injected target completed.";
        internal const int OutputLength = 128 * 1024;

        private const string kInvalidTargetMessage = "An invalid fuzz target must not be invoked.";
    }
}
