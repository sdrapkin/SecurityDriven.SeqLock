using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace SecurityDriven
{
	/// <summary>
	///  - Optimistic lock-free readers (assume they can finish before the writers interfere)
	///  - Exclusive writers
	///  - Allocates ~200 bytes (~3 cache lines)
	/// </summary>
	/// <typeparam name="TState"></typeparam>
	/// <remarks>Wrap any TState structs in StrongBox<typeparamref name="TState"/> instace.</remarks>
	public sealed class SeqLock<TState>(TState state) where TState : class
	{
		// cannot use Explicit layout because Generic types cannot have explicit layout
		[StructLayout(LayoutKind.Sequential)]
		struct Container(TState state)
		{
			// CACHE LINE 1: Reader State (Hot & Frequent)
			// GC forces object pointers to the top anyway, so we explicitly declare it first.
			public TState _state = state; // offset 0 (relative to struct start)
			public long _version; // offset 8

			// CACHE LINES 1 and 2: [64-8]+64 = 120 byte Padding
			long _p00, _p01, _p02, _p03, _p04, _p05, _p06, _p07;
			long _p08, _p09, _p10, _p11, _p12, _p13, _p14;

			// CACHE LINE 3: Writer Contention (Exclusive)
			// Writes here will invalidate CL_3, leaving CL_1 (Readers) intact.
			public long _writeLatch;

			// CACHE LINE 3: Trailing Padding [64-8] = 56 bytes
			// Writes here will invalidate CL_3, leaving CL_1 (Readers) intact.
			long _tp00, _tp01, _tp02, _tp03, _tp04, _tp05, _tp06;
		}//struct Container

		Container _container = new(state);

		// =====================================================================
		// WRITE PATH (exclusive)
		// =====================================================================

		/// <summary>Executes a write. 
		/// WARNING: The <paramref name="writer"/> delegate MUST NOT crash (throw exceptions)
		/// and leave the state data partially modified/inconsistent.</summary>
		public void Write<TArg>(TArg arg, Action<TArg, TState> writer)
		{
			AcquireExclusiveWriteLatch(); // does not throw

			// Full Fence (Interlocked) ensures prior reads/writes don't cross
			Interlocked.Add(ref _container._version, 1L); // _version becomes ODD
			try
			{
				writer(arg, _container._state); // might throw
			}
			finally
			{
				// Full Fence ensures mutation doesn't bleed out
				Interlocked.Add(ref _container._version, 1L); // _version becomes EVEN
				ReleaseExclusiveWriteLatch(); // does not throw
			}
		}//Write()

		// =====================================================================
		// READ PATH (optimistic)
		// =====================================================================

		/// <summary>
		/// Executes a read optimistically. 
		/// WARNING: The <paramref name="reader"/> delegate may be invoked multiple times
		/// and may observe inconsistent (torn) state if a write is in progress.
		/// Your reader should not crash (throw) on inconsistent state data.
		/// </summary>
		/// <remarks>
		/// SeqLock allows the state to change while your reader is reading it.
		/// A writer might modify pointers while a reader is traversing them.
		/// Torn state will not be returned, but your reader should not crash on torn state.
		/// </remarks>
		public TResult Read<TArg, TResult>(TArg arg, Func<TArg, TState, TResult> reader)
		{
			SpinWait sw = default;

			while (true)
			{
				// 1. Wait for the writer to finish (Version must be EVEN)
				// Uses Volatile.Read() which has Acquire-Load semantics.
				// Subsequent data-read cannot be reordered prior to 1st version-read.
				long startVersion = Volatile.Read(ref _container._version);
				if ((startVersion & 1) != 0)
				{
					sw.SpinOnce();
					continue;
				}

				// 2. Optimistic Read: The Volatile.Read above (Acquire) ensures we don't read data before checking version.
				TResult result = reader(arg, _container._state); // might throw

				// 3. StoreLoad Barrier (ARM64 / Weak Memory Model Barrier)
				// MemoryBarrier() is required to prevent reordering of 2nd version-read before the data-read
				Interlocked.MemoryBarrier();

				// 4. Validate that no writer modified the state while we were reading it
				if (Volatile.Read(ref _container._version) == startVersion) return result; // not Interlocked.Read()

				// 5. Failure: A writer interfered. Just back off and retry.
				sw.SpinOnce();
			}//while (true)
		}//Read()

		// =====================================================================
		// INTERNALS
		// =====================================================================

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void AcquireExclusiveWriteLatch()
		{
			// Fast path: Try to grab _writeLatch immediately (optimization for low contention)
			if (Interlocked.CompareExchange(ref _container._writeLatch, 1, 0) == 0) return;

			SpinWait sw = default;
			// OPTIMIZATION: TATAS (Test-and-Test-And-Set)
			// 1. "Test" (Cheap Read): Check if it looks free without locking the bus.
			while (Volatile.Read(ref _container._writeLatch) == 1 ||
				   // 2. "Test-And-Set" (Expensive Write): Only try to lock if it looked free.
				   Interlocked.CompareExchange(ref _container._writeLatch, 1, 0) != 0)
			{
				sw.SpinOnce();
			}
		}//AcquireExclusiveWriteLatch()

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void ReleaseExclusiveWriteLatch() => Volatile.Write(ref _container._writeLatch, 0); // Release-store
	}//class SeqLock<TState>

	/// <summary>SeqLock helper class</summary>
	public static class SeqLock
	{
		/// <summary>Allows type inference for the TState parameter, for simpler usage.</summary>
		public static SeqLock<TState> Create<TState>(TState state) where TState : class => new(state);
	}//class SeqLock
}//ns
