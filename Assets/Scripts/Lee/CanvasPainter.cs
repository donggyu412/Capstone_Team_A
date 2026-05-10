using UnityEngine;

[RequireComponent(typeof(CanvasMeshGenerator))]
public class CanvasPainter : MonoBehaviour
{
    [Header("Render Texture 설정")]
    public RenderTexture canvasRenderTexture;

    private RenderTexture tempRenderTexture;

    void Start()
    {
        if (canvasRenderTexture == null)
        {
            Debug.LogError("CanvasPainter: canvasRenderTexture가 할당되지 않았습니다!");
            return;
        }

        tempRenderTexture = new RenderTexture(
            canvasRenderTexture.width,
            canvasRenderTexture.height,
            0,
            canvasRenderTexture.format
        );
        tempRenderTexture.Create();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.K))
        {
            ClearCanvas();
            Debug.Log("캔버스 초기화!");
        }
    }

    public void Paint(Vector2 uv, Material brushMaterial)
    {
        if (canvasRenderTexture == null || brushMaterial == null) return;

        brushMaterial.SetVector("_BrushUV", new Vector4(uv.x, uv.y, 0, 0));

        Graphics.Blit(canvasRenderTexture, tempRenderTexture, brushMaterial);
        Graphics.Blit(tempRenderTexture, canvasRenderTexture);
    }

    public void ClearCanvas()
    {
        if (canvasRenderTexture == null) return;

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = canvasRenderTexture;
        GL.Clear(true, true, new Color(1f, 0.96f, 0.86f));
        RenderTexture.active = previous;
    }

    public RenderTexture GetCanvasTexture()
    {
        return canvasRenderTexture;
    }

    void OnDestroy()
    {
        if (tempRenderTexture != null)
        {
            tempRenderTexture.Release();
            Destroy(tempRenderTexture);
        }
    }
}