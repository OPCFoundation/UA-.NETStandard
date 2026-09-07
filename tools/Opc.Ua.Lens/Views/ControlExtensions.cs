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
using Avalonia.Controls;

namespace UaLens.Views;

/// <summary>
/// Extension helpers for Avalonia control trees.  Centralises the
/// "this control MUST exist in the XAML" pattern that dialog and view
/// code-behinds need at every <c>InitializeComponent</c> hand-off, so
/// the null-forgiving operator (<c>!</c>) doesn't need to be sprinkled
/// at every call site.
/// </summary>
internal static class ControlExtensions
{
    /// <summary>
    /// Looks up a named child control and asserts it is non-null.  Throws
    /// <see cref="InvalidOperationException"/> with a useful diagnostic if
    /// the name is missing from the loaded XAML — this catches typos
    /// between the <c>x:Name</c> in <c>.axaml</c> and the lookup in the
    /// code-behind at startup rather than silently NREing on first use.
    /// </summary>
    /// <remarks>
    /// Equivalent to <c>FindControl&lt;T&gt;(name)!</c> with a clearer
    /// failure mode.  Use this from constructors of dialog/window
    /// code-behind files where each named control is guaranteed by the
    /// XAML compilation; the cast is metadata-only and does not change
    /// generated IL relative to the bang-operator form.
    /// </remarks>
    public static T RequiredControl<T>(this Control host, string name) where T : Control
    {
        T? c = host.FindControl<T>(name);
        if (c is null)
        {
            throw new InvalidOperationException(
                $"Control '{name}' of type {typeof(T).Name} not found in {host.GetType().Name}'s XAML.");
        }
        return c;
    }
}
