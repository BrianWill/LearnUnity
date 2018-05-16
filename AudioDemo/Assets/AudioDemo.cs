using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class AudioDemo : MonoBehaviour {

    public AudioClip clip1;
    public AudioClip clip2;
    public AudioSource source;


    // Update is called once per frame
    void Update() {
        if (Input.GetKeyDown(KeyCode.Alpha1)) {
            source.PlayOneShot(clip1);
            Debug.Log("Play clip 1");
        }

        if (Input.GetKeyDown(KeyCode.Alpha2)) {
            source.PlayOneShot(clip2);
            Debug.Log("Play clip 2");
        }

        if (Input.GetKeyDown(KeyCode.Z)) {
            source.Play();
            Debug.Log("Play");
        }

        if (Input.GetKeyDown(KeyCode.X)) {
            source.Pause();
            Debug.Log("Pause");
        }

        if (Input.GetKeyDown(KeyCode.C)) {
            source.UnPause();
            Debug.Log("UnPause");
        }

        if (Input.GetKeyDown(KeyCode.V)) {
            source.Stop();
            Debug.Log("Stop");
        }
    }
}
