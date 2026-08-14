/*
 * Compatibility shim for the Unity build of Jitter2.Core.
 * SPDX-License-Identifier: MIT
 *
 * Not part of upstream Jitter2. Compiled only into the netstandard2.1 assembly.
 */

#if !NET6_0_OR_GREATER

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices
{
    /// <summary>
    /// Stands in for the .NET 6 allocator API. Backed by the HGlobal allocator, which is the same
    /// underlying malloc on every platform Unity targets.
    /// </summary>
    internal static unsafe class NativeMemory
    {
        /// <summary>Allocates <paramref name="byteCount"/> bytes.</summary>
        public static void* Alloc(nuint byteCount) => (void*)Marshal.AllocHGlobal((IntPtr)(ulong)byteCount);

        /// <summary>Releases memory obtained from <see cref="Alloc"/>.</summary>
        public static void Free(void* ptr)
        {
            if (ptr != null)
            {
                Marshal.FreeHGlobal((IntPtr)ptr);
            }
        }

        // The platform allocator makes no alignment promise beyond pointer size, so the block is
        // over-allocated and the returned pointer is walked forward to the boundary. The original
        // address is parked immediately before it, because AlignedFree is given the aligned
        // pointer and has no other way back to what was actually allocated.
        /// <summary>Allocates <paramref name="byteCount"/> bytes on an <paramref name="alignment"/> boundary.</summary>
        public static void* AlignedAlloc(nuint byteCount, nuint alignment)
        {
            if (alignment == 0 || (alignment & (alignment - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(alignment), "Alignment must be a non-zero power of two.");
            }

            nuint headroom = alignment + (nuint)sizeof(void*);
            byte* raw = (byte*)Alloc(byteCount + headroom);
            if (raw == null)
            {
                return null;
            }

            byte* payload = raw + sizeof(void*);
            nuint misalignment = (nuint)payload & (alignment - 1);
            if (misalignment != 0)
            {
                payload += alignment - misalignment;
            }

            ((void**)payload)[-1] = raw;
            return payload;
        }

        /// <summary>Releases memory obtained from <see cref="AlignedAlloc"/>.</summary>
        public static void AlignedFree(void* ptr)
        {
            if (ptr != null)
            {
                Free(((void**)ptr)[-1]);
            }
        }
    }

    /// <summary>
    /// Stands in for the .NET 5 marshalling helper. Only <see cref="AsSpan{T}"/> is provided,
    /// because that is all Jitter2 asks for.
    /// </summary>
    internal static class CollectionsMarshal
    {
        /// <summary>
        /// Returns a span over the list's backing array.
        /// </summary>
        /// <remarks>
        /// Callers sort through this span and expect the list to be reordered, so it has to alias
        /// the real storage; handing back a copy would silently discard the result. Reaching the
        /// private array means relying on the field order of <see cref="List{T}"/>, which is
        /// verified once at startup rather than assumed - see <see cref="ListLayout{T}"/>.
        /// </remarks>
        /// <typeparam name="T">Element type.</typeparam>
        /// <param name="list">List to view. May be <see langword="null"/>.</param>
        public static Span<T> AsSpan<T>(List<T> list)
        {
            if (list is null)
            {
                return default;
            }

            ListLayout<T>.EnsureVerified();
            var view = Unsafe.As<ListLayout<T>>(list);
            return new Span<T>(view.Items, 0, view.Size);
        }

        /// <summary>
        /// Mirrors the private layout of <see cref="List{T}"/>: a backing array followed by the
        /// element count.
        /// </summary>
        private sealed class ListLayout<T>
        {
#pragma warning disable CS0649 // never assigned: instances are only ever reinterpreted, never created
            public readonly T[] Items;
            public readonly int Size;
#pragma warning restore CS0649

            private static bool verified;

            private ListLayout()
            {
                Items = Array.Empty<T>();
                Size = 0;
            }

            // A wrong guess about the layout would not throw, it would read arbitrary memory and
            // return plausible-looking garbage. Proving the guess against a list whose contents are
            // known turns that into an immediate, explainable failure.
            internal static void EnsureVerified()
            {
                if (verified)
                {
                    return;
                }

                var probe = new List<T> { default, default, default };
                probe.RemoveAt(2);

                var view = Unsafe.As<ListLayout<T>>(probe);
                if (view.Size != 2 || view.Items is null || view.Items.Length < 2)
                {
                    throw new PlatformNotSupportedException(
                        "CollectionsMarshal.AsSpan cannot be emulated on this runtime: the layout "
                        + "of List<T> does not match the expected backing array and count.");
                }

                verified = true;
            }
        }
    }
}

#endif

