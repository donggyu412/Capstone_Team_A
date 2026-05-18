using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BrushTip 오브젝트에 부착하는 스크립트.
///
/// ─── 브러시 종류 ────────────────────────────────────────────────
///  Material    (index 0) : 필압 ✓  농도 ✓  → SimpleBrushShader
///  Pencil      (index 1) : 필압 ✓  농도 ✗  → PencilBrushShader
///  Spray       (index 2) : 필압 ✗  농도 ✗  → SimpleBrushShader (UV 분산)
///  Blur        (index 3) : 필압 ✗  농도 ✗  → BlurBrushShader
///  Evaporating (index 4) : 필압 ✓  농도 ✗  → EvaporatingBrush (시간 페이드)
///
/// ─── 농도(InkAccumulation) 적용 브러시 ────────────────────────
///  Material 만 CanvasPainter.AccumulateInkOnly() 호출 → InkFlow 효과 적용
///
/// ─── 외부(ReacherRobot / ML-Agents) 인터페이스 ─────────────────
///  CurrentBrushType = RobotBrushType.Pencil;   // 브러시 전환
///  IsTouching, CurrentPressure                  // 보상 계산용
/// </summary>
public class RobotBrush : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // 브러시 타입 열거형
    // ─────────────────────────────────────────────────────────────
    public enum RobotBrushType
    {
        Material    = 0,
        Pencil      = 1,
        Spray       = 2,
        Blur        = 3,
        Evaporating = 4
    }

    // ─────────────────────────────────────────────────────────────
    // Inspector 설정
    // ─────────────────────────────────────────────────────────────

    [Header("연결 설정")]
    [Tooltip("캔버스의 CanvasPainter 스크립트를 연결하세요")]
    public CanvasPainter canvasPainter;

    [Header("브러시 배열 (Inspector 순서 고정)")]
    [Tooltip(
        "Element 0 : BrushMaterial     (SimpleBrushShader)   — 필압 ✓ 농도 ✓\n" +
        "Element 1 : BrushPencil       (PencilBrushShader)   — 필압 ✓ 농도 ✗\n" +
        "Element 2 : BrushSpray        (SimpleBrushShader)   — 필압 ✗ 농도 ✗\n" +
        "Element 3 : BrushBlur         (BlurBrushShader)     — 필압 ✗ 농도 ✗\n" +
        "Element 4 : BrushEvaporating  (EvaporatingBrush)    — 필압 ✓ 농도 ✗")]
    public Material[] brushes = new Material[5];

    [Header("현재 브러시 (외부에서 설정 가능)")]
    [Tooltip("ML-Agents 또는 외부 스크립트에서 직접 변경 가능")]
    public RobotBrushType currentBrushType = RobotBrushType.Material;

    [Header("붓 설정")]
    [Tooltip("Raycast를 쏠 거리 (붓모 길이)")]
    public float brushLength = 0.5f;

    [Header("필압 설정")]
    [Range(0.1f, 3.0f)]
    [Tooltip("1.0 = 선형. 높을수록 같은 거리에서 더 강한 필압")]
    public float pressureSensitivity = 1.0f;

    [Range(0f, 0.5f)]
    [Tooltip("이 값 미만의 필압은 '닿지 않음'으로 처리")]
    public float minPressureThreshold = 0.05f;

    [Header("스프레이 설정")]
    [Tooltip("스프레이 최대 유효 거리 (이 거리 안에서는 캔버스를 안 닿아도 분사됨)")]
    [Range(0.1f, 3.0f)]
    public float sprayMaxDistance = 1.5f;

    [Tooltip("캔버스에 바짝 붙었을 때 스프레이 분산 반경 (UV 기준)")]
    [Range(0.001f, 0.05f)]
    public float sprayMinRadius = 0.01f;

    [Tooltip("최대 거리에서 스프레이 분산 반경 (UV 기준) — 멀수록 더 퍼짐")]
    [Range(0.01f, 0.15f)]
    public float sprayMaxRadius = 0.08f;

    [Tooltip("스프레이 브러시일 때 한 번에 찍히는 점 수")]
    [Range(1, 40)]
    public int sprayParticleCount = 15;

    [Header("기화펜 설정")]
    [Tooltip("기화펜 획이 완전히 사라지기까지 걸리는 프레임 수")]
    public float evaporateDuration = 300f;

    [Tooltip("기화펜이 지워질 때 돌아올 캔버스 배경색")]
    public Color canvasBackgroundColor = new Color(1f, 0.96f, 0.86f); // 아이보리

    // ─────────────────────────────────────────────────────────────
    // 외부(ReacherRobot)에서 읽는 상태 프로퍼티
    // ─────────────────────────────────────────────────────────────

    /// <summary>이번 프레임에 붓이 캔버스에 닿아 있음</summary>
    public bool IsTouching { get; private set; }

    /// <summary>마지막 페인팅 UV (보상 계산용)</summary>
    public Vector2 LastPaintedUV { get; private set; }

    /// <summary>현재 필압 0~1 (닿지 않으면 0)</summary>
    public float CurrentPressure { get; private set; }

    // ─────────────────────────────────────────────────────────────
    // 내부 데이터
    // ─────────────────────────────────────────────────────────────

    // 기화펜 활성 포인트 목록
    private class EvaporatingPoint
    {
        public Vector2 uv;
        public float   life, maxLife;
        public Color   baseColor;
        public float   brushSize;   // 압력에 따른 크기 기록
        public Material matInstance; // 인스턴스별 머티리얼 (색상 변경용)
    }

    private List<EvaporatingPoint> activeEvaporatingPoints = new List<EvaporatingPoint>();

    // ─────────────────────────────────────────────────────────────
    // Unity 라이프사이클
    // ─────────────────────────────────────────────────────────────

    void Update()
    {
        IsTouching      = false;
        CurrentPressure = 0f;

        if (canvasPainter == null)
        {
            Debug.LogWarning("RobotBrush: CanvasPainter가 연결되지 않았습니다!");
            return;
        }

        // 매 프레임: 기화펜 페이드 처리
        UpdateEvaporatingPoints();

        Vector3 rayDir = transform.up;

        // ── 스프레이는 더 긴 거리로 별도 처리 ─────────────────────
        // 캔버스에 닿지 않아도 일정 거리 안에서 분사 가능
        if (currentBrushType == RobotBrushType.Spray)
        {
            if (Physics.Raycast(transform.position, rayDir, out RaycastHit sprayHit, sprayMaxDistance))
            {
                CanvasPainter hitCanvas = sprayHit.collider.GetComponent<CanvasPainter>();
                if (hitCanvas != null)
                {
                    IsTouching    = true;
                    LastPaintedUV = sprayHit.textureCoord;
                    PaintSpray(hitCanvas, sprayHit.textureCoord, sprayHit.distance);
                    Debug.DrawRay(transform.position, rayDir * sprayHit.distance, Color.cyan);
                    Debug.Log($"[RobotBrush] Spray | 거리:{sprayHit.distance:F2} | UV:{sprayHit.textureCoord:F2}");
                }
                else
                    Debug.DrawRay(transform.position, rayDir * sprayMaxDistance, Color.red);
            }
            else
                Debug.DrawRay(transform.position, rayDir * sprayMaxDistance, Color.red);
            return;
        }

        // ── 나머지 브러시: 기존 brushLength 기반 Raycast ──────────
        if (!Physics.Raycast(transform.position, rayDir, out RaycastHit hit, brushLength))
        {
            Debug.DrawRay(transform.position, rayDir * brushLength, Color.red);
            return;
        }

        CanvasPainter canvas = hit.collider.GetComponent<CanvasPainter>();
        if (canvas == null)
        {
            Debug.DrawRay(transform.position, rayDir * brushLength, Color.red);
            return;
        }

        // 필압 계산
        float rawPressure = 1f - Mathf.Clamp01(hit.distance / brushLength);
        float pressure    = Mathf.Clamp01(rawPressure * pressureSensitivity);

        if (pressure < minPressureThreshold)
        {
            Debug.DrawRay(transform.position, rayDir * brushLength, Color.yellow);
            return;
        }

        IsTouching      = true;
        LastPaintedUV   = hit.textureCoord;
        CurrentPressure = pressure;

        switch (currentBrushType)
        {
            case RobotBrushType.Material:
                PaintMaterial(canvas, hit.textureCoord, pressure);
                break;
            case RobotBrushType.Pencil:
                PaintPencil(canvas, hit.textureCoord, pressure);
                break;
            case RobotBrushType.Blur:
                PaintBlur(canvas, hit.textureCoord);
                break;
            case RobotBrushType.Evaporating:
                PaintEvaporating(canvas, hit.textureCoord, pressure);
                break;
        }

        Debug.DrawRay(transform.position, rayDir * brushLength, Color.green);
        Debug.Log($"[RobotBrush] {currentBrushType} | UV:{hit.textureCoord:F2} | 필압:{pressure:F2}");
    }

    // ─────────────────────────────────────────────────────────────
    // 브러시별 페인팅 메서드
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// BrushMaterial — 필압 ✓  농도 ✓
    /// 필압에 따라 굵기·농도 변화 + InkMap에 잉크 누적
    /// </summary>
    private void PaintMaterial(CanvasPainter canvas, Vector2 uv, float pressure)
    {
        Material mat = GetBrush(RobotBrushType.Material);
        if (mat == null) return;

        mat.SetVector("_BrushUV",  new Vector4(uv.x, uv.y, 0, 0));
        mat.SetFloat("_Pressure",  pressure);

        // 캔버스에 획 그리기
        canvas.Paint(uv, mat, pressure);

        // 농도: InkMap에도 잉크 누적 (흘러내림 효과)
        canvas.AccumulateInkOnly(uv, pressure);
    }

    /// <summary>
    /// BrushPencil — 필압 ✓  농도 ✗
    /// 연필 텍스처 기반, 필압에 따라 브러시 크기 변화. 잉크 누적 없음.
    /// </summary>
    private void PaintPencil(CanvasPainter canvas, Vector2 uv, float pressure)
    {
        Material mat = GetBrush(RobotBrushType.Pencil);
        if (mat == null) return;

        mat.SetVector("_BrushUV", new Vector4(uv.x, uv.y, 0, 0));
        mat.SetFloat("_Pressure", pressure);

        // 농도 없음 → AccumulateInkOnly 호출하지 않음
        canvas.Paint(uv, mat, pressure);
    }

    /// <summary>
    /// BrushSpray — 필압 ✗  농도 ✗
    /// 거리 기반 스프레이: 캔버스에 닿지 않아도 sprayMaxDistance 안에서 분사.
    /// 거리가 가까울수록 집중적으로, 멀수록 넓게 퍼짐.
    /// PaintOnly() 사용 → InkMap 누적 없음 (번짐 방지)
    /// </summary>
    private void PaintSpray(CanvasPainter canvas, Vector2 centerUV, float distance)
    {
        Material mat = GetBrush(RobotBrushType.Spray);
        if (mat == null) return;

        // 거리에 비례해 분산 반경 계산
        // distance=0      → sprayMinRadius (집중)
        // distance=max    → sprayMaxRadius (넓게)
        float t              = Mathf.Clamp01(distance / sprayMaxDistance);
        float effectiveRadius = Mathf.Lerp(sprayMinRadius, sprayMaxRadius, t);

        mat.SetFloat("_Pressure", 1.0f);

        for (int i = 0; i < sprayParticleCount; i++)
        {
            Vector2 offset   = Random.insideUnitCircle * effectiveRadius;
            Vector2 sprayUV  = centerUV + offset;

            mat.SetVector("_BrushUV", new Vector4(sprayUV.x, sprayUV.y, 0, 0));

            // PaintOnly: 캔버스에만 그리고 InkMap 누적 없음
            canvas.PaintOnly(sprayUV, mat, 1.0f);
        }
    }

    /// <summary>
    /// BrushBlur — 필압 ✗  농도 ✗
    /// 주변 픽셀을 평균내서 흐리게 만드는 효과. PaintOnly 사용.
    /// </summary>
    private void PaintBlur(CanvasPainter canvas, Vector2 uv)
    {
        Material mat = GetBrush(RobotBrushType.Blur);
        if (mat == null) return;

        mat.SetVector("_BrushUV", new Vector4(uv.x, uv.y, 0, 0));
        canvas.PaintOnly(uv, mat, 1.0f);
    }

    /// <summary>
    /// BrushEvaporating — 필압 ✓  농도 ✗
    /// 그린 획이 시간이 지남에 따라 서서히 사라짐.
    /// 필압에 따라 초기 크기 결정. 잉크 누적 없음.
    /// </summary>
    private void PaintEvaporating(CanvasPainter canvas, Vector2 uv, float pressure)
    {
        Material srcMat = GetBrush(RobotBrushType.Evaporating);
        if (srcMat == null) return;

        // 인스턴스별 머티리얼 생성 (색상·크기 개별 제어)
        Material inst = new Material(srcMat);

        // 필압에 따른 크기 반영: 원래 BrushSize × lerp(0.3, 1.0, pressure)
        float baseBrushSize = srcMat.HasProperty("_BrushSize")
            ? srcMat.GetFloat("_BrushSize") : 0.02f;
        float pressuredSize = baseBrushSize * Mathf.Lerp(0.3f, 1.0f, pressure);

        Color baseColor = srcMat.HasProperty("_BrushColor")
            ? srcMat.GetColor("_BrushColor")
            : Color.black;

        inst.SetVector("_BrushUV",  new Vector4(uv.x, uv.y, 0, 0));
        inst.SetFloat("_BrushSize", pressuredSize);
        inst.SetColor("_BrushColor", baseColor);

        // 즉시 한 번 그리기
        canvas.Paint(uv, inst, pressure);

        // 페이드 목록에 추가
        activeEvaporatingPoints.Add(new EvaporatingPoint
        {
            uv          = uv,
            life        = evaporateDuration,
            maxLife     = evaporateDuration,
            baseColor   = baseColor,
            brushSize   = pressuredSize,
            matInstance = inst
        });
    }

    // ─────────────────────────────────────────────────────────────
    // 기화펜 페이드 업데이트 (매 프레임)
    // ─────────────────────────────────────────────────────────────

    private void UpdateEvaporatingPoints()
    {
        if (canvasPainter == null) return;

        for (int i = activeEvaporatingPoints.Count - 1; i >= 0; i--)
        {
            EvaporatingPoint p = activeEvaporatingPoints[i];
            p.life -= 1f;

            float t         = p.life / p.maxLife;                  // 1 → 0
            Color fadeColor = Color.Lerp(canvasBackgroundColor, p.baseColor, t);

            if (p.matInstance != null)
            {
                p.matInstance.SetColor("_BrushColor", fadeColor);
                p.matInstance.SetVector("_BrushUV", new Vector4(p.uv.x, p.uv.y, 0, 0));
                canvasPainter.Paint(p.uv, p.matInstance, 1.0f);
            }

            if (p.life <= 0)
            {
                Destroy(p.matInstance);
                activeEvaporatingPoints.RemoveAt(i);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 유틸리티
    // ─────────────────────────────────────────────────────────────

    private Material GetBrush(RobotBrushType type)
    {
        int index = (int)type;
        if (brushes == null || index >= brushes.Length || brushes[index] == null)
        {
            Debug.LogWarning($"[RobotBrush] brushes[{index}] ({type}) 가 연결되지 않았습니다.");
            return null;
        }
        return brushes[index];
    }

    // ─────────────────────────────────────────────────────────────
    // ML-Agents / 외부 스크립트용 공개 메서드
    // ─────────────────────────────────────────────────────────────

    /// <summary>외부에서 브러시 타입을 정수 인덱스로 변경합니다.</summary>
    public void SetBrushByIndex(int index)
    {
        if (index >= 0 && index <= 4)
            currentBrushType = (RobotBrushType)index;
    }

    void OnDestroy()
    {
        // 남은 기화펜 머티리얼 인스턴스 정리
        foreach (var p in activeEvaporatingPoints)
            if (p.matInstance != null) Destroy(p.matInstance);
        activeEvaporatingPoints.Clear();
    }
}
