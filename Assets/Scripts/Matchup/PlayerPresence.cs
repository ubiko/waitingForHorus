using System;
using System.Collections.Generic;
using System.Linq;
using Com.EpixCode.Util.WeakReference.WeakDictionary;
using UnityEngine;

public class PlayerPresence : uLink.MonoBehaviour
{
    public Server Server { get; set; }

    private string _Name = "Nameless";
    public string Name {
        get
        {
            return _Name;
        }
        private set
        {
            ReceiveNameSent(value);
            if (networkView.isMine)
            {
                if (Relay.Instance.IsConnected)
                {
                    networkView.RPC("ReceiveNameSent", uLink.RPCMode.Others, value);
                }
            }
        }
    }

    public static IEnumerable<PlayerPresence> AllPlayerPresences { get { return UnsafeAllPlayerPresences.ToList(); }}
    public static List<PlayerPresence> UnsafeAllPlayerPresences = new List<PlayerPresence>();

    public delegate void PlayerPresenceExistenceHandler(PlayerPresence newPlayerPresence);
    public static event PlayerPresenceExistenceHandler OnPlayerPresenceAdded = delegate {};
    public static event PlayerPresenceExistenceHandler OnPlayerPresenceRemoved = delegate {};

    public PlayerScript DefaultPlayerCharacterPrefab;

    public uLink.NetworkViewID PossessedCharacterViewID;

    private PlayerScript _Possession;

    private float _Ping;
    public float Ping
    {
        get { return _Ping; }
        private set { _Ping = value; }
    }

    private float _ConnectionQuality;
    public float ConnectionQuality
    {
        get { return _ConnectionQuality; }
        set
        {
            _ConnectionQuality = value;
            if (Server.networkView.isMine)
            {
                networkView.RPC("ReceiveSetConnectionQuality", uLink.RPCMode.Others, value);
            }
        }
    }

    [RPC]
// ReSharper disable once UnusedMember.Local
    protected void ReceiveSetConnectionQuality(float quality, uLink.NetworkMessageInfo info)
    {
        if (Server == null || networkView == null) return;
        if (info.sender == Server.networkView.owner || networkView.isMine)
        {
            _ConnectionQuality = quality;
        }
    }

    public PlayerScript Possession
    {
        get
        {
            return _Possession;
        }
        set
        {
            //var newName = value == null ? "null" : value.name;
            //Debug.Log("Setting presence " + Name + "'s possession to " + newName);

            // Do nothing if same
            if (_Possession == value) return;

            // Not dead yet, but we won't be interested anymore if it dies
            if (_Possession != null)
            {
                _Possession.OnDeath -= ReceivePawnDeath;
                // Hmmm...
                //_Possession.RequestedToDieByOwner(this);
            }

            _Possession = value;
            if (_Possession != null)
            {
                _Possession.Possessor = this;
                _Possession.CameraScript.IsExteriorView = WantsExteriorView;
                if (networkView.isMine)
                {
                    _Possession.CameraScript.BaseFieldOfView = PlayerPrefs.GetFloat("fov",
                        CameraScript.DefaultBaseFieldOfView);
                    _Possession.CameraScript.AdjustCameraFOVInstantly();
                }
                _Possession.OnDeath += ReceivePawnDeath;

                if (_Possession.networkView != null)
                    PossessedCharacterViewID = _Possession.networkView.viewID;

                IsSpectating = false;
            }
        }
    }

    public delegate void PlayerPresenceWantsRespawnHandler();
    public event PlayerPresenceWantsRespawnHandler OnPlayerPresenceWantsRespawn = delegate {};

    private WeakDictionary<PlayerScript, Vector2> LastGUIDebugPositions;

    public bool HasBeenNamed { get { return Name != "Nameless"; } }
    public delegate void NameChangedHandler();

    public event NameChangedHandler OnNameChanged = delegate {}; 
    public event NameChangedHandler OnBecameNamed = delegate {};

    private int _Score;
    public int Score
    {
        get { return _Score; }
        set { _Score = value; }
    }

    private bool wasMine = false;

    public bool WantsExteriorView
    {
        get { return Relay.Instance.OptionsMenu.IsExteriorView; }
        set { Relay.Instance.OptionsMenu.IsExteriorView = value; }
    }

    private bool _IsDoingMenuStuff = false;

    private float TimeToHoldLeaderboardFor = 0f;
    private float DefaultAutoLeaderboardTime = 1.85f;

