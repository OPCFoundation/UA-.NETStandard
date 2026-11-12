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
