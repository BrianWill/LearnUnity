[\<\< prev](jobs.md) 
 
## Entity Component System

In Unity's traditional programming model, an instance of the GameObject class is a container of instances of the Component class, and the Components have not just data but also methods like *Update()*, which are called in the Unity event loop.

In ECS, an ***entity*** is just a unique ID number, and ***components*** are structs implementing the **IComponentData** interface (which has no required methods): 

- An IComponentData struct can have methods, but Unity itself will not call them, and the ECS pattern prescribes that we avoid putting logic in our components.
- A single entity can have any number of associated components but only one component of any particular type. An entity's set of component types is called its ***archetype***. Like the columns of a relational table, there is no sense of order amongst the component types of an archetype: given component types A, B, and C, then ABC, ACB, BAC, BCA, CAB, and CBA all describe the same archetype.
- The fields of an IComponentData struct must be [blittable types](https://en.wikipedia.org/wiki/Blittable_types) (reference types are not blittable!).
- An IComponentData struct should generally be very small (under 100 bytes, typically). Large data, like textures and meshes, should only be stored in ISharedComponentData structs (explained later).
- Unlike GameObjects, entities cannot have parents or children.

A ***system*** is a class inheriting from **ComponentSystem**, whose methods *OnUpdate()*, *OnCreateManager()*, and *OnDestroyManager()* are called in the system event loop. It's common in a system's *OnUpdate()* to access many hundreds or thousands of entities rather than just one or a few.

A **World** stores an EntityManager instance and a set of system instances. Each EntityManager has its own set of entities, so the entities of one World are separate from any other World, and the systems of a World only access entities of that World. A common reason to have more than one world is to separate simulation from presentation, which is particularly useful for networked games because it separates client-only concerns from server concerns. (In cases where multiple Worlds need the same ComponentSystem, we give each World its own instance; a ComponentSystem used by just one World would be instantiated only once.)

### pure ECS vs. hybrid ECS

The old Component types offer tons of functionality: rendering, collisions, physics, audio, animations, *etc.* As of yet, the ECS package has a few stock components and systems for basic rendering but has very little else. Consequently, making a 'pure' ECS-only game today requires replicating much core engine functionality yourself. As we'll demonstrate later, a 'hybrid' approach uses both ECS and the old GameObjects/Components. (Just be clear that, the more we involve GameObjects, the more we lose the benefits of linear memory layout.) Eventually, the set of stock ECS components and systems should grow to provide all the functionality of the old GameObjects and their Components.

Also understand that the ECS editor workflow is very much a work-in-progress. The Entity Debugger window allows us to inspect systems and entities, but we cannot yet construct scenes out of entities without involving GameObjects.

### entity storage

An EntityManager's entities and their components are stored in chunks:

- Each chunk is 16KB.
- A single chunk only stores entities of the same archetype. (Consequently, adding or removing a component on an entity requires moving it to another chunk!)
- A chunk is divided into parallel arrays: one for each component type of the archetype and one array for the entity ID's themselves. (These are not normal C# arrays but rather arrays stored directly in the chunk's native-allocated memory.) The arrays are kept tightly packed: when an entity is removed, the last entity in the chunk is moved down to fill the gap.

For example, say a chunk stores entities of the archetype made up of component types A, B, and C. The number of entities the chunk can store is approximately:

```csharp
    // 16KB divided by size of each entity
    int maxEntities = 16536 / (sizeof(id) + sizeof(A) + sizeof(B) + sizeof(C));
```

So this chunk is divided into four logical arrays, each *maxEntities* in size: one array for the ID's, one for the A components, one for the B components, and one for the C components. The chunk also, of course, stores the offsets to these arrays and the count of entities currently stored. The first entity of the chunk is stored at index 0 of all four of the arrays, the second at index 1, the third at index 2, *etc.* If the chunk has, say, 100 stored entities but we then remove the entity at index 37, the entity at index 99 will be moved down to index 37.

![chunk layout](ecs%20slides.png?raw=true)

This chunk layout allows us to very efficiently loop over a set of component types for all entities. For example, to loop over all entities with component types A and B:

```csharp
// pseudocode
for (all chunks of the archetypes that include A and B)
    for (i = 0; i < chunk.count; i++)
        // a and b of the same entity
        var a = chunk.A[i]
        var b = chunk.B[i]
```

