/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
