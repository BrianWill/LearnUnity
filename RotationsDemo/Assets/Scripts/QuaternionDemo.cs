using UnityEngine;
using System;



public class QuaternionDemo : MonoBehaviour {

    public Transform capsule;
    public Transform axis;
    private Quaternion axisBaseRotation;
    private Quaternion rotation;
    private float spin = 0, pitch = 0, orbit = 0;
    private Vector3 handleVector = new Vector3(0, 0, 1);
    private GUIStyle style, style2;

    private void Awake() {
        style = new GUIStyle();
        style.fontSize = 20;
        style.normal.textColor = Color.white;

        style2 = new GUIStyle();
        style2.fontSize = 14;
        style2.normal.textColor = Color.gray;

        axisBaseRotation = axis.localRotation;
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
        
        axis.localRotation = q * axisBaseRotation;
        handleVector = (q * new Vector3(0, 0, 1)).normalized;
        rotation = Quaternion.AngleAxis(spin, handleVector);
        capsule.localRotation = rotation;
    }

    private void OnGUI() {
        GUI.Label(new Rect(5, 10, 200, 40), string.Format("Spin:            {0:0.000}", spin), style);

        int top = 35;
        GUI.Label(new Rect(5, top, 200, 40), string.Format("Axis X:     {0:0.000}", handleVector.x), style);
        GUI.Label(new Rect(5, top += 25, 200, 40), string.Format("Axis Y:     {0:0.000}", handleVector.y), style);
        GUI.Label(new Rect(5, top += 25, 200, 40), string.Format("Axis Z:     {0:0.000}", handleVector.z), style);


        Vector3 qAxis = new Vector3(rotation.x, rotation.y, rotation.z);
        string ijkw = string.Format("Quaternion: \nX: {0:0.000}\nY: {1:0.000}\nZ: {2:0.000}\nW: {3:0.000}\nxyz mag: {4:0.000}\n",
            rotation.x, rotation.y, rotation.z, rotation.w, qAxis.magnitude
        );
        GUI.Label(new Rect(5, top += 40, 140, 135), ijkw, style);

        qAxis = qAxis.normalized;
        string xyz = string.Format("Norm X: {0:0.000}\nNorm Y: {1:0.000}\nNorm Z: {2:0.000}\n",
            qAxis.x, qAxis.y, qAxis.z
        );
        GUI.Label(new Rect(5, top += 180, 140, 135), xyz, style);

        GUI.Label(new Rect(5, top += 180, 200, 40), "Spin:   Q  W   (E to reset)", style2);
        GUI.Label(new Rect(5, top += 25, 200, 40), "Pitch:   A  S    (D to reset)", style2);
        GUI.Label(new Rect(5, top += 25, 200, 40), "Orbit:   Z  X    (C to reset) ", style2);
    }
}


