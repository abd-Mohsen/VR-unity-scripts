using System;
using System.Net;
using System.Threading;
using UnityEngine;

public class ObjectControllerServer : MonoBehaviour
{
    private GameObject selectedObject; // The object to control
    private HttpListener httpListener; // HTTP server
    private Thread serverThread; // Thread for the server
    private Vector3 movementDirection = Vector3.zero; // Direction to move the object
    public float moveSpeed = 1f; // Movement speed

    void Start()
    {
        // Get the first GameObject in the scene as the selected object
        if (transform.childCount > 0)
        {
            selectedObject = transform.GetChild(0).gameObject;
        }

        if (selectedObject == null)
        {
            Debug.LogError("No GameObject found to control!");
            return;
        }

        // Start the HTTP server
        StartServer();
    }

    void Update()
    {
        // Move the object based on the movement direction
        if (selectedObject != null)
        {
            selectedObject.transform.Translate(movementDirection * moveSpeed * Time.deltaTime);
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
                    Debug.LogError($"Server error: {ex.Message}");
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
}
