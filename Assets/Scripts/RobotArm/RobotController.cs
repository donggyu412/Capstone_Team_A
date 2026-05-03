using UnityEngine;

/// <summary>
/// RobotIKSolver 결과를 읽어 실제 관절 Transform을 구동한다.
///
/// 호출 흐름:
///   RobotInputHandler → SetTarget()
///   → IKSolver.Solve() → θ1~4, D 갱신
///   → Update()에서 매 프레임 Lerp 보간 → ApplyToJoints()
/// </summary>
public class RobotController : MonoBehaviour
{
    // ── 관절 Transform ────────────────────────────────────────────────────────
    // Inspector에서 각 오브젝트를 드래그해서 연결한다.
    [Header("관절 Transform 연결")]
    public Transform arm1;        // Arm1       — Y축 회전 (θ1)
    public Transform arm2;        // Arm2       — Y축 회전 (θ2)
    public Transform wristYaw;    // WristYaw   — Y축 회전 (θ3)
    public Transform slide;       // Slide      — Y축 선형 이동 (d)
    public Transform wristPitch;  // WristPitch — X축 회전 (θ4)
    public Transform brushTip;    // BrushTip   — 참조용

    // ── 참조 ──────────────────────────────────────────────────────────────────
    [Header("참조")]
    public RobotIKSolver ikSolver;

    // ── 보간 속도 ─────────────────────────────────────────────────────────────
    [Header("보간 속도")]
    public float rotateSpeed = 6f;
    public float slideSpeed  = 6f;

    // ── 내부 보간 상태 ────────────────────────────────────────────────────────
    private float _t1, _t2, _t3, _t4, _d;


    void Start()
    {
        if (ikSolver == null)
            ikSolver = GetComponent<RobotIKSolver>();

        if (ikSolver == null)
        {
            Debug.LogError("[RobotController] RobotIKSolver를 찾을 수 없습니다!");
            enabled = false;
            return;
        }

        // 시작 시 IK 결과와 동기화 — 관절 튀는 현상 방지
        _t1 = ikSolver.Theta1; _t2 = ikSolver.Theta2;
        _t3 = ikSolver.Theta3; _t4 = ikSolver.Theta4;
        _d  = ikSolver.D;
        ApplyToJoints();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        _t1 = Mathf.LerpAngle(_t1, ikSolver.Theta1, dt * rotateSpeed);
        _t2 = Mathf.LerpAngle(_t2, ikSolver.Theta2, dt * rotateSpeed);
        _t3 = Mathf.LerpAngle(_t3, ikSolver.Theta3, dt * rotateSpeed);
        _t4 = Mathf.LerpAngle(_t4, ikSolver.Theta4, dt * rotateSpeed);
        _d  = Mathf.Lerp    (_d,  ikSolver.D,       dt * slideSpeed);
        ApplyToJoints();
    }

    private void ApplyToJoints()
    {
        if (arm1)       arm1.localEulerAngles      = new Vector3(0f, _t1, 0f);
        if (arm2)       arm2.localEulerAngles       = new Vector3(0f, _t2, 0f);
        if (wristYaw)   wristYaw.localEulerAngles   = new Vector3(0f, _t3, 0f);
        if (wristPitch) wristPitch.localEulerAngles = new Vector3(_t4, 0f, 0f);
        if (slide)
        {
            Vector3 pos = slide.localPosition;
            pos.y = -_d;
            slide.localPosition = pos;
        }
    }

    // ── 외부 API ──────────────────────────────────────────────────────────────

    /// <summary>월드 좌표로 목표 설정. 캔버스 범위 초과 시 자동 클램프.</summary>
    public void SetTarget(Vector3 worldPos, float yawDeg = 0f, float pitchDeg = -90f)
    {
        ikSolver.Solve(worldPos, yawDeg, pitchDeg);
    }

    /// <summary>캔버스 정규화 좌표(0~1)로 목표 설정. (0,0)=좌하단 (1,1)=우상단.</summary>
    public void SetTargetNormalized(float u, float v, float yawDeg = 0f, float pitchDeg = -90f)
    {
        ikSolver.SolveNormalized(u, v, yawDeg, pitchDeg);
    }

    /// <summary>보간 완료 여부. 연속 획 구현 시 다음 획 시작 조건으로 활용.</summary>
    public bool IsAtTarget(float angleThr = 0.5f, float slideThr = 0.01f)
    {
        return
            Mathf.Abs(Mathf.DeltaAngle(_t1, ikSolver.Theta1)) < angleThr &&
            Mathf.Abs(Mathf.DeltaAngle(_t2, ikSolver.Theta2)) < angleThr &&
            Mathf.Abs(Mathf.DeltaAngle(_t3, ikSolver.Theta3)) < angleThr &&
            Mathf.Abs(Mathf.DeltaAngle(_t4, ikSolver.Theta4)) < angleThr &&
            Mathf.Abs(_d - ikSolver.D) < slideThr;
    }

    // Gizmo: BrushTip 위치를 Scene뷰에서 빨간 점으로 표시
    private void OnDrawGizmos()
    {
        if (brushTip == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(brushTip.position, 0.08f);
    }
}