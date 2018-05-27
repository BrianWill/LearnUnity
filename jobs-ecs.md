Unity version 2018.1 introduces a few major new features for achieving high performance:

 - The **Job System** farms units of work called 'jobs' out to threads while helping us avoid race conditions.
 - The **Burst compiler** optimizes code using [SIMD instructions](https://en.wikipedia.org/wiki/SIMD), which are particularly beneficial for math-heavy code. The Burst compiler is not a general-purpose C# compiler: it only works on Job System code written in a subset of C# called HPC# (High Performance C#).
 - **ECS (Entity Component System)** is an architectural pattern in which we lay out data in native (non-garbage collected) memory in the optimal, linear fashion: tightly packed, contiguous, and accessible in sequence. By separating code from data, ECS not only improves performance, it (arguably) improves code structure over the traditional Object-Oriented approach.

ECS and the Job System can be used separately, but they are highly complementary: ECS guarantees data is layed out linearly in memory, which speeds up job code accessing the data and gives the Burst compiler more optimization opportunities.

 ## Entity Component System

In Unity's traditional programming model, an instance of the GameObject class is a container of instances of the Component class, and the Components have not just data but also methods like *Update()*, which are called in the Unity event loop.

In ECS, an ***entity*** is just a unique ID number, and ***components*** are structs implementing the **IComponentData** interface (which has no required methods): 

- An IComponentData struct can have methods, but Unity itself will not call them.
- A single entity can have any number of associated components but only one component of any particular type. An entity's set of component types is called its ***archetype***. Like the cloumns of a relational table, there is no sense of order amongst the component types of an archetype: given component types A, B, and C, then ABC, ACB, BAC, BCA, CAB, and CBA all describe the same archetype.
- An IComponentData struct should generally be very small, and it should not store references. Large data, like textures and meshes, should not be stored in components.
- Unlike GameObjects, entities cannot have parents or children.

A ***system*** is a class inheriting from **ComponentSystem**, whose methods *OnUpdate()*, *OnCreateManager()*, and *OnDestroyManager()* are called in the system event loop. It's common in system updates to access many hundreds or thousands of entities rather than just one or a few.

A **World** stores an EntityManager instance and a set of ComponentSystem instances. Each EntityManager has its own set of entities, so the entities of one World are separate from any other World, and the systems of a World only access entities of that World. A common reason to have more than one world is to separate simulation from presentation, which is particularly useful for networked games because it separates client-only concerns from server concerns. In cases where multiple Worlds need the same ComponentSystem, we give each World its own instance; a ComponentSystem used by just one World usually has just one instance.

### pure ECS vs. hybrid

The old Component types offer tons of functionality: rendering, collisions, physics, audio, animations, *etc.* As of yet, the ECS package has a few stock components and systems for basic rendering but has very little else. Consequently, making a 'pure' ECS-only game today requires replicating much core engine functionality yourself. As we'll demonstrate later, a 'hybrid' approach uses both ECS and the old GameObjects/Components. (Just be clear that the more we involve GameObjects, the more we lose the benefits of linear memory layout.) Eventually, the set of stock ECS components and systems should grow to provide all the functionality of the old GameObjects and their Components.

Also understand that the ECS editor workflow is very much a work-in-progress. The Entity Debugger window allows us to inspect systems and entities, but we cannot yet construct scenes out of entities without involving GameObjects.

### native containers

Not every piece of data fits well into the mold of entities and components, which is one reason why Unity provides a set of 'native containers': basic data structures implemented as structs pointing into native memory. As this memory is not garbage-collected, it's your responsibility to deallocate any native container (by calling its *Dispose()* method) when it's no longer needed.

The provided native container types are:

- NativeArray
- NativeSlice (logical indices into a NativeArray)
- NativeList
- NativeHashMap
- NativeQueue
- NativeMultiHashMap

You can implement your own native containers as described [here](https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/master/Documentation/content/custom_job_types.md#custom-nativecontainers).

Because they're not blitable, native containers cannot be stored in components (and besides, we shouldn't store large data in components). Instead, long-lived native containers are generally stored in the systems themselves.

[why are NativeContainers not blitable?]

As we'll discuss with the Job System, the native containers have thread-safety semantics.

### entity storage

An EntityManager's entities and their components are stored in chunks:

- Each chunk is 16KB.
- A single chunk only stores entities of the same ***archetype***. (Consequently, adding or removing a component on an entity requires moving it to another chunk!)
- A chunk is divided into parallel arrays: one for each component of the archetype and one array for the entity id's themselves. (These are not normal C# arrays but rather arrays stored directly in the chunk's native-allocated memory.) The arrays are kept tightly packed: when an entity is removed, everything above its slots is shifted down in the arrays to fill the gaps.

