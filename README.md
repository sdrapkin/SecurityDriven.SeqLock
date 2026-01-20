# **SeqLock** [![NuGet](https://img.shields.io/nuget/v/SeqLock.svg)](https://www.nuget.org/packages/SeqLock/)

### by [Stan Drapkin](https://github.com/sdrapkin/)

A high-performance, optimistic reader-writer synchronization primitive for .NET that enables lock-free, consistent snapshots of compound state in read-heavy scenarios.

## What is SeqLock?
`SeqLock` ([sequence lock](https://en.wikipedia.org/wiki/Seqlock)) is a *reader-optimized synchronization primitive* designed for
scenarios where reads are much more frequent than writes. It allows multiple readers to access shared data concurrently
without blocking, even in the presence of concurrent writers.

Instead of blocking readers when a writer is active, `SeqLock` readers run optimistically and without locking.
Writers mark the shared state as "in-progress" using a version counter, update the state, then mark it as "stable" again.
Readers detect writer interference by checking the version before and after reading, and simply retry on interference.
* Writers serialize
* Readers never block, and retry instead of blocking on interference

## Why use SeqLock?
Traditional reader–writer locks (ex. `ReaderWriterLockSlim`) provide strong consistency guarantees:
a reader always sees a fully consistent view of shared state. To achieve this, every reader must participate
in lock coordination via atomic operations and memory fences.

This has 2 consequences:
1. **Every read has non-trivial overhead, even when no writers interfere.**
2. **Read scalability degrades as the number of readers increases.**

`SeqLock` exists to solve a different problem:
> *How to make reads as cheap, fast and scalable as possible when writers are rare and brief?<br>
> ...while still providing strong external consistency?*

By removing reader-side locking entirely, `SeqLock` reduces the read path to:

`Version check #1` →  `Fast data read` →  `Version check #2`

If no writes interfere (the common case), the reads complete with no blocking and minimal synchronization cost.

## How SeqLock differs from `ReaderWriterLockSlim`?
The key difference is **optimistic** vs **pessimistic** synchronization:
* `ReaderWriterLockSlim` **prevents** inconsistency
	* Readers block writers (or vice versa)
	* Strong internal consistency is guaranteed
	* Higher per-read overhead
* `SeqLock` **detects** inconsistency
	* Readers may observe intermediate or torn state
	* Inconsistent reads are discarded and retried
	* Near-minimal overhead in the uncontended case

`SeqLock` trades *internal safety* for *external correctness*:
* A reader may observe inconsistent state
* But it will never return an inconsistent result
* Correctness is enforced via version validation, not locking

This makes `SeqLock` extremely effective for controlled read-heavy data paths
where there is no danger of arbitrary object graphs or exception-sensitive readers and writers.
Used correctly, `SeqLock` outperforms traditional reader–writer locks by a wide margin on modern multicore
systems - especially under heavy read concurrency.

#### Summary Comparison of `SeqLock` vs `ReaderWriterLockSlim` in `System.Threading`:

| Feature | SeqLock | ReaderWriterLockSlim |
|---------|---------|----------------------|
| **Read Performance** | **Very Fast** (minimal overhead) | Moderate (Interlocked ops on every read) |
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
