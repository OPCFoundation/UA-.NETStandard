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
using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// The resource a <see cref="ResourceBudgetException"/> reports.
    /// </summary>
    public enum ResourceFindingKind
    {
        /// <summary>
        /// A decode allocated far more memory than its input can justify.
        /// </summary>
        Allocation,

        /// <summary>
        /// An input took longer than the time budget of its size, confirmed by re-runs.
        /// </summary>
        Time,

        /// <summary>
        /// A decoded value exceeded an encoding limit of the message context.
        /// </summary>
        Limit
    }

    /// <summary>
    /// Raised by a fuzz oracle when an input stays within the exception contract but abuses a
    /// resource: memory, CPU time, or an encoding limit the decoder should have enforced.
    /// <para>
    /// These are resource-abuse findings. Unlike an <see cref="EncodingFidelityException"/>
    /// they are never tolerated: a decoder must bound what an arbitrary input can cost.
    /// </para>
    /// </summary>
    public sealed class ResourceBudgetException : InvalidOperationException
    {
        /// <summary>
        /// Creates the exception for a finding of the given kind.
        /// </summary>
        public ResourceBudgetException(ResourceFindingKind kind, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Kind = kind;
        }

        /// <summary>
        /// Creates the exception with a default message.
        /// </summary>
        public ResourceBudgetException()
            : base("Resource budget exceeded.")
        {
        }

        /// <summary>
        /// Creates the exception with the supplied message.
        /// </summary>
        public ResourceBudgetException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates the exception with the supplied message and inner exception.
        /// </summary>
        public ResourceBudgetException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// The resource that was abused.
        /// </summary>
        public ResourceFindingKind Kind { get; }
    }

    /// <summary>
    /// Oracles that turn resource abuse into fuzzing findings.
    /// <para>
    /// Crash and exception oracles alone never flag an input that makes a decoder allocate
    /// gigabytes, recurse close to the stack limit or run for seconds, as long as it finally
    /// returns or throws the documented exception. These oracles make such inputs fail:
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="RunTarget"/> runs a target on a thread with a small stack, so recursion
    /// that would come close to the limit of a 1 MB thread overflows and is reported as a crash,
    /// and checks the time budget of the input.</item>
    /// <item><see cref="MeasureAllocation{T}"/> fails a decode that allocates more than
    /// <see cref="AllocationFactor"/> times its input plus <see cref="AllocationAllowance"/>.</item>
    /// </list>
    /// </summary>
    public static partial class FuzzOracles
    {
        /// <summary>
        /// The stack of the thread that runs a fuzz target. A quarter of the 1 MB default of a
        /// Windows thread: a decoder that is not guarded against deep nesting overflows it at a
        /// depth random mutation can reach, instead of only past 200 levels.
        /// </summary>
        public const int WorkerStackSize = 256 * 1024;

        /// <summary>
        /// Bytes a decode may allocate per input byte.
        /// </summary>
        public const long AllocationFactor = 64;

        /// <summary>
        /// Bytes a decode may allocate regardless of its input: readers, buffers, pools and
        /// the bounded preallocation of nested arrays.
        /// </summary>
        public const long AllocationAllowance = 8L * 1024 * 1024;

        /// <summary>
        /// Time budget of an input in milliseconds, before scaling, regardless of its size.
        /// </summary>
        public const int TimeBudgetBaseMilliseconds = 1000;

        /// <summary>
        /// Time budget in milliseconds per KB of input, before scaling. Generous for linear
        /// work; an input that takes longer does quadratic or worse work in its size.
        /// </summary>
        public const int TimeBudgetMillisecondsPerKilobyte = 100;

        /// <summary>
        /// Re-runs of an input over its time budget. Only the fastest run counts, so JIT,
        /// garbage collection and a noisy CI neighbour are not reported.
        /// </summary>
        public const int TimeConfirmationRuns = 2;

        /// <summary>
        /// Scales the time budget. 0 disables the time oracle.
        /// </summary>
        public const string TimeBudgetScaleVariable = "OPCUA_FUZZ_TIME_BUDGET_SCALE";

        /// <summary>
        /// Overrides the worker stack in KB. 0 runs targets on the calling thread.
        /// </summary>
        public const string WorkerStackVariable = "OPCUA_FUZZ_STACK_KB";

#if NETFRAMEWORK
        static FuzzOracles()
        {
            // The only allocation counter of .NET Framework; it cannot be switched off again.
            AppDomain.MonitoringIsEnabled = true;
        }
#endif

        /// <summary>
        /// Whether the allocation oracle can measure on this runtime.
        /// </summary>
        public static bool IsAllocationMeasurementSupported
        {
            get
            {
#if NETFRAMEWORK
                return AppDomain.MonitoringIsEnabled;
#else
                return true;
#endif
            }
        }

        /// <summary>
        /// The time budget of an input of the given size, 0 when the time oracle is disabled.
        /// </summary>
        public static long GetTimeBudgetMilliseconds(long inputLength)
        {
            double scale = s_timeBudgetScale.Value;
            if (scale <= 0)
            {
                return 0;
            }
            double budget = TimeBudgetBaseMilliseconds +
                (Math.Max(0, inputLength) * (double)TimeBudgetMillisecondsPerKilobyte / 1024);
            return (long)Math.Ceiling(budget * scale);
        }

        /// <summary>
        /// The allocation budget of a decode of the given input size.
        /// </summary>
        public static long GetAllocationBudget(long inputLength)
        {
            return AllocationAllowance + (Math.Max(0, inputLength) * AllocationFactor);
        }

        /// <summary>
        /// Runs a fuzz target on a thread with a <see cref="WorkerStackSize"/> stack and fails
        /// with <see cref="ResourceFindingKind.Time"/> if it is over the time budget of its
        /// input in the fastest of <see cref="TimeConfirmationRuns"/> additional runs.
        /// Exceptions of the target propagate unchanged.
        /// </summary>
        /// <exception cref="ResourceBudgetException"></exception>
        public static void RunTarget(string target, long inputLength, Action run)
        {
            RunTarget(target, inputLength, run, GetTimeBudgetMilliseconds(inputLength));
        }

        /// <summary>
        /// Runs a fuzz target like <see cref="RunTarget(string, long, Action)"/> with an
        /// explicit time budget in milliseconds; 0 disables the time oracle.
        /// </summary>
        /// <exception cref="ResourceBudgetException"></exception>
        public static void RunTarget(string target, long inputLength, Action run, long budget)
        {
            if (run == null)
            {
                throw new ArgumentNullException(nameof(run));
            }

            long fastest = RunOnWorker(run);
            if (budget <= 0 || fastest <= budget)
            {
                return;
            }

            long first = fastest;
            for (int ii = 0; ii < TimeConfirmationRuns && fastest > budget; ii++)
            {
                fastest = Math.Min(fastest, RunOnWorker(run));
            }

            if (fastest > budget)
            {
                throw new ResourceBudgetException(
                    ResourceFindingKind.Time,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Time budget exceeded by target '{0}': {1} ms (first run {2} ms, fastest of {3}) " +
                        "for {4} input bytes, budget {5} ms.",
                        target,
                        fastest,
                        first,
                        TimeConfirmationRuns + 1,
                        inputLength,
                        budget));
            }
        }

        /// <summary>
        /// Runs a decode and fails with <see cref="ResourceFindingKind.Allocation"/> when it
        /// allocated more than the budget of its input, whether it returned or threw. A decode
        /// that throws over budget reports the finding with the decode exception as the inner
        /// exception.
        /// </summary>
        /// <exception cref="ResourceBudgetException"></exception>
        public static T MeasureAllocation<T>(string operation, long inputLength, Func<T> decode)
        {
            if (decode == null)
            {
                throw new ArgumentNullException(nameof(decode));
            }

            long before = GetAllocatedBytes();
            T result;
            try
            {
                result = decode();
            }
            catch (Exception ex) when (ex is not ResourceBudgetException)
            {
                CheckAllocation(operation, inputLength, before, ex);
                throw;
            }
            CheckAllocation(operation, inputLength, before, null);
            return result;
        }

        /// <summary>
        /// Runs a decode under <see cref="MeasureAllocation{T}"/>.
        /// </summary>
        public static void MeasureAllocation(string operation, long inputLength, Action decode)
        {
            if (decode == null)
            {
                throw new ArgumentNullException(nameof(decode));
            }

            _ = MeasureAllocation<object>(operation, inputLength, () =>
            {
                decode();
                return null!;
            });
        }

        private static void CheckAllocation(string operation, long inputLength, long before, Exception? inner)
        {
            if (before < 0)
            {
                return;
            }

            long allocated = GetAllocatedBytes() - before;
            long budget = GetAllocationBudget(inputLength);
            if (allocated > budget)
            {
                throw new ResourceBudgetException(
                    ResourceFindingKind.Allocation,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Allocation budget exceeded by '{0}': {1} bytes allocated for {2} input bytes, " +
                        "budget {3} bytes ({4}x input + {5}).{6}",
                        operation,
                        allocated,
                        inputLength,
                        budget,
                        AllocationFactor,
                        AllocationAllowance,
                        inner == null ? string.Empty : " The decode then failed with " + inner.GetType().Name + "."),
                    inner!);
            }
        }

        /// <summary>
        /// Bytes allocated so far, or -1 when this runtime cannot measure them.
        /// </summary>
        private static long GetAllocatedBytes()
        {
#if NETFRAMEWORK
            // Per domain rather than per thread, which only makes the oracle stricter.
            return AppDomain.MonitoringIsEnabled ? AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize : -1;
#else
            return GC.GetAllocatedBytesForCurrentThread();
#endif
        }

        private static long RunOnWorker(Action run)
        {
            int stackSize = s_workerStackSize.Value;
            if (stackSize <= 0 || s_isWorkerThread)
            {
                var stopwatch = Stopwatch.StartNew();
                run();
                return stopwatch.ElapsedMilliseconds;
            }

            s_worker ??= new SmallStackWorker(stackSize);
            return s_worker.Run(run);
        }

        private static double ReadTimeBudgetScale()
        {
            string value = Environment.GetEnvironmentVariable(TimeBudgetScaleVariable)!;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double scale) &&
                scale >= 0 ? scale : 1.0;
        }

        private static int ReadWorkerStackSize()
        {
            string value = Environment.GetEnvironmentVariable(WorkerStackVariable)!;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int kilobytes) &&
                kilobytes >= 0 ? kilobytes * 1024 : WorkerStackSize;
        }

        /// <summary>
        /// A background thread with a small stack that runs one target at a time for the
        /// thread that owns it. One per calling thread, so a hanging target only blocks the
        /// caller that is already waiting for it.
        /// </summary>
        private sealed class SmallStackWorker : IDisposable
        {
            public SmallStackWorker(int stackSize)
            {
                var thread = new Thread(Loop, stackSize)
                {
                    IsBackground = true,
                    Name = "Fuzz target (small stack)"
                };
                thread.Start();
            }

            // A strict ping-pong: the caller releases m_workReady and blocks on m_workDone,
            // the worker does the reverse, so only one side touches the shared fields at a
            // time and each release/acquire publishes them. No monitor sync root is needed.
            public long Run(Action run)
            {
                m_work = run;
                m_workReady.Release();
                m_workDone.Wait();

                ExceptionDispatchInfo error = m_error;
                long elapsed = m_elapsed;
                m_error = null!;
                error?.Throw();
                return elapsed;
            }

            private void Loop()
            {
                s_isWorkerThread = true;
                while (true)
                {
                    m_workReady.Wait();
                    Action work = m_work!;
                    m_work = null!;

                    ExceptionDispatchInfo? error = null;
                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        work!();
                    }
                    catch (Exception ex)
                    {
                        error = ExceptionDispatchInfo.Capture(ex);
                    }

                    m_elapsed = stopwatch.ElapsedMilliseconds;
                    m_error = error!;
                    m_workDone.Release();
                }
            }

            public void Dispose()
            {
                m_workReady.Dispose();
                m_workDone.Dispose();
            }

            private readonly SemaphoreSlim m_workReady = new(0, 1);
            private readonly SemaphoreSlim m_workDone = new(0, 1);
            private Action m_work = null!;
            private ExceptionDispatchInfo m_error = null!;
            private long m_elapsed;
        }

        private static readonly Lazy<double> s_timeBudgetScale = new(ReadTimeBudgetScale);
        private static readonly Lazy<int> s_workerStackSize = new(ReadWorkerStackSize);

        [ThreadStatic]
        private static SmallStackWorker? s_worker;

        [ThreadStatic]
        private static bool s_isWorkerThread;
    }
}
