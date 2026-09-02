using System.Buffers;

namespace VyralBot.Core.Services.Performance;

/// <summary>
/// High-performance buffer pooling utility with thread-local caching.
/// Minimizes allocations for network I/O and message parsing.
/// Per CONTEXT_MASTER.md: Use ArrayPool<byte> for all buffers >= 1KB.
/// </summary>
public static class PooledBuffer
{
    /// <summary>
    /// Thread-local buffer cache for fast path allocation
    /// </summary>
    [ThreadStatic]
    private static byte[]? _threadBuffer;
    
    /// <summary>
    /// Maximum size for thread-local caching (8KB)
    /// </summary>
    private const int MaxThreadLocalSize = 8192;
    
    /// <summary>
    /// Default buffer size for most operations
    /// </summary>
    public const int DefaultBufferSize = 4096;
    
    /// <summary>
    /// Rent a buffer of at least the specified size.
    /// Uses thread-local cache for buffers <= 8KB, otherwise ArrayPool.
    /// </summary>
    /// <param name="minSize">Minimum required buffer size</param>
    /// <returns>A byte array of at least the requested size</returns>
    public static byte[] Rent(int minSize)
    {
        // Thread-local fast path for common sizes
        if (minSize <= MaxThreadLocalSize)
        {
            var buffer = _threadBuffer;
            if (buffer != null && buffer.Length >= minSize)
            {
                _threadBuffer = null;
                return buffer;
            }
        }
        
        // Fall back to ArrayPool for larger or when thread-local is empty
        return ArrayPool<byte>.Shared.Rent(minSize);
    }
    
    /// <summary>
    /// Return a buffer to the pool.
    /// Buffers <= 8KB are cached thread-locally for faster reuse.
    /// </summary>
    /// <param name="buffer">Buffer to return</param>
    /// <param name="clearArray">Whether to clear the buffer contents (default: false for performance)</param>
    public static void Return(byte[] buffer, bool clearArray = false)
    {
        if (buffer == null) return;
        
        if (clearArray)
        {
            Array.Clear(buffer, 0, buffer.Length);
        }
        
        // Cache small buffers thread-locally for fast path
        if (buffer.Length <= MaxThreadLocalSize)
        {
            // Only cache if we don't already have one or new one is larger
            if (_threadBuffer == null || buffer.Length > _threadBuffer.Length)
            {
                _threadBuffer = buffer;
                return;
            }
        }
        
        // Return larger buffers to the shared pool
        ArrayPool<byte>.Shared.Return(buffer, clearArray);
    }
    
    /// <summary>
    /// Rent a buffer with a specific initial capacity hint.
    /// Useful when you know the approximate size needed.
    /// </summary>
    /// <param name="capacity">Hint for expected usage</param>
    /// <returns>A byte array of at least the hinted capacity</returns>
    public static byte[] RentWithCapacity(int capacity)
    {
        // Round up to nearest power of 2 for efficiency
        int size = 1;
        while (size < capacity && size < 1048576) // Cap at 1MB
        {
            size <<= 1;
        }
        return Rent(Math.Max(size, DefaultBufferSize));
    }
    
    /// <summary>
    /// Create a span wrapper that auto-returns the buffer on dispose.
    /// Usage: using var rental = PooledBuffer.RentScoped(4096);
    /// </summary>
    public static BufferRental RentScoped(int minSize)
    {
        return new BufferRental(Rent(minSize));
    }
    
    /// <summary>
    /// RAII wrapper for automatic buffer return
    /// </summary>
    public readonly struct BufferRental : IDisposable
    {
        public byte[] Buffer { get; }
        
        public BufferRental(byte[] buffer)
        {
            Buffer = buffer;
        }
        
        public Span<byte> Span => Buffer.AsSpan();
        public Memory<byte> Memory => Buffer.AsMemory();
        
        public void Dispose()
        {
            Return(Buffer);
        }
    }
}
