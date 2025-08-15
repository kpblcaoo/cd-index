using Microsoft.Extensions.DependencyInjection;

namespace TestDiApp;

public interface IFoo { }
public sealed class Foo : IFoo { }

public interface IBar { }
public sealed class Bar : IBar { }

public sealed class Baz { }

public sealed class MyHosted : Microsoft.Extensions.Hosting.IHostedService
{
    public System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken ct) => System.Threading.Tasks.Task.CompletedTask;
    public System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken ct) => System.Threading.Tasks.Task.CompletedTask;
}

public class Program
{
    public static void Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFoo, Foo>();                    // 1) generic TI,TImpl
        services.AddScoped<IBar, Bar>();                       //   scoped
        services.AddTransient<Baz>();                          // 2) self-binding (TI == TImpl)
        services.AddSingleton<IFoo>(sp => new Foo());          // 3) factory with TI
        services.AddHostedService<MyHosted>();                 // 4) hosted
    }
}
