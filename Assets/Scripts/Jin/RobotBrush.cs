using UnityEngine;

/// <summary>
/// BrushTip 오브젝트에 부착하는 스크립트.
/// 매 프레임 Raycast로 캔버스 감지 → CanvasPainter.Paint() 호출.
/// isTouching 플래그를 공개해서 ReacherRobot(ML-Agents)이 보상 계산에 활용.
/// </summary>
public class RobotBrush : MonoBehaviour
{
    [Header("연결 설정")]
    [Tooltip("캔버스에 있는 CanvasPainter 스크립트를 연결하세요")]
    public CanvasPainter canvasPainter;

    [Tooltip("붓 팀원이 만든 브러시 셰이더(Material)를 연결하세요")]
    public Material brushMaterial;

    [Header("붓 설정")]
    [Tooltip("붓끝에서 Raycast를 쏠 거리 (붓모의 길이)")]
    public float brushLength = 0.5f;

    // ─────────────────────────────────────────────────────────────
    // 외부(ReacherRobot)에서 읽는 상태 플래그
    // true  : 이번 프레임에 붓이 캔버스에 닿아 있음 → 보상 지급
    // false : 닿지 않음
    // ─────────────────────────────────────────────────────────────
    public bool IsTouching { get; private set; }

    // 마지막으로 페인팅한 UV 좌표 (디버그 및 보상 계산 용도)
    public Vector2 LastPaintedUV { get; private set; }

    void Update()
    {
        IsTouching = false;

        if (canvasPainter == null)
        {
            Debug.LogWarning("RobotBrush: Canvas Painter가 연결되지 않았습니다!");
            return;
        }

        // 캔버스 중심 방향으로 자동 계산 — 회전값과 무관하게 항상 올바른 방향
        Vector3 rayDir = transform.up;

        if (Physics.Raycast(transform.position, rayDir, out RaycastHit hit, brushLength))
        {
            CanvasPainter hitCanvas = hit.collider.GetComponent<CanvasPainter>();

            if (hitCanvas != null)  // brushMaterial 조건 임시 제거
            {
                IsTouching = true;
                Debug.Log("캔버스 감지! UV: " + hit.textureCoord);

                if (brushMaterial != null)  // Material 있을 때만 페인팅
                    hitCanvas.Paint(hit.textureCoord, brushMaterial);
            }
        }

        // 시각화
        Debug.DrawRay(transform.position, rayDir * brushLength,
            IsTouching ? Color.green : Color.red);
    }
}