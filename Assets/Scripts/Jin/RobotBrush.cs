using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BrushTip 오브젝트에 부착하는 스크립트.
///
/// ─── 브러시 종류 ──────────────────────────────────────────────
///  Material    (0) : 필압 ✓  농도 ✓  → BristleBrushShader (붓털 효과)
///  Pencil      (1) : 필압 ✓  농도 ✗  → PencilBrushShader
///  Spray       (2) : 필압 ✗  농도 ✗  → SimpleBrushShader  (ON/OFF 가능)
///  Blur        (3) : 필압 ✗  농도 ✗  → BlurBrushShader
///  Evaporating (4) : 필압 ✓  농도 ✗  → EvaporatingBrush
///  Eraser      (5) : 필압 ✓  농도 ✗  → EraserShader (배경색 덧칠 + inkMap 초기화)
///
/// ─── 새 기능 ─────────────────────────────────────────────────
///  1. BrushMaterial → BristleBrushShader 사용 (붓털 시뮬레이션)
///  2. Eraser 브러시 타입 추가 (필압 ✓, 잉크 흐름 ✗)
///  3. 스프레이 ON/OFF: isSprayActive 토글 가능
/// </summary>
public class RobotBrush : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // 브러시 타입
    // ─────────────────────────────────────────────────────────────
    public enum RobotBrushType
    {
        Material    = 0,
        Pencil      = 1,
        Spray       = 2,
        Blur        = 3,
        Evaporating = 4,
        Eraser      = 5   // ★ 신규
    }

    // ─────────────────────────────────────────────────────────────
    // Inspector 설정
    // ─────────────────────────────────────────────────────────────

    [Header("연결 설정")]
    public CanvasPainter canvasPainter;

    [Header("브러시 배열 (Inspector 순서 고정)")]
    [Tooltip(
        "Element 0 : BrushMaterial     (BristleBrushShader) — 필압 ✓ 농도 ✓\n" +
        "Element 1 : BrushPencil       (PencilBrushShader)  — 필압 ✓ 농도 ✗\n" +
        "Element 2 : BrushSpray        (SimpleBrushShader)  — 필압 ✗ 농도 ✗\n" +
        "Element 3 : BrushBlur         (BlurBrushShader)    — 필압 ✗ 농도 ✗\n" +
        "Element 4 : BrushEvaporating  (EvaporatingBrush)   — 필압 ✓ 농도 ✗\n" +
        "Element 5 : BrushEraser       (EraserShader)       — 필압 ✓ 농도 ✗")]
    public Material[] brushes = new Material[6];

    [Header("현재 브러시")]
    [Tooltip("ML-Agents 또는 외부 스크립트에서 직접 변경 가능")]
    public RobotBrushType currentBrushType = RobotBrushType.Material;

    [Header("붓 설정")]
    public float brushLength = 0.5f;

    [Header("필압 설정")]
    [Range(0.1f, 3.0f)] public float pressureSensitivity  = 1.0f;
    [Range(0f,   0.5f)] public float minPressureThreshold = 0.05f;

    [Header("스프레이 설정")]
    [Tooltip("스프레이 활성화 여부 (false = 분사 안 함)")]
    public bool isSprayActive = true;

    [Range(0.1f, 3.0f)] public float sprayMaxDistance  = 1.5f;
    [Range(0.001f, 0.05f)] public float sprayMinRadius = 0.01f;
    [Range(0.01f, 0.15f)] public float sprayMaxRadius  = 0.08f;
    [Range(1, 40)] public int sprayParticleCount        = 15;

    [Header("기화펜 설정")]
    public float evaporateDuration   = 300f;
    public Color canvasBackgroundColor = new Color(1f, 0.96f, 0.86f);

    [Header("지우개 설정")]
    [Tooltip("지우개 효과 후 inkMap도 함께 지울지 여부.\n" +
             "true = inkMap까지 지워 잉크 흘러내림 완전 제거.\n" +
             "false = 시각적 지우기만 수행.")]
    public bool eraseInkMap = true;

    // ─────────────────────────────────────────────────────────────
    // 외부 읽기용 프로퍼티
    // ─────────────────────────────────────────────────────────────
    public bool  IsTouching      { get; private set; }
    public Vector2 LastPaintedUV { get; private set; }
    public float CurrentPressure { get; private set; }

    // ─────────────────────────────────────────────────────────────
    // 내부 데이터
    // ─────────────────────────────────────────────────────────────
    private class EvaporatingPoint
    {
        public Vector2  uv;
        public float    life, maxLife;
        public Color    baseColor;
        public float    brushSize;
        public Material matInstance;
    }
    private List<EvaporatingPoint> activeEvaporatingPoints = new List<EvaporatingPoint>();

    // ─────────────────────────────────────────────────────────────
    // Update
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

        UpdateEvaporatingPoints();

        Vector3 rayDir = transform.up;

        // ── 스프레이: 별도 거리로 처리 + ON/OFF ─────────────────
        if (currentBrushType == RobotBrushType.Spray)
        {
            if (!isSprayActive)
            {
                // 비활성화: 회색 Ray로 시각화만
                Debug.DrawRay(transform.position, rayDir * sprayMaxDistance, Color.gray);
                return;
            }

            if (Physics.Raycast(transform.position, rayDir, out RaycastHit sprayHit, sprayMaxDistance))
            {
                CanvasPainter hitCanvas = sprayHit.collider.GetComponent<CanvasPainter>();
                if (hitCanvas != null)
                {
                    IsTouching    = true;
                    LastPaintedUV = sprayHit.textureCoord;
                    PaintSpray(hitCanvas, sprayHit.textureCoord, sprayHit.distance);
                    Debug.DrawRay(transform.position, rayDir * sprayHit.distance, Color.cyan);
                }
                else
                    Debug.DrawRay(transform.position, rayDir * sprayMaxDistance, Color.red);
            }
            else
                Debug.DrawRay(transform.position, rayDir * sprayMaxDistance, Color.red);
            return;
        }

        // ── 나머지 브러시: brushLength 기반 Raycast ──────────────
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
            case RobotBrushType.Material:    PaintMaterial(canvas, hit.textureCoord, pressure);    break;
            case RobotBrushType.Pencil:      PaintPencil  (canvas, hit.textureCoord, pressure);    break;
            case RobotBrushType.Blur:        PaintBlur    (canvas, hit.textureCoord);              break;
            case RobotBrushType.Evaporating: PaintEvaporating(canvas, hit.textureCoord, pressure); break;
            case RobotBrushType.Eraser:      PaintEraser  (canvas, hit.textureCoord, pressure);    break;
        }

        Debug.DrawRay(transform.position, rayDir * brushLength, Color.green);
    }

    // ─────────────────────────────────────────────────────────────
    // 브러시별 페인팅
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// BrushMaterial — 필압 ✓  농도 ✓
    /// BristleBrushShader 사용: 붓털이 캔버스에 제각각 닿는 효과.
    /// 필압 → 접촉 반경 → 닿는 붓털 수 결정.
    /// </summary>
    private void PaintMaterial(CanvasPainter canvas, Vector2 uv, float pressure)
    {
        Material mat = GetBrush(RobotBrushType.Material);
        if (mat == null) return;

        mat.SetVector("_BrushUV", new Vector4(uv.x, uv.y, 0, 0));
        mat.SetFloat("_Pressure", pressure);

        canvas.Paint(uv, mat, pressure);

        // 농도: inkMap 누적 → 흘러내림 효과
        canvas.AccumulateInkOnly(uv, pressure);
    }

    /// <summary>BrushPencil — 필압 ✓  농도 ✗</summary>
    private void PaintPencil(CanvasPainter canvas, Vector2 uv, float pressure)
    {
        Material mat = GetBrush(RobotBrushType.Pencil);
        if (mat == null) return;

        mat.SetVector("_BrushUV", new Vector4(uv.x, uv.y, 0, 0));
        mat.SetFloat("_Pressure", pressure);
        canvas.Paint(uv, mat, pressure);
    }

    /// <summary>
    /// BrushSpray — 필압 ✗  농도 ✗  ON/OFF 가능
    /// 거리 기반: 멀수록 더 넓게 퍼짐.
    /// </summary>
    private void PaintSpray(CanvasPainter canvas, Vector2 centerUV, float distance)
    {
        Material mat = GetBrush(RobotBrushType.Spray);
        if (mat == null) return;

        float t               = Mathf.Clamp01(distance / sprayMaxDistance);
        float effectiveRadius = Mathf.Lerp(sprayMinRadius, sprayMaxRadius, t);

        mat.SetFloat("_Pressure", 1.0f);

        for (int i = 0; i < sprayParticleCount; i++)
        {
            Vector2 offset   = Random.insideUnitCircle * effectiveRadius;
            Vector2 sprayUV  = centerUV + offset;
            mat.SetVector("_BrushUV", new Vector4(sprayUV.x, sprayUV.y, 0, 0));
            canvas.PaintOnly(sprayUV, mat, 1.0f);
        }
    }

    /// <summary>BrushBlur — 필압 ✗  농도 ✗</summary>
    private void PaintBlur(CanvasPainter canvas, Vector2 uv)
    {
        Material mat = GetBrush(RobotBrushType.Blur);
        if (mat == null) return;

        mat.SetVector("_BrushUV", new Vector4(uv.x, uv.y, 0, 0));
        canvas.PaintOnly(uv, mat, 1.0f);
    }

    /// <summary>BrushEvaporating — 필압 ✓  농도 ✗  (시간 페이드)</summary>
    private void PaintEvaporating(CanvasPainter canvas, Vector2 uv, float pressure)
    {
        Material srcMat = GetBrush(RobotBrushType.Evaporating);
        if (srcMat == null) return;

        Material inst = new Material(srcMat);

        float baseBrushSize  = srcMat.HasProperty("_BrushSize") ? srcMat.GetFloat("_BrushSize") : 0.02f;
        float pressuredSize  = baseBrushSize * Mathf.Lerp(0.3f, 1.0f, pressure);
        Color baseColor      = srcMat.HasProperty("_BrushColor") ? srcMat.GetColor("_BrushColor") : Color.black;

        inst.SetVector("_BrushUV",   new Vector4(uv.x, uv.y, 0, 0));
        inst.SetFloat("_BrushSize",  pressuredSize);
        inst.SetColor("_BrushColor", baseColor);

        canvas.Paint(uv, inst, pressure);

        activeEvaporatingPoints.Add(new EvaporatingPoint
        {
            uv = uv, life = evaporateDuration, maxLife = evaporateDuration,
            baseColor = baseColor, brushSize = pressuredSize, matInstance = inst
        });
    }

    /// <summary>
    /// Eraser — 필압 ✓  농도 ✗
    /// EraserShader로 배경색(아이보리) 덧칠 + inkMap도 지움.
    /// 필압에 따라 지우개 크기 변화.
    /// </summary>
    private void PaintEraser(CanvasPainter canvas, Vector2 uv, float pressure)
    {
        Material mat = GetBrush(RobotBrushType.Eraser);
        if (mat == null) return;

        mat.SetVector("_BrushUV",  new Vector4(uv.x, uv.y, 0, 0));
        mat.SetFloat("_Pressure",  pressure);

        // 시각적 지우기: 배경색으로 덧칠 (inkMap 누적 없음)
        canvas.PaintOnly(uv, mat, pressure);

        // inkMap도 지우기 (선택적)
        if (eraseInkMap)
        {
            float eraseRadius = mat.HasProperty("_BrushSize")
                ? mat.GetFloat("_BrushSize") * Mathf.Lerp(0.3f, 1.0f, pressure)
                : 0.03f;
            canvas.EraseInkAtUV(uv, eraseRadius);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 기화펜 페이드
    // ─────────────────────────────────────────────────────────────
    private void UpdateEvaporatingPoints()
    {
        if (canvasPainter == null) return;
        for (int i = activeEvaporatingPoints.Count - 1; i >= 0; i--)
        {
            EvaporatingPoint p = activeEvaporatingPoints[i];
            p.life -= 1f;
            float t         = p.life / p.maxLife;
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

    /// <summary>브러시를 인덱스로 변경 (0~5).</summary>
    public void SetBrushByIndex(int index)
    {
        if (index >= 0 && index <= 5)
            currentBrushType = (RobotBrushType)index;
    }

    /// <summary>스프레이 활성화.</summary>
    public void ActivateSpray()   => isSprayActive = true;

    /// <summary>스프레이 비활성화.</summary>
    public void DeactivateSpray() => isSprayActive = false;

    /// <summary>스프레이 ON/OFF 토글.</summary>
    public void ToggleSpray()     => isSprayActive = !isSprayActive;

    void OnDestroy()
    {
        foreach (var p in activeEvaporatingPoints)
            if (p.matInstance != null) Destroy(p.matInstance);
        activeEvaporatingPoints.Clear();
    }
}
