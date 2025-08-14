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
public static class Router { public static void Handle() {} }
public static class JoinFacade { public static void Handle() {} }
public static class ModerationService { public static void Check() {} }
