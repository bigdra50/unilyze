using System.Buffers;
using System.Runtime.CompilerServices;

namespace Unilyze;

ref struct LinearAllocator<T>(int sizeHint = 32)
{
    T[]? _buffer = ArrayPool<T>.Shared.Rent(sizeHint);
    int _tail;

    public bool IsDisposed => _tail == -1;
    public int Count => _tail;

    public Span<T> Allocate(int count)
    {
        ThrowIfDisposed();
        var newSize = _tail + count;
        if (_buffer == null || _buffer.Length < newSize)
        {
            var newArray = ArrayPool<T>.Shared.Rent(newSize);
            if (_buffer != null)
            {
                _buffer.AsSpan().CopyTo(newArray);
                ArrayPool<T>.Shared.Return(_buffer);
            }
            _buffer = newArray;
        }
        var span = new Span<T>(_buffer, _tail, count);
        _tail += count;
        return span;
    }

    public void Deallocate(int count)
    {
        ThrowIfDisposed();
        if (count < 0 || count > _tail)
            throw new ArgumentOutOfRangeException(nameof(count), "Cannot deallocate more than allocated or negative count.");
        _tail -= count;
    }

    public void Clear()
    {
        ThrowIfDisposed();
        if (_buffer != null)
            new Span<T>(_buffer, 0, _tail).Clear();
        _tail = 0;
    }

    public void Dispose()
    {
        ThrowIfDisposed();
        if (_buffer != null)
        {
            ArrayPool<T>.Shared.Return(_buffer);
            _buffer = null;
        }
        _tail = -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<T> AsSpan()
    {
        return new ReadOnlySpan<T>(_buffer, 0, _tail);
    }

    void ThrowIfDisposed()
    {
        if (_tail == -1) ThrowDisposedException();
    }

    static void ThrowDisposedException()
    {
        throw new ObjectDisposedException(nameof(LinearAllocator<T>));
    }
}
