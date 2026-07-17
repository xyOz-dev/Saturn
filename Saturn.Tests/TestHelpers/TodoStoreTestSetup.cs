using System;
using System.IO;
using System.Runtime.CompilerServices;
using Saturn.Data;
using Saturn.Tools.Todo;

namespace Saturn.Tests.TestHelpers
{
    // TodoStore is a process-wide static that by default persists to
    // .saturn/chats.db in the current directory. Redirect it to an isolated
    // per-run workspace before any test touches it, so unit tests never
    // depend on (or corrupt) a shared database file.
    internal static class TodoStoreTestSetup
    {
        [ModuleInitializer]
        internal static void RedirectTodoPersistenceToTempWorkspace()
        {
            var workspace = Path.Combine(Path.GetTempPath(), $"SaturnTodoStoreTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(workspace);
            TodoStore.OverrideRepositoryFactory(() => new ChatHistoryRepository(workspace));

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try
                {
                    Directory.Delete(workspace, recursive: true);
                }
                catch
                {
                    // Best-effort cleanup; the DB file may still be locked.
                }
            };
        }
    }
}