    // Prevent rapid attempts at respawning. This is to prevent high-latency
	// situations from causing players to respawn multiple times in a row, which
	// is confusing and weird. Ideally we would have smarter logic here than
	// just a timer, but this is a good enough hack for now.
    private const float MinTimeBetweenRespawnRequests = 0.75f;
    private float TimeSinceLastRespawnRequest = MinTimeBetweenRespawnRequests;


    private bool _IsSpectating;

    public bool IsSpectating
    {
        get { return _IsSpectating; }
        set { _IsSpectating = value; }
    }

    private void UpdateShouldCameraSpin()
    {
        if ((Possession == null && IsSpectating) || !Server.IsGameActive)
        {
            CameraSpin.Instance.ShouldSpin = true;
        }
        else
        {
            CameraSpin.Instance.ShouldSpin = false;
        }
    }

    public void DisplayScoreForAWhile()
    {
        TimeToHoldLeaderboardFor = DefaultAutoLeaderboardTime;
    }

    public bool IsDoingMenuStuff
    {
        get { return _IsDoingMenuStuff; }
        set { _IsDoingMenuStuff = value; }
    }

    public void uLink_OnSerializeNetworkView(uLink.BitStream stream, uLink.NetworkMessageInfo info)
    {
        uLink.NetworkViewID prevPossesedID = PossessedCharacterViewID;
        stream.Serialize(ref PossessedCharacterViewID);
        stream.Serialize(ref _IsDoingMenuStuff);
        stream.Serialize(ref _Score);

        stream.Serialize(ref _IsSpectating);
        stream.Serialize(ref _Ping);

        if (stream.isReading)
        {
            if (Possession == null)
            {
                // see if possession id from network is not null
                // see if new possession object from that id is not null
                // then assign
                PlayerScript character = TryGetPlayerScriptFromNetworkViewID(PossessedCharacterViewID);
                if (character != null) Possession = character;
            }
            else
            {
                // see if new possession id is different from current possession id
                // assign new possession, even if null
                if (prevPossesedID != PossessedCharacterViewID)
                {
                    Possession = TryGetPlayerScriptFromNetworkViewID(PossessedCharacterViewID);
                }
            }
        }
    }

    // TODO factor out
    public static PlayerPresence TryGetPlayerPresenceFromNetworkViewID(uLink.NetworkViewID viewID)
    {
        if (viewID == uLink.NetworkViewID.unassigned) return null;
        uLink.NetworkView view = null;
        try
        {
            view = uLink.NetworkView.Find(viewID);
        }
        catch (Exception)
        {
            //Debug.Log(e);
        }
        if (view != null)
        {
            var presence = view.observed as PlayerPresence;
            return presence;
        }
        return null;
    }

    private PlayerScript TryGetPlayerScriptFromNetworkViewID(uLink.NetworkViewID viewID)
    {
        if (viewID == uLink.NetworkViewID.unassigned) return null;
        uLink.NetworkView view = null;
        try
        {
            view = uLink.NetworkView.Find(viewID);
        }
        catch (Exception)
        {
            //Debug.Log(e);
        }
        if (view != null)
        {
            var character = view.observed as PlayerScript;
            return character;
        }
        return null;
    }

    public void Awake()
    {
        DontDestroyOnLoad(this);
        PossessedCharacterViewID = uLink.NetworkViewID.unassigned;
        LastGUIDebugPositions = new WeakDictionary<PlayerScript, Vector2>();

        // Ladies and gentlemen, the great and powerful Unity
        wasMine = networkView.isMine;

        if (networkView.isMine)
        {
            Name = PlayerPrefs.GetString("username", "Anonymous");

            // TODO will obviously send messages to server twice if there are two local players, fix
            Relay.Instance.MessageLog.OnMessageEntered += ReceiveMessageEntered;
            Relay.Instance.MessageLog.OnCommandEntered += ReceiveCommandEntered;
        }

    }

    public void Start()
    {
        UnsafeAllPlayerPresences.Add(this);
        OnPlayerPresenceAdded(this);

        if (networkView.isMine)
        {
            // Hack, should really compare against Server, but don't know if it's possibly null here?
            if (uLink.Network.isServer)
                IsSpectating = false;
            else
                IsSpectating = true;

            Relay.Instance.OptionsMenu.OnOptionsMenuWantsSpectate += OwnerGoSpectate;
        }
    }

    public void OwnerGoJoin()
    {
        if (networkView.isMine)
        {
            if (Possession == null)
                IndicateRespawn();
            IsSpectating = false;
        }
    }

    public void OwnerGoSpectate()
    {
        if (networkView.isMine)
        {
            if (Possession != null)
                Possession.HealthScript.DoDamageOwner(999, Possession.transform.position, this);
            IsSpectating = true;
            Score = 0;
        }
    }

