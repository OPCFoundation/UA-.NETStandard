// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Equality and hashing of variants that hold nested variants recurse once per
    /// nesting level. They stop where the stack runs short, like the codecs do.
    /// </summary>
    /// <remarks>
    /// The tests run the comparison on a thread whose stack is used up to the point where
    /// the runtime reports that too little is left, so a shallow value is enough to show
    /// whether the stack is checked.
    /// </remarks>
    [TestFixture]
    [Category("BuiltInType")]
    [Parallelizable]
    public class VariantNestingStackTests
    {
        public enum Shape
        {
            VariantArray,
            DataValue
        }

        [TestCase(Shape.VariantArray)]
        [TestCase(Shape.DataValue)]
        public void NestedValuesAreNotComparedBeyondTheAvailableStack(Shape shape)
        {
            Variant left = Build(shape, 3);
            Variant right = Build(shape, 3);
            Assert.That(left, Is.EqualTo(right));

            bool equalAtStackLimit = RunAtStackLimit(() => left.Equals(right));
            Assert.That(equalAtStackLimit, Is.False);

            // scalars and arrays of scalars do not nest and are compared as usual.
            var scalar = new Variant(5);
            var array = new Variant(s_oneTwo);
            Assert.That(RunAtStackLimit(() => scalar.Equals(new Variant(5))), Is.True);
            Assert.That(RunAtStackLimit(() => array.Equals(new Variant(s_oneTwo))), Is.True);
        }

        private static readonly int[] s_oneTwo = [1, 2];

        /// <summary>
        /// The hash of a nested value does not depend on the stack left to the caller, and
        /// only the first levels of nesting contribute to it, so hashing does not recurse
        /// with the depth of the value.
        /// </summary>
        [TestCase(Shape.VariantArray)]
        [TestCase(Shape.DataValue)]
        public void NestedValuesHashTheSameAtAnyStackDepthAndOnlyTheirFirstLevels(Shape shape)
        {
            Variant value = Build(shape, 3);
            int hash = value.GetHashCode();
            Assert.That(hash, Is.EqualTo(Build(shape, 3).GetHashCode()));
            Assert.That(RunAtStackLimit(value.GetHashCode), Is.EqualTo(hash));

            // values that differ only below the hashed levels hash equally.
            Assert.That(Build(shape, 40, 1).GetHashCode(), Is.EqualTo(Build(shape, 40, 2).GetHashCode()));
            Assert.That(Build(shape, 1, 1).GetHashCode(), Is.Not.EqualTo(Build(shape, 1, 2).GetHashCode()));
        }

        /// <summary>
        /// A value nested as deep as the decoders accept is copied and
        /// compared on a thread with a 1 MB stack. Copying recursed through
        /// frames of several KB per level: a server cloning a written value
        /// for a monitored item overflowed the stack (which ends the process)
        /// at about 100 levels, and equality reported such a value unequal
        /// to an identical one.
        /// </summary>
        [TestCase(Shape.VariantArray)]
        [TestCase(Shape.DataValue)]
        public void ValuesNestedToTheDecoderLimitAreCopiedAndCompared(Shape shape)
        {
            int depth = DefaultEncodingLimits.MaxEncodingNestingLevels;
            Variant value = Build(shape, depth);

            (Variant copy, bool equal, bool equalToCopy) = RunOnThread(() =>
            {
                Variant c = CoreUtils.Clone(value);
                return (c, value.Equals(Build(shape, depth)), value.Equals(c));
            });

            Assert.That(equal, Is.True);
            Assert.That(equalToCopy, Is.True);
            Assert.That(copy.TypeInfo, Is.EqualTo(value.TypeInfo));
        }

        /// <summary>
        /// Copying a value nested deeper than the stack allows fails with a
        /// service result instead of overflowing the stack.
        /// </summary>
        [TestCase(Shape.VariantArray)]
        [TestCase(Shape.DataValue)]
        public void NestedValuesAreNotCopiedBeyondTheAvailableStack(Shape shape)
        {
            Variant value = Build(shape, 3);

            StatusCode status = RunAtStackLimit(() =>
            {
                try
                {
                    value.Copy();
                    return StatusCodes.Good;
                }
                catch (ServiceResultException sre)
                {
                    return sre.StatusCode;
                }
            });
            Assert.That(status, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));

            // scalars and arrays of scalars do not nest and are copied as usual.
            var array = new Variant(s_oneTwo);
            Assert.That(RunAtStackLimit(() => array.Copy()), Is.EqualTo(array));

            if (shape != Shape.VariantArray)
            {
                // a data value is a struct the copy shares, it does not recurse.
                return;
            }

            // a value far deeper than the stack fails the copy the same way.
            Variant deep = Build(shape, 20_000);
            StatusCode deepStatus = RunOnThread(() =>
            {
                try
                {
                    deep.Copy();
                    return StatusCodes.Good;
                }
                catch (ServiceResultException sre)
                {
                    return sre.StatusCode;
                }
            });
            Assert.That(deepStatus, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Formatting a value nested deeper than the stack allows elides the
        /// innermost levels instead of overflowing the stack.
        /// </summary>
        [TestCase(Shape.VariantArray)]
        [TestCase(Shape.DataValue)]
        public void NestedValuesAreNotFormattedBeyondTheAvailableStack(Shape shape)
        {
            Variant deep = Build(shape, 20_000);
            string text = RunOnThread(() => deep.ToString());
            Assert.That(text, Does.Contain("..."));
        }

        private static Variant Build(Shape shape, int depth, int innermost = 1)
        {
            var value = new Variant(innermost);
            for (int ii = 0; ii < depth; ii++)
            {
                value = shape == Shape.VariantArray
                    ? new Variant(new Variant[] { value, new Variant(ii) })
                    : new Variant(new DataValue(value));
            }
            return value;
        }

        /// <summary>
        /// Runs <paramref name="action"/> on a new thread after using up its stack to the
        /// point where the runtime reports that too little stack is left.
        /// </summary>
        private static T RunAtStackLimit<T>(Func<T> action)
        {
            T result = default!;
            Exception? error = null;
            var thread = new Thread(
                () =>
                {
                    try
                    {
                        result = Recurse(action, 0);
                    }
                    catch (Exception e)
                    {
                        error = e;
                    }
                },
                1024 * 1024);
            thread.Start();
            thread.Join();
            Assert.That(error, Is.Null, error?.ToString());
            return result;
        }

        /// <summary>
        /// Runs <paramref name="action"/> on a new thread with a 1 MB stack,
        /// the smallest default stack of a thread pool thread.
        /// </summary>
        private static T RunOnThread<T>(Func<T> action)
        {
            T result = default!;
            Exception? error = null;
            var thread = new Thread(
                () =>
                {
                    try
                    {
                        result = action();
                    }
                    catch (Exception e)
                    {
                        error = e;
                    }
                },
                1024 * 1024);
            thread.Start();
            thread.Join();
            Assert.That(error, Is.Null, error?.ToString());
            return result;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static T Recurse<T>(Func<T> action, int depth)
        {
            if (!HasSufficientStack())
            {
                return action();
            }

            // use a small amount of stack per frame to stop close to the limit.
            Span<byte> buffer = stackalloc byte[256];
            buffer[depth % buffer.Length] = 1;
            T result = Recurse(action, depth + 1);
            GC.KeepAlive(buffer[0]);
            return result;
        }

        private static bool HasSufficientStack()
        {
#if NET5_0_OR_GREATER
            return RuntimeHelpers.TryEnsureSufficientExecutionStack();
#else
            try
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
                return true;
            }
            catch (InsufficientExecutionStackException)
            {
                return false;
            }
#endif
        }
    }
}
