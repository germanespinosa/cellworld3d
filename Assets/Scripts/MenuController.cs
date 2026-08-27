using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.Globalization;
using System.Collections;
using System;
using System.IO;

public class MenuController : MonoBehaviour
{
    [SerializeField] private TMP_InputField worldNameInput;
    [SerializeField] private TMP_InputField patientNameInput;
    [SerializeField] private TMP_InputField protocolInput;
    [SerializeField] private TMP_Dropdown sampleRateDropDown;
    [SerializeField] private Toggle showCellworldGameToggle;
    [SerializeField] private Image physiologySyncFlashTarget;
    [SerializeField] private Vector2 physiologySyncFlashSize = new Vector2(140f, 140f);
    // Keep the sync marker 24 UI units inward from the Canvas's bottom-right corner.
    [SerializeField] private Vector2 physiologySyncFlashOffset = new Vector2(-24f, 24f);

    private CellworldGameBridge cellworldGameBridge;
    private CellworldBridgeState lastObservedBridgeState = CellworldBridgeState.Unknown;
    private Coroutine physiologySyncCoroutine;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        TryResolveBridge();

        if (cellworldGameBridge != null)
            lastObservedBridgeState = cellworldGameBridge.CurrentState;
    }

    // Update is called once per frame
    void Update()
    {
        if (!TryResolveBridge())
            return;

        var currentState = cellworldGameBridge.CurrentState;
        if (currentState == lastObservedBridgeState)
            return;

        lastObservedBridgeState = currentState;

        if (currentState == CellworldBridgeState.Paused)
        {
            int nextSceneIndex = SceneManager.GetActiveScene().buildIndex + 1;
            if (nextSceneIndex < SceneManager.sceneCountInBuildSettings)
                SceneManager.LoadScene(nextSceneIndex);
        }
    }

    public void InitiateCellworldGame()
    {
        if (!TryResolveBridge())
            return;

        cellworldGameBridge.SampleInterval = ResolveSampleInterval();

        string worldName = worldNameInput != null ? worldNameInput.text : "21_05";
        string patientName = patientNameInput != null ? patientNameInput.text : "patient01";
        string protocolName = protocolInput != null ? protocolInput.text : "protocol01";
        bool shouldRender = showCellworldGameToggle != null && showCellworldGameToggle.isOn;
        cellworldGameBridge.SendInit(worldName, shouldRender, cellworldGameBridge.SampleInterval, protocolName, patientName);
    }

    public void SynchronizePhysiologyEquipment()
    {
        if (physiologySyncCoroutine != null)
            StopCoroutine(physiologySyncCoroutine);

        physiologySyncCoroutine = StartCoroutine(PhysiologySyncSequence());
    }

    public void SyncPhysiology()
    {
        SynchronizePhysiologyEquipment();
    }

    private bool TryResolveBridge()
    {
        if (cellworldGameBridge != null)
            return true;

        cellworldGameBridge = CellworldGameBridge.Instance;
        if (cellworldGameBridge == null)
            cellworldGameBridge = FindFirstObjectByType<CellworldGameBridge>();

        return cellworldGameBridge != null;
    }


    private float ResolveSampleInterval()
    {
        if (sampleRateDropDown == null)
            return 1f / 60f;

        if (sampleRateDropDown.options == null || sampleRateDropDown.options.Count == 0)
            return 1f / 60f;

        int idx = Mathf.Clamp(sampleRateDropDown.value, 0, sampleRateDropDown.options.Count - 1);
        string text = sampleRateDropDown.options[idx].text?.Trim() ?? "";

        string hzText = text.EndsWith("Hz") ? text.Substring(0, text.Length - 2).Trim() : text;
        if (float.TryParse(hzText, NumberStyles.Float, CultureInfo.InvariantCulture, out float hz) && hz > 0f)
            return 1f / hz;

        if (idx == 0) return 1f / 40f;
        if (idx == 1) return 1f / 60f;
        if (idx == 2) return 1f / 90f;
        return 1f / 60f;
    }

    private IEnumerator PhysiologySyncSequence()
    {
        Image flashTarget = ResolvePhysiologySyncFlashTarget();
        if (flashTarget == null)
        {
            physiologySyncCoroutine = null;
            yield break;
        }

        SetPhysiologySyncFlashVisible(flashTarget, false);
        yield return new WaitForSeconds(3f);

        DateTime firstFlashTimestamp = DateTime.MinValue;

        yield return FlashPhysiologySyncPulses(flashTarget, 4, 0.020f, timestamp =>
        {
            if (firstFlashTimestamp == DateTime.MinValue)
                firstFlashTimestamp = timestamp;
        });
        yield return new WaitForSeconds(1f);
        yield return FlashPhysiologySyncPulses(flashTarget, 4, 0.050f);
        yield return new WaitForSeconds(1f);
        yield return FlashPhysiologySyncPulses(flashTarget, 4, 0.100f);

        SetPhysiologySyncFlashVisible(flashTarget, false);
        yield return new WaitForSeconds(3f);
        SavePhysiologySyncTimestamp(firstFlashTimestamp);
        flashTarget.gameObject.SetActive(false);
        physiologySyncCoroutine = null;
    }

    private IEnumerator FlashPhysiologySyncPulses(Image flashTarget, int pulseCount, float pulseDuration, Action<DateTime> firstFlashCallback = null)
    {
        for (int i = 0; i < pulseCount; i++)
        {
            DateTime flashTimestamp = DateTime.Now;
            SetPhysiologySyncFlashVisible(flashTarget, true);
            firstFlashCallback?.Invoke(flashTimestamp);
            yield return new WaitForSeconds(pulseDuration);
            SetPhysiologySyncFlashVisible(flashTarget, false);
            yield return new WaitForSeconds(pulseDuration);
        }
    }

    private Image ResolvePhysiologySyncFlashTarget()
    {
        if (physiologySyncFlashTarget != null)
            return physiologySyncFlashTarget;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();

        if (canvas == null)
            return null;

        GameObject flashObject = new GameObject("PhysiologySyncFlash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        flashObject.transform.SetParent(canvas.transform, false);

        RectTransform rectTransform = flashObject.GetComponent<RectTransform>();
        // Bottom-right anchoring makes the negative X and positive Y offset point inward.
        rectTransform.anchorMin = new Vector2(1f, 0f);
        rectTransform.anchorMax = new Vector2(1f, 0f);
        rectTransform.pivot = new Vector2(1f, 0f);
        rectTransform.anchoredPosition = physiologySyncFlashOffset;
        rectTransform.sizeDelta = physiologySyncFlashSize;

        physiologySyncFlashTarget = flashObject.GetComponent<Image>();
        physiologySyncFlashTarget.color = Color.black;
        physiologySyncFlashTarget.raycastTarget = false;
        physiologySyncFlashTarget.gameObject.SetActive(true);

        return physiologySyncFlashTarget;
    }

    private void SetPhysiologySyncFlashVisible(Image flashTarget, bool visible)
    {
        flashTarget.gameObject.SetActive(true);
        flashTarget.color = visible ? Color.white : Color.black;
    }

    private void SavePhysiologySyncTimestamp(DateTime firstFlashTimestamp)
    {
        if (firstFlashTimestamp == DateTime.MinValue)
            return;

        string saveDirectory = Environment.GetEnvironmentVariable("CELLWORLD_EXPERIMENT_DIR");
        if (string.IsNullOrWhiteSpace(saveDirectory))
            saveDirectory = Application.persistentDataPath;

        Directory.CreateDirectory(saveDirectory);

        string fileName = $"sync_signal_{firstFlashTimestamp:yyyyMMddHHmmss}.json";
        string path = Path.Combine(saveDirectory, fileName);
        string json = JsonUtility.ToJson(new PhysiologySyncSignal
        {
            first_flash_timestamp = firstFlashTimestamp.ToString("o", CultureInfo.InvariantCulture),
            first_flash_unix_time_ms = new DateTimeOffset(firstFlashTimestamp).ToUnixTimeMilliseconds()
        }, true);

        File.WriteAllText(path, json);
        Debug.Log($"[PHYSIOLOGY SYNC] Saved sync signal timestamp: {path}");
    }

    [Serializable]
    private class PhysiologySyncSignal
    {
        public string first_flash_timestamp;
        public long first_flash_unix_time_ms;
    }

}
