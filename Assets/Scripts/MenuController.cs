using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.Globalization;

public class MenuController : MonoBehaviour
{
    [SerializeField] private TMP_InputField worldNameInput;
    [SerializeField] private TMP_InputField patientNameInput;
    [SerializeField] private TMP_InputField protocolInput;
    [SerializeField] private TMP_Dropdown sampleRateDropDown;
    [SerializeField] private Toggle showCellworldGameToggle;

    private CellworldGameBridge cellworldGameBridge;
    private CellworldBridgeState lastObservedBridgeState = CellworldBridgeState.Unknown;

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
}
