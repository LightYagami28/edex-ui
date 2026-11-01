// SPDX-License-Identifier: GPL-3.0-only
using System.ComponentModel;
using System.Net.NetworkInformation;

namespace Edex.Core;

/// <summary>Runs a read from the operating system and returns a fallback when the system refuses or the data is unusable.</summary>
internal static class Safe
{
    public static T Run<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException
                                      or FormatException or Win32Exception or NetworkInformationException or System.Security.SecurityException)
        {
            return fallback;
        }
    }
}