    private void ReceiveMessageEntered(string text)
    {
        BroadcastChatMessageFrom(text);
    }

    private void ReceiveCommandEntered(string command, string[] args)
    {
        switch (command)
        {
            case "join":
                OwnerGoJoin();
            break;
            case "spectate":
                OwnerGoSpectate();
            break;
        }
    }

    private bool ShouldDisplayRespawnNotice
    { get { return Possession == null && !IsSpectating; } }
    private bool ShouldDisplayJoinPanel
    { get { return IsSpectating; } }

    public void Update()
    {
        if (networkView.isMine)
        {
            WeaponIndicatorScript.Instance.ShouldRender = Possession != null;
            UpdateShouldCameraSpin();

            Relay.Instance.OptionsMenu.ShouldDisplaySpectateButton = !IsSpectating;

            if (Possession == null)
            {
                if (Input.GetButtonDown("Fire") && !IsSpectating)
                {
                    IndicateRespawn();
                }
            }

            // Update player labels
            if (Camera.current != null)
            {
                foreach (var playerScript in PlayerScript.UnsafeAllEnabledPlayerScripts)
                {
                    if (playerScript == null) continue;
                    Vector3 position = Camera.current.WorldToScreenPoint(InfoPointForPlayerScript(playerScript));
                    Vector2 prevScreenPosition;
                    if (!LastGUIDebugPositions.TryGetValue(playerScript, out prevScreenPosition))
                    {
                        prevScreenPosition = (Vector2) position;
                    }
                    Vector2 newScreenPosition = Vector2.Lerp(prevScreenPosition, (Vector2) position,
                        1f - Mathf.Pow(0.0000000001f, Time.deltaTime));
                    LastGUIDebugPositions[playerScript] = newScreenPosition;
                }
            }

            IsDoingMenuStuff = Relay.Instance.MessageLog.HasInputOpen;

            // Debug visibility info for other playerscripts
            if (Possession != null)
            {
                foreach (var character in PlayerScript.UnsafeAllEnabledPlayerScripts)
                {
                    if (character != Possession)
                    {
                        bool canSee = Possession.CanSeeOtherPlayer(character);
                        if (canSee)
                        {
                            ScreenSpaceDebug.AddMessageOnce("VISIBLE", character.transform.position);
                        }
                    }
                }
            }

            // Leaderboard show/hide
            // Always show when not possessing anything
            // Never show when already showing options screen
            if (Possession == null || TimeToHoldLeaderboardFor >= 0f)
            {
                Server.Leaderboard.Show = true;
            }
            // Otherwise, show when holding tab
            else
            {
                Server.Leaderboard.Show = Input.GetKey("tab") && !Relay.Instance.ShowOptions;
            }

            TimeToHoldLeaderboardFor -= Time.deltaTime;

            if (!Relay.Instance.ShowOptions && Possession != null && !ShouldDisplayJoinPanel)
                Screen.lockCursor = true;

            if (ShouldDisplayJoinPanel || Relay.Instance.ShowOptions || !Server.IsGameActive)
                Screen.lockCursor = false;

            // Update ping
            Ping = uLink.Network.GetAveragePing(Server.networkView.owner);
        }

        if (Possession != null)
        {
            // toggle bubble
            Possession.TextBubbleVisible = IsDoingMenuStuff;
        }

        if (Input.GetKeyDown("f11"))
            ScreenSpaceDebug.LogMessageSizes();

        TimeSinceLastRespawnRequest += Time.deltaTime;
    }

    private Vector3 InfoPointForPlayerScript(PlayerScript playerScript)
    {
        Vector3 start = playerScript.gameObject.transform.position;
        start.y -= playerScript.Bounds.extents.y;
        return start;
    }

    private void IndicateRespawn()
    {
        // Don't try to respawn if we just did it half a second ago
        if (TimeSinceLastRespawnRequest < MinTimeBetweenRespawnRequests) return;
        TimeSinceLastRespawnRequest = 0f;

        if (uLink.Network.isServer)
            OnPlayerPresenceWantsRespawn();
        else
            networkView.RPC("ServerIndicateRespawn", uLink.RPCMode.Server);
    }

    [RPC]
// ReSharper disable once UnusedMember.Local
    protected void ServerIndicateRespawn(uLink.NetworkMessageInfo info)
    {
        if (info.sender == networkView.owner)
            OnPlayerPresenceWantsRespawn();
    }

