# **SeqLock** [![NuGet](https://img.shields.io/nuget/v/SeqLock.svg)](https://www.nuget.org/packages/SeqLock/)

### by [Stan Drapkin](https://github.com/sdrapkin/)

A high-performance, optimistic reader-writer synchronization primitive for .NET that enables lock-free, consistent snapshots of compound state in read-heavy scenarios.

Summary Comparison of `SeqLock` vs `ReaderWriterLockSlim`in `System.Threading`:

| Feature | SeqLock | ReaderWriterLockSlim |
|---------|---------|----------------------|
| **Read Performance** | **Very Fast** (minimal overhead, plain memory loads) | Moderate (Interlocked operations on every read) |
| **Write Performance** | **Fast** (spinlock, minimal coordination) | Slower (state machine, reader coordination) |
| **Internal Consistency** | Weak (readers may observe intermediate/torn state) | Strong (atomic view guaranteed) |
| **External Consistency** | **Strong** (retry mechanism ensures valid snapshot) | Strong (lock ensures atomic view) |
| **Safety** | Careful use (Readers must be ready for torn/inconsistent state) | No special care needed |
| **Mechanism** | Optimistic versioning + retry loop | Pessimistic locking |
| **Contention Behavior** | Writers can delay readers (spinner exhaustion) | Readers block on writer presence |
| **Scalability** | Great for read-often write-rarely | Degrades with heavy contention |
| **Tail Latency** | Less predictable (retry-dependent) | More predictable (bounded lock wait) |
| **Memory Overhead** | Minimal (few 64-bit integers) | Moderate (state machine, queues) |
| **Extra Features** | None | Write-upgradeable read locks, recursion support, timeouts |
| **Best For** | Struct/blittable data, read-often workloads | Complex objects, general-purpose use, mixed I/O |
| **Worst For** | Write-heavy scenarios | Extreme read-heavy scenarios |
