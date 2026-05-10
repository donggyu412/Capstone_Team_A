using UnityEngine;

public class RobotBrush : MonoBehaviour
{
    [Header("연결 설정")]
    public CanvasPainter canvasPainter;
    public Material brushMaterial;

    [Header("붓 설정")]
    public float brushLength = 0.5f;

    public bool IsTouching { get; private set; }
    public Vector2 LastPaintedUV { get; private set; }

    void Update()
    {
        IsTouching = false;

        if (canvasPainter == null) return;

        // BrushTip Y방향으로 Raycast
        Ray ray = new Ray(transform.position, transform.up);

        Debug.DrawRay(transform.position, transform.up * brushLength,
            IsTouching ? Color.green : Color.red);

        if (Physics.Raycast(ray, out RaycastHit hit, brushLength))
        {
            if (hit.collider.CompareTag("Canvas"))
            {
                IsTouching = true;
                LastPaintedUV = hit.textureCoord;

                Debug.Log($"캔버스 감지! UV: ({hit.textureCoord.x:F2}, {hit.textureCoord.y:F2})");

                if (brushMaterial != null)
                    canvasPainter.Paint(hit.textureCoord, brushMaterial);
            }
        }
    }
}