using UnityEngine;
using System;

public class Handle : MonoBehaviour {

    public Transform capsule;

    private float spin = 0, pitch = 0, orbit = 0;

    private Vector3 handleBaseVector = new Vector3(0, 0, 1);
    private Vector3 handleVector = new Vector3(0, 0, 1);
    private GUIStyle style;
    private GUIStyle style2;

    private void Awake() {
        style = new GUIStyle();
        style.fontSize = 20;
        style.normal.textColor = Color.white;

        style2 = new GUIStyle();
        style2.fontSize = 14;
        style2.normal.textColor = Color.gray;
    }

    // Update is called once per frame
    void Update() {
        const float rate = 90;
        float delta = rate * Time.deltaTime;

        if (Input.GetKey(KeyCode.W)) {
            spin -= delta;
        } else if (Input.GetKey(KeyCode.Q)) {
            spin += delta;
        } else if (Input.GetKey(KeyCode.E)) {
            spin = 0;
        } else if (Input.GetKey(KeyCode.S)) {
            pitch += delta;
        } else if (Input.GetKey(KeyCode.A)) {
            pitch -= delta;
        } else if (Input.GetKey(KeyCode.D)) {
            pitch = 0;
        } else if (Input.GetKey(KeyCode.X)) {
            orbit += delta;
        } else if (Input.GetKey(KeyCode.Z)) {
            orbit -= delta;
        } else if (Input.GetKey(KeyCode.C)) {
            orbit = 0;
        }

        if (spin > 180) {
            spin = -180;
        }
        if (spin < -180) {
            spin = 180;
        }

        if (orbit > 180) {
            orbit = -180;
        }
        if (orbit < -180) {
            orbit = 180;
        }

        pitch = Mathf.Clamp(pitch, -90, 90);


        // extrinsically apply spin, then pitch, then orbit
        Quaternion q = Quaternion.Euler(0, orbit, 0) *
            Quaternion.Euler(pitch, 0, 0) *
            Quaternion.Euler(0, 0, spin);

        handleVector = q * handleBaseVector;
        capsule.localRotation = q;

    }

    

    private void OnGUI() {
        GUI.Label(new Rect(5, 10, 200, 40), string.Format("Spin:            {0:0.000}", spin), style);

        GUI.Label(new Rect(5, 35, 200, 40), string.Format("Handle X:     {0:0.000}", handleVector.x), style);
        GUI.Label(new Rect(5, 60, 200, 40), string.Format("Handle Y:     {0:0.000}", handleVector.y), style);
        GUI.Label(new Rect(5, 85, 200, 40), string.Format("Handle Z:     {0:0.000}", handleVector.z), style);

        GUI.Label(new Rect(5, 235, 200, 40), "Spin:   Q  W   (E to reset)", style2);
        GUI.Label(new Rect(5, 255, 200, 40), "Pitch:   A  S    (D to reset)", style2);
        GUI.Label(new Rect(5, 275, 200, 40), "Orbit:   Z  X    (C to reset) ", style2);
    }
}


