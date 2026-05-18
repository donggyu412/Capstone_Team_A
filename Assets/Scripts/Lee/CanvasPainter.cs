using UnityEngine;

[RequireComponent(typeof(CanvasMeshGenerator))]
public class CanvasPainter : MonoBehaviour
{
    [Header("Render Texture 설정")]
    public RenderTexture canvasRenderTexture;

    [Header("잉크 시뮬레이션 머티리얼")]
    public Material inkAddMaterial;
    public Material inkFlowMaterial;
    public Material inkDripMaterial;

    [Header("잉크 물리 파라미터")]
    [Range(0.1f, 3.0f)] public float dripThreshold       = 0.5f;
    [Range(0.0f, 2.0f)] public float flowSpeed           = 0.5f;
    [Range(0.0f, 3.0f)] public float inkAccumulationRate = 0.8f;
    [Range(0.005f, 0.15f)] public float inkBrushRadius   = 0.025f;
    [Range(0.0f, 0.2f)] public float inkDryRate          = 0.01f;
    public Color inkColor = new Color(0.08f, 0.12f, 0.75f, 1f);

    private RenderTexture tempRenderTexture;
    private RenderTexture inkMapTexture;
    private RenderTexture inkMapTempTexture;

    // ─────────────────────────────────────────────────────────────
    // Awake: Inspector 연결이 끊겨도 이름으로 자동 탐색
    // ML-Agents 에피소드 리셋으로 재생성돼도 머티리얼을 올바르게 연결
    // ─────────────────────────────────────────────────────────────
    void Awake()
    {
        inkAddMaterial  = EnsureMaterial(inkAddMaterial,  "InkAddMat");
        inkFlowMaterial = EnsureMaterial(inkFlowMaterial, "InkFlowMat");
        inkDripMaterial = EnsureMaterial(inkDripMaterial, "InkDripMat");

        Debug.Log($"[CanvasPainter] Awake 머티리얼 — " +
                  $"Add:{inkAddMaterial?.name ?? "NULL"} " +
                  $"Flow:{inkFlowMaterial?.name ?? "NULL"} " +
                  $"Drip:{inkDripMaterial?.name ?? "NULL"}");
    }

    /// <summary>
    /// 이미 올바르게 연결돼 있으면 그대로 반환.
    /// null이거나 이름이 다르면 프로젝트 전체에서 targetName으로 탐색.
    /// </summary>
    private Material EnsureMaterial(Material mat, string targetName)
    {
        if (mat != null && mat.name == targetName) return mat;

        foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            if (m.name == targetName) return m;

        Debug.LogWarning($"[CanvasPainter] '{targetName}' 머티리얼을 찾지 못했습니다.");
        return null;
    }

    // ─────────────────────────────────────────────────────────────
    // Start: 반드시 텍스처 생성 후 ClearCanvas 호출 (순서 중요)
    // ─────────────────────────────────────────────────────────────
    void Start()
    {
        if (canvasRenderTexture == null)
        {
            Debug.LogError("CanvasPainter: canvasRenderTexture가 연결되지 않았습니다!");
            return;
        }

        int w = canvasRenderTexture.width;
        int h = canvasRenderTexture.height;

        // 1. 텍스처 먼저 생성
        tempRenderTexture = CreateRT(w, h, canvasRenderTexture.format);
        inkMapTexture     = CreateRT(w, h, RenderTextureFormat.RFloat);
        inkMapTempTexture = CreateRT(w, h, RenderTextureFormat.RFloat);

        // 2. 생성 후 초기화 (inkMapTexture 쓰레기값 제거 포함)
        ClearCanvas();
    }

    void Update()
    {
        if (inkMapTexture == null) return;

        SimulateInkFlow();
        ApplyInkToCanvas();
    }

    // ─────────────────────────────────────────────────────────────
    // 공개 API
    // ─────────────────────────────────────────────────────────────

    public void Paint(Vector2 uv, Material brushMaterial, float pressure)
    {
        if (canvasRenderTexture == null || brushMaterial == null) return;

        brushMaterial.SetVector("_BrushUV", new Vector4(uv.x, uv.y, 0, 0));
        brushMaterial.SetFloat("_Pressure", pressure);

        Graphics.Blit(canvasRenderTexture, tempRenderTexture, brushMaterial);
        Graphics.Blit(tempRenderTexture, canvasRenderTexture);

        AddInkToMap(uv, pressure);
    }

    public void AccumulateInkOnly(Vector2 uv, float pressure)
    {
        AddInkToMap(uv, pressure);
    }

