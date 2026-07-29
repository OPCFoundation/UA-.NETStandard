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

namespace Opc.Ua
{
    /// <summary>
    /// Optional <see cref="IFileSystem"/> capability that publishes a fully written
    /// file under its final name in a single indivisible step.
    /// <para>
    /// Durable writers stage content under a temporary name and then publish it, so
    /// that an interrupted write can never leave a partially written file visible at
    /// the destination. Expressing that as a separate capability keeps
    /// <see cref="IFileSystem"/> itself unchanged, which matters because that
    /// interface is implemented outside this repository and the library targets
    /// frameworks without default interface members.
    /// </para>
    /// <para>
    /// A file system that cannot publish indivisibly simply does not implement this
    /// interface. Callers that require durability must detect its absence and decide
    /// explicitly what to do, rather than silently degrading to a destructive write.
    /// </para>
    /// </summary>
    public interface IAtomicFileReplace
    {
        /// <summary>
        /// Publishes <paramref name="sourcePath"/> as <paramref name="destinationPath"/>
        /// in a single indivisible step, overwriting any existing destination.
        /// <para>
        /// The source is consumed by the operation. Observers of
        /// <paramref name="destinationPath"/> only ever see the complete previous
        /// content or the complete new content, never an intermediate state.
        /// </para>
        /// </summary>
        /// <param name="sourcePath">The staged file to publish.</param>
        /// <param name="destinationPath">The final name to publish it under.</param>
        void Replace(string sourcePath, string destinationPath);
    }
}
