///*************************************************
// * 工具名: 网格信息工具 (Mesh-Info-Tools) 之插件窗口
// * 作者  : ayangbing@hotmail.com
// * 仓库  : https://github.com/ayang2019/Unity-Mesh-Info-Tool/
// * 许可  : MIT License
// * 版本  : 1.0.0
// * 日期  : <最后修改日期：2025-06-25>
// * 
// * 功能  : Unity Editor 插件，一键标注网格点并实时测量多段距离
// * 说明  : 详见 GitHub README
// *************************************************/
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class MeshFillerAsync
{
    public class Report
    {
        public float progress;          // 0~1
        public string phase;            // 当前阶段文字
        public bool finished;
        public bool cancelled;
    }

    private readonly Mesh _mesh;
    private readonly CancellationToken _token;
    public Report CurrentReport { get; private set; }

    public MeshFillerAsync(Mesh mesh, CancellationToken token)
    {
        _mesh = mesh;
        _token = token;
    }

    // 主协程，由窗口驱动 StartCoroutine
    public IEnumerator Fill()
    {
        CurrentReport = new Report { phase = "正在分析边界…", progress = 0 };
        yield return null;

        /* ---------- 1. 找边界 ---------- */
        var border = new HashSet<Edge>();
        var tri = _mesh.triangles;
        int len = tri.Length;
        for (int i = 0; i < len; i += 3)
        {
            if (_token.IsCancellationRequested) { CurrentReport.cancelled = true; yield break; }
            AddEdge(border, tri[i], tri[i + 1]);
            AddEdge(border, tri[i + 1], tri[i + 2]);
            AddEdge(border, tri[i + 2], tri[i]);
            if (i % 300 == 0)          // 每 100 个三角 yield 一次
            {
                CurrentReport.progress = (float)i / len * 0.3f;
                yield return null;
            }
        }

        /* ---------- 2. 连成环 ---------- */
        CurrentReport.phase = "正在构建边界环…";
        CurrentReport.progress = 0.3f;
        yield return null;

        var loops = BuildLoops(border);
        int totalLoops = loops.Count;
        int loopIndex = 0;

        /* ---------- 3. 三角化 ---------- */
        var newVerts = new List<Vector3>(_mesh.vertices);
        var newTris = new List<int>(_mesh.triangles);
        foreach (var loop in loops)
        {
            if (_token.IsCancellationRequested) { CurrentReport.cancelled = true; yield break; }

            CurrentReport.phase = $"正在补洞 {loopIndex + 1}/{totalLoops}";
            CurrentReport.progress = 0.3f + (float)loopIndex / totalLoops * 0.6f;
            yield return null;

            var tris = TriangulateLoop(newVerts, loop);
            newTris.AddRange(tris);
            loopIndex++;
        }

        /* ---------- 4. 写回 ---------- */
        CurrentReport.phase = "正在写回Mesh…";
        CurrentReport.progress = 0.95f;
        yield return null;

        _mesh.Clear();
        _mesh.vertices = newVerts.ToArray();
        _mesh.triangles = newTris.ToArray();
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        CurrentReport.phase = "完成！";
        CurrentReport.progress = 1f;
        CurrentReport.finished = true;
    }

    #region 原逻辑稍作整理
    private struct Edge { public int a, b; public Edge(int x, int y) { a = Mathf.Min(x, y); b = Mathf.Max(x, y); } }
    private void AddEdge(HashSet<Edge> set, int x, int y)
    {
        var e = new Edge(x, y);
        if (!set.Remove(e)) set.Add(e);
    }

    private List<List<int>> BuildLoops(HashSet<Edge> border)
    {
        var dict = new Dictionary<int, int>();
        foreach (var e in border) { dict[e.a] = e.b; dict[e.b] = e.a; }
        var loops = new List<List<int>>();
        var vis = new HashSet<int>();
        foreach (var start in dict.Keys)
        {
            if (vis.Contains(start)) continue;
            var loop = new List<int>();
            int cur = start;
            do
            {
                vis.Add(cur);
                loop.Add(cur);
                cur = dict[cur];
            } while (cur != start);
            loops.Add(loop);
        }
        return loops;
    }

    private List<int> TriangulateLoop(List<Vector3> meshVerts, List<int> loop)
    {
        int n = loop.Count;
        var pts = new List<Vector3>(n);
        var indexMap = new List<int>(n);
        for (int i = 0; i < n; i++)
        {
            pts.Add(meshVerts[loop[i]]);
            indexMap.Add(loop[i]);
        }

        var tris = new List<int>();
        while (n >= 3)
        {
            bool earFound = false;
            for (int i = 0; i < n; i++)
            {
                int a = (i - 1 + n) % n;
                int b = i;
                int c = (i + 1) % n;
                if (!IsConvex(pts[a], pts[b], pts[c])) continue;

                bool inside = false;
                for (int k = 0; k < n; k++)
                {
                    if (k == a || k == b || k == c) continue;
                    if (PointInTriangle(pts[k], pts[a], pts[b], pts[c])) { inside = true; break; }
                }
                if (inside) continue;

                tris.Add(indexMap[a]);
                tris.Add(indexMap[b]);
                tris.Add(indexMap[c]);

                pts.RemoveAt(i);
                indexMap.RemoveAt(i);
                n--;
                earFound = true;
                break;
            }
            if (!earFound) break;
        }
        return tris;
    }

    private bool IsConvex(Vector3 a, Vector3 b, Vector3 c) =>
        Vector3.Cross(c - b, a - b).sqrMagnitude > 1e-6f;

    private bool PointInTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 u = b - a, v = c - a, w = p - a;
        float d00 = Vector3.Dot(u, u), d01 = Vector3.Dot(u, v), d11 = Vector3.Dot(v, v);
        float d20 = Vector3.Dot(w, u), d21 = Vector3.Dot(w, v);
        float denom = d00 * d11 - d01 * d01;
        float v2 = (d11 * d20 - d01 * d21) / denom;
        float v3 = (d00 * d21 - d01 * d20) / denom;
        return v2 >= 0 && v3 >= 0 && v2 + v3 <= 1;
    }
    #endregion
}
