using System;
using System.Collections.Generic;
using System.Linq;
using uLink.MasterServer;
using UnityEngine;

public class Server : MonoBehaviour
{
    public GameMode CurrentGameMode { get; private set; }
    public PlayerPresence BasePlayerPresence;
    public PlayerScript PlayerPrefab;

    public List<uLink.NetworkPlayer> NetworkPlayers { get; private set; }
    public IEnumerable<PlayerPresence> Presences { get { return PlayerPresence.AllPlayerPresences; }}
    public IEnumerable<PlayerPresence> Combatants { get
    {
        return PlayerPresence.AllPlayerPresences.Where(p => !p.IsSpectating);
    } } 

    public GameMode DefaultGameMode;

    // Only available on server owner
    public ExternalServerNotifier ServerNotifier { get; private set; }
    // Only available on server owner
    private PortMapper PortMapper;

    private bool WasMine = false;

    public Leaderboard Leaderboard { get; private set; }

    private bool _IsGameActive;
    public bool IsGameActive
    {
        get { return _IsGameActive; }
        set
        {
            _IsGameActive = value;
        }
    }

    public delegate void StatusMessageChangedHandler();
    public event StatusMessageChangedHandler OnStatusMessageChange = delegate {};

    public string StatusMessage
    {
        get { return _StatusMessage; }
        set
        {
            if (_StatusMessage != value)
            {
                _StatusMessage = value;
                if (GetComponent<uLink.NetworkView>().isMine)
                    UpdateStatusMessage();
                OnStatusMessageChange();
            }
        }
    }

    private GUIStyle BannerStyle;

    private void UpdateStatusMessage()
    {
        GetComponent<uLink.NetworkView>().RPC("RemoteReceiveStatusMessage", uLink.RPCMode.Others, StatusMessage);
    }

    private void RequestStatusMessageFromRemote()
    {
        GetComponent<uLink.NetworkView>().RPC("OwnerReceiveRemoteWantsStatusMessage", GetComponent<uLink.NetworkView>().owner);
    }

    [RPC]
// ReSharper disable once UnusedMember.Local
    protected void OwnerReceiveRemoteWantsStatusMessage(uLink.NetworkMessageInfo info)
    {
        if (GetComponent<uLink.NetworkView>().isMine)
        {
            GetComponent<uLink.NetworkView>().RPC("RemoteReceiveStatusMessage", info.sender, StatusMessage);
        }
    }

    [RPC]
// ReSharper disable once UnusedMember.Local
    protected void RemoteReceiveStatusMessage(string newStatusMessage, uLink.NetworkMessageInfo info)
    {
        if (info.sender == GetComponent<uLink.NetworkView>().owner)
            StatusMessage = newStatusMessage;
    }


    // Map name stuff
    private string _CurrentMapName;
    private string _StatusMessage;

    public string CurrentMapName
    {
        get { return _CurrentMapName; }
        private set
        {
            _CurrentMapName = value;
            if (GetComponent<uLink.NetworkView>().isMine)
            {
                GetComponent<uLink.NetworkView>().RPC("RemoteReceiveMapName", uLink.RPCMode.Others, _CurrentMapName);
            }
            if (Application.CanStreamedLevelBeLoaded(_CurrentMapName))
            {
                Application.LoadLevel(_CurrentMapName);
                uLink.Network.isMessageQueueRunning = false;
            }
        }
    }

    [RPC]
// ReSharper disable once UnusedMember.Local
    protected void RequestedMapNameFromRemote(uLink.NetworkMessageInfo info)
    {
        GetComponent<uLink.NetworkView>().RPC("RemoteReceiveMapName", info.sender, CurrentMapName);
    }

    [RPC]
// ReSharper disable once UnusedMember.Local
    protected void RemoteReceiveMapName(string mapName, uLink.NetworkMessageInfo info)
    {
        if (GetComponent<uLink.NetworkView>().owner == info.sender)
            CurrentMapName = mapName;
    }

