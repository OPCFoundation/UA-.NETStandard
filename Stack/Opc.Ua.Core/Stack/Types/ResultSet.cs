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

#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Opc.Ua
{
    /// <summary>
    /// Result set
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="Results"></param>
    /// <param name="Errors"></param>
    public readonly record struct ResultSet<T>(
        IReadOnlyList<T> Results,
        IReadOnlyList<ServiceResult> Errors)
    {
        /// <summary>
        /// Empty result set
        /// </summary>
        public static ResultSet<T> Empty { get; } = new([], []);
    }

    /// <summary>
    /// Result set helpers
    /// </summary>
    public static class ResultSet
    {
        /// <summary>
        /// Create result set
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="results"></param>
        /// <returns></returns>
        public static ResultSet<T> From<T>(IReadOnlyList<T> results)
        {
            return results.Count == 0 ?
                ResultSet<T>.Empty :
                new(results, [.. Enumerable.Repeat(ServiceResult.Good, results.Count)]);
        }

        /// <summary>
        /// Create result set
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="results"></param>
        /// <returns></returns>
        public static ResultSet<T> From<T>(IEnumerable<T> results)
        {
            return From(results.ToArray());
        }

        /// <summary>
        /// Create result set
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="results"></param>
        /// <param name="errors"></param>
        /// <returns></returns>
        public static ResultSet<T> From<T>(
            IReadOnlyList<T> results,
            IReadOnlyList<ServiceResult> errors)
        {
            return results.Count == 0 ?
                ResultSet<T>.Empty :
                new(results, errors);
        }

        /// <summary>
        /// Create result set
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="results"></param>
        /// <param name="errors"></param>
        /// <returns></returns>
        public static ResultSet<T> From<T>(
            IEnumerable<T> results,
            IEnumerable<ServiceResult> errors)
        {
            return new([.. results], [.. errors]);
        }
    }
}
