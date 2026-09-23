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
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace Opc.Ua.Client.ComplexTypes.Tests.TypeBuilder
{
    /// <summary>
    /// Regression tests for the dynamic assembly created by <see cref="AssemblyModule"/>.
    /// Each session load builds a fresh <see cref="AssemblyModule"/>; if its assembly is
    /// defined with <see cref="AssemblyBuilderAccess.Run"/> instead of
    /// <see cref="AssemblyBuilderAccess.RunAndCollect"/>, the assembly (and the OS handle
    /// backing it) can never be reclaimed, leaking one dynamic assembly per load.
    /// </summary>
    [TestFixture]
    [Category("ComplexTypes")]
    [Parallelizable]
    public class AssemblyModuleTests
    {
#if NET8_0_OR_GREATER
        /// <summary>
        /// The assembly must be defined as collectible. This is a direct,
        /// deterministic guard against silently reverting to
        /// <see cref="AssemblyBuilderAccess.Run"/>.
        /// </summary>
        [Test]
        public void AssemblyIsCollectible()
        {
            var module = new AssemblyModule();

            Assert.That(module.GetModuleBuilder().Assembly.IsCollectible, Is.True);
        }
#endif

        /// <summary>
        /// The dynamic assembly must actually be reclaimed by the GC once the
        /// module, its defined type and all instances of that type go out of scope.
        /// </summary>
        [Test]
        public void AssemblyIsReclaimedAfterReferencesReleased()
        {
            WeakReference weakAssemblyRef = CreateModuleWithTypeAndGetWeakAssemblyRef();

            GC.Collect(2, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();

            Assert.That(weakAssemblyRef.IsAlive, Is.False);
        }

        /// <summary>
        /// Builds an <see cref="AssemblyModule"/>, defines and instantiates a type in it
        /// (mirroring how <see cref="ComplexTypeBuilder"/> uses it to build a complex type),
        /// then returns a weak reference to the assembly without retaining any strong
        /// reference to the module, the type or the instance.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateModuleWithTypeAndGetWeakAssemblyRef()
        {
            var module = new AssemblyModule(
                $"___leak_test_assembly{DateTime.UtcNow.Ticks}");

            System.Reflection.Emit.TypeBuilder typeBuilder = module.GetModuleBuilder().DefineType(
                "Opc.Ua.ComplexTypes.Tests.AssemblyModuleLeakTestType",
                TypeAttributes.Public);

            Type type = typeBuilder.CreateType();

            // Instantiate the type to exercise the same shape of usage as a loaded
            // complex type instance, without keeping a strong reference around.
            _ = Activator.CreateInstance(type);

            return new WeakReference(type.Assembly);
        }
    }
}
