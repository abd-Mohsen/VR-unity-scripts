using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using GLTFast;
using System.Linq;

public class LoadModels : MonoBehaviour
{
    public string modelsFolderPath = "C:\\Users\\ABD\\Desktop\\models";

    private GameObject selectedObject; // The object to control
    private HttpListener httpListener; // HTTP server
    private Thread serverThread; // Thread for the server
    private Vector3 movementDirection = Vector3.zero; // Direction to move the object
    public float moveSpeed = 1f; // Movement speed

    async void Start()
    {
        // Load the models and wait until they are fully loaded
        //await LoadAllModels();
        await LoadModelsFromServer();

        // Now that the models are loaded, check for the first one to control
        if (transform.childCount > 0)
        {
            selectedObject = transform.GetChild(0).gameObject;
            Debug.Log("Selected Object: " + selectedObject.name);
        }
        else
        {
            Debug.LogError("No GameObject found to control!");
            return;
        }

        // Start the HTTP server to handle movement commands
        StartServer();
    }

    void Update()
    {
        // Move the object based on the movement direction
        if (selectedObject != null)
        {
            selectedObject.transform.Translate(movementDirection * moveSpeed);
        }

        // Reset the movement direction after moving
        movementDirection = Vector3.zero;
    }

    private void StartServer()
    {
        httpListener = new HttpListener();
        httpListener.Prefixes.Add("http://127.0.0.1:9000/"); // Add the prefix for the server
        httpListener.Start();

        // Start the server in a separate thread
        serverThread = new Thread(() =>
        {
            while (httpListener.IsListening)
            {
                try
                {
                    // Wait for an incoming request
                    var context = httpListener.GetContext();
                    HandleRequest(context);
                }
                catch (Exception ex)
                {
                    //Debug.LogError($"Server error: {ex.Message}");
                }
            }
        });
        serverThread.IsBackground = true;
        serverThread.Start();

        Debug.Log("HTTP server started on http://127.0.0.1:9000/");
    }

    private void HandleRequest(HttpListenerContext context)
    {
        // Parse the command from the URL
        string command = context.Request.RawUrl?.Trim('/').ToLower();

        // Handle the movement commands
        switch (command)
        {
            case "left":
                movementDirection = Vector3.left;
                break;
            case "right":
                movementDirection = Vector3.right;
                break;
            case "up":
                movementDirection = Vector3.up;
                break;
            case "down":
                movementDirection = Vector3.down;
                break;
            default:
                Debug.LogWarning($"Unknown command: {command}");
                break;
        }

        // Respond to the client
        string responseString = $"Command '{command}' executed!";
        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
        context.Response.ContentLength64 = buffer.Length;
        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        context.Response.OutputStream.Close();
    }

    private void OnDestroy()
    {
        // Stop the server when the game ends
        if (httpListener != null && httpListener.IsListening)
        {
            httpListener.Stop();
            httpListener.Close();
        }

        if (serverThread != null && serverThread.IsAlive)
        {
            serverThread.Abort();
        }
    }

    // Asynchronously load all GLB models from the folder
    private async Task LoadAllModels()
    {
        // Check if folder exists
        if (!Directory.Exists(modelsFolderPath))
        {
            Debug.LogError($"The folder '{modelsFolderPath}' does not exist!");
            return;
        }

        // Get all .glb files in the folder
        string[] modelFiles = Directory.GetFiles(modelsFolderPath, "*.glb");

        if (modelFiles.Length == 0)
        {
            Debug.LogWarning("No .glb files found in the folder!");
            return;
        }

        // Position offset for placing models
        Vector3 positionOffset = Vector3.zero;
        float spacing = 2.0f; // Space between models

        // Use a list to track tasks
        List<Task<GameObject>> loadTasks = new List<Task<GameObject>>();

        // Start loading all models in parallel
        foreach (string modelFile in modelFiles)
        {
            loadTasks.Add(LoadGLBModelAsync(modelFile));
        }

        // Wait for all models to finish loading
        GameObject[] loadedModels = await Task.WhenAll(loadTasks);

        // Position the models after they've all loaded
        foreach (GameObject model in loadedModels)
        {
            if (model != null)
            {
                model.transform.position = positionOffset;
                positionOffset += new Vector3(spacing, 0, 0);
                model.transform.SetParent(transform); // Set model as a child of the parent object
            }
        }

        Debug.Log("All models loaded!");
    }

