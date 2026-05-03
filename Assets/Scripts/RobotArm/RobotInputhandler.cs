using UnityEngine;

/// <summary>
/// 마우스/키보드 입력을 RobotController에 전달한다.
///
/// [마우스] 캔버스 Collider에 레이캐스트 → 붓끝 목표 위치
/// [키보드]
///   A/D — 캔버스 좌우 (X)
///   W/S — 캔버스 상하 (Y)
///   Q/E — 붓 기울기 (PITCH)
///   Z/C — 붓 방향 (YAW)
/// </summary>
public class RobotInputHandler : MonoBehaviour
{
    // ── 참조 ──────────────────────────────────────────────────────────────────
    [Header("참조")]
    public RobotController robotController;
    public RobotIKSolver   ikSolver;
    public Collider        canvasCollider; // Canvas 오브젝트의 Collider
    public Camera          mainCamera;

    // ── 설정 ──────────────────────────────────────────────────────────────────
    [Header("키보드 속도")]
    public float moveSpeed  = 2f;
    public float pitchSpeed = 40f;
    public float yawSpeed   = 40f;

    [Header("입력 모드")]
    public InputMode mode = InputMode.Both;
    public enum InputMode { Mouse, Keyboard, Both }

    // ── 내부 상태 ─────────────────────────────────────────────────────────────
    private Vector3 _target;
    private float   _yaw   =   0f;
    private float   _pitch = -90f;


    void Start()
    {
        if (robotController == null) robotController = GetComponent<RobotController>();
        if (ikSolver == null)        ikSolver        = GetComponent<RobotIKSolver>();
        if (mainCamera == null)      mainCamera      = Camera.main;

        _target = ikSolver.canvasCenter;
    }

    void Update()
    {
        if (mode == InputMode.Mouse || mode == InputMode.Both) UpdateMouse();
        if (mode == InputMode.Keyboard || mode == InputMode.Both) UpdateKeyboard();

        robotController.SetTarget(_target, _yaw, _pitch);
    }

    private void UpdateMouse()
    {
        if (canvasCollider == null || mainCamera == null) return;
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (canvasCollider.Raycast(ray, out RaycastHit hit, 200f))
            _target = hit.point;
    }

    private void UpdateKeyboard()
    {
        float dt = Time.deltaTime;

        if (Input.GetKey(KeyCode.A)) _target.x -= moveSpeed * dt;
        if (Input.GetKey(KeyCode.D)) _target.x += moveSpeed * dt;
        if (Input.GetKey(KeyCode.S)) _target.y -= moveSpeed * dt;
        if (Input.GetKey(KeyCode.W)) _target.y += moveSpeed * dt;

        if (Input.GetKey(KeyCode.Q)) _pitch -= pitchSpeed * dt;
        if (Input.GetKey(KeyCode.E)) _pitch += pitchSpeed * dt;
        _pitch = Mathf.Clamp(_pitch, ikSolver.pitchMin, ikSolver.pitchMax);

        if (Input.GetKey(KeyCode.Z)) _yaw -= yawSpeed * dt;
        if (Input.GetKey(KeyCode.C)) _yaw += yawSpeed * dt;
        _yaw = Mathf.Clamp(_yaw, ikSolver.yawMin, ikSolver.yawMax);

        _target = ikSolver.ClampToCanvas(_target);
    }
}