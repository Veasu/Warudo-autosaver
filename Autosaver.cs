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
    Id = "c6ca6224-bedd-4c4b-b7d1-f51dad80fc56",
    Title = "Autosaver",
    Category ="AutoSave")]
public class Autosaver : Node {

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

    [FlowOutput]
    public Continuation OnAutosave;
    private float nextSave = 0; 

    private WebSocket _ws;

    [DataInput]
    [Hidden]
    [Disabled]
    private string websocketPasswordHidden = "";

    private bool shouldBroadcast = false;

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
        };

        _ws.OnError += (sender, e) => {
          websocketConnected = false;
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
          shouldBroadcast = true;
          break;
        case 5:
          if (message.d.eventType.Equals("StreamStateChanged")) {
            isEnabled = !message.d.eventData.outputActive;
            shouldBroadcast = true;
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
      _ws = new WebSocket($"ws://{websocketIP}:{websocketPort}");
      setupWebsocketHandlers();

      if (websocketEnabled){
        _ws.Connect();
      }

      Watch<string>(nameof(websocketIP), (from, to) => {
        if (debouncedSocketIP == null) {
          debouncedSocketIP = Warudo.Core.Utils.Debouncer.Create<string>(it => {
            if (_ws.IsAlive) {
              _ws.Close();
            } 
            _ws = new WebSocket($"ws://{it}:{websocketPort}");
            _ws.Connect();
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
            _ws.Connect();
            setupWebsocketHandlers();
          }, TimeSpan.FromSeconds(1));
        }
        debouncedSocketPort(to);
      });

      Watch<bool>(nameof(isEnabled), (from, to) => {
        shouldBroadcast = true;
      });

      Watch<bool>(nameof(websocketConnected), (from, to) => {
        shouldBroadcast = true;
      });

      Watch<bool>(nameof(websocketEnabled), (from, to) => {
        if (to) {
          _ws.Connect();
        } else {
          _ws.Close();
        }
      });

      Watch<string>(nameof(websocketPassword), (from, to) => {
        if (to.Length == 0) {
          websocketPasswordHidden = "";
        }
        else if (to.Length > websocketPasswordHidden.Length) {
          websocketPassword = new System.String('*', to.Length);
          websocketPasswordHidden += to[to.Length - 1];
        } else {
          websocketPasswordHidden = websocketPasswordHidden.Substring(0, to.Length);
        }
        if (_ws.IsAlive) {
          _ws.Close();
        }
        _ws.Connect();
        shouldBroadcast = true;
      });

      Watch<float>(nameof(saveTimeInSeconds), (from, to) => {
        if (to < 1.0f) {
          to = 1.0f;
          saveTimeInSeconds = to;
        } 

        float diff = from - to;
        nextSave -= diff;
        shouldBroadcast = true;
      });
    }

    public override void OnUpdate() {
      if (Time.time > nextSave && isEnabled && !disableOnStartStream)
      {
          nextSave = Time.time + saveTimeInSeconds;
          AutoSave();
      }
      if (shouldBroadcast) {
        this.Broadcast();
        shouldBroadcast = false;
      }
    }

    protected override void OnDestroy() {
      if (_ws.IsAlive) {
        _ws.Close();
      }
    }
}