    public void Awake()
    {
        DontDestroyOnLoad(this);
        NetworkPlayers = new List<uLink.NetworkPlayer>();

        ServerNotifier = new ExternalServerNotifier();

        Leaderboard = new Leaderboard();
        Leaderboard.Skin = Relay.Instance.BaseSkin;

        StatusMessage = "?";

        BannerMessages = new List<BannerMessage>();

        if (GetComponent<uLink.NetworkView>().isMine)
        {
            _CurrentMapName = Application.loadedLevelName;
            IsGameActive = false;
            // Set up hooks for UPnP
            PortMapper = new PortMapper(Relay.Port);
        }
    }

    public void uLink_OnNetworkInstantiate(uLink.NetworkMessageInfo info)
    {
        if (!GetComponent<uLink.NetworkView>().isMine)
        {
            RequestStatusMessageFromRemote();
        }
    }

    public void Start()
    {
        Relay.Instance.CurrentServer = this;
        PlayerPresence.OnPlayerPresenceAdded += ReceivePlayerPresenceAdd;
        PlayerPresence.OnPlayerPresenceRemoved += ReceivePlayerPresenceRemove;
        SpawnPresence();
        if (GetComponent<uLink.NetworkView>().isMine)
        {
            WasMine = true;
            uLink_OnPlayerConnected(uLink.Network.player);
            CurrentGameMode = (GameMode) Instantiate(DefaultGameMode, Vector3.zero, Quaternion.identity);
            CurrentGameMode.Server = this;
            ServerNotifier.Name = PlayerPrefs.GetString("username", "Anonymous") + "'s server";

            Relay.Instance.OptionsMenu.ListOfMaps = Relay.Instance.ListedMaps.Where(Application.CanStreamedLevelBeLoaded).ToList();

            PortMapper.ShouldMapNatDevices = true;
        }

        // I guess this check is redundant?
        if (!GetComponent<uLink.NetworkView>().isMine)
        {
            GetComponent<uLink.NetworkView>().RPC("RequestedMapNameFromRemote", GetComponent<uLink.NetworkView>().owner);
            // Get our external address
        }

        BannerStyle = Relay.Instance.BaseSkin.customStyles[3];
        Relay.Instance.MessageLog.OnCommandEntered += ReceiveCommandEntered;
    }

    private void ReceivePlayerPresenceAdd(PlayerPresence newPlayerPresence)
    {
        newPlayerPresence.Server = this;
        if (newPlayerPresence.HasBeenNamed)
            ReceivePlayerPresenceIdentified(newPlayerPresence);
        else
            newPlayerPresence.OnBecameNamed += () =>
                ReceivePlayerPresenceIdentified(newPlayerPresence);
    }

    private void ReceivePlayerPresenceRemove(PlayerPresence removedPlayerPresence)
    {
        BroadcastMessageFromServer(removedPlayerPresence.Name + " has left");
    }

    private void ReceivePlayerPresenceIdentified(PlayerPresence playerPresence)
    {
        BroadcastMessageFromServer(playerPresence.Name + " has joined");
    }

// ReSharper disable once UnusedMethodReturnValue.Local
    private PlayerPresence SpawnPresence()
    {
        var presence = (PlayerPresence) uLink.Network.Instantiate(BasePlayerPresence, Vector3.zero, Quaternion.identity, 0);
        return presence;
    }

    public void uLink_OnSerializeNetworkView(uLink.BitStream stream, uLink.NetworkMessageInfo info)
    {
        stream.Serialize(ref _IsGameActive);
    }

