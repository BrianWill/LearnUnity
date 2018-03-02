using UnityEngine;
using System;

public class Euler : MonoBehaviour {
    private RotationOrder order = RotationOrder.XYZ;
    private bool intrinsic = false;
    private float x = 0, y = 0, z = 0;

    void Update() {
        const float rate = 60;
        float delta = rate * Time.deltaTime;
        if (Input.GetKey(KeyCode.W)) {
            x += delta;
        } else if (Input.GetKey(KeyCode.Q)) {
            x -= delta;
        } else if (Input.GetKey(KeyCode.E)) {
            x = 0;
        } else if (Input.GetKey(KeyCode.S)) {
            y += delta;
        } else if (Input.GetKey(KeyCode.A)) {
            y -= delta;
        } else if (Input.GetKey(KeyCode.D)) {
            y = 0;
        } else if (Input.GetKey(KeyCode.X)) {
            z += delta;
        } else if (Input.GetKey(KeyCode.Z)) {
            z -= delta;
        } else if (Input.GetKey(KeyCode.C)) {
            z = 0;
        }
    }

    private void OnGUI() {

        string X = x.ToString(), Y = y.ToString(), Z = z.ToString();

        GUI.Label(new Rect(5, 10, 20, 40), "X:");
        X = GUI.TextField(new Rect(25, 10, 100, 20), X, 10);

        GUI.Label(new Rect(5, 30, 20, 40), "Y:");
        Y = GUI.TextField(new Rect(25, 30, 100, 20), Y, 10);

        GUI.Label(new Rect(5, 50, 20, 40), "Z:");
        Z = GUI.TextField(new Rect(25, 50, 100, 20), Z, 10);


        GUI.Label(new Rect(5, 80, 200, 40), "Rotation order: " + order);
        if (GUI.Button(new Rect(5, 100, 40, 20), "XYZ")) {
            order = RotationOrder.XYZ;
        }
        if (GUI.Button(new Rect(5, 125, 40, 20), "XZY")) {
            order = RotationOrder.XZY;
        }
        if (GUI.Button(new Rect(5, 150, 40, 20), "YXZ")) {
            order = RotationOrder.YXZ;
        }
        if (GUI.Button(new Rect(5, 175, 40, 20), "YZX")) {
            order = RotationOrder.YZX;
        }
        if (GUI.Button(new Rect(5, 200, 40, 20), "ZXY")) {
            order = RotationOrder.ZXY;
        }
        if (GUI.Button(new Rect(5, 225, 40, 20), "ZYX")) {
            order = RotationOrder.ZYX;
        }

        intrinsic = GUI.Toggle(new Rect(5, 250, 100, 20), intrinsic, " intrinsic");

        try {
            x = X.Trim().Equals("") ? 0 : float.Parse(X);
            y = Y.Trim().Equals("") ? 0 : float.Parse(Y);
            z = Z.Trim().Equals("") ? 0 : float.Parse(Z);
        } catch (FormatException ex) {
            Debug.Log("X, Y, or Z string must be a float" + ex);
        } catch (OverflowException ex) {
            Debug.Log("X, Y, or Z value is too large or small" + ex);
        }

        Quaternion qx = Quaternion.Euler(x, 0, 0),
            qy = Quaternion.Euler(0, y, 0),
            qz = Quaternion.Euler(0, 0, z);

        switch (order) {
            case RotationOrder.XYZ:
                transform.localRotation = intrinsic ? qx * qy * qz : qz * qy * qx;
                break;
            case RotationOrder.XZY:
                transform.localRotation = intrinsic ? qx * qz * qy : qy * qz * qx;
                break;
            case RotationOrder.YXZ:
                transform.localRotation = intrinsic ? qy * qx * qz : qz * qx * qy;
                break;
            case RotationOrder.YZX:
                transform.localRotation = intrinsic ? qy * qz * qx : qx * qz * qy;
                break;
            case RotationOrder.ZXY:
                transform.localRotation = intrinsic ? qz * qx * qy : Quaternion.Euler(x, y, z);
                break;
            case RotationOrder.ZYX:
                transform.localRotation = intrinsic ? qz * qy * qx : qx * qy * qz;
                break;
        }

        //Quaternion q = transform.localRotation;
        //string ijkw = string.Format("Quaternion: \nX: {0:0.000}\n Y: {1:0.000}\n Z: {2:0.000}\n W: {3:0.000}\n",
        //    q.x, q.y, q.z, q.w
        //);
        //GUIStyle style = new GUIStyle(GUI.skin.box);
        //style.fontSize = 18;
        //GUI.Box(new Rect(5, 280, 120, 115), ijkw, style);
    }
}

enum RotationOrder {
    XZY,
    XYZ,
    YXZ,
    YZX,
    ZXY,
    ZYX
}