using System;

public sealed class StartHandler {}
public sealed class StatsHandler {}

public static class RouterExtensions {
  public static void Map(this object r, string cmd, object h) {}
  public static void Register<T>(this object r, string cmd) {}
}

public class Program {
  public static void Main() {
    var router = new object();
    router.Map("/start", new StartHandler());
    router.Register<StatsHandler>("/stats");
  // constant-based registration
  router.Map(CommandConstants.Start, new StartHandler()); // duplicate intentional for dedup
  var ping = new PingHandler();
  router.Add(CommandConstants.Ping, ping);
    var text = "/ignored"; // noise
    if (text == "/help") { }
    if (text.Equals("/about")) { }
    if (text.StartsWith("/ban")) { }
  }
}
