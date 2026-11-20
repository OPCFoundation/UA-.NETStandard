// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
        Variant initial,
        int valueRank = ValueRanks.Scalar,
        ArrayOf<uint> declaredDimensions = default,
        CancellationToken cancellationToken = default)
    {
        m_lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        InitializeComponent();
        ComplexValueEditor editor = this.RequiredControl<ComplexValueEditor>("Editor");
        var ok = this.RequiredControl<Button>("OkButton");
        ok.IsEnabled = false;
        Closed += async (_, _) => await StopAsync().ConfigureAwait(true);
        Opened += async (_, _) =>
        {
            try
            {
                m_initialization = editor.InitializeValueAsync(
                    dataTypeId, definition, service, initial, valueRank, declaredDimensions, m_lifetime.Token);
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
                status.Foreground = (Application.Current?.FindResource("ErrorText") as IBrush)
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

    private readonly CancellationTokenSource m_lifetime;
    private Task m_initialization = Task.CompletedTask;
    private Task? m_shutdown;
}
