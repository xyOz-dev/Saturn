using Saturn.Data.Tasks;

namespace Saturn.Core.Tasks
{
    // Static access point for tools (which require parameterless constructors)
    // and for cross-layer wiring. Populated by WebServer at startup; remains
    // null in TUI mode, where task tools report the system as unavailable.
    public static class TaskSystem
    {
        public static TaskStore? Store { get; set; }
    }
}
