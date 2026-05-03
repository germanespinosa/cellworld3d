using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using static UnityEngine.Rendering.DebugUI;

public class PlayerMotor : MonoBehaviour
{
    private CharacterController controller;
    private Vector3 player_velocity;
    public float speed = 10.0f;
    private bool is_grounded = false;
    private float gravity = -9.8f;
    private float jump_height = 2.0f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    // Update is called once per frame
    void Update()
    {
        is_grounded = controller.isGrounded;
    }

    public void ProcessMove(Vector2 movement_input, Vector2 pointing_input)
    {
        Vector3 move_direction = Vector3.zero;
        move_direction.x = movement_input.x;
        move_direction.z = movement_input.y;
        controller.Move(transform.TransformDirection(move_direction) * speed * Time.deltaTime);
        player_velocity.y += gravity * Time.deltaTime;
        if (is_grounded && player_velocity.y < 0f) player_velocity.y = -2f;
        controller.Move(player_velocity * Time.deltaTime);
        //Debug.Log($"Value: {player_velocity.y:F2}, A: {is_grounded}, B: {controller.isGrounded}");
    }

    public void Jump()
    {
        if (is_grounded)
        {
            player_velocity.y = Mathf.Sqrt(2f * (-gravity) * jump_height);
        }
    }
}
