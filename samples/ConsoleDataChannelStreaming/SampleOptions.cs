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
using System.Globalization;
using Opc.Ua;

namespace ConsoleDataChannelStreaming
{
    /// <summary>
    /// Which framing the sample exercises.
    /// </summary>
    internal enum SampleTransport
    {
        /// <summary>
        /// Inline framing: STR MessageChunks interleaved with Service
        /// traffic on one connection, with credit-based flow control.
        /// </summary>
        Tcp,

        /// <summary>
        /// opc.quic: the channel is bound to its own QUIC stream and QUIC
        /// applies the flow control.
        /// </summary>
        Quic
    }

    /// <summary>
    /// The command line of the sample.
    /// </summary>
    internal sealed record SampleOptions
    {
        /// <summary>
        /// Which framing to exercise.
        /// </summary>
        public SampleTransport Transport { get; init; } = SampleTransport.Tcp;

        /// <summary>
        /// How many frames to send.
        /// </summary>
        public int FrameCount { get; init; } = 300;

        /// <summary>
        /// The payload size of each frame.
        /// </summary>
        public int FrameSize { get; init; } = 1200;

        /// <summary>
        /// The delivery guarantee to negotiate.
        /// </summary>
        public DataChannelDeliveryMode DeliveryMode { get; init; }
            = DataChannelDeliveryMode.ReliableOrdered;

        /// <summary>
        /// True when the user asked for usage.
        /// </summary>
        public bool ShowHelp { get; init; }

        /// <summary>
        /// Parses the command line.
        /// </summary>
        /// <param name="args">The arguments.</param>
        public static SampleOptions Parse(string[] args)
        {
            var options = new SampleOptions();

            for (int ii = 0; ii < args.Length; ii++)
            {
                string argument = args[ii];

                switch (argument)
                {
                    case "-h":
                    case "--help":
                        return options with { ShowHelp = true };
                    case "--transport":
                        options = options with
                        {
                            Transport = Next(args, ref ii)
                                .Equals("quic", StringComparison.OrdinalIgnoreCase)
                                ? SampleTransport.Quic
                                : SampleTransport.Tcp
                        };
                        break;
                    case "--frames":
                        options = options with { FrameCount = ParseInt(Next(args, ref ii)) };
                        break;
                    case "--size":
                        options = options with { FrameSize = ParseInt(Next(args, ref ii)) };
                        break;
                    case "--mode":
                        options = options with { DeliveryMode = ParseMode(Next(args, ref ii)) };
                        break;
                    default:
                        Console.Error.WriteLine($"unknown argument '{argument}'");
                        return options with { ShowHelp = true };
                }
            }

            return options;
        }

        /// <summary>
        /// Prints the usage banner.
        /// </summary>
        public static void PrintUsage()
        {
            Console.WriteLine("usage: ConsoleDataChannelStreaming [options]");
            Console.WriteLine();
            Console.WriteLine("  --transport tcp|quic  framing to exercise (default tcp)");
            Console.WriteLine("  --frames N            frames to send (default 300)");
            Console.WriteLine("  --size N              payload bytes per frame (default 1200,");
            Console.WriteLine("                        which fits one QUIC datagram without");
            Console.WriteLine("                        IP fragmentation)");
            Console.WriteLine("  --mode reliable|reliable-unordered|partial|unreliable");
            Console.WriteLine("  -h, --help            this text");
        }

        private static string Next(string[] args, ref int index)
        {
            index++;
            return index < args.Length ? args[index] : string.Empty;
        }

        private static int ParseInt(string value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : 0;
        }

        private static DataChannelDeliveryMode ParseMode(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "reliable-unordered":
                    return DataChannelDeliveryMode.ReliableUnordered;
                case "partial":
                    return DataChannelDeliveryMode.PartiallyReliable;
                case "unreliable":
                    return DataChannelDeliveryMode.Unreliable;
                default:
                    return DataChannelDeliveryMode.ReliableOrdered;
            }
        }
    }
}
