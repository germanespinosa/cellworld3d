using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class GameController : MonoBehaviour
{
    [SerializeField] private Transform startdoorTransform;
    [SerializeField] private Transform finishdoorTransform;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private TMP_Text puffCounter;
    [SerializeField] private TMP_Text trialCounter;
    [SerializeField] private RawImage background;
    [SerializeField] private float backgroundFadeDuration = 1f;


    private CellworldGameBridge bridge;
    private Coroutine backgroundFadeCoroutine;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        ResolveReferences();
        SubscribeToBridgePuffEvent();
        SetBackgroundAlpha(0f);
        RefreshCounters();
    }

    void OnEnable()
    {
        ResolveReferences();
        SubscribeToBridgePuffEvent();
        SetBackgroundAlpha(0f);
    }

    void OnDisable()
    {
        UnsubscribeFromBridgePuffEvent();
    }

    // Update is called once per frame
    void Update()
    {
        if (!ResolveReferences())
            return;

        Vector3 playerPosition = playerTransform.position;
        float scale = Mathf.Approximately(bridge.positionScale, 0f) ? 1f : bridge.positionScale;
        float preyX = playerPosition.x / scale;
        float preyY = playerPosition.z / scale;
        float preyDirection = 90 - playerTransform.eulerAngles.y;
        bridge.SendPrey(preyX, preyY, preyDirection);
        RefreshCounters();
        if (bridge.CurrentState == CellworldBridgeState.Stopped)
        {
            #if UNITY_EDITOR
            EditorApplication.isPlaying = false;
            #else
                            Application.Quit();
            #endif
        }
    }

    public void CloseDoor(Transform door)
    {
        door.position = new Vector3(door.position.x, 4f, door.position.z);
    }


    public void OpenDoor(Transform door)
    {
        door.position = new Vector3(door.position.x, 12f, door.position.z);
    }


    private bool ResolveReferences()
    {
        if (playerTransform == null)
            playerTransform = transform;

        if (puffCounter == null)
        {
            var puffCounterObject = GameObject.Find("Puffs");
            if (puffCounterObject != null)
                puffCounter = puffCounterObject.GetComponent<TMP_Text>();
        }
        if (trialCounter == null)
        {
            var trialCounterObject = GameObject.Find("Trials");
            if (trialCounterObject != null)
                trialCounter = trialCounterObject.GetComponent<TMP_Text>();
        }
        if (background == null)
        {
            var backgroundObject = GameObject.Find("Background");
            if (backgroundObject != null)
                background = backgroundObject.GetComponent<RawImage>();
        }
        if (bridge == null)
            bridge = CellworldGameBridge.Instance;

        if (bridge == null)
            bridge = FindFirstObjectByType<CellworldGameBridge>();

        return bridge != null && playerTransform != null;
    }

    private void SubscribeToBridgePuffEvent()
    {
        if (bridge == null)
            return;

        bridge.PuffEventReceived -= HandlePuffEventReceived;
        bridge.PuffEventReceived += HandlePuffEventReceived;
    }

    private void UnsubscribeFromBridgePuffEvent()
    {
        if (bridge == null)
            return;
        bridge.PuffEventReceived -= HandlePuffEventReceived;
    }

    private void HandlePuffEventReceived()
    {
        // Puff packets arrive through the bridge; this callback is the game-side puff trigger.
        Vibrate(0.5f,1.0f,1.0f);
        TriggerBackgroundFlash();
        RefreshCounters();
    }

    public void SendUnpause()
    {
        if (bridge == null)
            return;
        bridge.SendUnpause();
    }

    public void SendBegin()
    {
        Debug.Log($"[GameController] -> SendBegin");

        if (bridge == null)
            return;
        OpenDoor(finishdoorTransform);
        CloseDoor(startdoorTransform);
        bridge.SendBegin();
    }

    public void ResetGame()
    {
        if (bridge == null)
            return;
        OpenDoor(startdoorTransform);
        CloseDoor(finishdoorTransform);
        bridge.SendReset();
    }

    private void RefreshCounters()
    {
        if (bridge == null)
            return;
        if (puffCounter != null)
            puffCounter.text =  bridge.PuffCount .ToString();
        if (trialCounter != null)
            trialCounter.text =  bridge.TrialCount .ToString();
    }

    public void Vibrate(float lowFreq, float highFreq, float duration)
    {
        if (Gamepad.current != null)
        {
            Gamepad.current.SetMotorSpeeds(lowFreq, highFreq);
            Invoke(nameof(StopVibration), duration);
        }
    }

    void StopVibration()
    {
        if (Gamepad.current != null)
        {
            Gamepad.current.SetMotorSpeeds(0f, 0f);
        }
    }

    private void TriggerBackgroundFlash()
    {
        if (background == null)
            return;

        if (backgroundFadeCoroutine != null)
            StopCoroutine(backgroundFadeCoroutine);

        backgroundFadeCoroutine = StartCoroutine(FadeBackgroundOut());
    }

    private System.Collections.IEnumerator FadeBackgroundOut()
    {
        SetBackgroundAlpha(1f);

        float duration = Mathf.Max(0.01f, backgroundFadeDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            SetBackgroundAlpha(1f - normalized);
            yield return null;
        }

        SetBackgroundAlpha(0f);
        backgroundFadeCoroutine = null;
    }

    private void SetBackgroundAlpha(float alpha)
    {
        if (background == null)
            return;

        Color c = background.color;
        c.a = Mathf.Clamp01(alpha);
        background.color = c;
    }
}
