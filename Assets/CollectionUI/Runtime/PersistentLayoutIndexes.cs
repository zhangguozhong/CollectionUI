using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CollectionUI
{
    // 仅复制块引用表及发生变化的末块；旧块发布后永不写入，也不保留历史结果对象链。
    internal sealed class LayoutEntryBlocks : IReadOnlyList<LayoutEntry>
    {
        const int BlockSize = 256;
        readonly LayoutEntry[][] blocks;
        public int Count { get; }

        public LayoutEntry this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return blocks[index / BlockSize][index % BlockSize];
            }
        }

        internal LayoutEntryBlocks(IReadOnlyList<LayoutEntry> source)
        {
            Count = source.Count;
            blocks = new LayoutEntry[(Count + BlockSize - 1) / BlockSize][];
            for (int b = 0; b < blocks.Length; b++)
            {
                blocks[b] = new LayoutEntry[Math.Min(BlockSize, Count - b * BlockSize)];
                for (int j = 0; j < blocks[b].Length; j++)
                    blocks[b][j] = source[b * BlockSize + j];
            }
        }

        internal LayoutEntryBlocks(LayoutEntryBlocks previous, int retained, IReadOnlyList<LayoutEntry> tail)
        {
            Count = retained + tail.Count;
            blocks = new LayoutEntry[(Count + BlockSize - 1) / BlockSize][];
            int shared = retained / BlockSize;
            Array.Copy(previous.blocks, blocks, shared);
            for (int b = shared; b < blocks.Length; b++)
            {
                blocks[b] = new LayoutEntry[Math.Min(BlockSize, Count - b * BlockSize)];
                for (int j = 0; j < blocks[b].Length; j++)
                {
                    int i = b * BlockSize + j;
                    blocks[b][j] = i < retained ? previous[i] : tail[i - retained];
                }
            }
        }

        public IEnumerator<LayoutEntry> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
                yield return this[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // 初次全量构建用字典；分页只在不可变 AVL 覆盖层中插入新身份或移动的页脚。
    // 查询最多走一棵平衡树再查一次字典，不会随着分页次数形成线性覆盖链。
    internal readonly struct PersistentLayoutLookup<T>
    {
        sealed class Node
        {
            internal readonly string Key;
            internal readonly T Value;
            internal readonly Node Left, Right;
            internal readonly int Height;
            internal Node(string key, T value, Node left, Node right)
            {
                Key = key;
                Value = value;
                Left = left;
                Right = right;
                Height = 1 + Math.Max(H(left), H(right));
            }
        }

        readonly Dictionary<string, T> basis;
        readonly Node root;
        internal PersistentLayoutLookup(Dictionary<string, T> basis)
        {
            this.basis = basis;
            root = null;
        }

        PersistentLayoutLookup(Dictionary<string, T> basis, Node root)
        {
            this.basis = basis;
            this.root = root;
        }

        internal bool TryGetValue(string key, out T value)
        {
            var node = root;
            while (node != null)
            {
                int c = string.CompareOrdinal(key, node.Key);
                if (c == 0)
                {
                    value = node.Value;
                    return true;
                }

                node = c < 0 ? node.Left : node.Right;
            }

            return basis.TryGetValue(key, out value);
        }

        internal PersistentLayoutLookup<T> Set(string key, T value) => new PersistentLayoutLookup<T>(basis, Put(root, key, value));
        static int H(Node node) => node == null ? 0 : node.Height;
        static Node Left(Node n)
        {
            var r = n.Right;
            return new Node(r.Key, r.Value, new Node(n.Key, n.Value, n.Left, r.Left), r.Right);
        }

        static Node Right(Node n)
        {
            var l = n.Left;
            return new Node(l.Key, l.Value, l.Left, new Node(n.Key, n.Value, l.Right, n.Right));
        }

        static Node Put(Node n, string key, T value)
        {
            if (n == null)
                return new Node(key, value, null, null);
            int c = string.CompareOrdinal(key, n.Key);
            if (c == 0)
                return new Node(key, value, n.Left, n.Right);
            n = c < 0 ? new Node(n.Key, n.Value, Put(n.Left, key, value), n.Right) : new Node(n.Key, n.Value, n.Left, Put(n.Right, key, value));
            if (H(n.Left) - H(n.Right) > 1)
            {
                if (H(n.Left.Left) < H(n.Left.Right))
                    n = new Node(n.Key, n.Value, Left(n.Left), n.Right);
                return Right(n);
            }

            if (H(n.Right) - H(n.Left) > 1)
            {
                if (H(n.Right.Right) < H(n.Right.Left))
                    n = new Node(n.Key, n.Value, n.Left, Right(n.Right));
                return Left(n);
            }

            return n;
        }
    }

    // 按 (顶边, 构建索引) 排序，保证 Query 与全量构建相同的稳定顺序。
    // 每个节点缓存整个子树的最高底边及最早顶边，高条目跨越多个分页时仍可正确命中。
    internal sealed class LayoutIntervalNode
    {
        internal readonly LayoutEntry Entry;
        internal readonly int Index, Height;
        internal readonly LayoutIntervalNode Left, Right;
        readonly float maxBottom, minTop;
        LayoutIntervalNode(LayoutEntry entry, int index, LayoutIntervalNode left, LayoutIntervalNode right)
        {
            Entry = entry;
            Index = index;
            Left = left;
            Right = right;
            Height = 1 + Math.Max(H(left), H(right));
            maxBottom = Math.Max(entry.Rect.yMax, Math.Max(left == null ? float.NegativeInfinity : left.maxBottom, right == null ? float.NegativeInfinity : right.maxBottom));
            minTop = left == null ? entry.Rect.yMin : left.minTop;
        }

        internal static LayoutIntervalNode Build(IReadOnlyList<LayoutEntry> entries, int[] sorted, int lo, int hi)
        {
            if (lo >= hi)
                return null;
            int mid = (lo + hi) / 2, index = sorted[mid];
            return new LayoutIntervalNode(entries[index], index, Build(entries, sorted, lo, mid), Build(entries, sorted, mid + 1, hi));
        }

        static int H(LayoutIntervalNode n) => n == null ? 0 : n.Height;
        static int Compare(LayoutEntry entry, int index, LayoutIntervalNode n)
        {
            int c = entry.Rect.yMin.CompareTo(n.Entry.Rect.yMin);
            return c == 0 ? index.CompareTo(n.Index) : c;
        }

        static LayoutIntervalNode RotateLeft(LayoutIntervalNode n)
        {
            var r = n.Right;
            return new LayoutIntervalNode(r.Entry, r.Index, new LayoutIntervalNode(n.Entry, n.Index, n.Left, r.Left), r.Right);
        }

        static LayoutIntervalNode RotateRight(LayoutIntervalNode n)
        {
            var l = n.Left;
            return new LayoutIntervalNode(l.Entry, l.Index, l.Left, new LayoutIntervalNode(n.Entry, n.Index, l.Right, n.Right));
        }

        static LayoutIntervalNode Balance(LayoutIntervalNode n)
        {
            if (H(n.Left) - H(n.Right) > 1)
            {
                if (H(n.Left.Left) < H(n.Left.Right))
                    n = new LayoutIntervalNode(n.Entry, n.Index, RotateLeft(n.Left), n.Right);
                return RotateRight(n);
            }

            if (H(n.Right) - H(n.Left) > 1)
            {
                if (H(n.Right.Right) < H(n.Right.Left))
                    n = new LayoutIntervalNode(n.Entry, n.Index, n.Left, RotateRight(n.Right));
                return RotateLeft(n);
            }

            return n;
        }

        internal static LayoutIntervalNode Insert(LayoutIntervalNode n, LayoutEntry entry, int index)
        {
            if (n == null)
                return new LayoutIntervalNode(entry, index, null, null);
            int c = Compare(entry, index, n);
            if (c == 0)
                return new LayoutIntervalNode(entry, index, n.Left, n.Right);
            return Balance(c < 0 ? new LayoutIntervalNode(n.Entry, n.Index, Insert(n.Left, entry, index), n.Right) : new LayoutIntervalNode(n.Entry, n.Index, n.Left, Insert(n.Right, entry, index)));
        }

        internal static LayoutIntervalNode Remove(LayoutIntervalNode n, LayoutEntry entry, int index)
        {
            if (n == null)
                throw new InvalidOperationException("Missing layout interval.");
            int c = Compare(entry, index, n);
            if (c < 0)
                return Balance(new LayoutIntervalNode(n.Entry, n.Index, Remove(n.Left, entry, index), n.Right));
            if (c > 0)
                return Balance(new LayoutIntervalNode(n.Entry, n.Index, n.Left, Remove(n.Right, entry, index)));
            if (n.Left == null)
                return n.Right;
            if (n.Right == null)
                return n.Left;
            var successor = n.Right;
            while (successor.Left != null)
                successor = successor.Left;
            return Balance(new LayoutIntervalNode(successor.Entry, successor.Index, n.Left, Remove(n.Right, successor.Entry, successor.Index)));
        }

        internal static void Query(LayoutIntervalNode n, Rect viewport, List<int> output)
        {
            if (n == null || n.maxBottom <= viewport.yMin || n.minTop >= viewport.yMax)
                return;
            Query(n.Left, viewport, output);
            if (n.Entry.Rect.Overlaps(viewport))
                output.Add(n.Index);
            Query(n.Right, viewport, output);
        }
    }
}
