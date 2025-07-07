using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using UnityEngine;
using UEVector2 = UnityEngine.Vector2;

/// <summary>
/// Drop‑in replacement for WFCFIslandGenerator (same public API, same results).
/// </summary>
public sealed class WFCFIslandGenerator : MonoBehaviour
{
    /* ---------- PUBLIC INSPECTOR FIELDS ---------- */

    [Header("Grid Size")]
    public int width = 20;
    public int height = 20;

    [Header("Cell Spacing (X,Z)")]
    public UEVector2 cellSize = new UEVector2(2f, 2f);

    [Header("Your Six Tile Prefabs")]
    [Tooltip("0: Air, 1: Island, 2: Water, 3: Bubble, 4: Tree, 5: Animal")]
    public TileDef[] tiles = new TileDef[6];

    [Serializable]
    public class TileDef
    {
        public string name;
        public GameObject prefab;
        [Range(0.1f, 10f)]
        public float weight = 1f;
    }

    /* ---------- INTERNAL CONSTANTS ---------- */

    private const int DIR_COUNT = 4;
    private static readonly int[] DX = { 0, 1, 0, -1 }; // N,E,S,W
    private static readonly int[] DY = { 1, 0, -1, 0 };

    /* ---------- RUNTIME DATA ---------- */

    // One UINT/ULong per cell. Bit i == 1 means tile i is still possible.
    private NativeArray<uint> _wave; // size = width * height
    private NativeArray<uint> _compatibleMaskN; // [tile]
    private NativeArray<uint> _compatibleMaskE; // [tile]
    private NativeArray<uint> _compatibleMaskS; // [tile]
    private NativeArray<uint> _compatibleMaskW; // [tile]

    // Entropy management
    private readonly MinHeap<CellEntropy> _heap = new();

    private readonly Queue<int> _propagateQueue = new(); // linear index
    private System.Random _rng;

    /* ---------- MONO BEHAVIOUR ---------- */

    private void Start() => _ = StartCoroutine(GenerateIslandCoroutine());

    /* ---------- HIGH‑LEVEL CONTROL ---------- */

    private System.Collections.IEnumerator GenerateIslandCoroutine()
    {
        _rng = new System.Random();
        BuildCompatibilityMasks();

        while (true) // infinite loop until success
        {
            ResetDataStructures();

            // Run WFC
            var stepper = WfcStepper();
            while (stepper.MoveNext())
                yield return null; // spread work over frames

            // Did any cell end up with mask==0 ?
            bool bad = false;
            for (int i = 0; i < _wave.Length && !bad; i++)
                bad |= _wave[i] == 0;

            if (bad)
            {
                Debug.LogWarning("WFC contradiction — retrying");
                continue; // restart the whole process
            }

            InstantiateTiles();
            FillAirWithWaterOrBubble();
            Debug.Log($"Island generated – {transform.childCount} tiles");
            break; // success ⇒ leave coroutine
        }
    }

    /* ---------- PRECOMPUTED COMPATIBILITY ---------- */

    private void BuildCompatibilityMasks()
    {
        int tCount = tiles.Length;
        if (_compatibleMaskN.IsCreated) _compatibleMaskN.Dispose();
        if (_compatibleMaskE.IsCreated) _compatibleMaskE.Dispose();
        if (_compatibleMaskS.IsCreated) _compatibleMaskS.Dispose();
        if (_compatibleMaskW.IsCreated) _compatibleMaskW.Dispose();

        _compatibleMaskN = new NativeArray<uint>(tCount, Allocator.Persistent);
        _compatibleMaskE = new NativeArray<uint>(tCount, Allocator.Persistent);
        _compatibleMaskS = new NativeArray<uint>(tCount, Allocator.Persistent);
        _compatibleMaskW = new NativeArray<uint>(tCount, Allocator.Persistent);

        for (int a = 0; a < tCount; a++)
        {
            uint maskN = 0, maskE = 0, maskS = 0, maskW = 0;
            for (int b = 0; b < tCount; b++)
            {
                bool ok = IsLogicallyAllowed(tiles[a].name, tiles[b].name);
                if (ok)
                {
                    maskN |= 1u << b;
                    maskE |= 1u << b;
                    maskS |= 1u << b;
                    maskW |= 1u << b;
                }
            }
            _compatibleMaskN[a] = maskN;
            _compatibleMaskE[a] = maskE;
            _compatibleMaskS[a] = maskS;
            _compatibleMaskW[a] = maskW;
        }
    }

