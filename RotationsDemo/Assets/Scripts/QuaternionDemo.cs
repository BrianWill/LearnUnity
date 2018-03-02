using UnityEngine;
using System;

public class QuaternionDemo  : MonoBehaviour {
    public Transform axis;
    public Transform capsule;
    public Transform sphere;

    private float spin = 0, pitch = 0, orbit = 0;

    private Quaternion axisBaseRotation;
    private Vector3 sphereBasePosition;

    private void Start() {
        axisBaseRotation = axis.localRotation;
        sphereBasePosition = sphere.localPosition;
    }


    void Update() {


        const float rate = 60;
        float delta = rate * Time.deltaTime;

        if (Input.GetKey(KeyCode.W)) {
            spin += delta;
        } else if (Input.GetKey(KeyCode.Q)) {
            spin -= delta;
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

        Quaternion q = Quaternion.Euler(0, orbit, 0) *
            Quaternion.Euler(pitch, 0, 0) *
            Quaternion.Euler(0, 0, spin);
        
        axis.localRotation = q * axisBaseRotation;
        Vector3 axisVector = q * new Vector3(0, 0, -1);
        sphere.localPosition = q * sphereBasePosition;

        Debug.DrawLine(Vector3.zero, sphere.localPosition, Color.green);

        capsule.localRotation = Quaternion.AngleAxis(spin, axisVector.normalized);
    }


    private void OnGUI() {

        string spinStr = spin.ToString(),
            heightStr = pitch.ToString(),
            orbitStr = orbit.ToString();

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 18; 

        GUI.Label(new Rect(5, 210, 55, 40), "Spin:", style);
        spinStr = GUI.TextField(new Rect(65, 210, 100, 20), spinStr, 10);

        GUI.Label(new Rect(5, 230, 55, 40), "Height:", style);
        heightStr = GUI.TextField(new Rect(65, 230, 100, 20), heightStr, 10);

        GUI.Label(new Rect(5, 250, 55, 40), "Orbit:", style);
        orbitStr = GUI.TextField(new Rect(65, 250, 100, 20), orbitStr, 10);
        try {
            spin = spinStr.Trim().Equals("") ? 0 : float.Parse(spinStr);
            pitch = heightStr.Trim().Equals("") ? 0 : float.Parse(heightStr);
            orbit = orbitStr.Trim().Equals("") ? 0 : float.Parse(orbitStr);
        } catch (FormatException ex) {
            Debug.Log(ex);
        } catch (OverflowException ex) {
            Debug.Log(ex);
        }

        Vector3 axis = sphere.localPosition.normalized;  // think ToAngleAxis already normalizes, but just in case
        axis.y = -axis.y;
        Quaternion q = Quaternion.AngleAxis(spin, axis);
        
        Vector3 qAxis = new Vector3(q.x, q.y, q.z);
        string ijkw = string.Format("Quaternion: \nX: {0:0.000}\nY: {1:0.000}\nZ: {2:0.000}\nW: {3:0.000}\nxyz mag: {4:0.000}\n",
            q.x, q.y, q.z, q.w, qAxis.magnitude
        );
        int top = 290;
        
        GUI.Label(new Rect(5, top, 140, 135), ijkw, style);

        top += 150;
        GUI.Label(new Rect(5, top, 200, 40), string.Format("Handle X: {0:0.00}", axis.x), style);
        GUI.Label(new Rect(5, top += 20, 200, 40), string.Format("Handle Y: {0:0.00}", axis.y), style);
        GUI.Label(new Rect(5, top += 20, 200, 40), string.Format("Handle Z: {0:0.00}", axis.z), style);

        qAxis = qAxis.normalized;
        top += 40;
        GUI.Label(new Rect(5, top, 200, 40), string.Format("\"axis\" X: {0:0.00}", qAxis.x), style);
        GUI.Label(new Rect(5, top += 20, 200, 40), string.Format("\"axis\" Y: {0:0.00}", qAxis.y), style);
        GUI.Label(new Rect(5, top += 20, 200, 40), string.Format("\"axis\" Z: {0:0.00}", qAxis.z), style);
    }
}


