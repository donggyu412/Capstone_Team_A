using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;

[CustomEditor(typeof(RobotIKSolver))]
public class RobotIKSolverEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        GUILayout.Space(4);

        RobotIKSolver solver = (RobotIKSolver)target;
        GUI.color = solver.IsReachable ? Color.green : Color.red;
        GUILayout.Label(solver.IsReachable ? "✔ 도달 가능" : "✘ 도달 불가");
        GUI.color = Color.white;

        if (GUILayout.Button("Solve IK (디버그 목표로 테스트)"))
            solver.Solve(solver.debugTarget);
    }
}
#endif


/// <summary>
/// 5DOF 드로잉 로봇의 역기구학 계산기.
///
/// 관절 구조 (씬 실측 기준):
///   Arm1(θ1,Y) →[L1=2.8]→ Arm2(θ2,Y) →[L2=2.8]→ WristYaw(θ3,Y)
///   → Slide(d,Y↕) → WristPitch(θ4,X) →[wrist=2.575]→ BrushTip
///
/// 캔버스는 벽면에 수직 (XY 평면, Z 고정).
///   θ1, θ2 — XZ 수평 SCARA로 X 위치 제어
///   d       — Slide 상하로 Y 위치 제어
///   θ3      — 붓 방향(YAW)
///   θ4      — 붓 기울기(PITCH)
/// </summary>
public class RobotIKSolver : MonoBehaviour
{
    // ── 링크 길이 ─────────────────────────────────────────────────────────────
    // 씬 실측값: Arm2.position.x = 2.8, WristYaw.position.x = 2.8 (Link2 수정 후)
    [Header("링크 길이 (씬 실측값)")]
    public float link1Length = 2.8f;    // Arm1 → Arm2
    public float link2Length = 2.8f;    // Arm2 → WristYaw
    public float wristLength = 2.575f;  // WristPitch → BrushTip (Brush 1.4 + BrushTip 1.175)

    // ── 슬라이드 설정 ─────────────────────────────────────────────────────────
    // slideBaseY = RobotRoot.y(7) + Arm1.localY(0.814) = 7.814
    // d = slideBaseY - target.y → 목표가 낮을수록 d 증가 (더 내려감)
    [Header("슬라이드 (상하 이동)")]
    public float slideBaseY = 7.814f;
    public float slideMin   = 0f;
    public float slideMax   = 5f;

    // ── 관절 한계 ─────────────────────────────────────────────────────────────
    [Header("관절 한계 (도)")]
    public float arm1Min  = -170f; public float arm1Max  = 170f;
    public float arm2Min  =    0f; public float arm2Max  = 150f;
    public float yawMin   = -180f; public float yawMax   = 180f;
    public float pitchMin =  -90f; public float pitchMax = -10f;

    // ── 캔버스 범위 ───────────────────────────────────────────────────────────
    // 벽면 수직 캔버스 (XY 평면, Z 고정)
    // canvasCenter: Canvas 오브젝트의 월드 Position과 일치시킬 것
    [Header("캔버스")]
    public Vector3 canvasCenter = new Vector3(0f, 5f, 4f);
    public float   canvasWidth  = 4f;
    public float   canvasHeight = 3f;

    // ── 디버그 ────────────────────────────────────────────────────────────────
    [Header("디버그")]
    public Vector3 debugTarget;

    // ── IK 결과 (읽기 전용) ───────────────────────────────────────────────────
    public float Theta1      { get; private set; }
    public float Theta2      { get; private set; }
    public float Theta3      { get; private set; }
    public float Theta4      { get; private set; }
    public float D           { get; private set; }
    public bool  IsReachable { get; private set; }