    public void Update()
    {
        if (GetComponent<uLink.NetworkView>().isMine)
        {
            ServerNotifier.GUID = Relay.Instance.AddressFinder.ExternalAddress;
            ServerNotifier.CurrentMapName = Application.loadedLevelName;
            ServerNotifier.NumberOfPlayers = PlayerPresence.UnsafeAllPlayerPresences.Count;
            // In case our external address is still null, Update() in the
			// ServerNotifier won't actually send to the list server, so it's
			// fine to call it here. (It will send as soon as our address is not
			// null).
            ServerNotifier.Update();
        }
        Leaderboard.Update();

        // Update banner messages
        float yAccum = 0f;
        for (int i = BannerMessages.Count - 1; i >= 0; i--)
        {
            //BannerMessages[i].Index = displayIndex;
            BannerMessages[i].IndexY = yAccum;
            BannerMessages[i].Update();
            if (BannerMessages[i].Active)
                yAccum += BannerMessages[i].CalculatedHeight + 1f;
            if (BannerMessages[i].Finished)
                BannerMessages.RemoveAt(i);
        }
    }

    // Only called by Unity on server
    public void uLink_OnPlayerConnected(uLink.NetworkPlayer player)
    {
        // Explictly add players to the Character group ID, so that they can
		// still see what happens even if they don't have a character currently
		// spawned.
        if (!player.isServer)
            uLink.Network.AddPlayerToGroup(player, Relay.CharacterSpawnGroupID);

        NetworkPlayers.Add(player);
    }

    // Only called by Unity on server
    public void uLink_OnPlayerDisconnected(uLink.NetworkPlayer player)
    {
        uLink.Network.RemoveRPCs(player);
        uLink.Network.DestroyPlayerObjects(player);
        NetworkPlayers.Remove(player);
    }

    public void OnLevelWasLoaded(int level)
    {
        uLink.Network.isMessageQueueRunning = true;
        if (GetComponent<uLink.NetworkView>().isMine)
        {
            CurrentGameMode.ReceiveMapChanged();
        }
    }

    public void OnDestroy()
    {
        PlayerPresence.OnPlayerPresenceAdded -= ReceivePlayerPresenceAdd;
        PlayerPresence.OnPlayerPresenceRemoved -= ReceivePlayerPresenceRemove;
        Relay.Instance.MessageLog.OnCommandEntered -= ReceiveCommandEntered;

        if (CurrentGameMode != null)
        {
            Destroy(CurrentGameMode.gameObject);
        }
        // TODO not sure what to do about this stuff now
        foreach (var playerPresence in PlayerPresence.AllPlayerPresences)
        {
            Destroy(playerPresence.gameObject);
        }
        foreach (var playerScript in PlayerScript.AllEnabledPlayerScripts)
        {
            Destroy(playerScript.gameObject);
        }
        if (Relay.Instance.CurrentServer == this)
        {
            Relay.Instance.CurrentServer = null;
        }

        // FIXME dirty hack
        {
            Screen.lockCursor = false;
            Screen.showCursor = true;
            if (Relay.Instance.MainCamera != null)
            {
                var indicator = Relay.Instance.MainCamera.GetComponent<WeaponIndicatorScript>();
                if (indicator != null)
                    indicator.enabled = false;
            }
        }


        // Will need to not dispose this if we're going to delist our server,
		// since we'll have to hand this off to the Relay for ownership.
        // TODO bit o' the old hack 'ere
        if (WasMine)
        {
            ServerNotifier.BecomeUnlisted();
        }

        // Remove hooks for UPnP
        if (PortMapper != null)
            PortMapper.Dispose();

        //ServerNotifier.Dispose();
    }

    public delegate void ReceiveServerMessageHandler(string text);

    public event ReceiveServerMessageHandler OnReceiveServerMessage = delegate {};

    public static int DefaultMessageType = 0;
    public static int ChatMessageType = 1;
    public static int BannerMessageType = 2;
    public static int BannerMessageWithSoundType = 3;

    public void BroadcastMessageFromServer(string text)
    {
        BroadcastMessageFromServer(text, DefaultMessageType);
    }

    public void BroadcastMessageFromServer(string text, int messageType)
    {
        if (GetComponent<uLink.NetworkView>().isMine)
        {
            GetComponent<uLink.NetworkView>().RPC("ReceiveServerMessage", uLink.RPCMode.Others, text, messageType);

            HandleMessage(text, messageType);
            OnReceiveServerMessage(text);
        }
    }

