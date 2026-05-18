using System.Collections.Generic;
using UnityEngine;

// ── 펜타블렛 필압을 사용하려면 New Input System 패키지가 필요합니다 ──
// Package Manager → Input System 설치 후 Project Settings → Active Input Handling
// → "Both" 또는 "Input System Package" 로 변경
// 없으면 필압 없이 마우스 모드로 자동 폴백됩니다
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MousePainter : MonoBehaviour
{
    #region 1. 변수 및 설정

    [Header("페인팅 기본 설정")]
    public CanvasPainter canvasPainter;
    public Material[] brushes;
    private int currentBrushIndex = 0;
    private Vector2 lastUV;

    [Header("브러시 인덱스 설정 (Inspector 기준)")]
    public int inkBrushIndex = 1;
    public int sprayBrushIndex = 3;
    public int blurBrushIndex = 4;
    public int evaporatingBrushIndex = 5;

    [Header("스프레이 브러시 설정")]
    public int sprayParticleCount = 15;
    public float sprayRadius = 0.05f;

    [Header("흐름(Drip) 효과 설정")]
    public Material dripTrailMaterial;
    private bool isDripEnabled = true;

    [Header("기화펜 효과 설정")]
    public Material brushEvaporatingMaterial;
    public float evaporateDuration = 500f;
    public Color canvasBackgroundColor = Color.white;

    // ── 잉크 시뮬레이션 (InkAdd/InkFlow/InkDrip 테스트용) ────────────
    [Header("잉크 시뮬레이션 테스트 설정")]
    [Tooltip("체크하면 펜을 제자리에 누르고 있을 때도 잉크가 누적됩니다 (요구사항 2 테스트)")]
    public bool enableStationaryInkAccumulation = true;

    [Tooltip("필압 표시 GUI (테스트용, 배포 시 끄세요)")]
    public bool showPressureDebugGUI = true;

    [Header("필압 리매핑")]
    [Tooltip("타블렛에서 나오는 실제 최대 압력값.\n" +
             "콘솔의 [펜타블렛] 로그에서 최대치 확인 후 설정.\n" +
             "예) 최대 0.2가 찍히면 0.2로 설정 → 0.2를 1.0으로 정규화")]
    [Range(0.05f, 1.0f)]
    public float maxExpectedPressure = 0.2f;

    // 현재 필압 (0~1) — GUI 표시 및 외부 읽기용
    [HideInInspector] public float currentPressure = 0f;
    private bool usesPressure = false;

    // 이전 프레임에 그리고 있었는지 (제자리 누름 감지용)
    private bool wasPaintingLastFrame = false;
    private Vector2 stationaryUV;

    private List<EvaporatingPoint> activeEvaporatingPoints = new List<EvaporatingPoint>();
    private List<Drip> activeDrips = new List<Drip>();

    #endregion

    #region 2. 내부 데이터 클래스

    private class EvaporatingPoint
    {
        public Vector2 uv;
        public float life, maxLife;
        public Color color;
        public Material materialInstance;

        public EvaporatingPoint(Vector2 uv, Color color, float duration, Material mat)
        {
            this.uv = uv; this.color = color;
            this.maxLife = duration; this.life = duration;
            this.materialInstance = mat;
        }
    }

    private class Drip
    {
        public Vector2 uv;
        public float life, speed;
        public Color color;
    }

    #endregion

    #region 3. Unity 라이프사이클

    void Update()
    {
        HandleBrushSelection();
        HandleDripToggle();

        if (isDripEnabled) UpdateDrips();
        UpdateEvaporation();

        if (canvasPainter == null) return;
        HandleMouseDrawing();
    }

    // 테스트용 필압 게이지 GUI
    void OnGUI()
    {
        if (!showPressureDebugGUI) return;

        // 배경
        GUI.Box(new Rect(10, 10, 220, 70), "");

        // 필압 텍스트
        GUI.Label(new Rect(15, 15, 200, 20),
            $"필압(Pressure): {currentPressure:F3}");

        // 필압 바
        GUI.Box(new Rect(15, 38, 200, 18), "");
        Color prev = GUI.color;
        GUI.color = Color.Lerp(Color.green, Color.red, currentPressure);
        GUI.Box(new Rect(15, 38, 200 * currentPressure, 18), "");
        GUI.color = prev;

        // 현재 브러시 표시
        string brushName = usesPressure 
            ? $"브러시 {currentBrushIndex} (필압 활성)" 
            : "기타 (필압 비활성)";
        GUI.Label(new Rect(15, 58, 200, 20), $"브러시: {brushName}");
    }

    #endregion

    #region 4. 필압 읽기

    void Start()
    {
    #if ENABLE_INPUT_SYSTEM
        // 연결된 모든 입력 장치 출력
        foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
            Debug.Log($"[장치] {device.name} / 타입: {device.GetType().Name}");
    #endif
    }

    /// <summary>
    /// 펜타블렛 필압을 읽습니다.
    /// New Input System이 있으면 실제 필압(0~1),
    /// 없거나 마우스 사용 시 1.0f 반환.
    /// </summary>
    private float GetPenPressure()
    {
    #if ENABLE_INPUT_SYSTEM
        // 1순위: Pen 장치
        if (Pen.current != null)
        {
            float raw = Pen.current.pressure.ReadValue();
            if (raw > 0.01f)
            {
                // 타블렛 실제 최대 압력(maxExpectedPressure) 기준으로 0~1 정규화
                // 예) raw=0.15, maxExpectedPressure=0.2 → remapped=0.75
                float remapped = Mathf.Clamp01(raw / maxExpectedPressure);
                Debug.Log($"[펜타블렛] 원본:{raw:F3} → 리매핑:{remapped:F3}");
                return remapped;
            }
        }

        // 2순위: 마우스 클릭 → 1.0 고정
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            Debug.Log("[마우스] 필압: 1.000");
            return 1.0f;
        }

        return 0f;
    #else
        return Input.GetMouseButton(0) ? 1.0f : 0f;
    #endif
    }

    #endregion

    #region 5. 입력 처리

    private void HandleBrushSelection()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) currentBrushIndex = 0;
        if (Input.GetKeyDown(KeyCode.Alpha2)) currentBrushIndex = inkBrushIndex;
        if (Input.GetKeyDown(KeyCode.Alpha3)) currentBrushIndex = 2;
        if (Input.GetKeyDown(KeyCode.Alpha4)) currentBrushIndex = sprayBrushIndex;
        if (Input.GetKeyDown(KeyCode.Alpha5)) currentBrushIndex = blurBrushIndex;
        if (Input.GetKeyDown(KeyCode.Alpha6)) currentBrushIndex = evaporatingBrushIndex;
    }

    private void HandleDripToggle()
    {
        if (Input.GetKeyDown(KeyCode.O)) isDripEnabled = false;
        if (Input.GetKeyDown(KeyCode.P)) isDripEnabled = true;
    }

    private void HandleMouseDrawing()
    {
        // ── 매 프레임 필압 업데이트 ──────────────────────────────────
        // 잉크 브러시일 때만 실제 필압 적용, 나머지는 항상 1.0f
        usesPressure = (currentBrushIndex == 0 || currentBrushIndex == inkBrushIndex);
        currentPressure = usesPressure ? GetPenPressure() : 1.0f;

        if (usesPressure && Input.GetMouseButton(0))
            Debug.Log($"[필압] {currentPressure:F3}  브러시 인덱스: {currentBrushIndex}");  

        // ── 클릭 시작 ────────────────────────────────────────────────
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                lastUV = hit.textureCoord;
                stationaryUV = lastUV;

                // 필압 전달: 잉크 브러시만 실제 필압, 나머지는 1.0f
                canvasPainter.Paint(lastUV, brushes[currentBrushIndex], currentPressure);

                if (currentBrushIndex == evaporatingBrushIndex)
                    AddEvaporatingPoint(lastUV);

                wasPaintingLastFrame = true;
            }
        }
        // ── 드래그 중 ────────────────────────────────────────────────
        else if (Input.GetMouseButton(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Vector2 currentUV = hit.textureCoord;
                bool moved = Vector2.Distance(lastUV, currentUV) > 0.0005f;

                if (currentBrushIndex == sprayBrushIndex)
                {
                    PaintWithSpray(currentUV);
                }
                else if (currentBrushIndex == inkBrushIndex)
                {
                    if (moved)
                    {
                        // 이동하면서 선 그리기 (필압 반영)
                        PaintLine(lastUV, currentUV, currentPressure);

                        if (isDripEnabled && Random.value < 0.05f)
                            CreateDrip(currentUV);
                    }
                    else if (enableStationaryInkAccumulation)
                    {
                        // ── 제자리에 누르고 있을 때 잉크 누적 (요구사항 2) ──
                        // AccumulateInkOnly: 캔버스에 획은 안 그리고
                        // inkMap에만 잉크를 계속 쌓아 나중에 흘러내리게 함
                        canvasPainter.AccumulateInkOnly(currentUV, currentPressure);
                    }
                }
                else if (currentBrushIndex == evaporatingBrushIndex)
                {
                    PaintLine(lastUV, currentUV, 1.0f);
                    if (Random.value < 0.2f) AddEvaporatingPoint(currentUV);
                }
                else
                {
                    if (moved)
                        PaintLine(lastUV, currentUV, currentPressure);
                    else if (enableStationaryInkAccumulation)
                        canvasPainter.AccumulateInkOnly(currentUV, currentPressure);
                }

                lastUV = currentUV;
                stationaryUV = currentUV;
                wasPaintingLastFrame = true;
            }
        }
        // ── 버튼을 뗐을 때 ───────────────────────────────────────────
        else
        {
            currentPressure = 0f;
            wasPaintingLastFrame = false;
        }
    }

    #endregion

    #region 6. 기본 드로잉

    /// <summary>두 UV 사이를 필압을 유지하며 선형 보간으로 채웁니다.</summary>
    private void PaintLine(Vector2 start, Vector2 end, float pressure)
    {
        float distance = Vector2.Distance(start, end);
        float minDistance = 0.002f;
        if (distance > minDistance)
        {
            int steps = Mathf.CeilToInt(distance / minDistance);
            for (int i = 1; i <= steps; i++)
            {
                Vector2 lerpUV = Vector2.Lerp(start, end, (float)i / steps);
                canvasPainter.Paint(lerpUV, brushes[currentBrushIndex], pressure);
            }
        }
    }

    private Color GetCurrentBrushColor()
    {
        Material mat = brushes[currentBrushIndex];
        if (mat.HasProperty("_BrushColor")) return mat.GetColor("_BrushColor");
        return Color.white;
    }

    #endregion

    #region 7. 스프레이 효과

    private void PaintWithSpray(Vector2 centerUV)
    {
        for (int i = 0; i < sprayParticleCount; i++)
        {
            Vector2 randomOffset = Random.insideUnitCircle * sprayRadius;
            canvasPainter.Paint(centerUV + randomOffset, brushes[sprayBrushIndex], 1.0f);
        }
    }

    #endregion

    #region 8. 기화펜 효과

    private void AddEvaporatingPoint(Vector2 uv)
    {
        if (brushEvaporatingMaterial == null) return;
        Color c = GetCurrentBrushColor();
        Material tempMat = new Material(brushEvaporatingMaterial);
        tempMat.SetVector("_BrushUV", new Vector4(uv.x, uv.y, 0, 0));
        tempMat.SetColor("_BrushColor", c);
        activeEvaporatingPoints.Add(new EvaporatingPoint(uv, c, evaporateDuration, tempMat));
    }

    private void UpdateEvaporation()
    {
        if (canvasPainter == null) return;
        for (int i = activeEvaporatingPoints.Count - 1; i >= 0; i--)
        {
            EvaporatingPoint p = activeEvaporatingPoints[i];
            p.life--;
            float alpha = (float)p.life / (float)p.maxLife;
            if (p.materialInstance != null)
            {
                Color fadeColor = Color.Lerp(canvasBackgroundColor, p.color, alpha);
                p.materialInstance.SetColor("_BrushColor", fadeColor);
                canvasPainter.Paint(p.uv, p.materialInstance, 1.0f);
            }
            if (p.life <= 0)
            {
                if (p.materialInstance != null) Destroy(p.materialInstance);
                activeEvaporatingPoints.RemoveAt(i);
            }
        }
    }

    #endregion

    #region 9. 흐름 효과 (기존 드립 — 새 InkFlow 셰이더와 병행)

    private void CreateDrip(Vector2 startUV)
    {
        activeDrips.Add(new Drip
        {
            uv    = startUV,
            life  = Random.Range(50, 5000),
            speed = Random.Range(0.00008f, 0.0000001f),
            color = GetCurrentBrushColor()
        });
    }

    private void UpdateDrips()
    {
        if (canvasPainter == null) return;
        for (int i = activeDrips.Count - 1; i >= 0; i--)
        {
            Drip drip = activeDrips[i];
            drip.uv.x -= drip.speed;
            if (dripTrailMaterial != null)
            {
                if (dripTrailMaterial.HasProperty("_BrushColor"))
                    dripTrailMaterial.SetColor("_BrushColor", drip.color);
                canvasPainter.Paint(drip.uv, dripTrailMaterial, 1.0f);
            }
            drip.life--;
            if (drip.life <= 0) activeDrips.RemoveAt(i);
        }
    }

    #endregion
}