    public void OnDestroy()
    {
        OnPlayerPresenceRemoved(this);
        UnsafeAllPlayerPresences.Remove(this);
        if (Possession != null)
        {
            Destroy(Possession.gameObject);
        }

        if (wasMine)
        {
            Relay.Instance.MessageLog.OnMessageEntered -= ReceiveMessageEntered;
            Relay.Instance.MessageLog.OnCommandEntered += ReceiveCommandEntered;
            Relay.Instance.OptionsMenu.OnOptionsMenuWantsSpectate -= OwnerGoSpectate;
        }

    }

    public void SpawnCharacter(Vector3 position)
    {
        if (networkView.isMine)
        {
            DoActualSpawn(position);
        }
        else
        {
            networkView.RPC("RemoteSpawnCharacter", networkView.owner, position);
        }
    }

    // Used by owner of this Presence
    private void DoActualSpawn(Vector3 position)
    {
        // ondestroy will be bound in the setter
        Possession = (PlayerScript)uLink.Network.Instantiate(DefaultPlayerCharacterPrefab, position, Quaternion.identity, Relay.CharacterSpawnGroupID);
    }

    [RPC]
// ReSharper disable once UnusedMember.Local
    protected void RemoteSpawnCharacter(Vector3 position, uLink.NetworkMessageInfo info)
    {
        if (info.sender == Server.networkView.owner)
        {
            if (Possession != null)
            {
                CleanupOldCharacter(Possession);
                Possession = null;
            }
            DoActualSpawn(position);
        }
    }

    private void CleanupOldCharacter(PlayerScript character)
    {
        if (Relay.Instance.IsConnected)
        {
            uLink.Network.RemoveRPCs(character.networkView.owner, Relay.CharacterSpawnGroupID);
            uLink.Network.Destroy(character.gameObject);
        }
        else
        {
            // TODO Don't need to remove RPCs if offline? What if we start the server again ?? Fuck unity
            Destroy(character.gameObject);
        }
    }

    private void ReceivePawnDeath()
    {
        if (Possession != null)
        {
            Possession.OnDeath -= ReceivePawnDeath;
            Possession = null;
            PossessedCharacterViewID = uLink.NetworkViewID.unassigned;
        }
    }

    public void OnGUI()
    {
        if (ScreenSpaceDebug.Instance.ShouldDraw)
        {
            OnDrawDebugStuff();
        }

        // Draw player names
        if (networkView.isMine && Possession != null && Camera.current != null)
        {
            GUI.skin = Relay.Instance.BaseSkin;
            GUIStyle boxStyle = new GUIStyle(Relay.Instance.BaseSkin.customStyles[2])
            {
                fixedWidth = 0,
                fixedHeight = 18,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(5, 5, 3, 3),
            };
            foreach (var character in PlayerScript.UnsafeAllEnabledPlayerScripts)
            {
                if (character == null) continue;
                if (character == Possession) continue;
                if (!Possession.ShootingScript.CharacterIsInTargets(character)) continue;
                Vector3 screenPosition = Camera.current.WorldToScreenPoint(InfoPointForPlayerScript(character));
                screenPosition.y = Screen.height - screenPosition.y;
                if (screenPosition.z < 0) continue;
                bool isVisible = Possession.CanSeeOtherPlayer(character);
                if (!isVisible) continue;
                string otherPlayerName;
                if (character.Possessor == null)
                    otherPlayerName = "?";
                else
                    otherPlayerName = character.Possessor.Name;
                Vector2 baseNameSize = boxStyle.CalcSize(new GUIContent(otherPlayerName));
                float baseNameWidth = baseNameSize.x + 10;
                var rect = new Rect(screenPosition.x - baseNameWidth/2, screenPosition.y, baseNameWidth,
                    boxStyle.fixedHeight);
                GUI.Box(rect, otherPlayerName, boxStyle);
            }
        }

        if (networkView.isMine)
        {
            if (ShouldDisplayJoinPanel)
            {
                GUI.skin = Relay.Instance.BaseSkin;
                GUILayout.Window(Definitions.PlayerGameChoicesWindowID, new Rect( 0, Screen.height - 110, Screen.width, 110), DrawGameChoices, string.Empty);
            }
        }
    }

    private void DrawGameChoices(int id)
    {
        GUIStyle joinStyle = new GUIStyle(Relay.Instance.BaseSkin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fixedWidth = 200,
            padding = new RectOffset(0,0,0,0)
        };
        GUILayout.BeginVertical();
        {
            GUILayout.BeginHorizontal();
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("JOIN", joinStyle))
                    OwnerGoJoin();
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
    }

