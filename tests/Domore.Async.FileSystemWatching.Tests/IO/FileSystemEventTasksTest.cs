using Domore.IO.FileSystemEventSubscriptions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Domore.IO;

[TestFixture]
internal sealed class FileSystemEventTasksTest {
    private string TempPath;

    [SetUp]
    public void SetUp() {
        TempPath = Path.Combine(
            Path.GetTempPath(),
            "Domore.Async.FileSystemWatching.Tests",
            nameof(FileSystemEventTasksTest),
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
        using var _ = FileSystemEventTasks.Add(TempPath, async (e, _) => {
            actual = e.ChangeType;
            await Task.CompletedTask;
        });
        await Task.Delay(250);
        File.WriteAllText(path, "bar");
        SpinWait.SpinUntil(() => actual != default, 2500);
        Assert.That(actual, Is.EqualTo(WatcherChangeTypes.Changed));
    }

    [Test]
    public async Task Events_ChangeType_IsChangedMultipleTimes() {
        var path = Path.Combine(TempPath, nameof(Events_ChangeType_IsChanged));
        var options = new FileSystemEventOptions() { NotifyFilter = NotifyFilters.LastWrite };
        var actual = default(int);
        File.WriteAllText(path, "foo");
        using var _ = FileSystemEventTasks.Add(TempPath, options, async (e, _) => {
            if (e.ChangeType == WatcherChangeTypes.Changed) {
                Interlocked.Increment(ref actual);
            }
            await Task.CompletedTask;
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
        var disposable = new List<IDisposable>();
        foreach (var sub in subs) {
            disposable.Add(FileSystemEventTasks.Add(TempPath, sub.Agent));
        }
        try {
            await Task.Delay(250);
            File.WriteAllText(path, "bar");
            SpinWait.SpinUntil(() => actual.Count >= 10, 2500);
            Assert.That(actual, Has.Count.EqualTo(10));
        }
        finally {
            foreach (var item in disposable) {
                item.Dispose();
            }
        }
    }

    [Test]
    public async Task Events_StaticEvent_InvokesAllHandlersAndAwaitsTheirTasks() {
        var path = Path.Combine(TempPath, nameof(Events_StaticEvent_InvokesAllHandlersAndAwaitsTheirTasks));
        var expected = new InvalidOperationException();
        var actual = new TaskCompletionSource<Exception>();
        var subscriberCount = 0;
        File.WriteAllText(path, "foo");
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
        FileSystemEventTasks.OnSubscriptionEventComplete += first;
        FileSystemEventTasks.OnSubscriptionEventComplete += second;
        FileSystemEventTasks.OnUnhandledError += error;
        using var subscription = FileSystemEventTasks.Add(TempPath, (_, _) => Task.CompletedTask);
        try {
            await Task.Delay(250);
            File.WriteAllText(path, "bar");
            Assert.That(SpinWait.SpinUntil(() => actual.Task.IsCompleted, 2500), Is.True);
            Assert.That(await actual.Task, Is.SameAs(expected));
            Assert.That(subscriberCount, Is.EqualTo(2));
        }
        finally {
            FileSystemEventTasks.OnSubscriptionEventComplete -= first;
            FileSystemEventTasks.OnSubscriptionEventComplete -= second;
            FileSystemEventTasks.OnUnhandledError -= error;
        }
    }
}
