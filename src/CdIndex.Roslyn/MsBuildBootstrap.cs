using System;
using System.Threading;
using Microsoft.Build.Locator;

namespace CdIndex.Roslyn;

public static class MsBuildBootstrap
{
    private static int _registered;
    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
            return;
        if (!MSBuildLocator.IsRegistered)
        {
            try
            {
                MSBuildLocator.RegisterDefaults();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("MSBuild SDK not found. Проверь global.json / setup-dotnet.", ex);
            }
        }
    }
}
