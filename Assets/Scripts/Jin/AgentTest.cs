using UnityEngine;

public class AgentTest : MonoBehaviour
{
    public CanvasManager canvasManager;
    public Color drawColor = Color.black; 
    public float brushSize = 50f; 

    void Update()
    {
        if (Input.GetMouseButton(0))
        {
            DrawAtMouse();
        }
    }

    void DrawAtMouse()
    {
        Vector2 mousePos = Input.mousePosition;
        
        float ratioX = canvasManager.canvasTexture.width / (float)Screen.width;
        float ratioY = canvasManager.canvasTexture.height / (float)Screen.height;

        float exactX = mousePos.x * ratioX;
        float exactY = (Screen.height - mousePos.y) * ratioY;

        Vector2 finalPos = new Vector2(exactX, exactY);

        // [확인용 로그] 마우스가 제대로 작동하는지 확인
        Debug.Log($"도장 쾅! 화면좌표: {mousePos}, 도화지좌표: {finalPos}");

        canvasManager.DrawStamp(finalPos, drawColor, brushSize);
    }
}