using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using GLTFast;

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
        await LoadAllGLBModels();

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
    private async Task LoadAllGLBModels()
    {
        // Check if folder exists
        if (!Directory.Exists(modelsFolderPath))
        {
            Debug.LogError($"The folder '{modelsFolderPath}' does not exist!");
            return;
        }

        // Get all .glb files in the folder
        string[] glbFiles = Directory.GetFiles(modelsFolderPath, "*.glb");

        if (glbFiles.Length == 0)
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
        foreach (string glbFile in glbFiles)
        {
            loadTasks.Add(LoadGLBModelAsync(glbFile));
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
        else
        {
            Debug.LogError($"Failed to load model: {glbFilePath}");
            return null;
        }
    }
}
