Unity version 2018.1 introduces a few major new features for achieving high performance:

 - The **Job System** farms units of work (jobs) out to threads while helping avoid race conditions.
 - The **Burst compiler** optimizes code using [SIMD instructions](https://en.wikipedia.org/wiki/SIMD) (which are particularly beneficial for math-heavy code). The Burst compiler is not a general-purpose C# compiler: it only works on Job System code written in a subset of C# called HPC# (High Performance C#).
 - **Entity Component System (ECS)** is an architectural pattern in which we lay out data in native (non-garbage collected) memory in the optimal, linear way (meaning tightly packed, contiguous, and accessible in sequence). By separating code from data, ECS not only improves performance, it (arguably) improves code structure over the traditional Object-Oriented approach.

ECS and the Job System can be used separately, but they are highly complementary: ECS guarantees data is layed out linearly in memory, which speeds up job code acessing this data and gives the Burst compiler more optimization opportunities.

 ## Entity Component System

In Unity's traditional programming model, an instance of the GameObject class is a container of instances of the Component class, and the Components have not just data but also methods like *Update()*, which are called in the Unity event loop.

In ECS, an ***entity*** is just a unique ID number, and ***components*** are structs implementing the **IComponentData** interface (which has no required methods): 

- An IComponentData struct can have methods, but Unity itself will not call them.
- A single entity can have any number of associated components but only one component of any particular type. An entity's set of component types is called its ***archetype***. Like the cloumns of a relational table, there is no sense of order amongst the component types of an archtype: given component types A, B, and C, then ABC, ACB, BAC, BCA, CAB, and CBA all describe the same archetype.
- An IComponentData struct should generally be very small, and it should not store references. Large data, like textures and meshes, should not be stored in a component.

A ***system*** is a class inheriting from **ComponentSystem**, whose methods like *OnUpdate()* and *OnInitialize()* are called in the system event loop. It's common in these system event methods to access many hundreds or thousands of entities rather than just one or a few.

A **World** stores an EntityManager instance and a set of ComponentSystem instances. Each EntityManager has its own set of entities, so the entities of one World are separate from any other World, and the systems of a World only access entities of that World. A common reason to have more than one world is to separate simulation from presentation, which is particularly useful for networked games because it separates client-only concerns from server concerns. In cases where multiple Worlds need the same ComponentSystem, we give each World its own instance; a ComponentSystem used by just one World usually has just one instance.

### entity storage

An EntityManager's entities and their components are stored in chunks:

- Each chunk is 16KB.
- A single chunk only stores entities of the same ***archetype***. (Consequently, adding or removing a component type on an entity requires moving it to another chunk.)
- A chunk is divided into parallel arrays for each component of the archetype and one array for the entity ids themselves. (These are not normal C# arrays but rather arrays stored directly in the chunk's native-allocated memory.)
- These arrays are kept tightly packed: when an entity is removed, everything above is shifted down to fill the gap.

For example, say a chunk stores entities of the archetype made up of component types A, B, and C. The chunk then could store approximately:

```csharp
    // 16KB divided by size of each entity
    int maxEntities = 16536 / (sizeof(id) + sizeof(A) + sizeof(B) + sizeof(C));
```

So this chunk is divided into four logical arrays, each *numEntities* in size: one array for the ids, followed by three arrays for each of the component types. Of course the chunk also stores the offsets to these four arrays and the count of entities currently stored. The first entity of the chunk is stored at index 0 of all four of the arrays, the second at index 1, the third at index 2, *etc.* If the chunk has 100 stored entities but we remove the entity at index 37, the entities at indexes 38 and above must all be moved down a slot.

### todo

[is an effort made to avoid fragmentation from too many non-full chunks of a given archetype?]

An EntityManager stores an array used to lookup components by their entity's id, as well as to track which id numbers are in use.


The Entity struct is an ID int and a Version int. When an entity is removed, its ID may be reused for a subsequently created entity, and the version number is incremented for each reuse of an ID.



what does it mean for World to be active?


what about arrays? component shouldn't (can't?) have an array because that would be a reference

### shared components

ISharedComponentData

## the Job System


 ## the Burst compiler

 - no GC
 - no reference types