    private void OnDrawDebugStuff()
    {
        if (networkView.isMine && Camera.current != null)
        {
            GUI.skin = Relay.Instance.BaseSkin;
            GUIStyle boxStyle = new GUIStyle(Relay.Instance.BaseSkin.box) {fixedWidth = 0};
            foreach (var playerScript in PlayerScript.UnsafeAllEnabledPlayerScripts)
            {
                if (playerScript == null) continue;
                Vector3 newScreenPosition = Camera.current.WorldToScreenPoint(InfoPointForPlayerScript(playerScript));
                if (newScreenPosition.z < 0) continue;
                Vector2 screenPosition;
                if (!LastGUIDebugPositions.TryGetValue(playerScript, out screenPosition))
                {
                    screenPosition = newScreenPosition;
                }
                // Good stuff, great going guys
                screenPosition.y = Screen.height - screenPosition.y;
                var rect = new Rect(screenPosition.x - 50, screenPosition.y, 125, 45);
                var healthComponent = playerScript.GetComponent<HealthScript>();
                if (healthComponent == null)
                {
                    GUI.Box(rect, "No health component");
                }
                else
                {
                    GUI.Box(rect, "H: " + healthComponent.Health + "   S: " + healthComponent.Shield, boxStyle);
                }
            }
        }
    }

    public void SendMessageTo(string text)
    {
        if (Server != null)
        {
            Server.SendMessageFromServer(text, networkView.owner);
        }
        else
        {
            Debug.LogWarning("Unable to send message to " + this + " because Server is null");
        }
    }

    public void uLink_OnNetworkInstantiate(uLink.NetworkMessageInfo info)
    {
        WantNameSentBack();
    }

    private void WantNameSentBack()
    {
        if (!networkView.isMine)
        {
            networkView.RPC("SendNameBack", networkView.owner);
        }
    }

    [RPC]
// ReSharper disable once UnusedMember.Local
    protected void SendNameBack(uLink.NetworkMessageInfo info)
    {
        networkView.RPC("ReceiveNameSent", info.sender, Name);
    }

    [RPC]
    protected void ReceiveNameSent(string text)
    {
        bool wasNamed = HasBeenNamed;
        _Name = text;
        if (HasBeenNamed)
        {
            OnNameChanged();
            if (!wasNamed)
            {
                OnBecameNamed();
            }
        }
    }

    public void BroadcastChatMessageFrom(string text)
    {
        if (Server.networkView.isMine)
        {
            Server.BroadcastChatMessageFromServer(text, this);
        }
        else
        {
            networkView.RPC("ServerBroadcastChatMessageFrom", Server.networkView.owner, text);
        }
    }

    [RPC]
// ReSharper disable once UnusedMember.Local
    protected void ServerBroadcastChatMessageFrom(string text, uLink.NetworkMessageInfo info)
    {
        if (Server.networkView.isMine && info.sender == networkView.owner)
        {
            Server.BroadcastChatMessageFromServer(text, this);
        }
    }

    // Only works from server and owner
    public void SetScorePoints(int points)
    {
        if (networkView.isMine)
            OwnerSetScorePoints(points);
        else
            networkView.RPC("RemoteSetScorePoints", networkView.owner, points);
    }
    [RPC]
// ReSharper disable once UnusedMember.Local
    protected void RemoteSetScorePoints(int points, uLink.NetworkMessageInfo info)
    {
        if (info.sender != Server.networkView.owner) return;
        if (networkView.isMine)
            OwnerSetScorePoints(points);
        else
            networkView.RPC("RemoteSetScorePoints", networkView.owner, points);
    }

    private void OwnerSetScorePoints(int points)
    {
        Score = points;
        DisplayScoreForAWhile();
    }

    // Only works from server and owner
    public void ReceiveScorePoints(int points)
    {
        if (networkView.isMine)
        {
            OwnerReceiveScorePoints(points);
        }
        else
        {
            networkView.RPC("RemoteReceiveScorePoints", networkView.owner, points);
        }
    }

    private void OwnerReceiveScorePoints(int points)
    {
        Score += points;
        DisplayScoreForAWhile();
    }

    [RPC]
// ReSharper disable once UnusedMember.Local
    protected void RemoteReceiveScorePoints(int points, uLink.NetworkMessageInfo info)
    {
        if (info.sender != Server.networkView.owner) return;
        if (networkView.isMine)
        {
            OwnerReceiveScorePoints(points);
        }
        else
        {
            networkView.RPC("RemoteReceiveScorePoints", networkView.owner, points);
        }
    }
}