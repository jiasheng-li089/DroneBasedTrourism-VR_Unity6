using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class DetectCameraMovement : MonoBehaviour
{
    private Vector3 lastPosition;
    private Vector3 initialRoation;

    private WebRTCManager webRTCManager;

    private long lastSampleTime = 0;

    private long captureInterval = 200L;

    [SerializeField]
    public XRInputValueReader<Vector2> m_LeftThumbStickReader = new XRInputValueReader<Vector2>("Thumbstick");
    [SerializeField]
    public XRInputValueReader<Vector2> m_RightThumbStickReader = new XRInputValueReader<Vector2>("Thumbstick");

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        lastPosition = transform.position;
        initialRoation = transform.rotation.eulerAngles;

        Debug.Log("Initial Position: " + lastPosition + ", Initial Rotation: " + initialRoation);
        webRTCManager = GetComponentInParent<WebRTCManager>();
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 currentPosition = transform.position;
        Vector3 currentRotation = transform.rotation.eulerAngles;

        // Debug.Log("Current Position: " + currentPosition + ", Current Rotation: " + currentRotation);

        // TODO: calculate the difference between the last and current position and rotation,
        // generate commands to control the drone based on the difference,
        // and send the commands to the drone
        if (null == webRTCManager) return;

        long currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (currentTimestamp - lastSampleTime >= captureInterval)
        {
            lastSampleTime = currentTimestamp;
            // webRTCManager.Send("Ping " + currentTimestamp, "data");
        }

        Vector2 leftInput = m_LeftThumbStickReader.ReadValue();
        Vector2 rightInput = m_RightThumbStickReader.ReadValue();

        if (leftInput != Vector2.zero || rightInput != Vector2.zero)
        {
            Debug.Log($"Left input: ({leftInput.x}, {leftInput.y})\t\tRight input: ({rightInput.x}, {rightInput.y})");
        }
    }

}