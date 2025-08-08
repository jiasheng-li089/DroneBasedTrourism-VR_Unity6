using System;
using UnityEngine;

public class DetectCameraMovement : MonoBehaviour
{
    private Vector3 lastPosition;
    private Vector3 initialRoation;

    private WebRTCManager webRTCManager;

    private long lastSampleTime = 0;

    private long captureInterval = 200L;


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
    }

}