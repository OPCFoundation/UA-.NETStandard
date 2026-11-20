// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
        ArrayOf<T> Results,
        ArrayOf<ServiceResult> Errors)
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
        public static ResultSet<T> From<T>(ArrayOf<T> results)
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
            return From(results.ToArrayOf());
        }

        /// <summary>
        /// Create result set
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="results"></param>
        /// <param name="errors"></param>
        /// <returns></returns>
        public static ResultSet<T> From<T>(
            ArrayOf<T> results,
            ArrayOf<ServiceResult> errors)
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
            return new(results.ToArrayOf(), errors.ToArrayOf());
        }
    }
}
