using Domore.IO.FileSystemEventSubscriptions;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Domore.IO;

/// <summary>
/// Adds callbacks for file-system events.
/// </summary>
public static class FileSystemEventTasks {
    private static readonly FileSystemEventManager Manager = new();

    static FileSystemEventTasks() {
        Manager.OnSubscriptionEventCanceled += (s, e) => {
            OnSubscriptionEventCanceled?.Invoke(s, e);
        };
        Manager.OnSubscriptionEventComplete += (s, e) => {
            OnSubscriptionEventComplete?.Invoke(s, e);
        };
        Manager.OnSubscriptionEventError += (s, e) => {
            OnSubscriptionEventError?.Invoke(s, e);
        };
        Manager.OnUnhandledError += (s, e) => {
            OnUnhandledError?.Invoke(s, e);
        };
    }

    private static async Task<bool> RaiseUnhandledError(Exception exception, CancellationToken token) {
        var eventArgs = new FileSystemEventUnhandledErrorEventArgs();
        var handler = OnUnhandledError;
        handler?.Invoke(null, eventArgs);
        var results = await eventArgs.Run(exception, token);
        return results.Any(i => i);
    }

    private static async Task
    Manage(FileSystemEventSubscription subscription,
           Func<FileSystemEventManager, FileSystemEventSubscription, CancellationToken, Task> function,
           CancellationToken token) {
        try {
            var canceled = false;
            var exception = default(Exception);
            try {
                await function(Manager, subscription, token);
            }
            catch (OperationCanceledException ex1) when (token.IsCancellationRequested) {
                canceled = true;
                exception = ex1;
            }
            catch (Exception ex2) {
                exception = ex2;
            }
            var result = new FileSystemEventResult {
                Canceled = canceled,
                Exception = exception,
                Subscription = subscription,
            };
            if (result.Canceled) {
                var eventArgs = new FileSystemEventResultEventArgs();
                var handler = OnManagerCanceled;
                handler?.Invoke(null, eventArgs);
                var tasks = eventArgs.Run(result, token);
                await Task.WhenAll(tasks);
                return;
            }
            if (result.Exception is not null) {
                var eventArgs = new FileSystemEventResultEventArgs();
                var handler = OnManagerError;
                handler?.Invoke(null, eventArgs);
                var tasks = eventArgs.Run(result, token);
                await Task.WhenAll(tasks);
                return;
            }
        }
        catch (Exception ex) {
            var handled = await RaiseUnhandledError(ex, token);
            if (handled != true) {
                throw;
            }
        }
    }

    /// <summary>
    /// Occurs when an unhandled error occurs while managing file-system events.
    /// </summary>
    public static event EventHandler<FileSystemEventUnhandledErrorEventArgs> OnUnhandledError;

    /// <summary>
    /// Occurs when a subscription event completes successfully.
    /// </summary>
    public static event EventHandler<FileSystemEventResultEventArgs> OnSubscriptionEventComplete;

    /// <summary>
    /// Occurs when a subscription event is canceled.
    /// </summary>
    public static event EventHandler<FileSystemEventResultEventArgs> OnSubscriptionEventCanceled;

    /// <summary>
    /// Occurs when a subscription event fails with an error.
    /// </summary>
    public static event EventHandler<FileSystemEventResultEventArgs> OnSubscriptionEventError;

    /// <summary>
    /// Occurs when a manager operation fails with an error.
    /// </summary>
    public static event EventHandler<FileSystemEventResultEventArgs> OnManagerError;

    /// <summary>
    /// Occurs when a manager operation is canceled.
    /// </summary>
    public static event EventHandler<FileSystemEventResultEventArgs> OnManagerCanceled;

    /// <summary>
    /// Adds a callback for a file-system event.
    /// </summary>
    /// <param name="path">The path to the directory to be watched.</param>
    /// <param name="options">The options.</param>
    /// <param name="task">The callback.</param>
    /// <returns>
    /// An instance of <see cref="IDisposable"/> that, when disposed, removes the callback.
    /// </returns>
    public static IDisposable Add(string path,
                                  FileSystemEventOptions options,
                                  Func<FileSystemEventArgs, CancellationToken, Task> task) {
        var subscription = new ProxyFileSystemEventSubscription() {
            Agent = task
        };
        var added = Manage(subscription, (m, s, t) => m.Add(s, path, options, t), token: default);
        return new Disposable(added, subscription, path, options);
    }

    /// <summary>
    /// Adds a callback for a file-system event.
    /// </summary>
    /// <param name="path">The path to the directory to be watched.</param>
    /// <param name="task">The callback.</param>
    /// <returns>
    /// An instance of <see cref="IDisposable"/> that, when disposed, removes the callback.
    /// </returns>
    public static IDisposable Add(string path, Func<FileSystemEventArgs, CancellationToken, Task> task) {
        return Add(path, options: null, task);
    }

    private sealed class Disposable : IDisposable {
        private readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        DisposeLocker = new();

        private bool Disposed;

        public string Path { get; }
        public Task Added { get; }
        public FileSystemEventOptions Options { get; }
        public FileSystemEventSubscription Subscription { get; }

        public Disposable(Task added, FileSystemEventSubscription subscription, string path, FileSystemEventOptions options) {
            Added = added ?? throw new ArgumentNullException(nameof(added));
            Subscription = subscription;
            Path = path;
            Options = options;
        }

        void IDisposable.Dispose() {
            lock (DisposeLocker) {
                if (Disposed == true) {
                    return;
                }
                Added.ContinueWith(
                    cancellationToken: CancellationToken.None,
                    continuationOptions: TaskContinuationOptions.ExecuteSynchronously,
                    scheduler: TaskScheduler.Default,
                    continuationAction: _ => {
                        _ = Manage(Subscription, (m, s, t) => m.Remove(s, Path, Options, t), token: default);
                    });
                Disposed = true;
            }
        }
    }
}
