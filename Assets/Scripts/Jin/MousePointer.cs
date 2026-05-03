using System.Collections.Generic;
using UnityEngine;

public class MousePainter : MonoBehaviour
{
    #region 1. 변수 및 설정 (Variables & Settings)
    
    [Header("페인팅 기본 설정")]
    public CanvasPainter canvasPainter;
    public Material[] brushes;
    private int currentBrushIndex = 0;
    private Vector2 lastUV;

    [Header("브러시 인덱스 설정 (Inspector 기준)")]
    public int inkBrushIndex = 1;         // 잉크 (Element 1)
    public int sprayBrushIndex = 3;       // 스프레이 (Element 3)
    public int blurBrushIndex = 4;        // 블러 (Element 4)
    public int evaporatingBrushIndex = 5; // 기화펜 (Element 5)

    [Header("스프레이 브러시 설정")]
    public int sprayParticleCount = 15;
    public float sprayRadius = 0.05f;

    [Header("흐름(Drip) 효과 설정")]
    public Material dripTrailMaterial; 
    private bool isDripEnabled = true;

    [Header("기화펜 효과 설정")]
    public Material brushEvaporatingMaterial;
    public float evaporateDuration = 500f; // 사라지는 시간
    public Color canvasBackgroundColor = Color.white;

    // 활성화된 이펙트 데이터를 담을 리스트
    private List<EvaporatingPoint> activeEvaporatingPoints = new List<EvaporatingPoint>();
    private List<Drip> activeDrips = new List<Drip>();

    #endregion

    #region 2. 내부 데이터 클래스 (Data Classes)

    private class EvaporatingPoint
    {
        public Vector2 uv;
        public float life;
        public float maxLife;
        public Color color;
        public Material materialInstance;

        public EvaporatingPoint(Vector2 uv, Color color, float duration, Material mat)
        {
            this.uv = uv;
            this.color = color;
            this.maxLife = duration;
            this.life = duration;
            this.materialInstance = mat;
        }
    }

    private class Drip
    {
        public Vector2 uv;
        public float life;
        public float speed;
        public Color color;
    }

    #endregion

    #region 3. 유니티 생명주기 (Unity Lifecycle)

    void Update()
    {
        // 1. 단축키 입력 처리
        HandleBrushSelection();
        HandleDripToggle();

        // 2. 실시간 이펙트 업데이트 로직
        if (isDripEnabled) UpdateDrips();
        UpdateEvaporation();

        // 3. 그리기 로직 (캔버스가 없으면 중단)
        if (canvasPainter == null) return;
        HandleMouseDrawing();
    }

    #endregion

    #region 4. 입력 처리 로직 (Input Handling)

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
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                lastUV = hit.textureCoord;
                canvasPainter.Paint(lastUV, brushes[currentBrushIndex]);

                if (currentBrushIndex == evaporatingBrushIndex)
                {
                    AddEvaporatingPoint(lastUV);
                }
            }
        }
        else if (Input.GetMouseButton(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Vector2 currentUV = hit.textureCoord;

                // 브러시 인덱스에 따른 분기 처리
                if (currentBrushIndex == sprayBrushIndex) 
                {
                    PaintWithSpray(currentUV);
                }
                else if (currentBrushIndex == inkBrushIndex) 
                {
                    PaintLine(lastUV, currentUV);
                    if (isDripEnabled && Random.value < 0.05f) 
                    {
                        CreateDrip(currentUV);
                    }
                }
                else if (currentBrushIndex == evaporatingBrushIndex) 
                {
                    PaintLine(lastUV, currentUV);
                    if (Random.value < 0.2f) 
                    {
                        AddEvaporatingPoint(currentUV);
                    }
                }
                else 
                {
                    PaintLine(lastUV, currentUV);
                }
                
                lastUV = currentUV; 
            }
        }
    }

    #endregion

    #region 5. 기본 드로잉 로직 (Core Drawing)

    private void PaintLine(Vector2 start, Vector2 end)
    {
        float distance = Vector2.Distance(start, end);
        float minDistance = 0.002f;
        if (distance > minDistance)
        {
            int steps = Mathf.CeilToInt(distance / minDistance);
            for (int i = 1; i <= steps; i++)
            {
                Vector2 lerpUV = Vector2.Lerp(start, end, (float)i / steps);
                canvasPainter.Paint(lerpUV, brushes[currentBrushIndex]);
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

    #region 6. 스프레이 효과 로직 (Spray Effect)

    private void PaintWithSpray(Vector2 centerUV)
    {
        for (int i = 0; i < sprayParticleCount; i++)
        {
            Vector2 randomOffset = Random.insideUnitCircle * sprayRadius;
            Vector2 sprayUV = centerUV + randomOffset;
            canvasPainter.Paint(sprayUV, brushes[sprayBrushIndex]);
        }
    }

    #endregion

    #region 7. 기화펜 효과 로직 (Evaporating Effect)

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
                canvasPainter.Paint(p.uv, p.materialInstance);
            }

            if (p.life <= 0)
            {
                if (p.materialInstance != null) Destroy(p.materialInstance);
                activeEvaporatingPoints.RemoveAt(i);
            }
        }
    }

    #endregion

    #region 8. 흐름 효과 로직 (Drip Effect)

    private void CreateDrip(Vector2 startUV)
    {
        Drip newDrip = new Drip();
        newDrip.uv = startUV;
        newDrip.life = Random.Range(50, 5000);
        newDrip.speed = Random.Range(0.00008f, 0.0000001f);
        newDrip.color = GetCurrentBrushColor();
        activeDrips.Add(newDrip);
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
                    
                canvasPainter.Paint(drip.uv, dripTrailMaterial);
            }
            
            drip.life--;
            if (drip.life <= 0) activeDrips.RemoveAt(i);
        }
    }

    #endregion
}