For example, say a chunk stores entities of the archetype made up of component types A, B, and C. The number of entities the chunk can store is approximately:

```csharp
    // 16KB divided by size of each entity
    int maxEntities = 16536 / (sizeof(id) + sizeof(A) + sizeof(B) + sizeof(C));
```

So this chunk is divided into four logical arrays, each *maxEntities* in size: one array for the id's, followed by three arrays, one for each of the component types. The chunk also, of course, stores the offsets to these arrays and the count of entities currently stored. The first entity of the chunk is stored at index 0 of all four of the arrays, the second at index 1, the third at index 2, *etc.* If the chunk has, say, 100 stored entities but we remove the entity at index 37, the entities at indexes 38 and above all get moved down a slot.

What this chunk layout allows us to do very efficiently is loop over a set of component types for all entities. For example, to loop over all entities with component types A and B:

```csharp
// pseudocode
for (all chunks of archetypes including A and B)
    for (i = 0; i < chunk.count; i++)
        // a and b of the same entity
        var a = chunk.A[i]
        var b = chunk.B[i]
```

This explains why the components are stored in their own arrays: for a chunk with archetype, say, ABCDEFG, we often only want to loop through a subset of the components, like A and B, rather than through all of them. If instead components of a single entity were packed together, looping through a subset of the components would require wastefully accessing the memory of other components.

The entity manager needs to keep track of which ids are in use and also needs sometimes to quickly lookup entities by id. So aside from the chunks, an EntityManager also stores an array of EntityData structs:

```csharp

struct EntityData
{
    public Chunk* Chunk;
    public int IndexInChunk;
    public Archetype* Archetype;
    public int Version;
}
```

The EntityData of entity *n* is stored at index *n* in this array, *e.g.* entity 72's EntityData is stored at index 72. So the id's themselves are implied in this array but not actually stored.

The Chunk field points to the chunk where the entity and its components are actually stored, and IndexInChunk denotes the index within the arrays of that chunk.

A pointer to an entity's archetype is stored in its chunk, but the same pointer is also stored in the EntityData so as to avoid one extra lookup in some common operations.

Not all slots in the EntityData array denote living entities because:

1. entities can be destroyed
2. the array length exceeds the number of created entities (except in the rare case where the number of created entities *exactly* matches the length of the array)

A free slot is denoted by the Chunk field being null. The EntityManager keeps track of the first free slot in the array, and a free slot's IndexInChunk field is repurposed to store the index of the next free slot. When new entities are created, they are created in the first free slots, which are quickly found by following this chain of indexes.

But what if an entity is destroyed and then its id reused for a subsequently created entity? How do we avoid confusing the new entity for the old? Well in truth, an entity's id is *really* a combination of its index in the EntityData array *and* its Version. The Version fields are all initialized to 1, and when an entity is destroyed, its Version is incremented. To reference an entity, we need not just its index but also its version so as to make sure our referenced entity still exists: if we lookup an entity by index but the version is greater than in our reference, that means the entity we're referencing no longer exists.

When we create more entities than will fit in the EntityData array, the array is expanded by copying everything to a new, larger array. The array is never shrunk.

Because the EntityData array stores the indexes of the entities within the chunks, these indexes must be updated when entities are moved within/between chunks. (Recall that many entities may get shifted down slots when an entity is removed from a chunk, hence removing entities is one of the costlier operations.)

### shared components

For some components whose values frequently reoccur among entities, we'd like to avoid storing those values repeatedly in memory. The ISharedComponentData interface does just that: entities of the same archetype which have equal values of an ISharedComponentData type all share that value in memory rather than each having its own copy.

A chunk stores only one shared component value of a particular type, and so setting a shared component value on an entity usually requires moving the entity to another chunk. Say two entities in a chunk share a FooSharedComponent value: if we set a new FooSharedComponent value on one entity, the other entity still has the old value, and the two values cannot both exist in the same chunk, so the modified entity is moved to a new chunk.

