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
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Opc.Ua;
using UaLens.StructuredValues;

namespace UaLens.Views;

/// <summary>
/// Thin modal wrapper around <see cref="ComplexValueEditor"/> for
/// editing a single nested structure value (array element or sub-field
/// of a parent struct). A separate commit flag distinguishes cancel from a
/// typed null value without wrapping Variant in Nullable.
/// </summary>
internal sealed partial class ComplexValueElementDialog : Window, IAsyncDisposable
{
    public ComplexValueElementDialog(
        NodeId dataTypeId,
        DataTypeDefinition? definition,
        IStructuredValueService service,
        Variant initial)
    {
        InitializeComponent();
        ComplexValueEditor editor = this.RequiredControl<ComplexValueEditor>("Editor");
        var ok = this.RequiredControl<Button>("OkButton");
        ok.IsEnabled = false;
        Closed += async (_, _) => await StopAsync().ConfigureAwait(true);
        Opened += async (_, _) =>
        {
            try
            {
                m_initialization = editor.InitializeAsync(dataTypeId, definition, service, initial, m_lifetime.Token);
                await m_initialization.ConfigureAwait(true);
                ok.IsEnabled = true;
            }
            catch (Exception ex)
            {
                this.RequiredControl<TextBlock>("StatusLabel").Text = ex.Message;
            }
        };

        this.RequiredControl<Button>("OkButton").Click += (_, _) =>
        {
            if (editor.TryCommit(out Variant v, out string? err))
            {
                Result = v;
                WasCommitted = true;
                Close();
            }
            else
            {
                TextBlock status = this.RequiredControl<TextBlock>("StatusLabel");
                status.Text = err ?? "Could not commit value.";
                status.Foreground = (Application.Current?.FindResource("AccentRedLight") as IBrush)
                    ?? Brushes.Transparent;
            }
        };
        this.RequiredControl<Button>("CancelButton").Click += (_, _) => Close();
    }

    public Variant Result { get; private set; }

    public bool WasCommitted { get; private set; }

    public Task StopAsync()
    {
        return m_shutdown ??= StopCoreAsync();
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(StopAsync());
    }

    private async Task StopCoreAsync()
    {
        m_lifetime.Cancel();
        await this.RequiredControl<ComplexValueEditor>("Editor").StopAsync().ConfigureAwait(true);
        try
        {
            await m_initialization.ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Initialization errors are displayed by the Opened handler.
        }
        m_lifetime.Dispose();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private readonly CancellationTokenSource m_lifetime = new();
    private Task m_initialization = Task.CompletedTask;
    private Task? m_shutdown;
}
