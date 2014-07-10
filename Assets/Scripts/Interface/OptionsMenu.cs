using System;
using System.Collections.Generic;
using UnityEngine;

public class OptionsMenu
{
    private float _FOVOptionValue;

    public GUISkin Skin
    {
        get { return _Skin; }
        set
        {
            _Skin = value;
            LabelStyle = new GUIStyle(Skin.label);
            LabelStyle.normal.textColor = new Color(0.05f, 0.05f, 0.05f, 0.97f);
        }
    }

    public OptionsMenu(GUISkin skin)
    {
        Skin = skin;
        FOVOptionValue = PlayerPrefs.GetFloat("fov", 85.0f);
        SensitivityOptionValue = PlayerPrefs.GetFloat("mousesensitivity", 0.5f);
        IsExteriorView = PlayerPrefs.GetInt("thirdperson", 1) > 0;
        ShouldPlaySoundEffects = PlayerPrefs.GetInt("soundeffects", 1) > 0;
        ShouldPlayMusic = PlayerPrefs.GetInt("music", 1) > 0;
        IsAimInverted = PlayerPrefs.GetInt("invertaim", 0) > 0;
        ListOfMaps = new List<string>();

        int initialLength;
        int initialBuffers; // unused by us
        AudioSettings.GetDSPBufferSize(out initialLength, out initialBuffers);
        InitialAudioBufferSize = initialLength;
        _UseLowLatencyAudio = initialLength <= LowLatencyBufferSize;
        UseLowLatencyAudio = PlayerPrefs.GetInt("lowlatencyaudio", 1) > 0;
    }

    public float FOVOptionValue
    {
        get { return _FOVOptionValue; }
        set
        {
            // Don't do anything if almost the same
            if (Mathf.Approximately(_FOVOptionValue, value)) return;

            _FOVOptionValue = value;
            PlayerPrefs.SetFloat("fov", value);
            OnFOVOptionChanged(value);
        }
    }

    private float _Sensitivity;
    public float SensitivityOptionValue
    {
        get { return _Sensitivity; }
        set
        {
            float clamped = Mathf.Clamp01(value);
            if (!Mathf.Approximately(_Sensitivity, clamped))
            {
                _Sensitivity = clamped;
                PlayerPrefs.SetFloat("mouse_sensitivity", clamped);
                OnSensitivityOptionChanged(clamped);
            }
        }
    }

    private bool _IsExteriorView;

    public bool IsExteriorView
    {
        get
        {
            return _IsExteriorView;
        }
        set
        {
            if (_IsExteriorView != value)
            {
                _IsExteriorView = value;
                int asNumber = _IsExteriorView ? 1 : 0;
                PlayerPrefs.SetInt("thirdperson", asNumber);
                OnExteriorViewOptionChanged(value);
            }
        }
    }

    private bool _ShouldPlaySoundEffects;

    public bool ShouldPlaySoundEffects
    {
        get
        {
            return _ShouldPlaySoundEffects;
        }
        set
        {
            if (_ShouldPlaySoundEffects != value)
            {
                _ShouldPlaySoundEffects = value;
                int asNumber = value ? 1 : 0;
                PlayerPrefs.SetInt("soundeffects", asNumber);
                OnShouldPlaySoundEffectsOptionChanged(value);
            }
        }
    }
    private bool _ShouldPlayMusic;

    public bool ShouldPlayMusic
    {
        get
        {
            return _ShouldPlayMusic;
        }
        set
        {
            if (_ShouldPlayMusic != value)
            {
                _ShouldPlayMusic = value;
                int asNumber = value ? 1 : 0;
                PlayerPrefs.SetInt("music", asNumber);
                OnShouldPlayMusicOptionChanged(value);
            }
        }
    }

    private bool _IsAimInverted;

    public bool IsAimInverted
    {
        get
        {
            return _IsAimInverted;
        }
        set
        {
            if (_IsAimInverted != value)
            {
                _IsAimInverted = value;
                int asNumber = value ? 1 : 0;
                PlayerPrefs.SetInt("invertaim", asNumber);
                OnIsAimInvertedOptionChanged(value);
            }
        }
    }

    private int InitialAudioBufferSize = 1024;
    private int LowLatencyBufferSize = 256;

    private bool _UseLowLatencyAudio;

