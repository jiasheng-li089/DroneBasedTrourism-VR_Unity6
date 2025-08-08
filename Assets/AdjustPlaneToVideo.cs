using UnityEngine;

public class AdjustPlaneToVideo : MonoBehaviour
{

    public float videoWidth = 1920f;
    public float videoHeight = 1080f;
    public float baseScale = 1f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        float aspectRatio = videoWidth / videoHeight;
        // transform.localScale = new Vector3(1, 1, baseScale / aspectRatio);

        // transform.rotation = Quaternion.Euler(90, 180, 0);

        // transform.localPosition = new Vector3(0, 0, 710);

        Canvas canvas = Camera.main.GetComponentInChildren<Canvas>();
        Debug.Log(canvas);
    }

    // Update is called once per frame
    void Update()
    {

    }

}