using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;

/// <summary>
/// 단어 입력 -> Claude API 프롬프트 생성 -> DALL-E 3 이미지 생성 -> 로봇팔 드로잉
/// </summary>
public class CreativeDrawingManager : MonoBehaviour
{
    [Header("API 키")]
    public string claudeApiKey = "여기에_Claude_API_키_입력";
    public string openAiApiKey = "여기에_OpenAI_API_키_입력";

    [Header("UI 연결")]
    public TMP_InputField wordInputField;
    public Button addWordButton;
    public Button startDrawingButton;
    public Button clearWordsButton;
    public Transform wordTagContainer;
    public GameObject wordTagPrefab;
    public TMP_Text statusText;
    public TMP_Text promptDisplayText;
    public RawImage previewImage;

    [Header("연결")]
    public DrawingController drawingController;

    private List<string> words = new List<string>();
    private List<GameObject> wordTagObjects = new List<GameObject>();
    private bool isProcessing = false;

    void Start()
    {
        addWordButton.onClick.AddListener(AddWord);
        startDrawingButton.onClick.AddListener(OnStartButtonClicked);
        clearWordsButton.onClick.AddListener(ClearWords);
        wordInputField.onSubmit.AddListener(_ => AddWord());
        UpdateUI();
    }

    void AddWord()
    {
        string word = wordInputField.text.Trim();
        if (string.IsNullOrEmpty(word)) return;
        if (words.Count >= 10)
        {
            SetStatus("Maximum 10 words allowed.");
            return;
        }

        words.Add(word);
        wordInputField.text = "";
        wordInputField.ActivateInputField();

        if (wordTagPrefab != null && wordTagContainer != null)
        {
            GameObject tag = Instantiate(wordTagPrefab, wordTagContainer);
            TMP_Text tagText = tag.GetComponentInChildren<TMP_Text>();
            if (tagText != null) tagText.text = word;

            Button deleteBtn = tag.GetComponentInChildren<Button>();
            if (deleteBtn != null)
            {
                GameObject tagRef = tag;
                int idx = words.Count - 1;
                deleteBtn.onClick.AddListener(() => RemoveWord(idx, tagRef));
            }

            wordTagObjects.Add(tag);
        }

        UpdateUI();
        SetStatus(words.Count + " word(s) added");
    }

    void RemoveWord(int index, GameObject tagObject)
    {
        if (index < words.Count) words.RemoveAt(index);
        if (tagObject != null)
        {
            wordTagObjects.Remove(tagObject);
            Destroy(tagObject);
        }
        UpdateUI();
    }

    void ClearWords()
    {
        words.Clear();
        foreach (var tag in wordTagObjects)
            if (tag != null) Destroy(tag);
        wordTagObjects.Clear();
        UpdateUI();
        SetStatus("Cleared.");
    }

    void OnStartButtonClicked()
    {
        if (isProcessing) return;
        if (words.Count < 2)
        {
            SetStatus("Please enter at least 2 words.");
            return;
        }
        StartCoroutine(ProcessPipeline());
    }

    IEnumerator ProcessPipeline()
    {
        isProcessing = true;
        startDrawingButton.interactable = false;

        // 1단계: Claude API로 창의적 프롬프트 생성
        SetStatus("Claude AI is generating a creative prompt...");
        string prompt = "";
        yield return StartCoroutine(GeneratePromptWithClaude(words, result => prompt = result));

        if (string.IsNullOrEmpty(prompt))
        {
            SetStatus("Failed to generate prompt. Please try again.");
            isProcessing = false;
            startDrawingButton.interactable = true;
            yield break;
        }

        if (promptDisplayText != null)
            promptDisplayText.text = "Generated Prompt:\n" + prompt;

        // 2단계: DALL-E 3으로 이미지 생성
        SetStatus("DALL-E 3 is generating an image...");
        Texture2D generatedImage = null;
        yield return StartCoroutine(GenerateImageWithDallE(prompt, result => generatedImage = result));

        if (generatedImage == null)
        {
            SetStatus("Failed to generate image. Please try again.");
            isProcessing = false;
            startDrawingButton.interactable = true;
            yield break;
        }

        if (previewImage != null)
            previewImage.texture = generatedImage;

        // 3단계: 로봇팔 드로잉 시작
        SetStatus("Robot arm is starting to draw...");
        if (drawingController != null)
        {
            drawingController.targetImage = generatedImage;
            drawingController.StartDrawingExternal();
        }

        isProcessing = false;
        startDrawingButton.interactable = true;
    }

