//! Counts the largest single allocation the current thread makes, for the tests that
//! prove a refusal happens before the pixels are allocated.
//!
//! It is a global allocator with a thread-local counter, so the measurement is exact and
//! is not disturbed by the other tests running in parallel: each thread keeps its own
//! high-water mark, and only the test that arms it reads it. The counter allocates
//! nothing itself — a `Cell<usize>` needs no destructor, so touching it cannot recurse.

use std::alloc::{GlobalAlloc, Layout, System};
use std::cell::Cell;

thread_local! {
    /// The largest single allocation, in bytes, this thread has made since it was armed.
    static LARGEST: Cell<usize> = const { Cell::new(0) };
    /// Whether the thread is measuring. The rest of the process is not counted.
    static ARMED: Cell<bool> = const { Cell::new(false) };
}

/// Records `size` as an allocation of this thread, when the thread is measuring.
fn note(size: usize) {
    let _ = ARMED.try_with(|armed| {
        if armed.get() {
            let _ = LARGEST.try_with(|largest| {
                if size > largest.get() {
                    largest.set(size);
                }
            });
        }
    });
}

/// The allocator every test binary of this crate runs under.
struct Probe;

// SAFETY: every method forwards to the system allocator with the layout it was given,
// and the only extra work is a thread-local counter that allocates nothing.
unsafe impl GlobalAlloc for Probe {
    unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
        let pointer = unsafe { System.alloc(layout) };
        if !pointer.is_null() {
            note(layout.size());
        }
        pointer
    }

    unsafe fn dealloc(&self, pointer: *mut u8, layout: Layout) {
        unsafe { System.dealloc(pointer, layout) }
    }

    unsafe fn realloc(&self, pointer: *mut u8, layout: Layout, new_size: usize) -> *mut u8 {
        let moved = unsafe { System.realloc(pointer, layout, new_size) };
        if !moved.is_null() {
            note(new_size);
        }
        moved
    }

    unsafe fn alloc_zeroed(&self, layout: Layout) -> *mut u8 {
        let pointer = unsafe { System.alloc_zeroed(layout) };
        if !pointer.is_null() {
            note(layout.size());
        }
        pointer
    }
}

#[global_allocator]
static GLOBAL: Probe = Probe;

/// Runs `body` and reports the largest single allocation it made on this thread, in bytes.
///
/// The measurement is per thread and per call: the high-water mark is reset before the
/// body runs, so a large allocation made by an earlier test on the same thread is not
/// counted against this one.
pub fn measure<T>(body: impl FnOnce() -> T) -> (T, usize) {
    LARGEST.with(|largest| largest.set(0));
    ARMED.with(|armed| armed.set(true));

    let result = body();

    ARMED.with(|armed| armed.set(false));

    (result, LARGEST.with(Cell::get))
}
