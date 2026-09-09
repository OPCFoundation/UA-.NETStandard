/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;

namespace Opc.Ua.OneFuzz.TestTarget
{
    /// <summary>
    /// Synthetic callbacks for subprocess contract tests. NEVER publish or register
    /// this assembly as a service target: NegativeControl deliberately crashes,
    /// exits without a receipt, or spins forever on its reserved control inputs.
    /// </summary>
    public static class ContractCallbacks
    {
        /// <summary>
        /// Provides an observable independent of the validator's own receipts.
        /// </summary>
        public static void Inspect(ReadOnlySpan<byte> input)
        {
            Console.WriteLine($"contract-executed:Inspect:{FixtureDependency.Hash(input)}");
        }

        /// <summary>
        /// A second exact callback with a different seed inventory.
        /// </summary>
        public static void Measure(ReadOnlySpan<byte> input)
        {
            Console.WriteLine($"contract-executed:Measure:{FixtureDependency.Hash(input)}");
        }

        /// <summary>
        /// Explicit NEGATIVE CONTROL, never a service target. Ordinary fixture seeds
        /// return normally; only the reserved inputs below activate destructive behavior.
        /// </summary>
        public static void NegativeControl(ReadOnlySpan<byte> input)
        {
            ReadOnlySpan<byte> receiptPrefix = "onefuzz-contract:receipt-"u8;
            if (input.StartsWith(receiptPrefix))
            {
                string change = Encoding.UTF8.GetString(input[receiptPrefix.Length..]);
                WriteControlMarker("receipt-" + change);
                WriteIncorrectReceipt(change, input);
                Environment.Exit(0);
            }

            if (input.SequenceEqual("onefuzz-contract:throw"u8))
            {
                WriteControlMarker("throw");
                throw new InvalidOperationException("SYNTHETIC_ONEFUZZ_CALLBACK_EXCEPTION");
            }

            if (input.SequenceEqual("onefuzz-contract:exit0"u8))
            {
                WriteControlMarker("exit0");
                Environment.Exit(0);
            }

            if (input.SequenceEqual("onefuzz-contract:spin"u8))
            {
                WriteControlMarker("spin");
                // Intentionally synchronous and noncooperative: only a process kill
                // can terminate this negative control. No timers or cancellation token.
                while (true)
                {
                    Interlocked.Increment(ref s_spinIterations);
                }
            }

            Console.WriteLine($"contract-executed:NegativeControl:{FixtureDependency.Hash(input)}");
        }

        private static void WriteControlMarker(string control)
        {
            using Process process = Process.GetCurrentProcess();
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"negative-control:{control}:{process.Id}:{process.StartTime.ToUniversalTime().Ticks}"));
            Console.Out.Flush();
        }

        private static void WriteIncorrectReceipt(string change, ReadOnlySpan<byte> input)
        {
            // Explicit negative control: imitate an incomplete/corrupted child
            // receipt and exit zero before the real replay code can write one.
            // Nothing here calls or references the validator implementation.
            string[] arguments = Environment.GetCommandLineArgs();
            int resultIndex = Array.IndexOf(arguments, "--result");
            int targetIndex = Array.IndexOf(arguments, "--target");
            if (resultIndex < 0 || targetIndex < 0)
            {
                throw new InvalidOperationException("Receipt negative controls require an isolated _replay child.");
            }

            var inputs = new JsonArray(new JsonObject
            {
                ["path"] = "fuzzing/ContractFixture/Corpus/NegativeControl/safe.bin",
                ["sha256"] = FixtureDependency.Hash(input)
            });
            var receipt = new JsonObject
            {
                ["target"] = arguments[targetIndex + 1],
                ["type"] = typeof(ContractCallbacks).FullName,
                ["method"] = nameof(NegativeControl),
                ["inputs"] = inputs
            };
            switch (change)
            {
                case "target":
                    receipt["target"] = "other.synthetic.target";
                    break;
                case "type":
                    receipt["type"] = "Synthetic.UnexpectedType";
                    break;
                case "method":
                    receipt["method"] = "OtherMethod";
                    break;
                case "count":
                    inputs.Clear();
                    break;
                case "extra":
                    inputs.Add(inputs[0]!.DeepClone());
                    break;
                case "path":
                    inputs[0]!["path"] = "fuzzing/ContractFixture/Corpus/NegativeControl/different.bin";
                    break;
                case "hash":
                    inputs[0]!["sha256"] = new string('0', 64);
                    break;
                default:
                    throw new InvalidOperationException("Unknown synthetic receipt control: " + change);
            }

            File.WriteAllText(arguments[resultIndex + 1], receipt.ToJsonString());
        }

        private static long s_spinIterations;
    }
}
