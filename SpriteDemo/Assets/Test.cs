using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Test : MonoBehaviour {

    public Texture2D tex;

    void Start() {
        Sprite sp = Sprite.Create(
            tex,
            new Rect(40, 0, 275, 269),          // x, y (of lower-left corner), width, height
            new Vector2(0.5f, 0.5f),            // pivot (relative within rect)
            30,                                 // pixels per unit
            0,                                  // extrude
            SpriteMeshType.Tight,            // FullRect or Tight
            new Vector4(20, 20, 80, 20)         // border: left, bottom, right, top (used for 9-slicing)
        );
        

        SpriteRenderer renderer = gameObject.AddComponent<SpriteRenderer>();    
        renderer.sprite = sp;


        Debug.Log("pivot " + sp.pivot);                    // 137.5, 134.5 (float pixel coordinates within Rect)

        // .vertices and .uv are parallel arrays
        for (int i = 0; i < sp.vertices.Length; i++) {
            Debug.Log("vertex " + sp.vertices[i]);
            Debug.Log("uv " + sp.uv[i]);                   // the texture UV coords (parallel array of the vertices)
        }


        //sp.OverrideGeometry(
        //    // in texels relative from bottom-left of Rect; vertices must be in Rect bounds
        //    new Vector2[] { new Vector2(0, 0), new Vector2(200, 200), new Vector2(100, 0) },
        //    // indexes into the vertices; each group of 3 makes one triangle
        //    new ushort[] { 0, 1, 2 }     // one triangle
        //);

        sp.OverrideGeometry(
            new Vector2[] { new Vector2(0, 0), new Vector2(200, 200), new Vector2(100, 0), new Vector2(200, 50) },
            new ushort[] { 0, 1, 2, 1, 2, 3 }   // two adjacent triangles (not required to be adjacent: they can be disconnected or overlap)
        );


        


        List<Vector2[]> l = new List<Vector2[]>();
        // shapes can be convex polygons
        l.Add(new Vector2[] { new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(50, 50), new Vector2(100, 0) });
        // physics geometry vertices needn't lie within Rect
        l.Add(new Vector2[] { new Vector2(300, 300), new Vector2(-150, 150), new Vector2(300, 150) });
        sp.OverridePhysicsShape(l);

        // upon creation, gets geometry from sprite of the spriterenderer,
        // so must be created *after* OverridePhysicsShape
        PolygonCollider2D collider = gameObject.AddComponent<PolygonCollider2D>();
    }



    // Update is called once per frame
    void Update() {

    }
}
