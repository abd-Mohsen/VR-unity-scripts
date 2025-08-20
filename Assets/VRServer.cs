using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

public class ObjectControllerServer : MonoBehaviour
{
    private HttpListener httpListener;
    private Thread serverThread;
    private bool serverRunning = true;

    // Queue for requests that need main thread processing
    private readonly Queue<HttpListenerContext> requestQueue = new Queue<HttpListenerContext>();
    private readonly object queueLock = new object();

    void Start()
    {
        Debug.Log("Starting HTTP server...");
        StartServer();
    }

    private void StartServer()
    {
        try
        {
            httpListener = new HttpListener();
            httpListener.Prefixes.Add("http://127.0.0.1:53148/");
            httpListener.Start();

            serverThread = new Thread(ListenForRequests);
            serverThread.IsBackground = true;
            serverThread.Start();

            Debug.Log("HTTP server started on http://127.0.0.1:53148/");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to start server: {ex.Message}");
        }
    }

    private void ListenForRequests()
    {
        while (serverRunning && httpListener != null && httpListener.IsListening)
        {
            try
            {
                var context = httpListener.GetContext();
                
                // For /notify endpoint, we need to process on main thread
                if (context.Request.Url.AbsolutePath == "/notify")
                {
                    lock (queueLock)
                    {
                        requestQueue.Enqueue(context);
                    }
                }
                else
                {
                    // For other requests, handle immediately on background thread
                    ThreadPool.QueueUserWorkItem(HandleSimpleRequest, context);
                }
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Server error: {ex.Message}");
            }
        }
    }

    void Update()
    {
        // Process queued requests on the main thread
        ProcessQueuedRequests();
    }

    private void ProcessQueuedRequests()
    {
        lock (queueLock)
        {
            while (requestQueue.Count > 0)
            {
                var context = requestQueue.Dequeue();
                HandleNotifyRequest(context);
            }
        }
    }

    private void HandleNotifyRequest(HttpListenerContext context)
    {
        try
        {
            Debug.Log("notified 🔔 - Processing on main thread");

            // Now we're on the main thread - safe to use Unity APIs
            LoadModelsController lMC = FindObjectOfType<LoadModelsController>();
            if (lMC != null)
            {
                // Start the async operation
                lMC.LoadModelsFromServer();
            }

            // Send response
            string responseString = "{\"status\": \"success\", \"message\": \"Unity notification received and processing\"}";
            byte[] buffer = Encoding.UTF8.GetBytes(responseString);
            
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.AddHeader("Access-Control-Allow-Origin", "*");
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Notify request error: {ex.Message}");
            SendErrorResponse(context, 500, $"Internal server error: {ex.Message}");
        }
        finally
        {
            context.Response.OutputStream.Close();
        }
    }

    private void HandleSimpleRequest(object state)
    {
        HttpListenerContext context = (HttpListenerContext)state;
        
        try
        {
            // Handle OPTIONS for CORS preflight
            if (context.Request.HttpMethod == "OPTIONS")
            {
                context.Response.StatusCode = 200;
                context.Response.AddHeader("Access-Control-Allow-Origin", "*");
                context.Response.AddHeader("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE");
                context.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type");
                context.Response.OutputStream.Close();
                return;
            }

            // Handle unknown endpoints
            SendErrorResponse(context, 404, "Endpoint not found");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Simple request error: {ex.Message}");
            SendErrorResponse(context, 500, $"Internal server error: {ex.Message}");
        }
        finally
        {
            context.Response.OutputStream.Close();
        }
    }

    private void SendErrorResponse(HttpListenerContext context, int statusCode, string message)
    {
        try
        {
            string errorJson = $"{{\"error\": \"{message}\"}}";
            byte[] buffer = Encoding.UTF8.GetBytes(errorJson);
            
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            context.Response.AddHeader("Access-Control-Allow-Origin", "*");
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        catch
        {
            // Ignore errors during error response
        }
    }

    private void OnApplicationQuit()
    {
        StopServer();
    }

    private void OnDestroy()
    {
        StopServer();
    }

    private void StopServer()
    {
        serverRunning = false;
        
        if (httpListener != null)
        {
            if (httpListener.IsListening)
            {
                httpListener.Stop();
            }
            httpListener.Close();
        }

        if (serverThread != null && serverThread.IsAlive)
        {
            serverThread.Join(1000);
        }
    }
}