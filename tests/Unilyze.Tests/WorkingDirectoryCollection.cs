namespace Unilyze.Tests;

[CollectionDefinition(Name)]
public sealed class WorkingDirectoryCollection : ICollectionFixture<WorkingDirectoryGate>
{
    public const string Name = "SerializedWorkingDirectory";
}

public sealed class WorkingDirectoryGate
{
    readonly object _lock = new();
    string? _savedCwd;

    public IDisposable Enter(string targetDirectory)
    {
        lock (_lock)
        {
            _savedCwd ??= Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(targetDirectory);
            return new Scope(this);
        }
    }

    void Restore()
    {
        lock (_lock)
        {
            if (_savedCwd is not null)
            {
                Directory.SetCurrentDirectory(_savedCwd);
                _savedCwd = null;
            }
        }
    }

    sealed class Scope(WorkingDirectoryGate gate) : IDisposable
    {
        bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            gate.Restore();
        }
    }
}
