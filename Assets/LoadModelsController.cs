using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using GLTFast;
using System.Text.Json;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor.Build.Content;
//using SimpleJSON;
//"com.unity.nuget.newtonsoft-json": "3.0.2",
public class LoadModelsController : MonoBehaviour
{
    private String sceneID = "fbb72e89-0f35-4418-aa62-d5a99550732a";
    private GameObject selectedObject; // The object to control
    private HttpListener httpListener; // HTTP server
    private Thread serverThread; // Thread for the server
    private Vector3 movementDirection = Vector3.zero; // Direction to move the object
    public float moveSpeed = 0.5f; // Movement speed

    private List<GameObject> loadedModels = new List<GameObject>();
    private Dictionary<GameObject, string> modelIdLookup = new Dictionary<GameObject, string>();

    async void Start()
    {
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

        Application.targetFrameRate = 30;

        //StartServer();
    }

    void Update()
    {
        HandleMouseClick();
        HandleKeyboardClick();

        if (selectedObject != null)
        {
            // for moving camera
            Vector3 worldDirection = Camera.main.transform.TransformDirection(movementDirection);
            selectedObject.transform.Translate(worldDirection * moveSpeed, Space.World);

            //selectedObject.transform.Translate(movementDirection * moveSpeed, Space.World); //fixed camera

            if (movementDirection != Vector3.zero)
            {
                // Now the movement has occurred, so we send the updated matrix
                _ = UpdateModelTransform(selectedObject); // Fire and forget
            }
        }

        movementDirection = Vector3.zero;
    }

    private void HandleMouseClick()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                GameObject clickedObject = hit.transform.gameObject;
                Debug.Log("Ray hit: " + clickedObject.name);

                // Walk up to find the associated model in the lookup
                Transform current = clickedObject.transform;
                while (current != null)
                {
                    if (modelIdLookup.ContainsKey(current.gameObject))
                    {
                        selectedObject = current.gameObject;
                        Debug.Log("Selected new object: " + selectedObject.name);
                        return;
                    }
                    current = current.parent;
                }