    /// <summary>
    /// 잉크 맵 누적 없이 캔버스에만 획을 그립니다.
    /// 스프레이·블러처럼 잉크 흘러내림 효과가 필요 없는 브러시 전용.
    /// </summary>
    public void PaintOnly(Vector2 uv, Material brushMaterial, float pressure)
    {
        if (canvasRenderTexture == null || brushMaterial == null) return;

        brushMaterial.SetVector("_BrushUV", new Vector4(uv.x, uv.y, 0, 0));
        brushMaterial.SetFloat("_Pressure", pressure);

        Graphics.Blit(canvasRenderTexture, tempRenderTexture, brushMaterial);
        Graphics.Blit(tempRenderTexture, canvasRenderTexture);
        // AddInkToMap 호출 없음 → 잉크 흘러내림 효과 없음
    }

    public void ClearCanvas()
    {
        RenderTexture prev = RenderTexture.active;

        if (canvasRenderTexture != null)
        {
            RenderTexture.active = canvasRenderTexture;
            GL.Clear(true, true, new Color(1f, 0.96f, 0.86f));
        }

        if (inkMapTexture != null)
        {
            RenderTexture.active = inkMapTexture;
            GL.Clear(true, true, Color.black);
        }

        RenderTexture.active = prev;
    }

    public RenderTexture GetCanvasTexture() => canvasRenderTexture;
    public RenderTexture GetInkMapTexture() => inkMapTexture;

    // ─────────────────────────────────────────────────────────────
    // 내부 잉크 시뮬레이션
    // ─────────────────────────────────────────────────────────────

    private void AddInkToMap(Vector2 uv, float pressure)
    {
        if (inkAddMaterial == null || inkMapTexture == null) return;

        float addAmount = pressure * inkAccumulationRate * Time.deltaTime;

        inkAddMaterial.SetVector("_BrushUV",    new Vector4(uv.x, uv.y, 0, 0));
        inkAddMaterial.SetFloat("_AddAmount",   addAmount);
        inkAddMaterial.SetFloat("_BrushRadius", inkBrushRadius);

        Graphics.Blit(inkMapTexture, inkMapTempTexture, inkAddMaterial);
        Graphics.Blit(inkMapTempTexture, inkMapTexture);
    }

    private void SimulateInkFlow()
    {
        if (inkFlowMaterial == null || inkMapTexture == null) return;

        Vector3 worldGravity = Physics.gravity.normalized;
        float uGravity = Vector3.Dot(worldGravity, transform.right);
        float vGravity = Vector3.Dot(worldGravity, transform.up);
        Vector2 uvGravity = new Vector2(uGravity, vGravity);

        if (uvGravity.sqrMagnitude < 0.001f)
            uvGravity = new Vector2(0f, -0.01f);
        else
            uvGravity.Normalize();

        inkFlowMaterial.SetVector("_GravityUV",    new Vector4(uvGravity.x, uvGravity.y, 0, 0));
        inkFlowMaterial.SetFloat("_DripThreshold", dripThreshold);
        inkFlowMaterial.SetFloat("_FlowSpeed",     flowSpeed * Time.deltaTime);
        inkFlowMaterial.SetFloat("_DryRate",       inkDryRate * Time.deltaTime);
        inkFlowMaterial.SetVector("_TexelSize", new Vector4(
            1f / inkMapTexture.width,
            1f / inkMapTexture.height,
            inkMapTexture.width,
            inkMapTexture.height));

        Graphics.Blit(inkMapTexture, inkMapTempTexture, inkFlowMaterial);
        Graphics.Blit(inkMapTempTexture, inkMapTexture);
    }

    private void ApplyInkToCanvas()
    {
        if (inkDripMaterial == null || inkMapTexture == null) return;

        inkDripMaterial.SetTexture("_InkMap",      inkMapTexture);
        inkDripMaterial.SetColor("_InkColor",      inkColor);
        inkDripMaterial.SetFloat("_DripThreshold", dripThreshold);

        Graphics.Blit(canvasRenderTexture, tempRenderTexture, inkDripMaterial);
        Graphics.Blit(tempRenderTexture, canvasRenderTexture);
    }

    // ─────────────────────────────────────────────────────────────
    // 유틸리티
    // ─────────────────────────────────────────────────────────────

    private RenderTexture CreateRT(int width, int height, RenderTextureFormat format)
    {
        var rt = new RenderTexture(width, height, 0, format)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp
        };
        rt.Create();
        return rt;
    }

    void OnDestroy()
    {
        ReleaseRT(tempRenderTexture);
        ReleaseRT(inkMapTexture);
        ReleaseRT(inkMapTempTexture);
    }

    private void ReleaseRT(RenderTexture rt)
    {
        if (rt != null) { rt.Release(); Destroy(rt); }
    }
}
