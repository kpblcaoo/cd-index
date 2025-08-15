public sealed class MessageHandler {
  public void HandleAsync() {
    if (IsWhitelisted()) return;
    if (IsDisabled()) return;
    if (IsCommand()) { Router.Handle(); return; }
    if (IsPrivate()) return;
    if (HasNewMembers()) { JoinFacade.Handle(); return; }
    ModerationService.Check();
  }
  bool IsWhitelisted()=>true; bool IsDisabled()=>false; bool IsCommand()=>false; bool IsPrivate()=>false; bool HasNewMembers()=>false;
}
// Async variant
public sealed class MessageHandlerAsync {
  public async System.Threading.Tasks.Task HandleAsync() {
    if (Cond1()) return;
    await System.Threading.Tasks.Task.CompletedTask;
    if (Cond2()) { JoinFacade.Handle(); return; }
    ModerationService.Check();
  }
  bool Cond1()=>false; bool Cond2()=>false;
}
// ValueTask variant
public sealed class MessageHandlerVT {
  public async System.Threading.Tasks.ValueTask HandleAsync() {
    if (CondA()) return;
    ModerationService.Check();
    await System.Threading.Tasks.Task.Yield();
  }
  bool CondA()=>false;
}

namespace Another.Namespace {
  // Duplicate simple name for fallback resolution test
  public sealed class MessageHandler {
     public void HandleAsync() { if (X()) return; }
     bool X()=>false;
  }
}
public static class Router { public static void Handle() {} }
public static class JoinFacade { public static void Handle() {} }
public static class ModerationService { public static void Check() {} }