    // Helper method to load individual GLB models asynchronously
    private async Task<GameObject> LoadGLBModelAsync(string glbFilePath)
    {
        var gltf = new GltfImport();
        bool success = await gltf.Load(glbFilePath);

        if (success)
        {
            GameObject model = new GameObject(Path.GetFileNameWithoutExtension(glbFilePath));
            await gltf.InstantiateMainSceneAsync(model.transform);
            return model;
        }
        return null;
    }

    ///////////////
    
    [Header("Server Settings")]
    [SerializeField] private string serverUrl = "http://127.0.0.1:8000";
    [SerializeField] private string modelsEndpoint = "/api/models/search";

    [Header("Display Settings")]
    [SerializeField] private float spacing = 2.0f;

    private async Task LoadModelsFromServer()
    {
        List<ModelInfo> modelList = await FetchModelList();
        
        if (modelList == null || modelList.Count == 0)
        {
            Debug.LogWarning("No models found on the server!");
            return;
        }

        // Position offset for placing models
        Vector3 positionOffset = Vector3.zero;

        // Use a list to track tasks
        List<Task<GameObject>> loadTasks = new List<Task<GameObject>>();

        // Start loading all models in parallel
        foreach (ModelInfo modelInfo in modelList)
        {
            loadTasks.Add(LoadGLBModelFromUrlAsync(modelInfo.modelPath, modelInfo.name));
        }

        // Wait for all models to finish loading
        GameObject[] loadedModels = await Task.WhenAll(loadTasks);

        // Position the models after they've all loaded
        foreach (GameObject model in loadedModels)
        {
            if (model != null)
            {
                model.transform.position = positionOffset;
                positionOffset += new Vector3(spacing, 0, 0);
                model.transform.SetParent(transform);
            }
        }

        Debug.Log($"Successfully loaded models from server!");
    }

    private async Task<List<ModelInfo>> FetchModelList()
    {
        string url = $"{serverUrl}{modelsEndpoint}";
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to fetch model list: {request.error}");
                return null;
            }

            // Fix 1: Parse the JSON array directly into a List<ModelInfo>
            string jsonResponse = request.downloadHandler.text;
            return ParseModelList(jsonResponse);
        }
    }

    // Helper method to parse the JSON array
    private List<ModelInfo> ParseModelList(string jsonArray)
    {
        // Add square brackets to make it a proper array if needed
        if (!jsonArray.StartsWith("["))
        {
            jsonArray = $"[{jsonArray}]";
        }

        // Fix 2: Use JsonHelper wrapper for arrays
        return JsonHelper.FromJson<ModelInfo>(jsonArray).ToList();
    }

    // Add this helper class to handle JSON arrays
    public static class JsonHelper
    {
        public static T[] FromJson<T>(string json)
        {
            Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>($"{{\"Items\":{json}}}");
            return wrapper.Items;
        }

        [System.Serializable]
        private class Wrapper<T>
        {
            public T[] Items;
        }
    }
    private async Task<GameObject> LoadGLBModelFromUrlAsync(string modelUrl, string modelName)
    {
        // Create a temporary file path
        string tempFilePath = Path.Combine(Application.temporaryCachePath, $"{modelName}.glb");

        try
        {
            // First download the file
            using (UnityWebRequest request = UnityWebRequest.Get(modelUrl))
            {
                request.downloadHandler = new DownloadHandlerFile(tempFilePath);
                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Failed to download model {modelName}: {request.error}");
                    return null;
                }
            }

            // Now load the downloaded file
            var gltf = new GltfImport();
            bool success = await gltf.Load(tempFilePath);

            if (success)
            {
                GameObject model = new GameObject(modelName);
                await gltf.InstantiateMainSceneAsync(model.transform);
                return model;
            }
            else
            {
                Debug.LogError($"Failed to load model {modelName} from downloaded file");
                return null;
            }
        }
        finally
        {
            // Clean up the temporary file
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    // Helper classes for JSON parsing
    [System.Serializable]
    private class ModelInfo
    {
        public string id;
        public string name;
        public string description;
        public string lastUpdated;
        public string thumbnail;
        public string modelPath;
    }

    [System.Serializable]
    private class ModelListResponse
    {
        public List<ModelInfo> models;
    }
}
