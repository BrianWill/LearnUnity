Unity version 2018.1 introduces a few major new features for achieving high performance:

 - The **Job System** farms units of work (jobs) out to threads while helping avoid race conditions.
 - The **Burst compiler** optimizes code using [SIMD instructions](https://en.wikipedia.org/wiki/SIMD) (which are particularly beneficial for math-heavy code). The Burst compiler is not a general-purpose C# compiler: it only works on Job System code written in a subset of C# called HPC# (High Performance C#).
 - **Entity Component System (ECS)** is an architectural pattern in which we lay out data in native (non-garbage collected) memory in the optimal, linear way (meaning tightly packed, contiguous, and accessible in sequence). By separating code from data, ECS not only improves performance, it (arguably) improves code structure over the traditional Object-Oriented approach.

ECS and the Job System can be used separately, but they are highly complementary: ECS guarantees data is layed out linearly in memory, which speeds up job code acessing this data and gives the Burst compiler more optimization opportunities.

 ## Entity Component System

In Unity's traditional programming model, an instance of the GameObject class is a container of instances of the Component class, and the Components have not just data but also methods like *Update()*, which are called in the Unity event loop.

In ECS, an ***entity*** is just a unique ID number, and ***components*** are structs implementing the **IComponentData** interface (which has no required methods): 

- An IComponentData struct can have methods, but Unity itself will not call them.
- A single entity can have any number of associated components but only one component of any particular type. An entity's set of component types is called its ***archetype***. Like the cloumns of a relational table, there is no sense of order amongst the component types of an archetype: given component types A, B, and C, then ABC, ACB, BAC, BCA, CAB, and CBA all describe the same archetype.
- An IComponentData struct should generally be very small, and it should not store references. Large data, like textures and meshes, should not be stored in components.

A ***system*** is a class inheriting from **ComponentSystem**, whose methods like *OnUpdate()* and *OnInitialize()* are called in the system event loop. It's common in these system event methods to access many hundreds or thousands of entities rather than just one or a few.

A **World** stores an EntityManager instance and a set of ComponentSystem instances. Each EntityManager has its own set of entities, so the entities of one World are separate from any other World, and the systems of a World only access entities of that World. A common reason to have more than one world is to separate simulation from presentation, which is particularly useful for networked games because it separates client-only concerns from server concerns. In cases where multiple Worlds need the same ComponentSystem, we give each World its own instance; a ComponentSystem used by just one World usually has just one instance.

### pure ECS vs. hybrid

The old Component types offer tons of functionality: rendering, collisions, physics, audio, animations, *etc.* As of yet, the ECS package has a few stock components and systems for basic rendering but very little else. Consequently, making a 'pure' ECS-only game today requires replicating much core engine functionality yourself. As we'll demonstrate later, a 'hybrid' approach uses both ECS and the old GameObjects/Components. (Just be clear that the more we involve GameObjects, the more we lose linear memory layout.) Over the coming years, the set of stock ECS components and systems should grow to provide much more functionality.

Also understand that the ECS editor workflow is very much a work-in-progress. The Entity Debugger window allows us to inspect systems and entities, but we cannot yet construct scenes out of entities without involving GameObjects.

### native containers



### entity storage

An EntityManager's entities and their components are stored in chunks:

- Each chunk is 16KB.
- A single chunk only stores entities of the same ***archetype***. (Consequently, adding or removing a component type on an entity requires moving it to another chunk!)
- A chunk is divided into parallel arrays: one for each component of the archetype and one array for the entity ids themselves. (These are not normal C# arrays but rather arrays stored directly in the chunk's native-allocated memory.) The arrays are kept tightly packed: when an entity is removed, everything above its slots is shifted down in the arrays to fill the gaps.

For example, say a chunk stores entities of the archetype made up of component types A, B, and C. The chunk then could store approximately:

```csharp
    // 16KB divided by size of each entity
    int maxEntities = 16536 / (sizeof(id) + sizeof(A) + sizeof(B) + sizeof(C));
```

So this chunk is divided into four logical arrays, each *maxEntities* in size: one array for the ids, followed by three arrays, one for each of the component types. The chunk also, of course, stores the offsets to these arrays and the count of entities currently stored. The first entity of the chunk is stored at index 0 of all four of the arrays, the second at index 1, the third at index 2, *etc.* If the chunk has, say, 100 stored entities but we remove the entity at index 37, the entities at indexes 38 and above all get moved down a slot.

Aside from the chunks, an EntityManager also stores an array of EntityData structs:

```csharp

struct EntityData
{
    public Chunk* Chunk;
    public int IndexInChunk;
    public Archetype* Archetype;
    public int Version;
}
```

The EntityData of entity id *n* is stored at index *n* in this array, *e.g.* entity 72's EntityData is stored at index 72. So the ids themselves are implied in this array but not actually stored.

The Chunk field points to the chunk where the entity and its components are actually stored, and IndexInChunk denotes the index within the arrays of that chunk.

A pointer to an entity's archetype is stored in its chunk, but the same pointer is also stored in the EntityData so as to avoid one extra lookup in some common operations.

Not all slots in the EntityData array denote living entities because:

1. entities can be destroyed
2. the array length exceeds the number of created entities (except in the rare case where the number of created entities exactly matches the length of the array)

A free slot is denoted by Chunk being null. The EntityManager keeps track of the first free slot in the array, and a free slot's IndexInChunk field is repurposed to store the index of the next free slot. When new entities are created, they are created in the first free slots, which are quickly found by following this chain of indexes.

But what if an entity is destroyed and then its id reused for a subsequently created entity? How do we avoid confusing the new entity for the old? Well that's why we have the Version field. The Version fields are all initialized to 1, and when an entity is destroyed, its Version is incremented. To reference an entity, we need not just its id but also its version so as to make sure our referenced entity still exists: if we lookup an entity but the version is greater than in our reference, that means the entity we're referencing no longer exists.

When we create more entities than will fit in the EntityData array, it's expanded by copying everything to a new, larger array. The array is never shrunk.

Because the EntityData array stores the indexes of the entities within the chunks, these indexes must be updated when entities are moved within/between chunks. (Recall that many entities may get shifted down slots when an entity is removed from a chunk, hence removing entities is one of the costlier operations.)

### shared components

The ISharedComponentData interface is like IComponentData except entities of a single archetype which have an equal shared component value all share the same value in memory.

A chunk stores only one shared component value of a particular type, and so setting a shared component value on an entity usually requires moving the entity to another chunk. For example, say two entities in a chunk share a FooSharedComponent value. If we set a new FooSharedComponent value on one entity, the other entity still has the old value, and the two values cannot both exist in the same chunk, so the modified entity is moved to a new chunk. The entity manager hashes shared component values to keep track of which chunks store which shared values (we wouldn't want multiple chunks to needlessly store the same shared component values and thereby excessively fragment our entities across chunks).

Shared components are most appropriate for component types which are mutated infrequently and which have the same values across many entities. For example, a component consisting of a single enum field is a good candidate for a shared component because many entities typically would share the same enum values.

### todo

[is an effort made to avoid fragmentation from too many non-full chunks of a given archetype?]








what does it mean for World to be active?


what about arrays? component shouldn't (can't?) have an array because that would be a reference



## the Job System


 ## the Burst compiler

 - no GC
 - no reference types