    public bool UseLowLatencyAudio
    {
        get { return _UseLowLatencyAudio; }
        set
        {
            if (_UseLowLatencyAudio != value)
            {
                int asNumber = value ? 1 : 0;
                PlayerPrefs.SetInt("lowlatencyaudio", asNumber);
                int newBufferLength = value ? LowLatencyBufferSize : InitialAudioBufferSize;
                int currentBufferLength;
                int currentNumBuffers;
                AudioSettings.GetDSPBufferSize(out currentBufferLength, out currentNumBuffers);
                if (newBufferLength != currentBufferLength)
                {
                    AudioSettings.SetDSPBufferSize(newBufferLength, currentNumBuffers);
                }
                _UseLowLatencyAudio = value;
                if (GlobalSoundsScript.Instance != null)
                    GlobalSoundsScript.Instance.RestartAudio();
            }
        }
    }

    public List<string> ListOfMaps { get; set; }

    public delegate void OptionsMenuStateChangedHandler();
    public event OptionsMenuStateChangedHandler OnOptionsMenuWantsClosed = delegate {};
    public event OptionsMenuStateChangedHandler OnOptionsMenuWantsGoToTitle = delegate {};
    public event OptionsMenuStateChangedHandler OnOptionsMenuWantsQuitGame = delegate {};
    public event OptionsMenuStateChangedHandler OnOptionsMenuWantsSpectate = delegate {};

    public delegate void FloatOptionChangedHandler(float optionValue);
    public delegate void BoolOptionChangedHandler(bool optionValue);

    public event FloatOptionChangedHandler OnFOVOptionChanged = delegate {};
    public event FloatOptionChangedHandler OnSensitivityOptionChanged = delegate {};
    public event BoolOptionChangedHandler OnExteriorViewOptionChanged = delegate {};
    public event BoolOptionChangedHandler OnShouldPlaySoundEffectsOptionChanged = delegate {}; 
    public event BoolOptionChangedHandler OnShouldPlayMusicOptionChanged = delegate {};
    public event BoolOptionChangedHandler OnIsAimInvertedOptionChanged = delegate {};

    public delegate void MapSelectionHandler(string mapName);
    public event MapSelectionHandler OnMapSelection = delegate {};

    private float VisibilityAmount = 0f;
    private GUISkin _Skin;

    public GUIStyle LabelStyle { get; private set; }

    public delegate void DisplayRoundOptionsHandler();
    public DisplayRoundOptionsHandler DisplayRoundOptionsDelegate = null;

    public bool ShouldDisplaySpectateButton { get; set; }
    private bool ShouldDisplayServerOptions { get; set; }

    public bool ShouldDisplayEndRound { get; set; }

    public void Update()
    {
        float speed = 0.000001f;
        float target = Relay.Instance.ShowOptions ? 1.0f : 0.0f;
        VisibilityAmount = Mathf.Lerp(VisibilityAmount, target, 1.0f - Mathf.Pow(speed, Time.deltaTime));

        // Bit of a hack for now
        if (Relay.Instance.CurrentServer != null)
            ShouldDisplayServerOptions = Relay.Instance.CurrentServer.GetComponent<uLink.NetworkView>().isMine;
    }

    public void DrawGUI()
    {
        GUI.skin = Skin;

        float height = 230f;
        float offscreenY = (-35f * 2) - (height + 50);
        float onscreenY = 35f;
        float actualY = Mathf.Lerp(offscreenY, onscreenY, VisibilityAmount);

        float serverHeight = 200f;
        float serverOffscreenY = Screen.height + serverHeight + 50f;
        float serverOnscreenY = Screen.height - (serverHeight + 35f);
        float serverActualY = Mathf.Lerp(serverOffscreenY, serverOnscreenY, VisibilityAmount);

        if (!Mathf.Approximately(VisibilityAmount, 0f))
        {
            GUILayout.Window(Definitions.OptionsWindowID, new Rect(35, actualY, Screen.width - 35*2, 200), DrawWindow,
                string.Empty);
            if (ShouldDisplayServerOptions)
                GUILayout.Window(Definitions.ServerOptionsWindowID, new Rect(35, serverActualY, Screen.width - 35*2, 200), DrawServerOptionsWindow,
                    string.Empty);
        }
    }

