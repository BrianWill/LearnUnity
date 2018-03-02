using UnityEngine;
using System;

public class Handle : MonoBehaviour {

    public Transform capsule;

    private float spin = 0, pitch = 0, orbit = 0;

    private Vector3 handleBaseVector = new Vector3(0, 0, -1);
    private Vector3 handleVector = new Vector3(0, 0, -1);

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
            pitch -= delta;
        } else if (Input.GetKey(KeyCode.A)) {
            pitch += delta;
        } else if (Input.GetKey(KeyCode.D)) {
            pitch = 0;
        } else if (Input.GetKey(KeyCode.X)) {
            orbit -= delta;
        } else if (Input.GetKey(KeyCode.Z)) {
            orbit += delta;
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

        string spinStr = spin.ToString(),
            heightStr = pitch.ToString(),
            orbitStr = orbit.ToString();

        GUI.Label(new Rect(5, 10, 50, 40), "Spin:");
        spinStr = GUI.TextField(new Rect(60, 10, 100, 20), spinStr, 10);

        GUI.Label(new Rect(5, 30, 50, 40), "Height:");
        heightStr = GUI.TextField(new Rect(60, 30, 100, 20), heightStr, 10);

        GUI.Label(new Rect(5, 50, 50, 40), "Orbit:");
        orbitStr = GUI.TextField(new Rect(60, 50, 100, 20), orbitStr, 10);

        try {
            spin = spinStr.Trim().Equals("") ? 0 : float.Parse(spinStr);
            pitch = heightStr.Trim().Equals("") ? 0 : float.Parse(heightStr);
            orbit = orbitStr.Trim().Equals("") ? 0 : float.Parse(orbitStr);
        } catch (FormatException ex) {
            Debug.Log(ex);
        } catch (OverflowException ex) {
            Debug.Log(ex);
        }

        int screenHeight = Screen.height;
        GUI.Label(new Rect(5, screenHeight - 100, 200, 40), string.Format("Handle X: {0:0.00}", handleVector.x));
        GUI.Label(new Rect(5, screenHeight - 80, 200, 40), string.Format("Handle Y: {0:0.00}", handleVector.y));
        GUI.Label(new Rect(5, screenHeight - 60, 200, 40), string.Format("Handle Z: {0:0.00}", handleVector.z));
    }
}


