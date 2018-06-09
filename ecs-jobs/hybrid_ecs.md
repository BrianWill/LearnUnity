[\<\< prev](ecs_jobs.md)

## hybrid ECS

We can add IComponentData struct values to GameObjects by making a MonoBehavior that inherits from ComponentDataWrapper:

```csharp
// a normal component struct
public struct MyComponent : IComponentData
{
    public float val;
}

// Instances of this wrapper can be added to GameObjects.
// The public fields of the wrapped struct are exposed to the inspector.
public class MyComponentWrapper : ComponentDataWrapper<MyComponent>
{}
```

We can also create an entity that mirrors a GameObject by adding a GameObjectEntity component to the GameObject.

[todo: is the entity and gameobject coordinated in any way, or are they just separate representations of the same data? give example]

[todo: is an effort made to avoid fragmentation from too many non-full chunks of a given archetype?]

[todo: ExclusiveEntityTransaction]

[todo: MoveEntitiesFrom]

[why are NativeContainers not blittable?]

[todo: FixedArrayArray is like a special component type]