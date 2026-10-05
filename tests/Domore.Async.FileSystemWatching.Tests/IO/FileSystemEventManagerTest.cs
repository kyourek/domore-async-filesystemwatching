using Domore.IO.FileSystemEventSubscriptions;
using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Domore.IO;

[TestFixture]
internal sealed class FileSystemEventManagerTest {
    private string TempPath;

    public FileSystemEventManager Subject {
        get => field ??= new();
        set;
    }

    [SetUp]
    public void SetUp() {
        Subject = null;
        TempPath = Path.Combine(
            Path.GetTempPath(),
            "Domore.Async.FileSystemWatching.Tests",
            nameof(FileSystemEventManagerTest),
            Guid.NewGuid().ToString());
        Directory.CreateDirectory(TempPath);
    }

    [TearDown]
    public void TearDown() {
        Directory.Delete(TempPath, recursive: true);
    }

    [Test]
    public async Task Events_ChangeType_IsChanged() {
        var path = Path.Combine(TempPath, nameof(Events_ChangeType_IsChanged));
        var actual = default(WatcherChangeTypes);
        File.WriteAllText(path, "foo");
        await Subject.Add(
            path: TempPath,
            options: null,
            token: default,
            subscription: new ProxyFileSystemEventSubscription {
                Agent = async (e, _) => {
                    actual = e.ChangeType;
                    await Task.CompletedTask;
                }
            });
        await Task.Delay(250);
        File.WriteAllText(path, "bar");
        SpinWait.SpinUntil(() => actual != default, 2500);
        Assert.That(actual, Is.EqualTo(WatcherChangeTypes.Changed));
    }

