# Do more in .NET

**Domore.Async.FileSystemWatching** provides async callbacks for file-system events, with no `FileSystemWatcher` bookkeeping. It targets .NET Framework 4.6.2 and .NET 6 or later, so you can use it in a brand-new service or a decade-old desktop app.

The package is MIT licensed and ships with SourceLink and symbol packages, so you can step straight into the source while debugging.

## Packages

| Package | What it does |
|---------|--------------|
| [Domore.Async.FileSystemWatching](#domoreasyncfilesystemwatching) | Async callbacks for file-system events, with no `FileSystemWatcher` bookkeeping. |

Install it with `dotnet add package Domore.Async.FileSystemWatching`.

---

## Domore.Async.FileSystemWatching

React to file changes with an async callback. There's no `FileSystemWatcher` to create, configure, keep alive, or dispose correctly:

```csharp
using Domore.IO;

using var subscription = FileSystemEventTasks.Add(
    @"C:\logs",
    new FileSystemEventOptions { FileFilter = "*.log", IncludeSubdirectories = true },
    async (e, token) => {
        Console.WriteLine($"{e.ChangeType}: {e.Name}");
        await ProcessAsync(e.FullPath, token);
    });
```

Disposing the subscription removes the callback. Errors and cancellations are reported through static events that support multiple subscribers. Event handlers register task delegates with `eventArgs.Add`; those delegates receive the result or exception and cancellation token after the handlers return, and their tasks are awaited together. Subscriptions to the same directory share a single watcher, and a watcher that fails can restart if an unhandled-error delegate's task returns `true`. Targets .NET Framework 4.6.2 and .NET 6 or later.

📖 [Full Domore.Async.FileSystemWatching documentation](source/Domore.Async.FileSystemWatching/README.md)

---

## License

[MIT](LICENSE) © Ken Yourek
