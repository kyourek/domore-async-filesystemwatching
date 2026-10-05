using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Domore.IO;

/// <summary>
/// Provides data for a subscription result event.
/// </summary>
public sealed class FileSystemEventResultEventArgs : EventArgs {
    private readonly IList<Func<FileSystemEventResult, CancellationToken, Task>> Callbacks = [];

    internal Task[] Run(FileSystemEventResult result, CancellationToken token) {
        var tasks = new List<Task>(Callbacks.Count);
        foreach (var callback in Callbacks) {
            try {
                var
                task = callback(result, token);
                tasks.Add(task ?? Task.CompletedTask);
            }
            catch (Exception exception) {
                tasks.Add(Task.FromException(exception));
            }
        }
        return [.. tasks];
    }

    /// <summary>
    /// Adds a task delegate to invoke after all subscribers have handled the event.
    /// </summary>
    /// <param name="task">The delegate that returns the task to await.</param>
    public void Add(Func<FileSystemEventResult, CancellationToken, Task> task) {
        Callbacks.Add(task);
    }
}