The entity manager hashes shared component values to keep track of which chunks store which shared values. (We wouldn't want multiple chunks to needlessly store the same shared component values and thereby excessively fragment our entities across chunks.)

Unlike regular components, shared components need not be blitable and so can store references into native and managed memory.

[can we access sharedcomponents in jobs if they're non-blitable?] 

Shared components are most appropriate for component types which are mutated infrequently and which have the same values across many entities. For example, a component consisting of a single enum field is a good candidate for a shared component because many entities typically would share the same enum values.

### data modeling guidelines

Our (non-shared) components cannot store arrays or collections of any kind, including native containers. What we *can* store in our components, though, are entity ids and indexes into native containers.

Say in a deathmatch game, the player who killed another the most times is that other player's nemesis. A single player can be the nemesis of multiple other players, so this is a one-to-many relationship. The most obvious way to represent this is to give the player component a Nemesis field to store the entity id of the player's nemesis (with id index -1 denoting that the player has no nemesis).

However, when looping through all player entities, understand that following these Nemesis references means jumping around memory, thus largely defeating the benefits of linear memory layout. (In fact, an entity lookup through the EntityManager is actually a bit more costly than following ordinary memory references because it requires looking in the EntityData array to find the entity's Chunk and IndexInChunk rather than just directly reading an address.) Sometimes non-linear access is necessary, but just be clear we should endeavor to minimize non-linear access.

What about many-to-many relationships? Say we have many characters, and each character has an inventory of items. One solution is the entity equivalent of a relational join table: for every item in a character's inventory, we'd have an entity with an Ownership component referencing an owner entity and an item entity.

An obvious issue with this arrangement is that common queries like *'Does this character have this item?'* require traversing all of these entities.

[use nativecontainers for alternate data structures? or use entities + nativeContainers for cache/lookups? is there benefit in always using entities for authoritative data?]

[depends on scale, of course: if you have only a few characters with small inventories, the join entities may be perfectly fine by themselves]

[a single-dimension array of a blitable type is itself blitable, right? so is it allowed in component? test this]

### integrating ECS with GameObjects/Components

As mentioned, ECS as of yet has few stock components and systems, and so lacks most of the functionality of the old GameObjects and their Components. Thus we may wish to still use GameObjects along with ECS.

### API examples

#### IComponentData

```csharp
struct MyComponent : IComponentData {
    public float A;
    public byte B;
    public OtherComponent C;   // the fields of OtherComponent struct must be blitable
}
```

Because we generally don't give the components methods or properties, it generally doesn't make sense to make any field non-public.

The fields must be [blitable types](https://docs.microsoft.com/en-us/dotnet/framework/interop/blittable-and-non-blittable-types).

#### ISharedComponentData

```csharp
public struct MySharedComponent : ISharedComponentData 
{
    public string A;             // can store non-blitable types
    public NativeArray<int> B;   // can store native containers
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

Contradictory orderings, such as A-before-B but B-before-A, trigger runtime errors.

#### EntityManager

Static methods of EntityManager allow 

```csharp
World world = World.Active;  // the default World
EntityManager em = world.GetOrCreateManager<EntityManager>();

// create an entity with no components (yet)
Entity entity = em.CreateEntity();
// the index and version together form a logical entity id
int index = entity.Index;     
int version = entity.Version;

// add a component to an existing entity
em.AddComponent(entity, new MyComponent());

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

// true if entity exists (false if never existed or if has been destroyd)
bool exists = em.Exists(entity);

// create new entity which is copy of existing entity (same components and data, but new id)
Entity entity2 = em.Instantiate(entity);

// destroy an entity
em.DestroyEntity(entity2);
```

#### ComponentGroup

```csharp
public MySystem : ComponentSystem

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
```

#### EntityCommandBuffer






injection

    can inject one system into another (why? so as to share fields?)




ExclusiveEntityTransaction

 MoveEntitiesFrom




### todo

[is an effort made to avoid fragmentation from too many non-full chunks of a given archetype?]

what does it mean for World to be active? is it just the default world for static EntityManager calls?

how to create worlds and copy entities between?



how much to lean on native containers?

only blitable types in components, but shouldn't a native container be blitable? it's not managed memory, so is the error message just misleading?

when is it ok to store entity references? doesn't looking up entities by id in our loop nullify linear memory benefits?



### the ComponentSystem class


[can't have booleans because they're not blitable, instead use enum of struct of byte?]



## the Job System


 ## the Burst compiler

 - no GC
 - no reference types