    private void DrawWindow(int id)
    {
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical();

        GUILayout.BeginHorizontal(Skin.box);
        GUILayout.Label("FOV: " + String.Format("{0:0}", FOVOptionValue), LabelStyle);
        FOVOptionValue = GUILayout.HorizontalSlider(FOVOptionValue, CameraScript.MinimumFieldOfView,
            CameraScript.MaximumFieldOfView, Skin.customStyles[0], Skin.customStyles[1]);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal(Skin.box);
        GUILayout.Label("SENSITIVITY", LabelStyle);
        SensitivityOptionValue = GUILayout.HorizontalSlider(SensitivityOptionValue, 0.01f,
            1f, Skin.customStyles[0], Skin.customStyles[1]);
        GUILayout.EndHorizontal();

        if (Debug.isDebugBuild)
        {
            float currentTarget = Application.targetFrameRate;
            if (currentTarget < 0) currentTarget = 175f;
            GUILayout.BeginHorizontal(Skin.box);
            GUILayout.Label("FPS LIMIT", LabelStyle);
            float newTarget = GUILayout.HorizontalSlider(currentTarget, 5f,
                175f, Skin.customStyles[0], Skin.customStyles[1]);
            GUILayout.EndHorizontal();
            if (newTarget >= 150f)
                Application.targetFrameRate = -1;
            else
                Application.targetFrameRate = Mathf.RoundToInt(newTarget);

            
            GUILayout.BeginHorizontal(Skin.box);
            GUILayout.Label("TIME SCALE", LabelStyle);
            Time.timeScale = GUILayout.HorizontalSlider(Time.timeScale, 0.1f,
                1f, Skin.customStyles[0], Skin.customStyles[1]);
            GUILayout.EndHorizontal();
        }



        GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();

                GUILayout.BeginHorizontal(Skin.box);
                GUILayout.Label("THIRD PERSON", LabelStyle);
                IsExteriorView = GUILayout.Toggle(IsExteriorView, "");
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal(Skin.box);
                GUILayout.Label("INVERT AIM", LabelStyle);
                IsAimInverted = GUILayout.Toggle(IsAimInverted, "");
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal(Skin.box);
                GUILayout.Label(" ", LabelStyle);
                GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUILayout.Space(1);

            GUILayout.BeginVertical();

                GUILayout.BeginHorizontal(Skin.box);
                GUILayout.Label("MUSIC", LabelStyle);
                ShouldPlayMusic = GUILayout.Toggle(ShouldPlayMusic, "");
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal(Skin.box);
                GUILayout.Label("SOUND EFFECTS", LabelStyle);
                ShouldPlaySoundEffects = GUILayout.Toggle(ShouldPlaySoundEffects, "");
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal(Skin.box);
                GUILayout.Label("LOW LAG AUDIO", LabelStyle);
                UseLowLatencyAudio = GUILayout.Toggle(UseLowLatencyAudio, "");
                GUILayout.EndHorizontal();

            GUILayout.EndVertical();

        GUILayout.EndHorizontal();


        //GUILayout.BeginHorizontal();
        //    GUILayout.BeginVertical();
        //    GUILayout.EndVertical();
        //GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUIExts.Button("ACCEPT", Skin.button))
            OnOptionsMenuWantsClosed();
        GUILayout.Space(1);
        if (Relay.Instance.CurrentServer != null)
        {
            if (GUIExts.Button("LEAVE SERVER", new GUIStyle(Skin.button) {fixedWidth = 95}))
                OnOptionsMenuWantsGoToTitle();
        }
        else
        {
            if (GUIExts.Button("QUIT GAME", new GUIStyle(Skin.button) {fixedWidth = 95}))
                OnOptionsMenuWantsQuitGame();
        }
        if (ShouldDisplaySpectateButton)
        {
            GUILayout.Space(1);
            if (GUIExts.Button("SPECTATE", new GUIStyle(Skin.button) {fixedWidth = 95}))
            {
                OnOptionsMenuWantsSpectate();
                OnOptionsMenuWantsClosed();
            }
        }

        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    private void DrawServerOptionsWindow(int id)
    {
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical();
        GUILayout.FlexibleSpace();


        if (DisplayRoundOptionsDelegate != null)
            DisplayRoundOptionsDelegate();

        GUILayout.Space(1);

        GUILayout.BeginHorizontal(Skin.box);
        GUILayout.Label("CHANGE MAP", LabelStyle);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        foreach (var map in ListOfMaps)
        {
            GUI.enabled = map != Application.loadedLevelName;
            if (GUIExts.Button(map, new GUIStyle(Skin.button) {fixedWidth = 95}))
            {
                OnMapSelection(map);
            }
            GUILayout.Space(1);
        }
        GUI.enabled = true;
        GUILayout.Space(-3);
        GUILayout.EndHorizontal();


        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }
}