    public void SendMessageFromServer(string text, uLink.NetworkPlayer target)
    {
        if (GetComponent<uLink.NetworkView>().isMine)
        {
            GetComponent<uLink.NetworkView>().RPC("ReceiveServerMessage", target, text, DefaultMessageType);
        }
    }

    private void HandleMessage(string text, int messageType)
    {
        if (messageType == ChatMessageType)
            GlobalSoundsScript.PlayChatMessageSound();
        else if (messageType == BannerMessageType)
        {
            BannerMessages.Add(new BannerMessage(text.ToUpper(), BannerStyle));
        }
        else if (messageType == BannerMessageWithSoundType)
        {
            GlobalSoundsScript.PlayServerMessageSound();
            BannerMessages.Add(new BannerMessage(text.ToUpper(), BannerStyle));
        }
    }

    private void ReceiveCommandEntered(string command, string[] args)
    {
        switch (command)
        {
            case "announce":
                if (args.Length < 1) break;
                if (GetComponent<uLink.NetworkView>().isMine)
                    BroadcastMessageFromServer(String.Join(" ", args), BannerMessageType);
            break;
        }
    }


    [RPC]
// ReSharper disable once UnusedMember.Local
    protected void ReceiveServerMessage(string text, int messageType, uLink.NetworkMessageInfo info)
    {
        // Only care about messages from server
        if (info.sender != GetComponent<uLink.NetworkView>().owner) return;

        HandleMessage(text, messageType);
        OnReceiveServerMessage(text);
    }

    public void BroadcastChatMessageFromServer(string text, PlayerPresence playerPresence)
    {
        bool isHost = playerPresence.GetComponent<uLink.NetworkView>().owner == GetComponent<uLink.NetworkView>().owner;
        string sigil = isHost ? "+ " : "";
        BroadcastMessageFromServer(sigil + playerPresence.Name + " : " + text, ChatMessageType);
    }

    public void ChangeLevel(string levelName)
    {
        if (Application.loadedLevelName != levelName &&
            Application.CanStreamedLevelBeLoaded(levelName))
        {
            ServerNotifier.CurrentMapName = levelName;
            CurrentMapName = levelName;
        }
    }

    // I have no idea if this actually works reliably or not, but it seems to
	// work in my testing. Semi-hack? I guess I could stall the application
	// until we know it's worked, but that's kind of jerky, too.
    public void OnApplicationQuit()
    {
        if (WasMine)
        {
            ServerNotifier.BecomeUnlisted();
        }
    }

    public void OnGUI()
    {
        Leaderboard.DrawGUI();

        for (int i = 0; i < BannerMessages.Count; i++)
            BannerMessages[i].OnGUI();
    }

    private List<BannerMessage> BannerMessages; 

    private class BannerMessage
    {
        public readonly string Text;
        public readonly GUIStyle Style;

        public float CalculatedHeight;

        public float IndexY;
        private float CurrentY = -100f;

        public float DesiredY
        {
            get { return Age >= Lifetime ? -100f : IndexY; }
        }

        public float Age = 0f;
        public float Lifetime = 4f;

        public int NumberOfLines
        { get { return Text.Split('\n').Length; } }

        public BannerMessage(string text, GUIStyle style)
        {
            Text = text;
            Style = style;
            CalculatedHeight = NumberOfLines * 22 + Style.padding.top + Style.padding.bottom;
        }

        public void Update()
        {
            CurrentY = Mathf.Lerp(CurrentY, DesiredY, 1f - Mathf.Pow(0.00001f, Time.deltaTime));
            Age += Time.deltaTime;
        }

        public bool Finished { get { return Age >= Lifetime && Mathf.Abs(CurrentY - DesiredY) < 0.01f; } }
        public bool Active { get { return Age < Lifetime; } }

        public void OnGUI()
        {
            var r = new Rect(35, 35 + CurrentY, Screen.width - 35 * 2, 100);
            GUILayout.BeginArea(r);
            GUILayout.BeginHorizontal();
            GUILayout.Space(350);
            GUILayout.Label(Text, Style);
            GUILayout.Space(350);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}