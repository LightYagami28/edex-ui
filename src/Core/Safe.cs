// SPDX-License-Identifier: GPL-3.0-only
using System.ComponentModel;
using System.Net.NetworkInformation;

namespace Edex.Core;

internal static class Safe
{
    private static readonly Type[] Expected =
    [
        typeof(IOException), typeof(UnauthorizedAccessException), typeof(InvalidOperationException), typeof(NotSupportedException),
        typeof(FormatException), typeof(Win32Exception), typeof(NetworkInformationException), typeof(System.Security.SecurityException),
    ];

    internal static T Run<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (IsExpected(e))
        {
            return fallback;
        }
    }

    private static bool IsExpected(Exception e) => Array.Exists(Expected, type => type.IsInstanceOfType(e));
}
