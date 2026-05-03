using UnityEngine;

public class PlayerLook : MonoBehaviour
{
    public Camera cam;
    private float xRotation = 0f;
    private float xSensitivity = 120f;
    private float ySensitivity = 120f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    [SerializeField] private float cameraPeekDistance = 0.35f;
    [SerializeField] private float cameraPeekLerpSpeed = 10f;
    private float targetCameraPeekX = 0f;

    public void ProcessLook(Vector2 input)
    {
        float mouse_x = input.x;
        float mouse_y = input.y;
        xRotation -= (mouse_y * Time.deltaTime) * ySensitivity;
        xRotation = Mathf.Clamp(xRotation, -80f, 80f);
        cam.transform.localRotation = Quaternion.Euler(xRotation, 0f,0f);
        transform.Rotate(Vector3.up * (mouse_x * Time.deltaTime) * xSensitivity);
    }

    public void CameraOffset(float offset)
    {
        cam.transform.localPosition = new Vector3(offset, 0.6f, 0f);
    }

}