This explains why the components are stored in their own arrays: for a chunk with archetype, say, ABCDEFG, we often only want to loop through a subset of the components, like A and B, rather than through all of them. If components of a single entity were instead packed together, looping through a subset of the components would require wastefully accessing the memory of the other components we don't care about.

![chunk traversal](ecs%20slides3.png?raw=true)

The entity manager needs to keep track of which ID's are in use and also needs sometimes to quickly lookup entities by ID. So aside from the chunks, an EntityManager also stores an array of EntityData structs:

```csharp

struct EntityData
{
    public Chunk* Chunk;
    public int IndexInChunk;
    public Archetype* Archetype;
    public int Version;
}
```

The EntityData of entity *n* is stored at index *n* in this array, *e.g.* entity 72's EntityData is stored at index 72. So the ID's themselves are *implied* in this array but not actually stored.

The Chunk field points to the chunk where the entity and its components are actually stored, and IndexInChunk denotes the index of the entity within the arrays of that chunk.

A pointer to an entity's archetype is stored in its chunk, but the same pointer is also stored in the EntityData so as to avoid one extra lookup in some common operations.

Not all slots in the EntityData array denote living entities because:

1. entities can be destroyed
2. the EntityData array's length exceeds the number of created entities (except in the rare case where the number of created entities *exactly* matches the length of the array)

A free slot is denoted by the Chunk field being null. The EntityManager keeps track of the first free slot in the array, and a free slot's IndexInChunk field is repurposed to store the index of the next free slot. When new entities are created, they are created in the first free slots, which are quickly found by following this chain of indexes.

![entitydata array](ecs%20slides2.png?raw=true)

But what if an entity is destroyed and then its ID reused for a subsequently created entity? How do we avoid confusing the new entity for the old? Well in truth, an entity's ID is *really* a combination of its index in the EntityData array *and* its Version. The Version fields are all initialized to 1, and when an entity is destroyed, its Version is incremented. To reference an entity, we need not just its index but also its Version so as to make sure our referenced entity still exists: if we lookup an entity by index but the Version is greater than in our reference, that means the entity we're referencing no longer exists.

When we create more entities than will fit in the EntityData array, the array is expanded by copying everything to a new, larger array. The array is never shrunk.

Because the EntityData array stores the indexes of the entities within the chunks, these indexes must be updated when entities are moved within or between chunks. (Recall that another entity gets moved down to fill the slot when an entity is removed from a chunk, hence moving entities is one of the costlier operations.)

### shared components

For some components whose values frequently reoccur among entities, we'd like to avoid storing those values repeatedly in memory. The ISharedComponentData interface does just that: entities of the same archetype which have equal values of an ISharedComponentData type all share that value in memory rather than have their own separate copies.

A single chunk can store only one value of each ISharedComponentData type, and so setting a shared component value on an entity usually requires moving the entity to another chunk. If we have two entities in the same chunk and set a new shared component value on one of them, the other entity still has the old value, and so the modified entity must be moved to a new chunk.

