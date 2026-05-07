using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace GameTime;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;
    internal static volatile string CurrentJson = "{\"missionActive\":false}";

    static TcpListener _listener;
    static Thread _serverThread;
    static volatile bool _running;
    const int Port = 1941;

    public override void Load()
    {
        Log = base.Log;
        AddComponent<TimeWatcher>();
        StartServer();
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded! (port {Port})");
    }

    static void StartServer()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            _running = true;
            _serverThread = new Thread(ServerLoop) { IsBackground = true, Name = "GT-GameTime" };
            _serverThread.Start();
            Log.LogInfo($"[GameTime] HTTP server listening on http://127.0.0.1:{Port}/time");
        }
        catch (Exception ex)
        {
            Log.LogError("[GameTime] Failed to start server: " + ex.Message);
        }
    }

    static void ServerLoop()
    {
        while (_running)
        {
            try
            {
                var client = _listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
            }
            catch (SocketException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { Log.LogWarning("[GameTime] Loop: " + ex.Message); }
        }
    }

    static void HandleClient(TcpClient client)
    {
        try
        {
            client.ReceiveTimeout = 2000;
            client.SendTimeout = 2000;
            using (client)
            using (var stream = client.GetStream())
            {
                byte[] buf = new byte[2048];
                int read = stream.Read(buf, 0, buf.Length);
                string req = read > 0 ? Encoding.ASCII.GetString(buf, 0, read) : "";
                string path = "/";
                int firstSpace = req.IndexOf(' ');
                if (firstSpace >= 0)
                {
                    int second = req.IndexOf(' ', firstSpace + 1);
                    if (second > firstSpace)
                    {
                        path = req.Substring(firstSpace + 1, second - firstSpace - 1).Split('?')[0];
                    }
                }

                string body;
                string status;
                if (path == "/time" || path == "/")
                {
                    body = CurrentJson;
                    status = "200 OK";
                }
                else
                {
                    body = "{\"error\":\"not found\"}";
                    status = "404 Not Found";
                }

                byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                string headers = "HTTP/1.1 " + status + "\r\n"
                    + "Content-Type: application/json; charset=utf-8\r\n"
                    + "Content-Length: " + bodyBytes.Length + "\r\n"
                    + "Access-Control-Allow-Origin: *\r\n"
                    + "Cache-Control: no-store\r\n"
                    + "Connection: close\r\n"
                    + "\r\n";
                byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
                stream.Write(headerBytes, 0, headerBytes.Length);
                stream.Write(bodyBytes, 0, bodyBytes.Length);
            }
        }
        catch { }
    }
}

class TimeWatcher : MonoBehaviour
{
    int _tick = 0;

    void Update()
    {
        // Refresh ~10x per second
        _tick++;
        if (_tick < 6) return;
        _tick = 0;

        try
        {
            string time = "??:??:??";
            try
            {
                float t = W_ServerTime.instance.azureTime.get() % 24f;
                int h = (int)t;
                float mf = (t - h) * 60f;
                int m = (int)mf;
                int s = (int)((mf - m) * 60f);
                time = h.ToString("D2") + ":" + m.ToString("D2") + ":" + s.ToString("D2");
            }
            catch { }

            string date = "Unknown";
            try { date = W_GameManager.instance?.lobbyData?.CurrentDate.ToString("dd.MM.yyyy") ?? "Unknown"; }
            catch { }

            string vessel = "";
            bool active = false;
            try
            {
                var gm = W_GameManager.instance;
                if (gm != null)
                {
                    int myCrew = gm.getMyCrew();
                    var ubs = gm.uboats;
                    if (ubs != null && myCrew >= 0 && myCrew < ubs.Length && ubs[myCrew] != null)
                    {
                        try { vessel = ubs[myCrew].GetUboatName() ?? ""; } catch { }
                        active = true;
                    }
                }
            }
            catch { }

            string json = "{"
                + "\"time\":\"" + time + "\","
                + "\"date\":\"" + date + "\","
                + "\"vessel\":\"" + JsonEscape(vessel) + "\","
                + "\"missionActive\":" + (active ? "true" : "false")
                + "}";
            Plugin.CurrentJson = json;
        }
        catch { }
    }

    static string JsonEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u" + ((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
