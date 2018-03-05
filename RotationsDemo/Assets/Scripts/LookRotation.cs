using System.Collections;
using UnityEngine;

public class LookRotation : MonoBehaviour {
    public Transform capsule1;
    public Transform capsule2;

    private void Start() {
        StartCoroutine(RandomLook());
    }

    private IEnumerator RandomLook() {
        while (true) {
            Vector3 a = new Vector3(Random.Range(-10, 10),
                Random.Range(-10, 10),
                Random.Range(-10, 10));
            Vector3 b = new Vector3(Random.Range(-10, 10),
                Random.Range(-10, 10),
                Random.Range(-10, 10));

            Vector3 c = b;          
            Vector3.OrthoNormalize(ref a, ref c);    // c is b swung unto plane perpendicular from a
            a = a.normalized * 5;  // set consistent length
            b = b.normalized * 5;
            c *= 5;            

            capsule1.transform.localRotation = Quaternion.LookRotation(a, b);
            capsule2.transform.localRotation = MyLookRotation(a, b);

            const float time = 3;
            Debug.DrawLine(Vector3.zero, a, Color.green, time);
            Debug.DrawLine(Vector3.zero, b, Color.cyan, time);
            Debug.DrawLine(Vector3.zero, c, Color.magenta, time);
            yield return new WaitForSeconds(time);
        }
    }

    private Quaternion MyLookRotation(Vector3 handle, Vector3 up) {
        // Can't use FromToRotation to find pitch and yaw because it may apply spin.
        // There's an infinite number of rotations that gets a single point from position a to b!
        // (Imagine the vector to that axis and spinning around that axis.)

        // handle rotation
        float pitchAngle = Mathf.Rad2Deg * Mathf.Atan2(-handle.y, new Vector2(handle.x, handle.z).magnitude);
        float yawAngle;
        if (handle.x == 0) {
            yawAngle = handle.z >= 0 ? 0 : 180;
        } else {
            if (handle.z == 0) {
                yawAngle = handle.x > 0 ? 90 : -90;
            }
            yawAngle = Mathf.Rad2Deg * Mathf.Atan2(handle.x, handle.z);
        }
        Quaternion handleRotation = Quaternion.Euler(pitchAngle, yawAngle, 0);

        // spin around the handle
        Vector3 handleUp = Vector3.up;
        Vector3.OrthoNormalize(ref handle, ref handleUp);
        Vector3.OrthoNormalize(ref handle, ref up);
        float spinAngle = Vector3.SignedAngle(handleUp, up, handle);
        Quaternion spinRotation = Quaternion.AngleAxis(spinAngle, handle);

        return spinRotation * handleRotation;
    }    
}


