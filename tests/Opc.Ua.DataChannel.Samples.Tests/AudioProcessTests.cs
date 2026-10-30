/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.DataChannel.Samples.Tests
{
    /// <summary>
    /// Validates the headless executable's real received PCM and finalized RIFF file.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class AudioProcessTests
    {
        /// <summary>
        /// Requires a finite real socket run, exact PCM format and sample data, without an audio device.
        /// </summary>
        [TestCase("dedicated")]
        [TestCase("shared")]
        [TestCase("quic", Category = "NativeDataChannelSamples")]
        [TestCase("webtransport", Category = "NativeDataChannelSamples")]
        public async Task HeadlessAudioWritesExactFinitePcmWave(string carrier)
        {
            using var run = new SampleProcessRun();
            string path = Path.Combine(run.DirectoryPath, "received.wav");
            ProcessResult process = await run.ExecuteAsync("ConsoleDataChannelAudio",
                ["--carrier", carrier, "--wav", path, "--duration-ms", "200", "--frame-ms", "20", "--revise"])
                .ConfigureAwait(false);
            Assert.That(process.ExitCode, Is.Zero, process.Output);
            JsonElement result = process.Summary();
            byte[] wave = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(wave.AsSpan(0, 4).SequenceEqual("RIFF"u8), Is.True);
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(4)), Is.EqualTo(wave.Length - 8));
                Assert.That(wave.AsSpan(8, 4).SequenceEqual("WAVE"u8), Is.True);
                Assert.That(result.GetProperty("Messages").GetInt32(), Is.EqualTo(10));
                Assert.That(result.GetProperty("Bytes").GetInt64(), Is.EqualTo(17640));
                Assert.That(result.GetProperty("Revision").GetUInt32(), Is.EqualTo(2));
                Assert.That(result.GetProperty("ResourcesReleased").GetBoolean(), Is.True);
                Assert.That(Directory.Exists(run.StateDirectory), Is.False);
            });
            (int format, int formatLength) = FindChunk(wave, "fmt "u8);
            Assert.That(formatLength, Is.GreaterThanOrEqualTo(16));
            Assert.Multiple(() =>
            {
                Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(wave.AsSpan(format)), Is.EqualTo(1));
                Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(wave.AsSpan(format + 2)), Is.EqualTo(1));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(format + 4)), Is.EqualTo(44100));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(format + 8)), Is.EqualTo(88200));
                Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(wave.AsSpan(format + 12)), Is.EqualTo(2));
                Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(wave.AsSpan(format + 14)), Is.EqualTo(16));
            });
            (int data, int dataLength) = FindChunk(wave, "data"u8);
            Assert.That(dataLength, Is.EqualTo(17640));
            short[] firstSamples = new short[8];
            for (int index = 0; index < firstSamples.Length; index++)
            {
                firstSamples[index] = BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(data + 2 * index));
            }
            Assert.Multiple(() =>
            {
                Assert.That(firstSamples, Is.EqualTo(new short[] { 0, 2, 11, 26, 45, 69, 96, 127 }));
                Assert.That(Convert.ToHexString(SHA256.HashData(wave.AsSpan(data, dataLength))),
                    Is.EqualTo(result.GetProperty("ReceivedSha256").GetString()));
                Assert.That(result.GetProperty("ReceivedSha256").GetString(),
                    Is.EqualTo(result.GetProperty("SourceSha256").GetString()));
                Assert.That(result.GetProperty("Sessions").GetInt32(), Is.EqualTo(carrier == "shared" ? 1 : 0));
            });
        }

        /// <summary>
        /// Requires each opaque audio message to contain complete samples, including fractional millisecond rounding.
        /// </summary>
        [TestCase(1, 2, 176, 2)]
        [TestCase(15, 30, 2644, 2)]
        [TestCase(250, 60000, 5292000, 240)]
        public async Task AudioMessagesKeepSampleAlignment(
            int frameMilliseconds, int durationMilliseconds, int bytes, int count)
        {
            using var run = new SampleProcessRun();
            string path = Path.Combine(run.DirectoryPath, "aligned.wav");
            ProcessResult process = await run.ExecuteAsync("ConsoleDataChannelAudio",
                ["--wav", path, "--frame-ms",
                    frameMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "--duration-ms", durationMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)])
                .ConfigureAwait(false);
            Assert.That(process.ExitCode, Is.Zero, process.Output);
            JsonElement result = process.Summary();
            byte[] wave = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            (int _, int length) = FindChunk(wave, "data"u8);
            Assert.Multiple(() =>
            {
                Assert.That(result.GetProperty("Messages").GetInt32(), Is.EqualTo(count));
                Assert.That(result.GetProperty("Bytes").GetInt64(), Is.EqualTo(bytes));
                Assert.That(length, Is.EqualTo(bytes));
                Assert.That(length % 2, Is.Zero);
            });
        }

        /// <summary>
        /// Crosses the existing 4.4-second melody boundary without emitting a short or partial application message.
        /// </summary>
        [Test]
        public async Task AudioMessagesRemainCompleteAcrossMelodyBoundary()
        {
            using var run = new SampleProcessRun();
            string path = Path.Combine(run.DirectoryPath, "loop.wav");
            ProcessResult process = await run.ExecuteAsync("ConsoleDataChannelAudio",
                ["--wav", path, "--frame-ms", "250", "--duration-ms", "4500"]).ConfigureAwait(false);
            Assert.That(process.ExitCode, Is.Zero, process.Output);
            JsonElement result = process.Summary();
            byte[] wave = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            (int data, int length) = FindChunk(wave, "data"u8);
            Assert.Multiple(() =>
            {
                Assert.That(result.GetProperty("Messages").GetInt32(), Is.EqualTo(18));
                Assert.That(length, Is.EqualTo(396900));
                Assert.That(wave.AsSpan(data + 388080, 16).ToArray(),
                    Is.EqualTo(new byte[] { 0, 0, 2, 0, 11, 0, 26, 0, 45, 0, 69, 0, 96, 0, 127, 0 }));
            });
        }

        /// <summary>
        /// Parses standard RIFF chunk lengths rather than assuming a particular library's header length.
        /// </summary>
        private static (int Offset, int Length) FindChunk(byte[] wave, ReadOnlySpan<byte> name)
        {
            int offset = 12;
            while (offset <= wave.Length - 8)
            {
                int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(offset + 4)));
                Assert.That(length, Is.LessThanOrEqualTo(wave.Length - offset - 8), "Truncated RIFF chunk.");
                if (wave.AsSpan(offset, 4).SequenceEqual(name))
                {
                    return (offset + 8, length);
                }
                offset = checked(offset + 8 + length + (length & 1));
            }
            Assert.Fail("The required RIFF chunk is absent.");
            return default;
        }
    }
}