    /// <summary>
    /// IK 계산. 붓끝 목표 위치를 받아 θ1~4, d를 갱신한다.
    /// </summary>
    public void Solve(Vector3 target, float yawDeg = 0f, float pitchDeg = -90f)
    {
        // 1. 캔버스 범위 클램프 + Z 고정
        Vector3 t = ClampToCanvas(target);

        // 2. BrushTip → WristPitch 역추적 (wristLength만큼 yaw 반대 방향)
        float yawRad = yawDeg * Mathf.Deg2Rad;
        float wx = t.x - wristLength * Mathf.Sin(yawRad);
        float wz = t.z - wristLength * Mathf.Cos(yawRad);

        // 3. XZ 수평 도달 거리 (SCARA)
        float r2       = wx * wx + wz * wz;
        float r        = Mathf.Sqrt(r2);
        float maxReach = link1Length + link2Length;
        float minReach = Mathf.Abs(link1Length - link2Length);

        IsReachable = r >= minReach && r <= maxReach;

        // 특이점 회피 클램프
        if (r > maxReach)
        {
            float s = maxReach * 0.995f / r;
            wx *= s; wz *= s; r2 = wx * wx + wz * wz;
        }
        else if (r < minReach + 0.01f)
        {
            float s = (minReach + 0.01f) / Mathf.Max(r, 0.001f);
            wx *= s; wz *= s; r2 = wx * wx + wz * wz;
        }

        // 4. 코사인 법칙 (elbow-up)
        float cosA2 = Mathf.Clamp(
            (r2 - link1Length * link1Length - link2Length * link2Length)
            / (2f * link1Length * link2Length), -1f, 1f);
        float sinA2     = Mathf.Sqrt(1f - cosA2 * cosA2);
        float rawTheta2 = Mathf.Atan2(sinA2, cosA2) * Mathf.Rad2Deg;
        float rawTheta1 = (Mathf.Atan2(wz, wx)
                         - Mathf.Atan2(link2Length * sinA2,
                                        link1Length + link2Length * cosA2))
                         * Mathf.Rad2Deg;

        // 5. 관절 한계 적용
        Theta1 = Mathf.Clamp(rawTheta1,                arm1Min,  arm1Max);
        Theta2 = Mathf.Clamp(rawTheta2,                arm2Min,  arm2Max);
        Theta3 = Mathf.Clamp(yawDeg - Theta1 - Theta2, yawMin,   yawMax);
        Theta4 = Mathf.Clamp(pitchDeg,                 pitchMin, pitchMax);

        // 6. 슬라이드: slideBaseY에서 목표 Y만큼 내려간 거리
        D = Mathf.Clamp(slideBaseY - t.y, slideMin, slideMax);

        Debug.Log($"[IK] θ1:{Theta1:F2}° θ2:{Theta2:F2}° θ3:{Theta3:F2}° " +
                  $"θ4:{Theta4:F2}° d:{D:F3} | 도달:{IsReachable}");
    }

    /// <summary>캔버스 XY 범위 클램프 + Z 고정.</summary>
    public Vector3 ClampToCanvas(Vector3 pos)
    {
        return new Vector3(
            Mathf.Clamp(pos.x, canvasCenter.x - canvasWidth  * 0.5f,
                                canvasCenter.x + canvasWidth  * 0.5f),
            Mathf.Clamp(pos.y, canvasCenter.y - canvasHeight * 0.5f,
                                canvasCenter.y + canvasHeight * 0.5f),
            canvasCenter.z
        );
    }

    /// <summary>캔버스 정규화 좌표(0~1)로 Solve 호출. (0,0)=좌하단 (1,1)=우상단.</summary>
    public void SolveNormalized(float u, float v, float yawDeg = 0f, float pitchDeg = -90f)
    {
        Solve(new Vector3(
            canvasCenter.x + (u - 0.5f) * canvasWidth,
            canvasCenter.y + (v - 0.5f) * canvasHeight,
            canvasCenter.z
        ), yawDeg, pitchDeg);
    }


    // ── Gizmo ─────────────────────────────────────────────────────────────────

    void OnDrawGizmos()
    {
        DrawCanvasGizmo();
        DrawReachGizmo();
        DrawTargetGizmo();
    }

    private void DrawCanvasGizmo()
    {
        float hw = canvasWidth  * 0.5f;
        float hh = canvasHeight * 0.5f;
        float z  = canvasCenter.z;

        Vector3 tl = new Vector3(canvasCenter.x - hw, canvasCenter.y + hh, z);
        Vector3 tr = new Vector3(canvasCenter.x + hw, canvasCenter.y + hh, z);
        Vector3 bl = new Vector3(canvasCenter.x - hw, canvasCenter.y - hh, z);
        Vector3 br = new Vector3(canvasCenter.x + hw, canvasCenter.y - hh, z);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(tl, tr); Gizmos.DrawLine(tr, br);
        Gizmos.DrawLine(br, bl); Gizmos.DrawLine(bl, tl);
        Gizmos.color = new Color(1f, 1f, 0f, 0.25f);
        Gizmos.DrawLine(tl, br); Gizmos.DrawLine(tr, bl);
        Gizmos.color = new Color(1f, 1f, 0f, 0.05f);
        Gizmos.DrawCube(canvasCenter, new Vector3(canvasWidth, canvasHeight, 0.01f));

#if UNITY_EDITOR
        UnityEditor.Handles.Label(tl + Vector3.up * 0.2f,
            $"캔버스 {canvasWidth}×{canvasHeight}");
#endif
    }

    private void DrawReachGizmo()
    {
        Vector3 origin = new Vector3(transform.position.x, 0f, transform.position.z);
        Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
        DrawCircleXZ(origin, link1Length + link2Length, 64);
        float minR = Mathf.Abs(link1Length - link2Length);
        if (minR > 0.05f)
        {
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.2f);
            DrawCircleXZ(origin, minR, 32);
        }
    }

    private void DrawTargetGizmo()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(debugTarget, 0.1f);
        Vector3 clamped = ClampToCanvas(debugTarget);
        if (Vector3.Distance(clamped, debugTarget) > 0.01f)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(clamped, 0.1f);
            Gizmos.DrawLine(debugTarget, clamped);
        }
    }

    private void DrawCircleXZ(Vector3 center, float radius, int segments)
    {
        float step = Mathf.PI * 2f / segments;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * step, a1 = (i + 1) * step;
            Gizmos.DrawLine(
                center + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius,
                center + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius);
        }
    }
}