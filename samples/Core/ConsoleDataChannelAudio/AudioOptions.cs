/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ConsoleDataChannelStreaming;

namespace Quickstarts.ConsoleDataChannelAudio
{
    /// <summary>
    /// Selects finite PCM production and an explicit headless output or supported playback device.
    /// </summary>
    internal sealed record AudioOptions(
        SampleOptions Connection,
        int FrameMilliseconds,
        int DurationMilliseconds,
        string? WavePath)
    {
        /// <summary>
        /// Separates audio application options from the common carrier and authorization options.
        /// </summary>
        public static AudioOptions Parse(string[] arguments)
        {
            var connectionArguments = new List<string>();
            int frameMilliseconds = 20;
            int durationMilliseconds = 1000;
            string? path = null;
            bool playback = false;
            for (int index = 0; index < arguments.Length; index++)
            {
                switch (arguments[index])
                {
                    case "--wav":
                        path = Path.GetFullPath(Next(arguments, ref index));
                        break;
                    case "--playback":
                        playback = true;
                        break;
                    case "--frame-ms":
                        frameMilliseconds = Number(Next(arguments, ref index), 1, 250);
                        break;
                    case "--duration-ms":
                        durationMilliseconds = Number(Next(arguments, ref index), 1, 60000);
                        break;
                    case "--count":
                    case "--size":
                    case "--interval-ms":
                    case "--mode":
                    case "--repeat":
                        throw new ArgumentException(
                            "For audio use --duration-ms and --frame-ms, not streaming options.");
                    default:
                        connectionArguments.Add(arguments[index]);
                        break;
                }
            }
            SampleOptions connection = SampleOptions.Parse([.. connectionArguments]);
            if (connection.ShowHelp)
            {
                return new AudioOptions(connection, frameMilliseconds, durationMilliseconds, path);
            }
            if (playback && path != null)
            {
                throw new ArgumentException("Choose either --wav FILE or --playback.");
            }
            if (path == null && !OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    "Playback requires Windows/NAudio. Use --wav FILE for headless output.");
            }
            if (durationMilliseconds < frameMilliseconds || durationMilliseconds % frameMilliseconds != 0)
            {
                throw new ArgumentException("--duration-ms must be a positive multiple of --frame-ms.");
            }
            connection = connection with
            {
                Count = durationMilliseconds / frameMilliseconds,
                Size = AudioFormat.BytesForDuration(frameMilliseconds),
                IntervalMilliseconds = path == null ? frameMilliseconds : 0
            };
            connection.Validate();
            return new AudioOptions(connection, frameMilliseconds, durationMilliseconds, path);
        }

        /// <summary>
        /// Prints finite, explicitly selected playback and headless modes.
        /// </summary>
        public static void PrintUsage()
        {
            Console.WriteLine(
                "ConsoleDataChannelAudio (.NET 10)\n" +
                "  --wav NEW-FILE.wav       finite headless output; no audio hardware needed\n" +
                "  --playback               default Windows device; no hardware fallback\n" +
                "  --duration-ms N          finite audio duration (default 1000)\n" +
                "  --frame-ms N             complete PCM message duration (default 20, range 1..250)\n" +
                "  --carrier dedicated|shared|quic|webtransport\n" +
                "  --authorization configured|session --publisher server|client --revise\n" +
                "  --timeout SECONDS --state NEW-DIRECTORY\n" +
                "  --quic-library ABSOLUTE-PATH --quic-sha256 EXPECTED-HASH\n" +
                "  --http3-library ABSOLUTE-PATH --http3-sha256 EXPECTED-HASH\n" +
                "Native asset environment variables are identical to ConsoleDataChannelStreaming.\n" +
                "PCM is the sample's fixed signed 16-bit little-endian, 44100 Hz mono application format.");
        }

        /// <summary>
        /// Reads a required audio option.
        /// </summary>
        private static string Next(string[] arguments, ref int index)
        {
            if (++index >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index]))
            {
                throw new ArgumentException("An audio option value is missing.");
            }
            return arguments[index];
        }

        /// <summary>
        /// Rejects unbounded durations and malformed numeric input.
        /// </summary>
        private static int Number(string value, int minimum, int maximum)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ||
                number < minimum || number > maximum)
            {
                throw new ArgumentException($"'{value}' must be an integer in [{minimum}, {maximum}].");
            }
            return number;
        }
    }
}
