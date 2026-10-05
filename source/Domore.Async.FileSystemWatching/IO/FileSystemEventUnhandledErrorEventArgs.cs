using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Domore.IO;

/// <summary>
/// Collects task delegates that respond to an unhandled file-system event error.
/// </summary>
/// <remarks>
/// Event subscribers use <see cref="Add"/> to register work. The event source invokes the delegates
/// after the event handlers return, supplies the exception and cancellation token, and awaits all returned
/// tasks. Adding a delegate does not invoke it. If all tasks complete successfully, any true result requests
/// that processing continue or the error be considered handled, according to the event source.
/// </remarks>
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
    /// Adds a task delegate to invoke after the event handlers return.
    /// </summary>
    /// <param name="task">
    /// The delegate that receives the exception and the operation's cancellation token and returns a task
    /// whose result indicates whether processing should continue or the error should be considered handled.
    /// </param>
    /// <remarks>
    /// A subscriber may add multiple delegates. All added delegates are invoked before their returned tasks
    /// are awaited together. A null returned task is treated as a false result. If a delegate throws synchronously,
    /// its exception is recorded in a faulted task so the remaining delegates are still invoked.
    /// Adding no delegates, or receiving only false results, requests that processing stop or that the error
    /// remain unhandled.
    /// </remarks>
    public void Add(Func<Exception, CancellationToken, Task<bool>> task) {
        Callbacks.Add(task);
    }
}