    private static bool IsLogicallyAllowed(string a, string b)
    {
        bool aAir = a.Equals("Air", StringComparison.OrdinalIgnoreCase);
        bool bAir = b.Equals("Air", StringComparison.OrdinalIgnoreCase);
        bool aIsl = a.Equals("Island", StringComparison.OrdinalIgnoreCase);
        bool bIsl = b.Equals("Island", StringComparison.OrdinalIgnoreCase);
        bool aWater = a.Equals("Water", StringComparison.OrdinalIgnoreCase);
        bool bWater = b.Equals("Water", StringComparison.OrdinalIgnoreCase);

        if (aAir && bAir) return true;
        if (aAir) return bIsl;
        if (bAir) return aIsl;

        bool aDecor =
            a.Equals("Tree", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("Animal", StringComparison.OrdinalIgnoreCase);
        bool bDecor =
            b.Equals("Tree", StringComparison.OrdinalIgnoreCase) ||
            b.Equals("Animal", StringComparison.OrdinalIgnoreCase);
        if (aDecor && bDecor) return true;
        if (aDecor) return bIsl || bWater;
        if (bDecor) return aIsl || aWater;

        bool aBubble = a.Equals("Bubble", StringComparison.OrdinalIgnoreCase);
        bool bBubble = b.Equals("Bubble", StringComparison.OrdinalIgnoreCase);
        if (aBubble) return bWater;
        if (bBubble) return aWater;

        return true; // Island↔Island etc.
    }

    /* ---------- INITIALISATION ---------- */

    private void ResetDataStructures()
    {
        int cells = width * height;
        if (_wave.IsCreated) _wave.Dispose();
        _wave = new NativeArray<uint>(cells, Allocator.Persistent);

        // Allow everything initially
        uint all = (uint)((1 << tiles.Length) - 1);
        for (int i = 0; i < cells; i++) _wave[i] = all;

        _heap.Clear();
        _propagateQueue.Clear();

        float cx = width / 2f, cy = height / 2f,
              radius = Mathf.Min(width, height) / 2f;

        // Seed entropy & circle mask
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            float dx = x - cx, dy = y - cy;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist > radius)
            {
                // outside – only Air (index 0)
                _wave[Idx(x, y)] = 1u;
            }

            _heap.Push(CalcEntropy(x, y));
        }
    }

    /* ---------- CORE WFC ---------- */

    // This method is no longer used due to changes in GenerateIslandCoroutine
    // private bool RunWfcCoroutine(out IEnumerator<object> routine)
    // {
    //     routine = WfcStepper();
    //     return true; // success reported through routine
    // }

    private IEnumerator<object> WfcStepper()
    {
        const int CELLS_PER_FRAME = 512;        // tweak to taste
        int stepBudget = CELLS_PER_FRAME;

        while (_heap.Count > 0)
        {
            CellEntropy ce = _heap.PopMin();
            if (!IsStillValid(ce)) continue;

            // Collapse one cell
            uint chosen = SelectTileMask(ce.waveMask);
            _wave[ce.index] = chosen;

            _propagateQueue.Enqueue(ce.index);
            Propagate();

            // budget bookkeeping
            if (--stepBudget == 0)
            {
                stepBudget = CELLS_PER_FRAME;
                yield return null;              // give the frame back
            }
        }
    }


    private void Propagate()
    {
        while (_propagateQueue.Count > 0)
        {
            int idx = _propagateQueue.Dequeue();
            (int x, int y) = UnIdx(idx);
            uint cellMask = _wave[idx];

            for (int d = 0; d < DIR_COUNT; d++)
            {
                int nx = x + DX[d];
                int ny = y + DY[d];
                if (!InBounds(nx, ny)) continue;

                int nIdx = Idx(nx, ny);
                uint before = _wave[nIdx];
                uint allow = AllowedMaskForNeighbour(cellMask, d);
                uint after = before & allow;

                if (after != before && after != 0)
                {
                    _wave[nIdx] = after;
                    _heap.Push(CalcEntropy(nx, ny));
                    _propagateQueue.Enqueue(nIdx);
                }
            }
        }
    }

    /* ---------- TILE WEIGHTS / ENTROPY ---------- */

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint SelectTileMask(uint mask)
    {
        // Weighted random pick
        double total = 0;
        for (int t = 0; t < tiles.Length; t++)
            if ((mask & (1u << t)) != 0)
                total += tiles[t].weight;

        double r = _rng.NextDouble() * total;
        for (int t = 0; t < tiles.Length; t++)
        {
            if ((mask & (1u << t)) == 0) continue;
            r -= tiles[t].weight;
            if (r <= 0) return (uint)(1 << t);
        }
        return mask; // fallback – shouldn't occur
    }

    private CellEntropy CalcEntropy(int x, int y)
    {
        int idx = Idx(x, y);
        uint mask = _wave[idx];
        if ((mask & (mask - 1)) == 0) // power of two → already collapsed
            return new CellEntropy(float.PositiveInfinity, idx, mask);

        double sumW = 0, sumWLogW = 0;
        for (int t = 0; t < tiles.Length; t++)
        {
            if ((mask & (1u << t)) == 0) continue;
            double w = tiles[t].weight;
            sumW += w;
            sumWLogW += w * Math.Log(w);
        }

        float entropy = (float)(
            Math.Log(sumW) - sumWLogW / sumW + _rng.NextDouble() * 1e-6
        ); // jitter
        return new CellEntropy(entropy, idx, mask);
    }

    /* ---------- UNITY-COMPATIBLE BIT OPERATIONS ---------- */

    /// <summary>
    /// Unity-compatible replacement for BitOperations.TrailingZeroCount
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int TrailingZeroCount(uint mask)
    {
        if (mask == 0) return 32;
        int cnt = 0;
        while ((mask & 1) == 0)
        {
            mask >>= 1;
            cnt++;
        }
        return cnt;
    }

    /* ---------- FILL AIR / INSTANTIATE ---------- */

    private void FillAirWithWaterOrBubble()
    {
        const int AIR = 0, WATER = 2, BUBBLE = 3;

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            int idx = Idx(x, y);
            if (_wave[idx] != 1u << AIR) continue; // not pure air

            int waterNeighbours = 0;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (InBounds(nx, ny) && _wave[Idx(nx, ny)] == 1u << WATER)
                    waterNeighbours++;
            }

            if (waterNeighbours >= 2)
                _wave[idx] =
                    1u << (_rng.NextDouble() < 0.8 ? BUBBLE : WATER);
        }

        /* clear previous meshes **before** re‑instantiating */
        foreach (Transform c in transform) DestroyImmediate(c.gameObject);
        InstantiateTiles();
    }

    private void InstantiateTiles()
    {
        foreach (Transform c in transform) DestroyImmediate(c.gameObject);

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            uint mask = _wave[Idx(x, y)];
            if (mask == 0) // ← skip contradictions
                continue;

            int tileIndex = TrailingZeroCount(mask);
            Vector3 pos =
                transform.position + new Vector3(x * cellSize.x, 0, y * cellSize.y);
            Instantiate(tiles[tileIndex].prefab, pos, Quaternion.identity, transform);
        }
    }
    /* ---------- INLINE HELPERS ---------- */

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint AllowedMaskForNeighbour(uint cellMask, int dir)
    {
        uint acc = 0;
        // Iterate set bits in cellMask
        uint m = cellMask;
        while (m != 0)
        {
            int t = TrailingZeroCount(m);
            acc |=
                dir switch
                {
                    0 => _compatibleMaskN[t],
                    1 => _compatibleMaskE[t],
                    2 => _compatibleMaskS[t],
                    _ => _compatibleMaskW[t],
                };
            m &= m - 1; // clear lowest
        }
        return acc;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool InBounds(int x, int y) => (uint)x < width && (uint)y < height;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Idx(int x, int y) => x + y * width;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (int, int) UnIdx(int idx) => (idx % width, idx / width);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsStillValid(CellEntropy ce) =>
        _wave[ce.index] == ce.waveMask && ce.entropy == ce.entropy; // NaN check

    /* ---------- NESTED TYPES ---------- */

    private readonly struct CellEntropy : IComparable<CellEntropy>
    {
        public readonly float entropy;
        public readonly int index;
        public readonly uint waveMask;
        public CellEntropy(float e, int i, uint m)
        {
            entropy = e;
            index = i;
            waveMask = m;
        }
        public int CompareTo(CellEntropy other) => entropy.CompareTo(other.entropy);
    }

    /// <summary> Extremely small binary min‑heap. </summary>
    private sealed class MinHeap<T> where T : IComparable<T>
    {
        private readonly List<T> _data = new();
        public int Count => _data.Count;
        public void Clear() => _data.Clear();
        public void Push(T item)
        {
            _data.Add(item);
            int c = _data.Count - 1;
            while (c > 0)
            {
                int p = (c - 1) >> 1;
                if (_data[c].CompareTo(_data[p]) >= 0) break;
                (_data[c], _data[p]) = (_data[p], _data[c]);
                c = p;
            }
        }
        public T PopMin()
        {
            int last = _data.Count - 1;
            T min = _data[0];
            _data[0] = _data[last];
            _data.RemoveAt(last);
            int p = 0;
            while (true)
            {
                int l = (p << 1) + 1;
                if (l >= _data.Count) break;
                int r = l + 1;
                int c = (r < _data.Count && _data[r].CompareTo(_data[l]) < 0) ? r : l;
                if (_data[p].CompareTo(_data[c]) <= 0) break;
                (_data[p], _data[c]) = (_data[c], _data[p]);
                p = c;
            }
            return min;
        }
    }

    /* ---------- CLEANUP ---------- */
    private void OnDestroy()
    {
        if (_wave.IsCreated) _wave.Dispose();
        if (_compatibleMaskN.IsCreated) _compatibleMaskN.Dispose();
        if (_compatibleMaskE.IsCreated) _compatibleMaskE.Dispose();
        if (_compatibleMaskS.IsCreated) _compatibleMaskS.Dispose();
        if (_compatibleMaskW.IsCreated) _compatibleMaskW.Dispose();
    }
}