                Debug.Log("Clicked object not in model lookup.");
            }
            else
            {
                Debug.Log("Raycast hit nothing.");
            }
        }
    }



    async private void HandleKeyboardClick()
    {
        bool updated = false;

        if (Input.GetKeyDown(KeyCode.A))
        {
            movementDirection = Vector3.left;
            updated = true;
        }
        else if (Input.GetKeyDown(KeyCode.D))
        {
            movementDirection = Vector3.right;
            updated = true;
        }
        else if (Input.GetKeyDown(KeyCode.W))
        {
            movementDirection = Vector3.up;
            updated = true;
        }
        else if (Input.GetKeyDown(KeyCode.S))
        {
            movementDirection = Vector3.down;
            updated = true;
        }
        else if (Input.GetKeyDown(KeyCode.Q))
        {
            selectedObject.transform.Rotate(0, -90f, 0, Space.World);
            updated = true;
        }
        else if (Input.GetKeyDown(KeyCode.E))
        {
            selectedObject.transform.Rotate(0, 90f, 0, Space.World);
            updated = true;
        }
        else if (Input.GetKeyDown(KeyCode.Z))
        {
            selectedObject.transform.localScale *= 2f;
            updated = true;
        }
        else if (Input.GetKeyDown(KeyCode.X))
        {
            selectedObject.transform.localScale *= 0.5f;
            updated = true;
        }

        // If any transform change happened, upload the new transform to server
        if (updated)
        {
            await UpdateModelTransform(selectedObject);
        }
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

    ///////////////
    
    [Header("Server Settings")]
    private string serverUrl = "http://127.0.0.1:8000";
    private string modelsEndpoint = "/api/models/search";

    public async Task LoadModelsFromServer()
    {
        // Clear previously loaded models before loading new ones
        
        List<ModelInfo> modelList = await FetchModelList();
    
        if (modelList == null || modelList.Count == 0)
        {
            Debug.LogWarning("No models found on the server!");
            return;
        }

        // Filter out hidden models
        var visibleModels = modelList.Where(model => !model.hidden).ToList();
        
        if (visibleModels.Count == 0)
        {
            Debug.LogWarning("No visible models found on the server!");
            return;
        }

        Debug.Log($"Loading {visibleModels.Count} visible models out of {modelList.Count} total models");

        List<Task<(GameObject, ModelInfo)>> loadTasks = new List<Task<(GameObject, ModelInfo)>>();

        foreach (ModelInfo modelInfo in visibleModels) // Use filtered list
        {
            loadTasks.Add(LoadModelWithInfo(modelInfo));
        }

        var loadedModels = await Task.WhenAll(loadTasks);

        ClearLoadedModels();
        foreach (var (model, modelInfo) in loadedModels)
        {
            if (model != null)
            {
                // Add to our tracking list
                this.loadedModels.Add(model);
                modelIdLookup[model] = modelInfo.id;
                Debug.Log(modelInfo.TooString());
                
                Matrix4x4 transformMatrix = ParseTransform(modelInfo.transform);

                Matrix4x4 rawMatrix = transformMatrix;
                Matrix4x4 matrix = rawMatrix.transpose; // Fix row-major to column-major

                // Extract TRS components
                Vector3 position = matrix.GetColumn(3);
                Vector3 scale = new Vector3(
                    matrix.GetColumn(0).magnitude,
                    matrix.GetColumn(1).magnitude,
                    matrix.GetColumn(2).magnitude
                );

                Vector3 xAxis = matrix.GetColumn(0).normalized;
                Vector3 yAxis = matrix.GetColumn(1).normalized;
                Vector3 zAxis = matrix.GetColumn(2).normalized;

                Quaternion rotation = Quaternion.LookRotation(zAxis, yAxis);

                Transform modelTransform = model.transform;
                modelTransform.position = position;
                modelTransform.rotation = rotation;
                modelTransform.localScale = scale;
            }
        }

        Debug.Log($"Successfully loaded {loadedModels.Length} models from server! (Filtered out {modelList.Count - visibleModels.Count} hidden models)");
    }

    private async Task<(GameObject, ModelInfo)> LoadModelWithInfo(ModelInfo modelInfo)
    {
        GameObject model = await LoadGLBModelFromUrlAsync(modelInfo.modelPath, modelInfo.name);
        if (model != null)
        {
            model.transform.SetParent(this.transform); // Ensure parenting
            model.transform.localPosition = Vector3.zero;
            if (model.GetComponent<Collider>() == null)
            {
                var renderer = model.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    Bounds bounds = renderer.bounds;
                    BoxCollider collider = model.AddComponent<BoxCollider>();
                    collider.center = model.transform.InverseTransformPoint(bounds.center);
                    collider.size = bounds.size;
                }
                else
                {
                    model.AddComponent<BoxCollider>(); // fallback
                }
            }
        }
        return (model, modelInfo);
    }

    // Public method to clear all loaded models
    public void ClearLoadedModels()
    {
        Debug.Log($"Clearing {loadedModels.Count} loaded models");
        
        foreach (GameObject model in loadedModels)
        {
            if (model != null)
            {
                // Remove from lookup dictionary
                if (modelIdLookup.ContainsKey(model))
                {
                    modelIdLookup.Remove(model);
                }
                
                // Destroy the GameObject
                Destroy(model);
            }
        }
        
        loadedModels.Clear();
        Debug.Log("All models cleared successfully");
    }
    
    private Matrix4x4 ParseTransform(float[][] matrixData)
    {
        if (matrixData == null)
        {
            Debug.LogError("Matrix data is null! Returning identity matrix.");
            return Matrix4x4.identity;
        }

        if (matrixData.Length != 4 || matrixData.Any(row => row == null || row.Length != 4))
        {
            Debug.LogError("Invalid matrix dimensions! Returning identity matrix.");
            return Matrix4x4.identity;
        }

        return new Matrix4x4(
            new Vector4(matrixData[0][0], matrixData[0][1], matrixData[0][2], matrixData[0][3]),
            new Vector4(matrixData[1][0], matrixData[1][1], matrixData[1][2], matrixData[1][3]),
            new Vector4(matrixData[2][0], matrixData[2][1], matrixData[2][2], matrixData[2][3]),
            new Vector4(matrixData[3][0], matrixData[3][1], matrixData[3][2], matrixData[3][3])
        );
    }

    private async Task<List<ModelInfo>> FetchModelList()
    {
        string url = $"{serverUrl}{modelsEndpoint}?scene_id={sceneID}";
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to fetch models: {request.error}");
                return null;
            }

            try
            {
                var json = request.downloadHandler.text;
                Debug.Log($"Received JSON: {json}"); // Verify raw JSON
                
                var models = JsonConvert.DeserializeObject<List<ModelInfo>>(json);
                
                // Verify deserialization
                if (models != null && models.Count() > 0)
                {
                    models[0].LogTransform();
                }
                
                return models;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to parse model data: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }
    }

    [Serializable]
    private class ModelListWrapper
    {
        public List<ModelInfo> models; // Changed to array for JsonUtility
    }

    private async Task<GameObject> LoadGLBModelFromUrlAsync(string modelUrl, string modelName)
    {
        string tempFilePath = Path.Combine(Application.temporaryCachePath, $"{modelName}.glb");

        try
        {
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
            if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
        }
    }

    [Serializable]
    public class ModelInfo
    {
        public string id;
        public string name;
        public string description;
        public string lastUpdated;
        public string thumbnail;
        public string modelPath;
        public float[][] transform;
        public bool hidden;

        public string TooString(){
            string res = "";
            for(int i=0; i<transform.Length; i++){
                for(int j=0; j<transform.Length; j++){
                    res += transform[i][j];
                }
                res += " / ";
            }
            return res;
        }

        public void LogTransform()
        {
            if (transform == null)
            {
                Debug.LogError("Transform is null!");
                return;
            }

            Debug.Log($"Transform matrix for {name}:");
            for (int i = 0; i < 4; i++)
            {
                Debug.Log($"{transform[i][0]}, {transform[i][1]}, {transform[i][2]}, {transform[i][3]}");
            }
        }
    }

        // public async Task UpdateModelTransform(GameObject gameObject)
    // {
    //     string url = $"{serverUrl}/api/models/{modelIdLookup[gameObject]}/transform";
    //     Debug.Log(url);
        
    //     // Convert matrix to JSON
    //     string jsonMatrix = MatrixUtils.ConvertMatrixToJson(gameObject.transform.localToWorldMatrix);
        
    //     // Create form data
    //     WWWForm form = new WWWForm();
    //     form.AddField("transform", jsonMatrix);
        
    //     using (UnityWebRequest request = UnityWebRequest.Post(url, form))
    //     {
    //         var operation = request.SendWebRequest();
            
    //         while (!operation.isDone)
    //             await Task.Yield();

   
    public async Task UpdateModelTransform(GameObject gameObject)
    {
        if (!modelIdLookup.TryGetValue(gameObject, out string modelId))
        {
            Debug.LogError($"GameObject {gameObject.name} not found in model lookup");
            return;
        }

        string url = $"{serverUrl}/api/models/{modelId}/transform";
        
        try
        {
            string jsonMatrix = ConvertMatrixToJson(gameObject.transform.localToWorldMatrix);
            Debug.Log($"Sending transform update: {jsonMatrix}");

            WWWForm form = new WWWForm();
            form.AddField("transform", jsonMatrix);
            form.AddField("scene_id", sceneID);

            using (UnityWebRequest request = UnityWebRequest.Post(url, form))
            {
                var operation = request.SendWebRequest();
                
                while (!operation.isDone)
                    await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Transform update failed: {request.error}");
                    Debug.LogError($"Response: {request.downloadHandler.text}");
                }
                else
                {
                    Debug.Log($"Transform updated successfully. Response: {request.downloadHandler.text}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception during transform update: {ex.Message}");
        }
    }
    private string ConvertMatrixToJson(Matrix4x4 matrix)
    {
        // Convert Matrix4x4 to float[4][4]
        float[][] matrixArray = new float[4][];
        for (int i = 0; i < 4; i++)
        {
            matrixArray[i] = new float[4];
            for (int j = 0; j < 4; j++)
            {
                matrixArray[i][j] = matrix[i, j];
            }
        }

        // Serialize JUST the array without wrapper
        return JsonConvert.SerializeObject(matrixArray);
    }


    
}