    [Test]
    public async Task Events_ChangeType_IsChangedMultipleTimes() {
        var path = Path.Combine(TempPath, nameof(Events_ChangeType_IsChanged));
        var actual = default(int);
        File.WriteAllText(path, "foo");
        await Subject.Add(
            path: TempPath,
            options: new() { NotifyFilter = NotifyFilters.LastWrite },
            token: default,
            subscription: new ProxyFileSystemEventSubscription {
                Agent = async (e, _) => {
                    if (e.ChangeType == WatcherChangeTypes.Changed) {
                        Interlocked.Increment(ref actual);
                    }
                    await Task.CompletedTask;
                }
            });
        await Task.Delay(250);
        File.WriteAllText(path, "bar");
        File.WriteAllText(path, "baz");
        File.WriteAllText(path, "cud");
        SpinWait.SpinUntil(() => actual > 2, 2500);
        Assert.That(actual, Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public async Task Events_ChangeType_IsChangedForMultipleSubscriptions() {
        var path = Path.Combine(TempPath, nameof(Events_ChangeType_IsChanged));
        var actual = new HashSet<object>();
        var subs = Enumerable.Range(1, 10).Select(i => {
            var 
            sub = new ProxyFileSystemEventSubscription();
            sub.Agent = async (e, _) => {
                if (e.ChangeType == WatcherChangeTypes.Changed) {
                    lock (actual) {
                        actual.Add(sub);
                    }
                }
            };
            return sub;
        });
        File.WriteAllText(path, "foo");
        await Task.WhenAll(subs.Select(sub => Subject.Add(
            path: TempPath,
            options: null,
            token: default,
            subscription: sub)));
        await Task.Delay(250);
        File.WriteAllText(path, "bar");
        SpinWait.SpinUntil(() => actual.Count >= 10, 2500);
        Assert.That(actual, Has.Count.EqualTo(10));
    }

    [Test]
    public async Task Events_CompletedSubscription_IsReported() {
        var path = Path.Combine(TempPath, nameof(Events_CompletedSubscription_IsReported));
        var subscription = new ProxyFileSystemEventSubscription {
            Agent = (_, _) => Task.CompletedTask
        };
        var actual = new TaskCompletionSource<FileSystemEventResult>();
        EventHandler<FileSystemEventResultEventArgs> handler = (_, eventArgs) => {
            eventArgs.Add((result, _) => {
                actual.TrySetResult(result);
                return Task.CompletedTask;
            });
        };
        Subject.OnSubscriptionEventComplete += handler;
        try {
            await Subject.Add(subscription, TempPath, options: null, token: default);
            await Task.Delay(250);
            File.WriteAllText(path, "foo");
            Assert.That(SpinWait.SpinUntil(() => actual.Task.IsCompleted, 2500), Is.True);
            var result = await actual.Task;
            using (Assert.EnterMultipleScope()) {
                Assert.That(result.Canceled, Is.False);
                Assert.That(result.Exception, Is.Null);
                Assert.That(result.Subscription, Is.SameAs(subscription));
            }
        }
        finally {
            Subject.OnSubscriptionEventComplete -= handler;
            await Subject.Remove(subscription, TempPath, options: null, token: default);
        }
    }

    [Test]
    public async Task Events_CompletedSubscription_InvokesAllHandlersAndAwaitsTheirTasks() {
        var path = Path.Combine(TempPath, nameof(Events_CompletedSubscription_InvokesAllHandlersAndAwaitsTheirTasks));
        var expected = new InvalidOperationException();
        var actual = new TaskCompletionSource<Exception>();
        var subscription = new ProxyFileSystemEventSubscription {
            Agent = (_, _) => Task.CompletedTask
        };
        var subscriberCount = 0;
        EventHandler<FileSystemEventResultEventArgs> first = (_, eventArgs) => {
            Interlocked.Increment(ref subscriberCount);
            eventArgs.Add((_, _) => {
                var allSubscribersInvoked = Volatile.Read(ref subscriberCount) == 2;
                if (allSubscribersInvoked) {
                    return Task.FromException(expected);
                }
                return Task.FromException(new InvalidOperationException("Not all subscribers ran before the task delegate."));
            });
        };
        EventHandler<FileSystemEventResultEventArgs> second = (_, eventArgs) => {
            Interlocked.Increment(ref subscriberCount);
            eventArgs.Add((_, _) => Task.CompletedTask);
        };
        EventHandler<FileSystemEventUnhandledErrorEventArgs> error = (_, eventArgs) => {
            eventArgs.Add((exception, _) => {
                actual.TrySetResult(exception);
                return Task.FromResult(false);
            });
        };
        Subject.OnSubscriptionEventComplete += first;
        Subject.OnSubscriptionEventComplete += second;
        Subject.OnUnhandledError += error;
        try {
            await Subject.Add(subscription, TempPath, options: null, token: default);
            await Task.Delay(250);
            File.WriteAllText(path, "foo");
            Assert.That(SpinWait.SpinUntil(() => actual.Task.IsCompleted, 2500), Is.True);
            Assert.That(await actual.Task, Is.SameAs(expected));
            Assert.That(subscriberCount, Is.EqualTo(2));
        }
        finally {
            Subject.OnSubscriptionEventComplete -= first;
            Subject.OnSubscriptionEventComplete -= second;
            Subject.OnUnhandledError -= error;
            await Subject.Remove(subscription, TempPath, options: null, token: default);
        }
    }

    [Test]
    public async Task Events_FailedSubscription_IsReported() {
        var path = Path.Combine(TempPath, nameof(Events_FailedSubscription_IsReported));
        var expected = new InvalidOperationException();
        var subscription = new ProxyFileSystemEventSubscription {
            Agent = (_, _) => Task.FromException(expected)
        };
        var actual = new TaskCompletionSource<FileSystemEventResult>();
        EventHandler<FileSystemEventResultEventArgs> handler = (_, eventArgs) => {
            eventArgs.Add((result, _) => {
                actual.TrySetResult(result);
                return Task.CompletedTask;
            });
        };
        Subject.OnSubscriptionEventError += handler;
        try {
            await Subject.Add(subscription, TempPath, options: null, token: default);
            await Task.Delay(250);
            File.WriteAllText(path, "foo");
            Assert.That(SpinWait.SpinUntil(() => actual.Task.IsCompleted, 2500), Is.True);
            var result = await actual.Task;
            using (Assert.EnterMultipleScope()) {
                Assert.That(result.Canceled, Is.False);
                Assert.That(result.Exception, Is.SameAs(expected));
                Assert.That(result.Subscription, Is.SameAs(subscription));
            }
        }
        finally {
            Subject.OnSubscriptionEventError -= handler;
            await Subject.Remove(subscription, TempPath, options: null, token: default);
        }
    }

    [Test]
    public async Task Events_SubscriptionIsCalledOnSchedulerOfSynchronizationContext() {
        var path = Path.Combine(TempPath, nameof(Events_SubscriptionIsCalledOnSchedulerOfSynchronizationContext));
        var actual = new TaskCompletionSource<int>();
        var subscription = new ProxyFileSystemEventSubscription {
            Agent = (_, _) => {
                actual.TrySetResult(Thread.CurrentThread.ManagedThreadId);
                return Task.CompletedTask;
            }
        };
        using var context = new SingleThreadSynchronizationContext();
        var added = default(Task);
        var original = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try {
            added = Subject.Add(subscription, TempPath, options: null, token: default);
        }
        finally {
            SynchronizationContext.SetSynchronizationContext(original);
        }
        try {
            await added;
            await Task.Delay(250);
            File.WriteAllText(path, "foo");
            Assert.That(SpinWait.SpinUntil(() => actual.Task.IsCompleted, 2500), Is.True);
            var threadId = await actual.Task;
            Assert.That(threadId, Is.EqualTo(context.ThreadId));
        }
        finally {
            await Subject.Remove(subscription, TempPath, options: null, token: default);
        }
    }

    private sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object State)> Queue = [];
        private readonly Thread Thread;

        public int ThreadId => Thread.ManagedThreadId;

        public SingleThreadSynchronizationContext() {
            Thread = new Thread(() => {
                SetSynchronizationContext(this);
                foreach (var (callback, state) in Queue.GetConsumingEnumerable()) {
                    callback(state);
                }
            }) {
                IsBackground = true
            };
            Thread.Start();
        }

        public override void Post(SendOrPostCallback d, object state) {
            Queue.Add((d, state));
        }

        public override void Send(SendOrPostCallback d, object state) {
            throw new NotSupportedException();
        }

        void IDisposable.Dispose() {
            Queue.CompleteAdding();
        }
    }
}
