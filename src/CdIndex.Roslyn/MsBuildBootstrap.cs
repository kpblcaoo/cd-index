using System;
using System.Threading;
using Microsoft.Build.Locator;

namespace CdIndex.Roslyn;

public static class MsBuildBootstrap
{
    private static int _registered;
    public static void EnsureRegistered()
    {
        // Fast path if we've already successfully (or benignly) attempted registration
        if (Volatile.Read(ref _registered) == 1 && MSBuildLocator.IsRegistered)
            return;

        if (Interlocked.Exchange(ref _registered, 1) == 1 && MSBuildLocator.IsRegistered)
            return;

        if (MSBuildLocator.IsRegistered)
            return; // another thread or earlier code path already did it

        try
        {
            MSBuildLocator.RegisterDefaults();
        }
        catch (InvalidOperationException ioe)
        {
            // If assemblies already loaded (typical in some CI / test runners) we cannot register anymore.
            // Treat as non-fatal: we proceed assuming environment already usable.
            if (ioe.Message.Contains("already loaded", StringComparison.OrdinalIgnoreCase))
                return;
            throw new InvalidOperationException("MSBuild SDK not found. Проверь global.json / setup-dotnet.", ioe);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("MSBuild SDK not found. Проверь global.json / setup-dotnet.", ex);
        }
    }
}
