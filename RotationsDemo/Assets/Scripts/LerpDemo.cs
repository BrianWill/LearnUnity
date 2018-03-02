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
    }

    void Update() {
        if (Input.GetKeyDown(KeyCode.Space)) {
            Start();   // reset
        }

        const float rate = 0.2f;
        percent += rate * Time.deltaTime;
        if (percent > 1) {
            percent = 1;
        }

        // gives same smoothness of slerp, but for larger movements, it may go the opposite way
        capsule.transform.localRotation = Quaternion.AngleAxis(endAngle * percent, axis) * start;

        capsule2.transform.localRotation = Quaternion.Slerp(start, end, percent);

        // always takes same path as slerp, but it gives less consistent speed
        capsule3.transform.localRotation = Quaternion.Lerp(start, end, percent);
    }
}



