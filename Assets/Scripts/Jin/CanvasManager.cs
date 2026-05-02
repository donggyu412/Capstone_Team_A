using UnityEngine;

public class CanvasManager : MonoBehaviour
{
    [Header("Canvas Settings")]
    public RenderTexture canvasTexture; 

    private Texture2D brushTexture;
    public Texture2D eraserTexture;
    private Material brushMaterial;

    void Start()
    {
        CreateBrushTexture(64);

        // 도화지를 하얗게 초기화
        RenderTexture.active = canvasTexture;
        GL.Clear(false, true, Color.white); 
        RenderTexture.active = null;

        // [핵심 해결] URP에서도 강제로 화면에 그려주는 가장 확실한 UI 셰이더 사용
        brushMaterial = new Material(Shader.Find("UI/Default"));
        brushMaterial.mainTexture = brushTexture;

        // 지우개용 딱딱한 텍스처 생성 (가장자리까지 꽉 찬 하얀색)
        eraserTexture = CreateHardBrushTexture(64);
    }

    // 딱딱한 원형 텍스처를 만드는 함수 추가
    Texture2D CreateHardBrushTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(size / 2, size / 2));
                // 가장자리까지 투명도 없이 꽉 채움
                float alpha = (dist < size / 2) ? 1.0f : 0.0f;
                tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        }
        tex.Apply();
        return tex;
    }

    public void DrawStamp(Vector2 position, Color color, float size)
    {
        RenderTexture.active = canvasTexture; 
        GL.PushMatrix();
        GL.LoadPixelMatrix(0, canvasTexture.width, canvasTexture.height, 0);

        Rect drawRect = new Rect(position.x - size / 2, position.y - size / 2, size, size);

        brushMaterial.color = color;
        Graphics.DrawTexture(drawRect, brushTexture, brushMaterial);

        GL.PopMatrix();
        RenderTexture.active = null; 
    }

    void CreateBrushTexture(int size)
    {
        brushTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float radius = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(radius, radius));
                float alpha = Mathf.Clamp01(1f - (dist / radius)); 
                brushTexture.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        }
        brushTexture.Apply();
    }
}