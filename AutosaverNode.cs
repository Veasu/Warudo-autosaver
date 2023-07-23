using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Core.Graphs;
using Warudo.Core.Events;
using Event = Warudo.Core.Events.Event;
using Context = Warudo.Core.Context;
using Newtonsoft.Json;
using Warudo.Scripts.Warudo.Core.Events;
using WebSocketSharp;
using System;
using System.Text;
using System.Security.Cryptography;

public class OpMessage{
  public int op { get; set;}
  public OpData d { get; set;}
}

public class OpData {
  public string obsWebSocketVersion { get; set;}
  public int rpcVersion  { get; set;}

  public OpAuth authentication { get; set;}
  public string negotiatedRpcVersion { get; set;}
  public string eventType{ get; set;}
  public int eventIntent { get; set;}
  public OpEventData eventData { get; set;}
}

public class OpAuth {
  public string challenge { get; set;}
  public string salt { get; set;}
}

public class OpEventData {
  public bool outputActive{ get; set;}
  public string outputPath { get; set;}
  public string outputState { get; set;}

}

[NodeType(
    Id = "com.veasu.autosaver",
    Title = "Autosaver",
    Category ="AutoSave")]
public class AutosaverNode  : Node {

    public new AutosaverPlugin Plugin => base.Plugin as AutosaverPlugin;

    [DataInput]
    [Label("Enabled")]
    public bool isEnabled = true;

    [DataInput]
    [Label("Time To Autosave (In Seconds)")]
    public float saveTimeInSeconds = 60.0f * 2.0f;

    [DataInput]
    [Label("Enable OBS Websocket")]
    public bool websocketEnabled = false;

    [DataInput]
    [HiddenIf(nameof(websocketEnabled), Is.False)]
    [Label("Use Default OBS Websocket Connection")]
    public bool defaultWebsocketConnection = true;

    [DataInput]
    [HiddenIf(nameof(hideWebsocketConnection))]
    [Label("Websocket IP")]
    public string websocketIP = "localhost";

    [DataInput]
    [HiddenIf(nameof(hideWebsocketConnection))]
    [Label("Websocket Port")]
    public string websocketPort = "4455";

    [DataInput]
    [HiddenIf(nameof(websocketEnabled), Is.False)]
    [Label("Use OBS Websocket Auth")]
    public bool websocketAuth = false;

    [DataInput]
    [HiddenIf(nameof(hideWebsocketAuth))]
    [Label("Websocket Password")]
    public string websocketPassword = "";

    [DataInput]
    [HiddenIf(nameof(websocketEnabled), Is.False)]
    [Label("Disable Autosave While Streaming")]
    public bool disableOnStartStream = false;

    [Trigger]
    [HiddenIf(nameof(hideConnectToWebsocket))]
    [Label("Connect To Websocket")]
    public void connectToWebsocket() {
      if (_ws.IsAlive){
        _ws.Close();
      }
      _ws.Connect();
    }

    [Trigger]
    [HiddenIf(nameof(hideDisconnectWebsocket))]
    [Label("Disconnect Websocket")]
    public void disconnectWebsocket() {
      if (_ws.IsAlive){
        _ws.Close();
      }
    }

    [DataInput]
    [HiddenIf(nameof(websocketEnabled), Is.False)]
    [Disabled]
    [Label("Websocket Connected")]
    public bool websocketConnected = false;

    [DataInput]
    [Label("Disable Toast Popup")]
    public bool disableToast = false;

    protected bool hideWebsocketAuth() {
      if (websocketEnabled) {
        return !websocketAuth;
      }
      return true;
    }

    protected bool hideWebsocketConnection() {
      if (websocketEnabled) {
        return defaultWebsocketConnection;
      }
      return true;
    }

    protected bool hideDisconnectWebsocket() {
      if (websocketEnabled) {
        return !websocketConnected;
      }
      return true;
    }

    protected bool hideConnectToWebsocket() {
      if (websocketEnabled) {
        return websocketConnected;
      }
      return true;
    }

    [FlowOutput]
    public Continuation OnAutosave;

    [FlowInput]
    public Continuation Enter() {
      AutoSave();
      return OnAutosave;
    }

    private float nextSave = 0; 

    private WebSocket _ws;

    [DataInput]
    [Hidden]
    [Disabled]
    private string websocketPasswordHidden = "";

    private Action<string> debouncedSocketIP;
    private Action<string> debouncedSocketPort;

    private void setupWebsocketHandlers () {
      if (_ws != null) {
        _ws.OnMessage += (sender, e) =>
        {
          OpMessage message = JsonConvert.DeserializeObject<OpMessage>(e.Data);
          handleMessage(message);
        };

        _ws.OnClose += (sender, e) => {
          websocketConnected = false;
          this.BroadcastDataInput(nameof(websocketConnected));
        };

        _ws.OnError += (sender, e) => {
          websocketConnected = false;
          this.BroadcastDataInput(nameof(websocketConnected));
        };
      }
    }