    // ─────────────────────────────────────────────────────────────
    // Claude API: 단어들을 조합해서 창의적인 DALL-E 프롬프트 생성
    // ─────────────────────────────────────────────────────────────
    IEnumerator GeneratePromptWithClaude(List<string> inputWords, System.Action<string> callback)
    {
        string wordList = string.Join(", ", inputWords);
        string userMessage = "Create a creative DALL-E 3 image prompt by combining these words: " + wordList + ". " +
                             "Requirements for the image: " +
                             "1. Transparent or plain background. " +
                             "2. Simple bold outlines. " +
                             "3. Flat colors with clearly separated color regions. " +
                             "4. Vector art or sticker style. " +
                             "5. No complex textures or gradients. " +
                             "6. Strong contrast between elements. " +
                             "7. Creatively synthesize all the concepts into one unique scene. " +
                             "Output only the image prompt in English, nothing else.";

        string requestBody = "{" +
            "\"model\": \"claude-sonnet-4-20250514\"," +
            "\"max_tokens\": 300," +
            "\"messages\": [{\"role\": \"user\", \"content\": \"" + EscapeJson(userMessage) + "\"}]" +
            "}";

        UnityWebRequest request = new UnityWebRequest("https://api.anthropic.com/v1/messages", "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("x-api-key", claudeApiKey);
        request.SetRequestHeader("anthropic-version", "2023-06-01");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Claude API 오류: " + request.error + "\n" + request.downloadHandler.text);
            callback("");
            yield break;
        }

        string json = request.downloadHandler.text;
        int textStart = json.IndexOf("\"text\":\"") + 8;
        int textEnd = json.IndexOf("\"", textStart);
        if (textStart > 8 && textEnd > textStart)
        {
            string prompt = json.Substring(textStart, textEnd - textStart);
            prompt = UnescapeJson(prompt);
            Debug.Log("Claude 생성 프롬프트: " + prompt);
            callback(prompt);
        }
        else
        {
            Debug.LogError("Claude 응답 파싱 실패: " + json);
            callback("");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // DALL-E 3: 프롬프트로 이미지 생성
    // ─────────────────────────────────────────────────────────────
    IEnumerator GenerateImageWithDallE(string prompt, System.Action<Texture2D> callback)
    {
        string requestBody = "{" +
            "\"model\": \"gpt-image-1\"," +
            "\"prompt\": \"" + EscapeJson(prompt) + "\"," +
            "\"n\": 1," +
            "\"size\": \"1024x1024\"," +
            "\"quality\": \"low\"" +
            "}";

        UnityWebRequest request = new UnityWebRequest("https://api.openai.com/v1/images/generations", "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + openAiApiKey);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("DALL-E API 오류: " + request.error + "\n" + request.downloadHandler.text);
            callback(null);
            yield break;
        }

        string json = request.downloadHandler.text;

        string b64Data = null;
        string imageUrlData = null;

        try
        {
            // "b64_json": " 형태 (공백 포함) 처리
            string[] searchKeys = new string[] { "\"b64_json\":\"", "\"b64_json\": \"" };
            int b64Start = -1;

            foreach (var key in searchKeys)
            {
                int idx = json.IndexOf(key);
                if (idx >= 0)
                {
                    b64Start = idx + key.Length;
                    break;
                }
            }

            if (b64Start >= 0)
            {
                int b64End = b64Start;
                while (b64End < json.Length)
                {
                    char c = json[b64End];
                    if (c == '"') break;
                    b64End++;
                }
                b64Data = json.Substring(b64Start, b64End - b64Start);
                b64Data = b64Data.Replace("\\n", "").Replace("\n", "").Replace(" ", "").Replace("\r", "");
                Debug.Log("Base64 데이터 길이: " + b64Data.Length);
            }
            else
            {
                string[] urlKeys = new string[] { "\"url\":\"", "\"url\": \"" };
                foreach (var key in urlKeys)
                {
                    int idx = json.IndexOf(key);
                    if (idx >= 0)
                    {
                        int urlStart = idx + key.Length;
                        int urlEnd = json.IndexOf("\"", urlStart);
                        imageUrlData = json.Substring(urlStart, urlEnd - urlStart);
                        imageUrlData = UnescapeJson(imageUrlData);
                        Debug.Log("Image URL: " + imageUrlData);
                        break;
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("이미지 파싱 예외: " + e.Message);
            callback(null);
            yield break;
        }

        // try 블록 밖에서 처리
        if (b64Data != null)
        {
            try
            {
                byte[] imageBytes = System.Convert.FromBase64String(b64Data);
                Texture2D texture = new Texture2D(2, 2);
                texture.LoadImage(imageBytes);
                Debug.Log("이미지 생성 완료! 크기: " + texture.width + "x" + texture.height);
                callback(texture);
            }
            catch (System.Exception e)
            {
                Debug.LogError("Base64 디코딩 실패: " + e.Message);
                callback(null);
            }
        }
        else if (imageUrlData != null)
        {
            yield return StartCoroutine(DownloadImage(imageUrlData, callback));
        }
        else
        {
            Debug.LogError("이미지 데이터를 찾을 수 없음");
            callback(null);
        }
    }

    IEnumerator DownloadImage(string url, System.Action<Texture2D> callback)
    {
        SetStatus("Downloading image...");
        UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("이미지 다운로드 오류: " + request.error);
            callback(null);
            yield break;
        }

        Texture2D texture = DownloadHandlerTexture.GetContent(request);
        callback(texture);
    }

    void UpdateUI()
    {
        bool hasWords = words.Count >= 2;
        startDrawingButton.interactable = hasWords && !isProcessing;
    }

    void SetStatus(string message)
    {
        Debug.Log("[CreativeDrawing] " + message);
        if (statusText != null)
            statusText.text = message;
    }

    string EscapeJson(string str)
    {
        return str.Replace("\\", "\\\\")
                  .Replace("\"", "\\\"")
                  .Replace("\n", "\\n")
                  .Replace("\r", "\\r");
    }

    string UnescapeJson(string str)
    {
        return str.Replace("\\n", "\n")
                  .Replace("\\\"", "\"")
                  .Replace("\\\\", "\\");
    }
}