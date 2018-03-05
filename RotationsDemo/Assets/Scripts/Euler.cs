using UnityEngine;
using System;

public class Euler : MonoBehaviour {

    public Transform capsule;
    private RotationOrder order = RotationOrder.ZXY;
    private bool intrinsic = false;
    private float x = 0, y = 0, z = 0;
    private GUIStyle style, style2;

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

        Quaternion qx = Quaternion.Euler(x, 0, 0),
            qy = Quaternion.Euler(0, y, 0),
            qz = Quaternion.Euler(0, 0, z);

        switch (order) {
            case RotationOrder.XYZ:
                capsule.localRotation = intrinsic ? qx * qy * qz : qz * qy * qx;
                break;
            case RotationOrder.XZY:
                capsule.localRotation = intrinsic ? qx * qz * qy : qy * qz * qx;
                break;
            case RotationOrder.YXZ:
                capsule.localRotation = intrinsic ? qy * qx * qz : qz * qx * qy;
                break;
            case RotationOrder.YZX:
                capsule.localRotation = intrinsic ? qy * qz * qx : qx * qz * qy;
                break;
            case RotationOrder.ZXY:
                capsule.localRotation = intrinsic ? qz * qx * qy : Quaternion.Euler(x, y, z);
                break;
            case RotationOrder.ZYX:
                capsule.localRotation = intrinsic ? qz * qy * qx : qx * qy * qz;
                break;
        }
    }
    

    private void OnGUI() {

        int height = 35;
        GUI.Label(new Rect(5, height, 200, 40), string.Format("X rotation:     {0:0.000}", x), style);
        GUI.Label(new Rect(5, height += 25, 200, 40), string.Format("Y rotation:     {0:0.000}", y), style);
        GUI.Label(new Rect(5, height += 25, 200, 40), string.Format("Z rotation:     {0:0.000}", z), style);

        intrinsic = GUI.Toggle(new Rect(5, height += 45, 100, 20), intrinsic, " intrinsic");

        GUI.Label(new Rect(5, height += 25, 200, 40), "Rotation order: " + order);
        if (GUI.Button(new Rect(5, height += 25, 40, 20), "XYZ")) {
            order = RotationOrder.XYZ;
        }
        if (GUI.Button(new Rect(5, height += 25, 40, 20), "XZY")) {
            order = RotationOrder.XZY;
        }
        if (GUI.Button(new Rect(5, height += 25, 40, 20), "YXZ")) {
            order = RotationOrder.YXZ;
        }
        if (GUI.Button(new Rect(5, height += 25, 40, 20), "YZX")) {
            order = RotationOrder.YZX;
        }
        if (GUI.Button(new Rect(5, height += 25, 40, 20), "ZXY")) {
            order = RotationOrder.ZXY;
        }
        if (GUI.Button(new Rect(5, height += 25, 40, 20), "ZYX")) {
            order = RotationOrder.ZYX;
        }

        GUI.Label(new Rect(5, height += 45, 200, 40), "X axis:   Q  W    (E to reset)", style2);
        GUI.Label(new Rect(5, height += 25, 200, 40), "Y axis:   A  S    (D to reset)", style2);
        GUI.Label(new Rect(5, height += 25, 200, 40), "Z axis:   Z  X    (C to reset) ", style2);
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