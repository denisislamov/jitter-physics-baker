/*
 * Compatibility shim for the Unity build of Jitter2.Core.
 * SPDX-License-Identifier: MIT
 *
 * Not part of upstream Jitter2. Compiled only into the netstandard2.1 assembly.
 */

// Two of these are compiler contracts rather than libraries: the compiler looks the type up by
// name and is satisfied by any declaration, so declaring them here restores the language feature
// without a framework upgrade. PriorityQueue is a real collection and is implemented as one.

#if !NET5_0_OR_GREATER

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Recognised by the compiler to emit init-only setters. Nothing reads it at runtime.
    /// </summary>
    internal static class IsExternalInit
    {
    }

    /// <summary>
    /// Suppresses the <c>localsinit</c> flag. Jitter2 applies it to methods that immediately fill
    /// large stack buffers, where zeroing them first is pure overhead.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct
        | AttributeTargets.Interface | AttributeTargets.Constructor | AttributeTargets.Method
        | AttributeTargets.Property | AttributeTargets.Event,
        Inherited = false)]
    internal sealed class SkipLocalsInitAttribute : Attribute
    {
    }
}

namespace System.Collections.Generic
{
    /// <summary>
    /// A minimal stand-in for the .NET 6 collection, offering the members Jitter2 uses when it
    /// walks the dynamic tree nearest-first.
    /// </summary>
    /// <remarks>
    /// A plain binary min-heap. Ties between equal priorities are broken by heap position, which
    /// is not the order the framework type would produce, so this implementation must be the one
    /// used everywhere rather than a fallback for some platforms: two builds disagreeing about
    /// which of two equally distant proxies comes first would return different query results from
    /// the same world.
    /// </remarks>
    /// <typeparam name="TElement">Queued value.</typeparam>
    /// <typeparam name="TPriority">Ordering key.</typeparam>
    internal sealed class PriorityQueue<TElement, TPriority>
    {
        private readonly IComparer<TPriority> comparer;
        private (TElement Element, TPriority Priority)[] nodes;
        private int count;

        /// <summary>Creates an empty queue ordered by the default comparer.</summary>
        public PriorityQueue() : this(Comparer<TPriority>.Default)
        {
        }

        /// <summary>Creates an empty queue ordered by <paramref name="comparer"/>.</summary>
        public PriorityQueue(IComparer<TPriority> comparer)
        {
            this.comparer = comparer ?? Comparer<TPriority>.Default;
            nodes = new (TElement, TPriority)[16];
        }

        /// <summary>The number of queued elements.</summary>
        public int Count => count;

        /// <summary>Adds an element with the given priority.</summary>
        public void Enqueue(TElement element, TPriority priority)
        {
            if (count == nodes.Length)
            {
                Array.Resize(ref nodes, count * 2);
            }

            nodes[count] = (element, priority);
            SiftUp(count);
            count++;
        }

        /// <summary>Removes and returns the lowest-priority element.</summary>
        public TElement Dequeue()
        {
            if (!TryDequeue(out TElement element, out _))
            {
                throw new InvalidOperationException("The queue is empty.");
            }

            return element;
        }

        /// <summary>Removes the lowest-priority element, reporting whether there was one.</summary>
        public bool TryDequeue(out TElement element, out TPriority priority)
        {
            if (count == 0)
            {
                element = default;
                priority = default;
                return false;
            }

            (element, priority) = nodes[0];
            count--;
            nodes[0] = nodes[count];
            nodes[count] = default;

            if (count > 0)
            {
                SiftDown(0);
            }

            return true;
        }

        /// <summary>Reads the lowest-priority element without removing it.</summary>
        public bool TryPeek(out TElement element, out TPriority priority)
        {
            if (count == 0)
            {
                element = default;
                priority = default;
                return false;
            }

            (element, priority) = nodes[0];
            return true;
        }

        /// <summary>Discards every queued element.</summary>
        public void Clear()
        {
            Array.Clear(nodes, 0, count);
            count = 0;
        }

        private void SiftUp(int index)
        {
            (TElement Element, TPriority Priority) node = nodes[index];

            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                if (comparer.Compare(node.Priority, nodes[parent].Priority) >= 0)
                {
                    break;
                }

                nodes[index] = nodes[parent];
                index = parent;
            }

            nodes[index] = node;
        }

        private void SiftDown(int index)
        {
            (TElement Element, TPriority Priority) node = nodes[index];

            while (true)
            {
                int left = (index << 1) + 1;
                if (left >= count)
                {
                    break;
                }

                int smallest = left;
                int right = left + 1;
                if (right < count && comparer.Compare(nodes[right].Priority, nodes[left].Priority) < 0)
                {
                    smallest = right;
                }

                if (comparer.Compare(nodes[smallest].Priority, node.Priority) >= 0)
                {
                    break;
                }

                nodes[index] = nodes[smallest];
                index = smallest;
            }

            nodes[index] = node;
        }
    }
}

#endif

