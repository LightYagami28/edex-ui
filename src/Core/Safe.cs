// SPDX-License-Identifier: GPL-3.0-only
using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace Edex.Core;

/// <summary>
/// Helper per eseguire letture "best-effort" che possono fallire per condizioni ambientali
/// attese (I/O, permessi, formato) senza propagare l'eccezione al chiamante.
/// </summary>
internal static class Safe
{
    private static readonly Type[] Expected =
    [
        typeof(IOException), typeof(UnauthorizedAccessException), typeof(InvalidOperationException), typeof(NotSupportedException),
        typeof(FormatException), typeof(Win32Exception), typeof(NetworkInformationException), typeof(System.Security.SecurityException),
    ];

    /// <summary>
    /// Esegue <paramref name="read"/> restituendo <paramref name="fallback"/> se viene sollevata
    /// un'eccezione attesa; le eccezioni inattese vengono propagate.
    /// </summary>
    internal static T Run<T>(Func<T> read, T fallback)
    {
        ArgumentNullException.ThrowIfNull(read);
        try
        {
            return read();
        }
        catch (Exception e) when (IsExpected(e))
        {
            Trace.WriteLine($"Safe.Run: eccezione attesa ignorata: {e.GetType().Name}: {e.Message}");
            return fallback;
        }
    }

    private static bool IsExpected(Exception e) => Array.Exists(Expected, type => type.IsInstanceOfType(e));
}