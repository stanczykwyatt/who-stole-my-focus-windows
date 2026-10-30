namespace WhoStoleMyFocus.Core;

/// <summary>
/// What's known about the processes seen lately, by process ID and start time together, so a process
/// ID that Windows hands to a new process later never brings back the old one's name. The least
/// recently seen are dropped first.
/// </summary>
public sealed class RecentProcesses<T>
{
    readonly int limit;
    readonly LinkedList<(int Pid, long Started, T Value)> order = new();
    readonly Dictionary<int, LinkedListNode<(int Pid, long Started, T Value)>> byPid = [];

    public RecentProcesses(int limit) => this.limit = Math.Max(1, limit);

    public int Count => byPid.Count;

    /// <summary>The value for this exact process, which is then the most recently seen.</summary>
    public bool TryGet(int pid, long started, out T value)
    {
        if (byPid.TryGetValue(pid, out var node) && node.Value.Started == started)
        {
            order.Remove(node);
            order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>Adds or replaces a process. Returns what had to make room, oldest first, including any old process with the same ID.</summary>
    public List<T> Add(int pid, long started, T value)
    {
        var dropped = new List<T>();
        if (byPid.Remove(pid, out var old))
        {
            order.Remove(old);
            if (old.Value.Started != started) dropped.Add(old.Value.Value);
        }
        byPid[pid] = order.AddFirst((pid, started, value));
        while (byPid.Count > limit && order.Last is { } oldest)
        {
            order.RemoveLast();
            byPid.Remove(oldest.Value.Pid);
            dropped.Add(oldest.Value.Value);
        }
        return dropped;
    }

    public bool Remove(int pid, out T value)
    {
        if (byPid.Remove(pid, out var node))
        {
            order.Remove(node);
            value = node.Value.Value;
            return true;
        }
        value = default!;
        return false;
    }

    public IEnumerable<(int Pid, T Value)> All => order.Select(e => (e.Pid, e.Value));
}