The entity manager hashes shared component values to keep track of which chunks store which shared values. (We wouldn't want multiple chunks to needlessly store the same shared component values and thereby excessively fragment our entities across chunks.)

Unlike regular components, shared components need not be blittable and so can store references into native and managed memory.

Shared components are most appropriate for component types which are mutated infrequently and which have the same values across many entities. For example, a component consisting of a single enum field is a good candidate for a shared component because many entities typically would share the same enum values.

### data modeling guidelines

Our (non-shared) components cannot store arrays or collections of any kind, including native containers. What we *can* store in our components, though, are entity ID's and indexes into native containers.

Say in a deathmatch game, the player who killed another the most times is that other player's nemesis. A single player can be the nemesis of multiple other players, so this is a one-to-many relationship. The most obvious way to represent this is to give the player component a Nemesis field to store the entity ID of the player's nemesis (with ID index -1 denoting that the player has no nemesis).

However, when looping through all player entities, understand that following these Nemesis references means jumping around memory, thus largely defeating the benefits of linear memory layout. (In fact, an entity lookup through the EntityManager is actually a bit more costly than following ordinary memory references because it requires looking in the EntityData array to find the entity's Chunk and IndexInChunk rather than just directly reading an address.) Sometimes non-linear access is necessary, but just be clear we should endeavor to minimize non-linear access.

What about many-to-many relationships? Say we have many characters, and each character has an inventory of items. One solution is the entity equivalent of a relational join table: for every item in a character's inventory, we'd have an entity with an Ownership component referencing an owner entity and an item entity.

An obvious issue with this arrangement is that common queries like *'Does this character have this item?'* require traversing all of these entities.

[use nativecontainers for alternate data structures? or use entities + nativeContainers for cache/lookups? is there benefit in always using entities as the authoritative source?]

[depends on scale, of course: if you have only a few characters with small inventories, the join entities may be perfectly fine by themselves]

### API examples

#### IComponentData

```csharp
struct MyComponent : IComponentData {
    public float A;
    public byte B;
    public MyStruct C;   // must be a struct with only blittable fields
}
```

Because we usually don't give the components methods or properties, it generally doesn't make sense to make any field non-public.

The fields must be [blittable types](https://docs.microsoft.com/en-us/dotnet/framework/interop/blittable-and-non-blittable-types).

#### ISharedComponentData

```csharp
struct MySharedComponent : ISharedComponentData 
{
    public string A;             // field types needn't be blittable
    public NativeArray<int> B;   // OK for native containers
    public Mesh C;               // OK for storing large data
}
```

#### ComponentSystem

```csharp
public class MySystem: ComponentSystem
{
    public void OnCreateManager(int capacity)
    {
        // called upon creation
        // (capacity is deprecated?)
    }

    protected override void OnDestroyManager()
    {
        // called upon destruction
    }

    protected override void OnUpdate()
    {
        // called every frame
    }
}
```

The bootstraping process automatically creates an instance of each ComponentSystem and adds the instances to the default World.

Execution order of systems is automatically optimized, but we can control the order when needed with attributes:

```csharp
// MySystem will update at some point before SomeSystem 
[UpdateBefore(typeof(SomeSystem))]   
// MySystem will update at some point after OtherSystem 
[UpdateAfter(typeof(OtherSystem))]   
public class MySystem: ComponentSystem
{ 
    // ...
}
```

Systems can belong to groups. System and groups can be ordered relative to other systems and groups. A group is denoted simply by an empty class:

```csharp
// systems in MyUpdateGroup will update at some point before OtherGroup 
[UpdateBefore(typeof(OtherGroup))]
// systems in MyUpdateGroup will update at some point after SomeSystem
[UpdateAfter(typeof(SomeSystem))]
public class MyUpdateGroup
{}

// MySystem belongs to MyUpdateGroup
[UpdateInGroup(typeof(MyUpdateGroup))]
public class MySystem: ComponentSystem
{
    // ...
}
```

Contradictory orderings, such as A-before-B while also B-before-A, trigger runtime errors.

#### EntityManager

```csharp
World world = World.Active;  // the default World
EntityManager em = world.GetOrCreateManager<EntityManager>();

// create an entity with no components (yet)
Entity entity = em.CreateEntity();
// the index and version together form a logical entity ID
int index = entity.Index;     
int version = entity.Version;

// add a component to an existing entity
em.AddComponent<MyComponent>(entity, new MyComponent());

// get the component of an entity
MyComponent myComp = em.GetComponentData<MyComponent>(entity);

// set new value for the component
// (throws exception if entity does not already have component of type MyComponent)
em.SetComponentData<MyComponent>(entity, myComp);

// remove a component
// (throws exception if entity does not have component of type MyComponent)
em.RemoveComponent<MyComponent>(entity);

// true if the entity exists and has component of type MyComponent
bool has = em.HasComponent<MyComponent>(entity);

// true if entity exists
bool exists = em.Exists(entity);

// create new entity which is copy of existing entity (same components and data, but new ID)
Entity entity2 = em.Instantiate(entity);

// destroy an entity
em.DestroyEntity(entity2);
```

#### ComponentGroup

```csharp
public class MySystem : ComponentSystem
{
    ComponentGroup group;

    public void OnCreateManager(int capacity)
    {
        // ComponentGroup is a class and so GC'd
        // (relatively expensive to create, so should not be created in OnUpdate)
        group = GetComponentGroup(
            typeof(FooComponent),
            typeof(BarComponent)
        );
    }

    protected override void OnUpdate()
    {
        // get iterators from the ComponentGroup
        EntityArray entities = group.GetEntityArray();
        ComponentDataArray<FooComponent> foos = group.GetComponentDataArray<FooComponent>();
        ComponentDataArray<BarComponent> bars = group.GetComponentDataArray<BarComponent>();

        // loop through every entity with a Foo and a Bar
        // (the iterators are all parallel and so all have same length)
        for (int i = 0; i != entities.length; i++) 
        {
            Entity e = entities[i];
            FooComponent f = foos[i];
            BarComponent b = bars[i];
            // ...
        }
    }
}
```

We can mark component types as read only and exclude entities which include specified types:

```csharp
// This group matches entities which have a Bar and Ack component but no Foo component.
// The Ack components can only be read, not written.
ComponentGroup group = GetComponentGroup(
    ComponentType.Subtractive(typeof(Foo)),
    typeof(Bar),
    ComponentType.ReadOnly(typeof(Ack))
);
```

[todo: filters]

#### EntityCommandBuffer

An EntityCommandBuffer queues up EntityManager add/remove/set operations to be performed later. This is useful because:

1. these operations invalidate ComponentGroup iterators, and so we can't otherwise perform these operations while looping through entity components
2. batching these operations can improve performance
3. jobs cannot do these operations directly

The EntityCommandBuffer methods are:

- *CreateEntity()*
- *RemoveEntity()*
- *AddComponent()*
- *SetComponent()*
- *Flush()* &mdash; enact all the queued commands and dispose of the buffer

The EntityCommandBuffer methods are not thread safe, so separate threads should use separate EntityCommandBuffers.

A ComponentSystem has an EntityCommandBuffer field called *PostUpdateCommands*. Operations queued on this buffer during *OnUpdate()* will be applied immediatly after the update.

#### injecting systems

We can inject systems into other systems, which is useful for accessing their fields:

```csharp
public class MySystem : ComponentSystem
{
    [Inject]
    private OtherSystem otherSystem;    // useful for accesing fields of OtherSystem

    protected override void OnUpdate()
    {
        // ...
    }
]
```

#### injecting component groups

Rather than explicitly create a component group, we can get the same iterators through injection: 

```csharp
public class MySystem : ComponentSystem

    public struct MyGroup
    {
        EntityArray Entities;
        [ReadOnly] ComponentDataArray<FooComponent> Foos;
        ComponentDataArray<BarComponent> Bars;
        public int Length;
    }

    [Inject]
    private MyGroup group;   // creates iterators from ComponentGroup implied by struct fields
    
    protected override void OnUpdate()
    {
        // the iterators are created for us before every OnUpdate
        for (int i = 0; i != group.Length; i++) 
        {
            Entity e = group.Entities[i];
            FooComponent f = group.Foos[i];  // Foos is read only
            BarComponent b = group.Bars[i];
            // ...
        }
    }
```

There is as of yet no injection equivalent for ComponentType.Subtractive, and we cannot set filters on an injected ComponentGroup.

#### BarrierSystem

A BarrierSystem is a kind of system whose update cannot be overridden and so a barrier class is always left empty:

```csharp
// we usually want to specify when a barrier updates
[UpdateAfter(typeof(MySystem))]
class MyBarrier : BarrierSystem
{
    // leave me empty!
}
```

A BarrierSystem's *CreateCommandBuffer()* method returns an EntityCommandBuffer that will be flushed in the barrier's update:

```csharp
class MySystem : ComponentSystem
{
    [Inject]
    private MyBarrier myBarrier;

    protected override void OnCreateManager(int capacity)
    {
        EntityCommandBuffer commands = myBarrier.CreateCommandBuffer();
        // ... commands will be flushed when MyBarrier updates
    }
}
```

Effectively, a barrier is a coordination point in our system update loop: entity component changes enqueued in previous system updates can be enacted in a barrier's update.

The EndFrameBarrier is created for us and updates very last thing in a frame, after all other systems and after rendering. (Actually, EndFrameBarrier is updated as the very *first* thing in a frame, but logically that's the same point.)

[next \>\>](ecs_jobs.md)
