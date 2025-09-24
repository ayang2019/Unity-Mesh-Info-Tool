///*************************************************
// * 工具名: 网格信息工具 (Mesh-Info-Tools) 之数学计算类
// * 作者  : ayangbing@hotmail.com
// * 仓库  : https://github.com/ayang2019/Unity-Mesh-Info-Tool/
// * 许可  : MIT License
// * 版本  : 1.0.0
// * 日期  : <最后修改日期：2025-06-25>
// * 
// * 功能  : Unity Editor 插件，一键标注网格点并实时测量多段距离
// * 说明  : 详见 GitHub README
// *************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class MeshMath
{
    public static bool cancelFlag = false;   // 全局取消令牌

    /* -------------------- 1. 焊接顶点 -------------------- */
    public static IEnumerator WeldAsync(Mesh mesh, Action<int[]> onFinish,
                                         Action<float> onProgress)
    {
        cancelFlag = false;
        Vector3[] verts = mesh.vertices;
        int n = verts.Length;
        int[] remap = new int[n];
        for (int i = 0; i < n; ++i) remap[i] = i;

        int step = 500;
        for (int i = 0; i < n; ++i)
        {
            if (remap[i] != i) continue;
            for (int j = i + 1; j < n; ++j)
            {
                if ((verts[i] - verts[j]).sqrMagnitude < 1e-10f)
                    remap[j] = i;
            }

            if (i % step == 0)
            {
                onProgress?.Invoke((float)i / n);
                if (cancelFlag) yield break;
                yield return null;
            }
        }
        onProgress?.Invoke(1f);
        onFinish?.Invoke(remap);
    }

    /* -------------------- 2. 封闭判断 -------------------- */
    public static IEnumerator IsClosedAsync(Mesh mesh, int[] weld,
                                             Action<bool> onFinish,
                                             Action<float> onProgress)
    {
        int[] tri = mesh.triangles;
        int total = tri.Length;
        int step = 300;
        var edges = new Dictionary<Edge, int>(tri.Length / 3);

        for (int i = 0; i < total; i += 3)
        {
            int i0 = weld[tri[i]];
            int i1 = weld[tri[i + 1]];
            int i2 = weld[tri[i + 2]];
            AddEdge(edges, i0, i1);
            AddEdge(edges, i1, i2);
            AddEdge(edges, i2, i0);

            if (i % (step * 3) == 0)
            {
                onProgress?.Invoke((float)i / total);
                if (cancelFlag) yield break;
                yield return null;
            }
        }

        bool closed = true;
        foreach (var kv in edges)
            if ((kv.Value & 1) != 0) { closed = false; break; }

        onProgress?.Invoke(1f);
        onFinish?.Invoke(closed);
    }

    /* -------------------- 3. 体积计算 -------------------- */
    public static IEnumerator VolumeAsync(Mesh mesh, int[] weld,
                                           Action<float> onFinish,
                                           Action<float> onProgress)
    {
        int[] tri = mesh.triangles;
        Vector3[] v = mesh.vertices;
        int total = tri.Length;
        int step = 300;
        double vol = 0.0;

        for (int i = 0; i < total; i += 3)
        {
            Vector3 p0 = v[weld[tri[i]]];
            Vector3 p1 = v[weld[tri[i + 1]]];
            Vector3 p2 = v[weld[tri[i + 2]]];
            vol += Vector3.Dot(p0, Vector3.Cross(p1, p2));

            if (i % (step * 3) == 0)
            {
                onProgress?.Invoke((float)i / total);
                if (cancelFlag) yield break;
                yield return null;
            }
        }

        onProgress?.Invoke(1f);
        onFinish?.Invoke((float)Math.Abs(vol) / 6f);
    }

    /* ---------- 工具 ---------- */
    private static void AddEdge(Dictionary<Edge, int> d, int a, int b)
    {
        var e = new Edge(Mathf.Min(a, b), Mathf.Max(a, b));
        if (!d.TryAdd(e, 1)) d[e]++;
    }

    private readonly struct Edge : IEquatable<Edge>
    {
        public readonly int a, b;
        public Edge(int x, int y) { a = x; b = y; }
        public bool Equals(Edge o) => a == o.a && b == o.b;
        public override int GetHashCode() => a * 0x7fffffff + b;
    }
}
