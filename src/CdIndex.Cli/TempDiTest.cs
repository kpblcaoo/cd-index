да // TEMP: For testing DI extractor functionality - REMOVE BEFORE MERGE
using Microsoft.Extensions.DependencyInjection;

namespace CdIndex.Cli;

// Temporary minimal DI test interfaces - REMOVE BEFORE MERGE
public interface ITempTestService { }
public class TempTestService : ITempTestService { }

public static class TempDiTestCode
{
    // TEMP: Small DI registration sample for testing - REMOVE BEFORE MERGE
    public static void RegisterTempServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITempTestService, TempTestService>();
        services.AddScoped<TempTestService>();
        // This is just for testing DiExtractor - not used in actual CLI
    }
}
