# Domore.Async.FileSystemWatching

React to file-system changes with async callbacks. Domore.Async.FileSystemWatching creates, configures, shares, restarts, and disposes `FileSystemWatcher` instances for you. You write the handler.

Install the package with `dotnet add package Domore.Async.FileSystemWatching`.

## Watch a directory

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

The callback receives the `FileSystemEventArgs` for each `Created`, `Changed`, `Deleted`, or `Renamed` event (for renames it's a `RenamedEventArgs`) and a cancellation token. Dispose the returned subscription to remove the callback. Omit the options to watch every file in the directory, not including subdirectories.

Watching starts in the background shortly after `Add` returns, so changes made immediately afterward may not be reported. If you need to know exactly when watching begins, use [`FileSystemEventProvider`](#stream-events) and its `ready` callback.

## Options

| Option | Default | Description |
|--------|---------|-------------|
| `FileFilter` | all files | A wildcard filter such as `*.log`. |
| `IncludeSubdirectories` | `false` | Also watch subdirectories. |
| `NotifyFilter` | all `NotifyFilters` | Which kinds of changes raise events. |
| `InternalBufferSize` | 65,536 | The size in bytes of the watcher's internal buffer. Larger buffers are less likely to overflow when many changes happen at once. |

`FileSystemEventOptions` is a record, so options with the same values are equal.

## How events are delivered

- **Watchers are shared.** Subscriptions to the same directory with equal options share a single `FileSystemWatcher`. Paths are compared by their full path, and on Windows the directory's case sensitivity is detected. When the last subscription is removed, the watcher is stopped after a short delay, so a quick unsubscribe and resubscribe reuses it.
- **Events arrive in order.** Each event is delivered to every subscription of a watcher at the same time. The next event isn't delivered until every subscription callback and every task registered for its result notifications has finished.
- **Callbacks run where you subscribed.** If `Add` is called with a `SynchronizationContext` (for example, on a UI thread), callbacks run on that context. Otherwise, they run on the thread pool.
- **Subscription failures are isolated.** An exception thrown by a file-change subscription callback doesn't affect other subscriptions or stop the watcher. Failures while reporting subscription results are sent to `OnUnhandledError` and can stop processing unless a subscriber requests that it continue.

## Handle results and errors

`FileSystemEventTasks` reports outcomes through static events that support multiple subscribers. Subscribe with `+=` and unsubscribe with `-=`; retain the event handler delegate if you need to remove it later.

Each event handler registers work with `eventArgs.Add((result, token) => ...)` or `eventArgs.Add((exception, token) => ...)`. `Add` stores the delegate without invoking it, and a subscriber may add multiple delegates. After the event handlers return, the event source invokes all registered delegates and awaits all returned tasks together. Register asynchronous work through `Add` so its completion and failures are observed. Result delegates receive a `FileSystemEventResult` with the `Subscription`, whether it was `Canceled`, and any `Exception`, along with the operation's cancellation token:

```csharp
FileSystemEventTasks.OnSubscriptionEventError += (_, eventArgs) => {
    eventArgs.Add((result, token) => {
        Console.Error.WriteLine($"A file handler failed: {result.Exception.Message}");
        return Task.CompletedTask;
    });
};

FileSystemEventTasks.OnManagerError += (_, eventArgs) => {
    eventArgs.Add((result, token) => {
        Console.Error.WriteLine($"Could not watch the directory: {result.Exception.Message}");
        return Task.CompletedTask;
    });
};

FileSystemEventTasks.OnUnhandledError += (_, eventArgs) => {
    eventArgs.Add((exception, token) => {
        Console.Error.WriteLine($"The watcher failed: {exception.Message}");
        return Task.FromResult(true); // request a watcher restart
    });
};
```

If a registered delegate throws synchronously, the remaining delegates are still invoked and its exception is recorded in a faulted task. A result delegate that returns a null task is treated as completed. Failures in result notification handlers or their registered tasks are reported through `OnUnhandledError`.

Unhandled-error delegates receive the exception and cancellation token and return `Task<bool>`. All of those tasks are awaited. If they complete successfully and any result is `true`, processing continues after a subscription notification failure, or the watcher restarts after a watcher failure. If no delegates were added or all results are `false`, the watcher stops. A null task counts as `false`. For errors while reporting a manager operation, a `true` result marks the error as handled; without one, that operation's task remains faulted.

| Event | Raised when |
|---------|-------------|
| `OnSubscriptionEventComplete` | A callback finishes handling an event. |
| `OnSubscriptionEventError` | A callback throws. |
| `OnSubscriptionEventCanceled` | A callback is canceled because its watcher stopped. |
| `OnManagerError` | Adding or removing a subscription fails, for example because the directory doesn't exist. |
| `OnManagerCanceled` | Adding or removing a subscription is canceled. |
| `OnUnhandledError` | A watcher fails, for example when its buffer overflows, or reporting a subscription or manager result fails. Successful task responses containing `true` request continued processing, a watcher restart, or handling of a manager-operation error, as described above. |

## Stream events

`FileSystemEventProvider` exposes a single watcher as an `IAsyncEnumerable<FileSystemEventArgs>`, for code that would rather `await foreach` than subscribe:

```csharp
using Domore.IO;

var provider = new FileSystemEventProvider(@"C:\data", new FileSystemEventOptions { FileFilter = "*.csv" });

await foreach (var e in provider.Events(
    ready: token => {
        Console.WriteLine("Watching.");
        return Task.CompletedTask;
    },
    token: cancellationToken)) {
    Console.WriteLine($"{e.ChangeType}: {e.Name}");
}
```

The optional `ready` callback runs once the watcher is running, so changes made from it or after it are reported. The watcher is disposed when the enumeration ends. That happens when the loop exits or the watcher fails. Canceling the token also ends it, by throwing an `OperationCanceledException`. Invalid paths or options throw a `FileSystemWatcherInitializationException`.

## Custom subscriptions

For more control, derive from `FileSystemEventSubscription` and manage subscriptions with your own `FileSystemEventManager`. It exposes `OnSubscriptionEventComplete`, `OnSubscriptionEventError`, `OnSubscriptionEventCanceled`, and `OnUnhandledError` as instance events, using the same deferred task delegates as the static events. `OnManagerError` and `OnManagerCanceled` belong to the static `FileSystemEventTasks` facade; direct manager callers observe add/remove failures through the returned tasks:

```csharp
using Domore.IO;

public sealed class ReportImporter : FileSystemEventSubscription {
    protected override async Task Receive(FileSystemEventArgs e, CancellationToken token) {
        await ImportAsync(e.FullPath, token);
    }
}

var manager = new FileSystemEventManager();
manager.OnSubscriptionEventError += (_, eventArgs) => {
    eventArgs.Add((result, token) => LogAsync(result.Exception));
};

var importer = new ReportImporter();
await manager.Add(importer, @"C:\reports", options: null, token);
// ...
await manager.Remove(importer, @"C:\reports", options: null, token);
```

## Supported frameworks

.NET Framework 4.6.2, .NET 6, .NET 8, and .NET 10.
