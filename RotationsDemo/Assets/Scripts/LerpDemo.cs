using System.Collections;
using UnityEngine;


public class LerpDemo : MonoBehaviour {
    public Transform capsule;
    public Transform capsule2;
    public Transform capsule3;

    private Quaternion start;
    private Quaternion end;

    private Vector3 axis;
    private float endAngle;
    private float percent;

    private void Start() {
        start = Random.rotation;
        end = Random.rotation;
        percent = 0;

        capsule.transform.localRotation = start;
        capsule2.transform.localRotation = start;
        capsule3.transform.localRotation = start;

        (end * Quaternion.Inverse(start)).ToAngleAxis(out endAngle, out axis);
        // quaternions only represent rotations in range +180 to -180
        if (endAngle > 180) {
            endAngle -= 360;
        }
    }


    private float resume = 0;
    private bool waiting = false;

    void Update() {
        if (waiting) {
            if (Time.time > resume) {
                Start();
                waiting = false;
            } else {
                return;
            }
        }

        const float wait = 1.3f;
        const float rate = 0.2f;
        percent += rate * Time.deltaTime;
        if (percent > 1) {
            percent = 1;
            resume = Time.time + wait;
            waiting = true;
        }

        // we can achieve same smoothness of slerp w/angle-axis and no quaternions (but less efficient?)
        capsule.transform.localRotation = Quaternion.AngleAxis(endAngle * percent, axis) * start;

        capsule2.transform.localRotation = Quaternion.Slerp(start, end, percent);

        // always takes same path as slerp, but it gives less consistent speed
        capsule3.transform.localRotation = Quaternion.Lerp(start, end, percent);
    }
}


