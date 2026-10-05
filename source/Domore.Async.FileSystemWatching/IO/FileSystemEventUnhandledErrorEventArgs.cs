using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Domore.IO;

/// <summary>
/// Provides data for an unhandled file-system event error.
/// </summary>
public sealed class FileSystemEventUnhandledErrorEventArgs : EventArgs {
    private readonly IList<Func<Exception, CancellationToken, Task<bool>>> Callbacks = [];

    internal async Task<bool[]> Run(Exception exception, CancellationToken token) {
        var tasks = new List<Task<bool>>(Callbacks.Count);
        foreach (var callback in Callbacks) {
            try {
                var
                task = callback(exception, token);
                tasks.Add(task ?? Task.FromResult(false));
            }
            catch (Exception error) {
                tasks.Add(Task.FromException<bool>(error));
            }
        }
        return await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Adds a task delegate to invoke after all subscribers have handled the event.
    /// </summary>
    /// <param name="task">The delegate that returns the task to await.</param>
    public void Add(Func<Exception, CancellationToken, Task<bool>> task) {
        Callbacks.Add(task);
    }
}