    public static string HashEncode(string input)
    {
      using var sha256 = new SHA256Managed();
      byte[] textBytes = Encoding.ASCII.GetBytes(input);
      byte[] hash = sha256.ComputeHash(textBytes);
      return System.Convert.ToBase64String(hash);
    }

    public string Base64Encode(string plainText) 
    {
      return System.Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
    }

    private void handleMessage(OpMessage message) {
      switch (message.op) {
        case 0 :
          if (message.d.authentication != null) {
            string saltyPass = HashEncode(websocketPasswordHidden + message.d.authentication.salt);
            string authString = HashEncode(saltyPass + message.d.authentication.challenge);
            _ws.Send ("{\"op\":1,\"d\":{\"rpcVersion\": 1, \"authentication\":\"" + authString + "\",\"eventSubscriptions\":64}}");
          } else {
            _ws.Send ("{\"op\":1,\"d\":{\"rpcVersion\": 1, \"eventSubscriptions\":64}}");
          }
          break;
        case 2: 
          websocketConnected = true;
          this.BroadcastDataInput(nameof(websocketConnected));
          break;
        case 5:
          if (message.d.eventType.Equals("StreamStateChanged")) {
            isEnabled = !message.d.eventData.outputActive;
            this.BroadcastDataInput(nameof(isEnabled));
          }
          break;
      }
    }

    private async void AutoSave()
    {
      var serializedScene = Context.OpenedScene.Serialize(false);
      var sceneName = Context.OpenedScene.Name;
      if (disableToast) {
        serializedScene.name = sceneName;
        var path = $"Scenes/{sceneName}.json";
        await Context.PersistentDataManager.WriteFileAsync(path, JsonConvert.SerializeObject(serializedScene));
        Context.EventBus.Broadcast(new SceneSaveEvent(sceneName, serializedScene, false));
      }
      else {
        await Context.SceneManager.SaveScene(sceneName, serializedScene);
      }
      InvokeFlow(nameof(OnAutosave));
    }

    protected override void OnCreate()
    {
      websocketConnected = false;
      this.BroadcastDataInput(nameof(websocketConnected));
      _ws = new WebSocket($"ws://{websocketIP}:{websocketPort}");
      setupWebsocketHandlers();

      Watch<string>(nameof(websocketIP), (from, to) => {
        if (debouncedSocketIP == null) {
          debouncedSocketIP = Warudo.Core.Utils.Debouncer.Create<string>(it => {
            if (_ws.IsAlive) {
              _ws.Close();
            } 
            _ws = new WebSocket($"ws://{it}:{websocketPort}");
            setupWebsocketHandlers();
          }, TimeSpan.FromSeconds(1));
        }
        debouncedSocketIP(to);
      });
        
      Watch<string>(nameof(websocketPort), (from, to) => {
        if (debouncedSocketPort == null) {
          debouncedSocketPort = Warudo.Core.Utils.Debouncer.Create<string>(it => {
            if (_ws.IsAlive) {
              _ws.Close();
            }
            _ws = new WebSocket($"ws://{websocketIP}:{it}");
            setupWebsocketHandlers();
          }, TimeSpan.FromSeconds(1));
        }
        debouncedSocketPort(to);
      });

      Watch<bool>(nameof(websocketEnabled), (from, to) => {
        if (!to) {
          _ws.Close();
        }
      });

      Watch<string>(nameof(websocketPassword), (from, to) => {
        websocketPassword = new System.String('*', to.Length);
        if (to.Length == 0) {
          websocketPasswordHidden = "";
        }
        else if ((to.Length - websocketPasswordHidden.Length) == 1) {
          websocketPasswordHidden += to[to.Length - 1];
        } else if ((to.Length - websocketPasswordHidden.Length) > 1) {
          websocketPasswordHidden += to.Substring(0, to.Length);
        } else {
          websocketPasswordHidden = websocketPasswordHidden.Substring(0, to.Length);
        }
        this.BroadcastDataInput(nameof(websocketPassword));
      });

      Watch<float>(nameof(saveTimeInSeconds), (from, to) => {
        if (to < 1.0f) {
          to = 1.0f;
          saveTimeInSeconds = to;
        } 
        float diff = from - to;
        nextSave -= diff;
        this.BroadcastDataInput(nameof(saveTimeInSeconds));
      });
    }

    public override void OnUpdate() {
      if (Time.time > nextSave && isEnabled && !disableOnStartStream)
      {
          nextSave = Time.time + saveTimeInSeconds;
          AutoSave();
      }
    }

    protected override void OnDestroy() {
      if (_ws.IsAlive) {
        _ws.Close();
      }
    }
}