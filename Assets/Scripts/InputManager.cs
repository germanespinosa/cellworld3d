#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Composites;
using UnityEngine.Rendering;
using static PlayerInput;



public class InputManager : MonoBehaviour 
{
    private PlayerInput player_input;
    private PlayerInput.OnFootActions on_foot_actions;
    private PlayerMotor motor;
    private PlayerLook look;
    private CellworldGameBridge bridge;

    private void Awake()
    {
        player_input = new PlayerInput();
        on_foot_actions = player_input.OnFoot;
        motor = GetComponent<PlayerMotor>();
        look = GetComponent<PlayerLook>();
        // IMPORTANT: PredatorUdpBridge might be on a different GameObject
        bridge = GetComponent<CellworldGameBridge>();
        if (bridge == null)
            bridge = FindFirstObjectByType<CellworldGameBridge>();

        on_foot_actions.Jump.performed += ctx => motor.Jump();
        on_foot_actions.Pause.performed += ctx => bridge.SendPause();
        on_foot_actions.Reset.performed += ctx => bridge.SendReset();
        on_foot_actions.Quit.performed += ctx => bridge.SendStop();
    }
    private void OnEnable()
    {
        on_foot_actions.Enable();
    }
    private void OnDisable()
    {
        on_foot_actions.Disable();
    }
    private void FixedUpdate()
    {
        float movement_lr = on_foot_actions.LRMovement.ReadValue<float>();
        float movement_du = on_foot_actions.DUMovement.ReadValue<float>();
        Vector2 left_stick_input = new Vector2();
        left_stick_input.x = movement_lr;
        left_stick_input.y = movement_du;
        float pointing_lr = on_foot_actions.LRPointing.ReadValue<float>();
        float pointing_du = on_foot_actions.DUPointing.ReadValue<float>();
        Vector2 right_stick_input = new Vector2();
        right_stick_input.x = pointing_lr;
        right_stick_input.y = pointing_du;
        motor.ProcessMove(left_stick_input, left_stick_input);

    }

    private void LateUpdate()
    {
        float pointing_lr = on_foot_actions.LRPointing.ReadValue<float>();
        float pointing_du = on_foot_actions.DUPointing.ReadValue<float>();
        Vector2 right_stick_input = new Vector2();
        right_stick_input.x = pointing_lr;
        right_stick_input.y = pointing_du;

        look.ProcessLook(right_stick_input);

        float cam_offset = on_foot_actions.CameraOffset.ReadValue<float>();

        look.CameraOffset(cam_offset);
    }

}
