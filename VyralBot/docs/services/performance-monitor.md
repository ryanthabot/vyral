# Performance Monitor

## Overview
Background service that logs application performance metrics every 15 seconds. Also includes object pooling utilities for event and buffer reuse.

## Source Files

### PerformanceMonitor
- **File:** `src/VyralBot.Core/Services/Performance/PerformanceMonitor.cs`
- **Namespace:** `VyralBot.Core.Services.Performance`
- **Class:** `sealed class PerformanceMonitor : BackgroundService`
- **Constructor:** `ILogger<PerformanceMonitor>`
- **Private fields:** `Process _currentProcess`, `_lastGen0/1/2Collections`, `_lastCpuTime`, `_lastCheckTime`
- **ExecuteAsync:** Logs metrics every 15 seconds
- **LogPerformanceMetrics:** Tracks memory usage, GC collections (gen 0/1/2), thread count, CPU percentage, WebView2 process count

### EventPool
- **File:** `src/VyralBot.Core/Services/Performance/EventPool.cs`
- **Namespace:** `VyralBot.Core.Services.Performance`
- **Static class:** `EventPool`
- **Methods:**
  - `PooledChatMessageEvent RentChatMessage()` — rent from pool
  - `void Return(PooledChatMessageEvent)` — return to pool
- **Nested types:**
  - `interface IResettable` — `bool TryReset()`
  - `sealed class PooledChatMessageEvent : IResettable` — poolable chat message with Platform, Channel, Username, UserId, DisplayName, Message, Color, badge flags, Timestamp, Badges dictionary; `ChatMessageData ToData()`, `bool TryReset()`
  - `sealed record ChatMessageData` — immutable snapshot of pooled event
  - `ResettablePoolPolicy<T>` — `IPooledObjectPolicy<T>` implementation

### PooledBuffer
- **File:** `src/VyralBot.Core/Services/Performance/PooledBuffer.cs`
- **Namespace:** `VyralBot.Core.Services.Performance`
- **Static class:** `PooledBuffer`
- **Constants:** `DefaultBufferSize = 4096`, `MaxThreadLocalSize = 8192`
- **Methods:** `byte[] Rent(int)`, `void Return(byte[], bool)`, `byte[] RentWithCapacity(int)`, `BufferRental RentScoped(int)`
- **Nested struct:** `readonly struct BufferRental : IDisposable` — `byte[] Buffer`, `Span<byte> Span`, `Memory<byte> Memory`

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.Core/Services/Performance/PerformanceMonitor.cs` | Background metrics logging |
| `src/VyralBot.Core/Services/Performance/EventPool.cs` | Object pooling for chat events |
| `src/VyralBot.Core/Services/Performance/PooledBuffer.cs` | Buffer pooling utilities |
