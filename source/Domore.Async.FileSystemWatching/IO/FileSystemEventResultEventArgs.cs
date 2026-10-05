using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Domore.IO;

/// <summary>
/// Collects task delegates to run when a subscription or manager operation reports its result.
/// </summary>
/// <remarks>
/// Event subscribers use <see cref="Add"/> to register work. The event source invokes the delegates
/// after the event handlers return, supplies the operation result and cancellation token, and awaits
/// all returned tasks. Adding a delegate does not invoke it.
/// </remarks>
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
    /// Adds a task delegate to invoke after the event handlers return.
    /// </summary>
    /// <param name="task">
    /// The delegate that receives the operation result and its cancellation token and returns a task to await.
    /// </param>
    /// <remarks>
    /// A subscriber may add multiple delegates. All added delegates are invoked before their returned tasks
    /// are awaited together. A null returned task is treated as completed. If a delegate throws synchronously,
    /// its exception is recorded in a faulted task so the remaining delegates are still invoked.
    /// </remarks>
    public void Add(Func<FileSystemEventResult, CancellationToken, Task> task) {
        Callbacks.Add(task);
    }
}
