// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input.Platform;

namespace UaLens.Views;

/// <summary>
/// Writes text to the system clipboard without blocking the Windows UI thread.
/// </summary>
internal static class ClipboardTextWriter
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const uint GmemZeroInit = 0x0040;
    private const int OpenAttempts = 10;
    private const int OpenRetryDelayMilliseconds = 50;

    /// <summary>
    /// Writes text using bounded native clipboard access on Windows and Avalonia elsewhere.
    /// </summary>
    internal static Task SetTextAsync(IClipboard? clipboard, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (OperatingSystem.IsWindows())
        {
            return Task.Run(() => SetWindowsText(text));
        }
        return clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
    }

    private static void SetWindowsText(string text)
    {
        OpenWindowsClipboard();
        IntPtr memory = IntPtr.Zero;
        try
        {
            if (!EmptyClipboard())
            {
                throw WindowsError("Could not clear the clipboard.");
            }

            int byteCount = checked((text.Length + 1) * sizeof(char));
            memory = GlobalAlloc(GmemMoveable | GmemZeroInit, (nuint)byteCount);
            if (memory == IntPtr.Zero)
            {
                throw WindowsError("Could not allocate clipboard memory.");
            }

            IntPtr target = GlobalLock(memory);
            if (target == IntPtr.Zero)
            {
                throw WindowsError("Could not lock clipboard memory.");
            }

            try
            {
                char[] characters = text.ToCharArray();
                Marshal.Copy(characters, 0, target, characters.Length);
                Marshal.WriteInt16(target, characters.Length * sizeof(char), 0);
            }
            finally
            {
                _ = GlobalUnlock(memory);
            }

            if (SetClipboardData(CfUnicodeText, memory) == IntPtr.Zero)
            {
                throw WindowsError("Could not store clipboard text.");
            }
            memory = IntPtr.Zero;
        }
        finally
        {
            if (memory != IntPtr.Zero)
            {
                _ = GlobalFree(memory);
            }
            _ = CloseClipboard();
        }
    }

    private static void OpenWindowsClipboard()
    {
        for (int attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                return;
            }
            if (attempt + 1 < OpenAttempts)
            {
                Thread.Sleep(OpenRetryDelayMilliseconds);
            }
        }
        throw WindowsError("Could not open the clipboard.");
    }

    private static Win32Exception WindowsError(string message)
    {
        return new Win32Exception(Marshal.GetLastPInvokeError(), message);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr newOwner);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
