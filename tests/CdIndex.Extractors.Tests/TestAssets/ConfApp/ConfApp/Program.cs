using System;

public interface IAppConfig { string AdminChatId { get; } bool AiEnabled { get; } }
public sealed class FeatureConfig { public string FeatureX { get; set; } = ""; }

public class Program {
  public static void Main() {
    var k1 = Environment.GetEnvironmentVariable("DOORMAN_BOT_API");
    var k2 = Environment.GetEnvironmentVariable("DOORMAN_LOG_ADMIN_CHAT");
    IAppConfig cfg = default!;
    var a = cfg.AdminChatId;
    var b = cfg.AiEnabled;
    FeatureConfig fcfg = default!;
    var c = fcfg.FeatureX;
  }
}
