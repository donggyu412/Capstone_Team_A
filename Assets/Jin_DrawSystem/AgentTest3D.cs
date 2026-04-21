using UnityEngine;

public class AgentTest3D : MonoBehaviour
{
    public CanvasManager canvasManager;
    
    // 툴 종류 열거형 업데이트 (기본 Brush를 HardBrush로 대체)
    public enum ToolType { HardBrush, Eraser, Pencil, Spray, Ink, Watercolor }
    
    [Header("현재 상태 (실시간 확인용)")]
    public ToolType currentTool = ToolType.HardBrush; // 시작 툴을 HardBrush로 설정
    public float currentSize = 50f;
    [Range(0.01f, 1f)] public float currentOpacity = 1.0f; // 1.0 = 100% 진함

    [Header("색상 설정")]
    public Color drawColor = Color.black;
    public Color eraserColor = Color.white;

    void Update()
    {
        HandleKeyboardInput();

        if (Input.GetMouseButton(0))
        {
            DrawOn3D();
        }
    }

    // 모든 키보드 단축키를 처리하는 함수
    void HandleKeyboardInput()
    {
        // 1. 툴 변경 (B 키에 새로운 거친 붓을 지정)
        if (Input.GetKeyDown(KeyCode.B)) ChangeTool(ToolType.HardBrush); // 'B'키 -> 새로운 거친 붓
        if (Input.GetKeyDown(KeyCode.E)) ChangeTool(ToolType.Eraser);
        if (Input.GetKeyDown(KeyCode.P)) ChangeTool(ToolType.Pencil);
        if (Input.GetKeyDown(KeyCode.S)) ChangeTool(ToolType.Spray);
        if (Input.GetKeyDown(KeyCode.I)) ChangeTool(ToolType.Ink);
        if (Input.GetKeyDown(KeyCode.U)) ChangeTool(ToolType.Watercolor); // 이전 붓(수채화)은 'U'키로 이동

        // 2. 크기 조절 (- / +)
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            currentSize = Mathf.Max(2f, currentSize - 5f);
            Debug.Log($"크기 감소: {currentSize}");
        }
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.Plus) || Input.GetKeyDown(KeyCode.KeypadPlus))
        {
            currentSize = Mathf.Min(300f, currentSize + 5f);
            Debug.Log($"크기 증가: {currentSize}");
        }

        // 3. 투명도 조절 ([ / ])
        if (Input.GetKeyDown(KeyCode.LeftBracket))
        {
            currentOpacity = Mathf.Max(0.05f, currentOpacity - 0.1f);
            Debug.Log($"연하게: {currentOpacity * 100}%");
        }
        if (Input.GetKeyDown(KeyCode.RightBracket))
        {
            currentOpacity = Mathf.Min(1.0f, currentOpacity + 0.1f);
            Debug.Log($"진하게: {currentOpacity * 100}%");
        }
    }

    void ChangeTool(ToolType newTool)
    {
        currentTool = newTool;
        Debug.Log($"<color=green>현재 툴: {currentTool}</color>");
    }

    void DrawOn3D()
    {
        if (Camera.main == null) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            if (hit.collider.gameObject.name.Contains("Canvas")) 
            {
                Vector2 uv = hit.textureCoord;
                float pixelX = uv.x * canvasManager.canvasTexture.width;
                float pixelY = (1 - uv.y) * canvasManager.canvasTexture.height; 
                Vector2 centerPos = new Vector2(pixelX, pixelY);

                ApplyToolLogic(centerPos);
            }
        }
    }

    // 선택된 툴에 따라 그리는 방식을 다르게 처리하는 핵심 로직
    void ApplyToolLogic(Vector2 pos)
    {
        Color baseColor = (currentTool == ToolType.Eraser) ? eraserColor : drawColor;
        Color finalColor = new Color(baseColor.r, baseColor.g, baseColor.b, currentOpacity);

        switch (currentTool)
        {
            case ToolType.HardBrush:
                // [구현 핵심] 딱딱하고 거친 드라이 브러쉬 (Hard, Grainy)
                // 여러 번 도장을 찍되, 스프레이처럼 퍼뜨리지 않고
                // '일정한 위치'에 '다양한 크기'의 도장을 겹쳐 찍어 획 전체에 균일한 거친 질감을 줍니다.
                int grainCount = 4; // 거칠게 표현하기 위해 겹쳐 찍는 횟수
                for (int i = 0; i < grainCount; i++)
                {
                    // 1. 아주 미세한 위치 오프셋 (스프레이의 1/10 수준)
                    Vector2 randomOffset = Random.insideUnitCircle * (currentSize * 0.05f);
                    
                    // 2. [가장 중요] 획을 긋는 동안 도장 크기를 무작위로 변화시켜 '거친 표면'을 만듭니다.
                    // 원래 붓 크기의 0.6배~1.2배 사이로 무작위 크기를 적용
                    float randomizedSize = currentSize * Random.Range(0.6f, 1.2f);
                    
                    canvasManager.DrawStamp(pos + randomOffset, finalColor, randomizedSize);
                }
                break;

            case ToolType.Eraser:
                // 지우개: 강력하게 10번 반복해서 하얗게 만듭니다.
                for (int i = 0; i < 10; i++) {
                    canvasManager.DrawStamp(pos, finalColor, currentSize);
                }
                break;

            case ToolType.Pencil:
                // 연필: 날카롭고 선명한 선
                Color pencilColor = new Color(baseColor.r, baseColor.g, baseColor.b, 1.0f);
                float pencilSize = Mathf.Clamp(currentSize * 0.1f, 1f, 10f);
                canvasManager.DrawStamp(pos, pencilColor, pencilSize);
                break;

            case ToolType.Spray:
                // 스프레이: 무작위 흩뿌림
                int dotCount = (int)(currentSize / 2);
                for (int i = 0; i < dotCount; i++)
                {
                    Vector2 randomOffset = Random.insideUnitCircle * (currentSize / 2f);
                    canvasManager.DrawStamp(pos + randomOffset, finalColor, Random.Range(1f, 3f));
                }
                break;

            case ToolType.Ink:
                // 잉크 붓: 캘리그라피 질감
                canvasManager.DrawStamp(pos, finalColor, currentSize);
                canvasManager.DrawStamp(pos + new Vector2(Random.Range(-2f, 2f), Random.Range(-2f, 2f)), finalColor, currentSize * 0.8f);
                break;

            case ToolType.Watercolor:
                // 수채화: 이전의 일반 붓 (수채화 느낌)
                canvasManager.DrawStamp(pos, finalColor, currentSize);
                break;
        }
    }
}