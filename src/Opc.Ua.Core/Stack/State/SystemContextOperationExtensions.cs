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

namespace Opc.Ua
{
    /// <summary>
    /// Reads the operation a system context was created for.
    /// </summary>
    public static class SystemContextOperationExtensions
    {
        /// <summary>
        /// Returns the operation the system context was created for, or <c>null</c> when the
        /// context does not belong to an operation.
        /// <para>
        /// A callback that receives an <see cref="ISystemContext"/> uses this to hand its
        /// operation to an API that has to know which operation invoked it, without having to
        /// downcast to a concrete context type. Note that this is not the same as casting the
        /// context to <see cref="IOperationContext"/>: a context is itself an operation context
        /// that delegates to the operation it was created for, so the cast yields the context
        /// rather than the operation, and the two are different objects.
        /// </para>
        /// <para>
        /// A context built for the server itself rather than for a request carries no operation
        /// and returns <c>null</c>.
        /// </para>
        /// </summary>
        /// <param name="context">The system context to read the operation from.</param>
        /// <returns>The operation context, or <c>null</c>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public static IOperationContext? GetOperationContext(this ISystemContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // SessionSystemContext and SystemContext are the two independent roots every context
            // in the stack derives from; neither derives from the other.
            return context switch
            {
                SessionSystemContext sessionContext => sessionContext.OperationContext,
                SystemContext systemContext => systemContext.OperationContext,
                _ => null
            };
        }
    }
}
