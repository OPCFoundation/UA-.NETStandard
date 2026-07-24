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

namespace Opc.Ua.Positioning
{
    /// <summary>
    /// Small dense linear algebra routines used by the ground control point
    /// fitting. Everything is implemented from scratch (no third party linear
    /// algebra) and works on square matrices with dimension two or three. The
    /// singular value decomposition uses a one sided Jacobi sweep which is
    /// numerically stable for the tiny matrices used here and allocation light.
    /// </summary>
    internal static class SmallLinearAlgebra
    {
        /// <summary>
        /// Returns the n x n identity matrix.
        /// </summary>
        public static double[,] Identity(int n)
        {
            double[,] m = new double[n, n];
            for (int i = 0; i < n; i++)
            {
                m[i, i] = 1.0;
            }
            return m;
        }

        /// <summary>
        /// Multiplies two n x n matrices.
        /// </summary>
        public static double[,] Multiply(double[,] a, double[,] b)
        {
            int n = a.GetLength(0);
            double[,] r = new double[n, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    double sum = 0.0;
                    for (int k = 0; k < n; k++)
                    {
                        sum += a[i, k] * b[k, j];
                    }
                    r[i, j] = sum;
                }
            }
            return r;
        }

        /// <summary>
        /// Returns the transpose of an n x n matrix.
        /// </summary>
        public static double[,] Transpose(double[,] a)
        {
            int n = a.GetLength(0);
            double[,] r = new double[n, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    r[j, i] = a[i, j];
                }
            }
            return r;
        }

        /// <summary>
        /// Computes the determinant of a two or three dimensional square matrix.
        /// </summary>
        public static double Determinant(double[,] a)
        {
            int n = a.GetLength(0);
            if (n == 2)
            {
                return (a[0, 0] * a[1, 1]) - (a[0, 1] * a[1, 0]);
            }
            return (a[0, 0] * ((a[1, 1] * a[2, 2]) - (a[1, 2] * a[2, 1]))) -
                (a[0, 1] * ((a[1, 0] * a[2, 2]) - (a[1, 2] * a[2, 0]))) +
                (a[0, 2] * ((a[1, 0] * a[2, 1]) - (a[1, 1] * a[2, 0])));
        }

        /// <summary>
        /// Attempts to invert an n x n matrix using Gauss-Jordan elimination
        /// with partial pivoting. Returns false when the matrix is singular.
        /// </summary>
        public static bool TryInvert(double[,] a, out double[,] inverse)
        {
            int n = a.GetLength(0);
            double[,] m = (double[,])a.Clone();
            inverse = Identity(n);

            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                double best = Math.Abs(m[col, col]);
                for (int r = col + 1; r < n; r++)
                {
                    double v = Math.Abs(m[r, col]);
                    if (v > best)
                    {
                        best = v;
                        pivot = r;
                    }
                }

                if (best < 1e-300)
                {
                    inverse = Identity(n);
                    return false;
                }

                if (pivot != col)
                {
                    SwapRows(m, col, pivot);
                    SwapRows(inverse, col, pivot);
                }

                double diag = m[col, col];
                for (int j = 0; j < n; j++)
                {
                    m[col, j] /= diag;
                    inverse[col, j] /= diag;
                }

                for (int r = 0; r < n; r++)
                {
                    if (r == col)
                    {
                        continue;
                    }
                    double factor = m[r, col];
                    if (factor == 0.0)
                    {
                        continue;
                    }
                    for (int j = 0; j < n; j++)
                    {
                        m[r, j] -= factor * m[col, j];
                        inverse[r, j] -= factor * inverse[col, j];
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Computes the singular value decomposition <c>A = U * diag(s) * V^T</c>
        /// of a square matrix using one sided Jacobi rotations. The singular
        /// values are returned in descending order and both <paramref name="u"/>
        /// and <paramref name="v"/> are orthonormal (proper or improper) bases.
        /// Degenerate columns are completed so that the returned factors remain
        /// orthonormal even for rank deficient input.
        /// </summary>
        public static void JacobiSvd(double[,] a, out double[,] u, out double[] s, out double[,] v)
        {
            int n = a.GetLength(0);

            // Work on a copy whose columns are rotated to become orthogonal.
            double[,] work = (double[,])a.Clone();
            v = Identity(n);

            const int kMaxSweeps = 60;
            const double kTolerance = 1e-16;

            for (int sweep = 0; sweep < kMaxSweeps; sweep++)
            {
                double offNorm = 0.0;
                for (int p = 0; p < n - 1; p++)
                {
                    for (int q = p + 1; q < n; q++)
                    {
                        double alpha = 0.0;
                        double beta = 0.0;
                        double gamma = 0.0;
                        for (int i = 0; i < n; i++)
                        {
                            double aip = work[i, p];
                            double aiq = work[i, q];
                            alpha += aip * aip;
                            beta += aiq * aiq;
                            gamma += aip * aiq;
                        }

                        offNorm += Math.Abs(gamma);
                        double denom = Math.Sqrt(alpha * beta);
                        if (denom < 1e-300 || Math.Abs(gamma) <= kTolerance * denom)
                        {
                            continue;
                        }

                        double zeta = (beta - alpha) / (2.0 * gamma);
                        double t = Math.Sign(zeta) / (Math.Abs(zeta) + Math.Sqrt(1.0 + (zeta * zeta)));
                        if (zeta == 0.0)
                        {
                            t = 1.0;
                        }
                        double c = 1.0 / Math.Sqrt(1.0 + (t * t));
                        double sn = c * t;

                        for (int i = 0; i < n; i++)
                        {
                            double aip = work[i, p];
                            double aiq = work[i, q];
                            work[i, p] = (c * aip) - (sn * aiq);
                            work[i, q] = (sn * aip) + (c * aiq);

                            double vip = v[i, p];
                            double viq = v[i, q];
                            v[i, p] = (c * vip) - (sn * viq);
                            v[i, q] = (sn * vip) + (c * viq);
                        }
                    }
                }

                if (offNorm < 1e-300)
                {
                    break;
                }
            }

            // Singular values are the norms of the rotated columns; U is the
            // normalised column. Collect then sort in descending order.
            s = new double[n];
            u = new double[n, n];
            for (int j = 0; j < n; j++)
            {
                double norm = 0.0;
                for (int i = 0; i < n; i++)
                {
                    norm += work[i, j] * work[i, j];
                }
                norm = Math.Sqrt(norm);
                s[j] = norm;
                if (norm > 1e-300)
                {
                    for (int i = 0; i < n; i++)
                    {
                        u[i, j] = work[i, j] / norm;
                    }
                }
            }

            SortDescending(u, s, v);
            CompleteOrthonormalColumns(u);
        }

        private static void SortDescending(double[,] u, double[] s, double[,] v)
        {
            int n = s.Length;
            for (int i = 0; i < n - 1; i++)
            {
                int max = i;
                for (int j = i + 1; j < n; j++)
                {
                    if (s[j] > s[max])
                    {
                        max = j;
                    }
                }
                if (max != i)
                {
                    (s[i], s[max]) = (s[max], s[i]);
                    SwapColumns(u, i, max);
                    SwapColumns(v, i, max);
                }
            }
        }

        private static void CompleteOrthonormalColumns(double[,] u)
        {
            int n = u.GetLength(0);
            for (int j = 0; j < n; j++)
            {
                double norm = 0.0;
                for (int i = 0; i < n; i++)
                {
                    norm += u[i, j] * u[i, j];
                }
                if (norm > 1e-18)
                {
                    continue;
                }

                if (n == 3)
                {
                    // Fill a zero column with the cross product of the other two.
                    int a = (j + 1) % 3;
                    int b = (j + 2) % 3;
                    double cx = (u[1, a] * u[2, b]) - (u[2, a] * u[1, b]);
                    double cy = (u[2, a] * u[0, b]) - (u[0, a] * u[2, b]);
                    double cz = (u[0, a] * u[1, b]) - (u[1, a] * u[0, b]);
                    double len = Math.Sqrt((cx * cx) + (cy * cy) + (cz * cz));
                    if (len > 1e-300)
                    {
                        u[0, j] = cx / len;
                        u[1, j] = cy / len;
                        u[2, j] = cz / len;
                        continue;
                    }
                }
                else if (n == 2)
                {
                    int a = (j + 1) % 2;
                    double px = -u[1, a];
                    double py = u[0, a];
                    double len = Math.Sqrt((px * px) + (py * py));
                    if (len > 1e-300)
                    {
                        u[0, j] = px / len;
                        u[1, j] = py / len;
                        continue;
                    }
                }

                // Fall back to a canonical axis if everything else is degenerate.
                u[j, j] = 1.0;
            }
        }

        private static void SwapRows(double[,] m, int r0, int r1)
        {
            int n = m.GetLength(1);
            for (int j = 0; j < n; j++)
            {
                (m[r0, j], m[r1, j]) = (m[r1, j], m[r0, j]);
            }
        }

        private static void SwapColumns(double[,] m, int c0, int c1)
        {
            int n = m.GetLength(0);
            for (int i = 0; i < n; i++)
            {
                (m[i, c0], m[i, c1]) = (m[i, c1], m[i, c0]);
            }
        }
    }
}
