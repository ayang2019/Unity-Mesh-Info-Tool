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
#if UNITY_EDITOR
using System.Collections;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;

public class MeshInfoWindow : EditorWindow
{
    [MenuItem("Tools/Mesh Info Tool")]
    static void Open() => GetWindow<MeshInfoWindow>("Mesh Info");

    /* ---------- 持久化 ---------- */
    [SerializeField] private float density = 1f;
    private bool lastClosed;
    private float lastVolume;
    private bool hasResult;

    /* ---------- 协程控制 ---------- */
    private EditorCoroutine runningCoroutine;
    private string phaseName = "";
    private float phasePercent = 0f;
    private bool isRunning = false;

    /* ------------------- 生命周期 ------------------- */
    void OnEnable()
    {
        if (EditorPrefs.HasKey("MeshInfo_density"))
            density = EditorPrefs.GetFloat("MeshInfo_density", 1f);
        Selection.selectionChanged += OnSelect;
        OnSelect();
    }

    void OnDisable()
    {
        EditorPrefs.SetFloat("MeshInfo_density", density);
        Selection.selectionChanged -= OnSelect;
        StopAll();
    }

    void OnSelect()
    {
        StopAll();
        hasResult = false;
        Repaint();
    }

    /* ------------------- GUI ------------------- */
    void OnGUI()
    {
        var mf = Selection.activeGameObject?.GetComponent<MeshFilter>();
        if (!mf || !mf.sharedMesh)
        {
            GUILayout.Label("请选中一个带 MeshFilter 的物体.");
            return;
        }

        Mesh mesh = mf.sharedMesh;
        GUILayout.Label($"网格：{mesh.name}");
        GUILayout.Label($"面数：{mesh.triangles.Length / 3}");

        density = EditorGUILayout.FloatField("密度（g/cm³）", density);
        if (GUI.changed) hasResult = false;

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = !isRunning;
            if (GUILayout.Button("开始计算")) StartThreeSteps(mesh);
            GUI.enabled = isRunning;
            if (GUILayout.Button("中断")) StopAll();
            GUI.enabled = true;
        }

        if (isRunning)
        {
            EditorGUILayout.LabelField($"{phaseName} {phasePercent * 100f:F1}%");
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.horizontalSlider);
            EditorGUI.ProgressBar(r, phasePercent, "");
            Repaint();
        }
        else if (hasResult)
        {
            GUILayout.Label(lastClosed ? "✔ 网格封闭" : "✘ 网格有开口");
            if (lastClosed)
            {
                GUILayout.Label($"体积：{lastVolume:F4} cm³");
                GUILayout.Label($"质量：{lastVolume * density:F2} g");
            }
        }
    }

    /* ------------------- 三阶段协程 ------------------- */
    void StartThreeSteps(Mesh mesh)
    {
        StopAll();
        isRunning = true;
        hasResult = false;
        runningCoroutine = EditorCoroutineUtility.StartCoroutine(ThreeStepsCoroutine(mesh), this);
    }

    void StopAll()
    {
        if (runningCoroutine != null)
        {
            MeshMath.cancelFlag = true;
            EditorCoroutineUtility.StopCoroutine(runningCoroutine);
            runningCoroutine = null;
        }
        isRunning = false;
    }

    IEnumerator ThreeStepsCoroutine(Mesh mesh)
    {
        int[] weld = null;
        bool closed = false;
        float volume = 0f;

        phaseName = "焊接顶点"; phasePercent = 0f;
        yield return MeshMath.WeldAsync(mesh, w => weld = w, p => phasePercent = p);
        if (MeshMath.cancelFlag) { StopAll(); yield break; }

        phaseName = "封闭判断"; phasePercent = 0f;
        yield return MeshMath.IsClosedAsync(mesh, weld, c => closed = c, p => phasePercent = p);
        if (MeshMath.cancelFlag) { StopAll(); yield break; }

        if (closed)
        {
            phaseName = "体积计算"; phasePercent = 0f;
            yield return MeshMath.VolumeAsync(mesh, weld, v => volume = v, p => phasePercent = p);
            if (MeshMath.cancelFlag) { StopAll(); yield break; }
        }

        lastClosed = closed;
        lastVolume = volume;
        hasResult = true;
        isRunning = false;
        phaseName = "";
    }
}
#endif
