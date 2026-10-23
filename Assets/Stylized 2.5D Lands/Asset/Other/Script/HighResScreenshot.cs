using UnityEngine;
using System.IO;

public class HighResScreenshot : MonoBehaviour
{
    [Header("Screenshot Settings")]
    public Camera targetCamera;
    public int width = 3840;  // 4K width
    public int height = 2160; // 4K height
    public KeyCode screenshotKey = KeyCode.F11;
    
    [Header("File Settings")]
    public string saveFolder = "HighResScreenshots";
    public string filenamePrefix = "Screenshot";
    
    void Update()
    {
        if (Input.GetKeyDown(screenshotKey))
        {
            CaptureScreenshot();
        }
    }
    
    public void CaptureScreenshot()
    {
        // Use main camera if none specified
        if (targetCamera == null)
            targetCamera = Camera.main;
            
        if (targetCamera == null)
        {
            Debug.LogError("No camera found! Please assign a camera.");
            return;
        }
        
        // Create temporary render texture
        RenderTexture renderTexture = new RenderTexture(width, height, 24);
        targetCamera.targetTexture = renderTexture;
        
        // Create texture for screenshot
        Texture2D screenshotTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        
        // Render camera to texture
        targetCamera.Render();
        RenderTexture.active = renderTexture;
        screenshotTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        screenshotTexture.Apply();
        
        // Clean up
        targetCamera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(renderTexture);
        
        // Encode to PNG
        byte[] imageBytes = screenshotTexture.EncodeToPNG();
        Destroy(screenshotTexture);
        
        // Create save directory
        string projectPath = Application.dataPath;
        string savePath = Path.Combine(projectPath, "..", saveFolder);
        
        if (!Directory.Exists(savePath))
        {
            Directory.CreateDirectory(savePath);
            Debug.Log($"Created directory: {savePath}");
        }
        
        // Generate filename with timestamp
        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string filename = $"{filenamePrefix}_{timestamp}_{width}x{height}.png";
        string fullPath = Path.Combine(savePath, filename);
        
        // Save file
        File.WriteAllBytes(fullPath, imageBytes);
        
        Debug.Log($"High-res screenshot saved: {fullPath}");
        Debug.Log($"Resolution: {width} × {height}");
        
        #if UNITY_EDITOR
        // Highlight in Project window (Editor only)
        UnityEditor.EditorUtility.RevealInFinder(fullPath);
        #endif
    }
    
    // Optional: Add Editor menu item
    #if UNITY_EDITOR
    [UnityEditor.MenuItem("Tools/Screenshot/Take High-Res Screenshot %#s")]
    static void ScreenshotMenuItem()
    {
        HighResScreenshot instance = FindObjectOfType<HighResScreenshot>();
        if (instance != null)
        {
            instance.CaptureScreenshot();
        }
        else
        {
            Debug.LogWarning("No HighResScreenshot component found in scene.");
        }
    }
    #endif
}