Unity version 2018.1 introduces a few major new features for achieving high performance:

 - **the Job System** farms units of work (jobs) out to threads while helping avoid race conditions
 - **the Burst compiler** optimizes code using [SIMD instructions](https://en.wikipedia.org/wiki/SIMD) (which are particularly beneficial for math-heavy code). The Burst compiler is not a general-purpose C# compiler: it only works on Job System code written in a subset of C# called HPC# (High Performance C#).
 - **Entity Component System (ECS)** is an architectural pattern in which we lay out data in native (non-garbage collected) memory in the optimal, linear way (meaning tightly packed, contiguous, and accessible in sequence). By separating code from data, ECS not only improves performance, it (arguably) improves code structure over the traditional Object-Oriented approach.

ECS and the Job System can be used separately, but they are highly complementary: ECS guarantees data is layed out linearly in memory, which speeds up job code acessing this data and gives the BURST compiler more optimization opportunities.

 ## Entity Component System

In Unity's traditional programming model, an instance of the GameObject class is a container of instances of the Component class, and the Components have not just data but also methods like *Update()*, which are called in the Unity event loop.

In ECS, an ***entity*** is just a unique ID number, and ***components*** are structs implementing the **IComponentData** interface (which has no required methods): 

- An IComponentData struct can have methods, but Unity itself will not call them.
- A single entity can have any number of components but only one component of any particular type.
- An IComponentData struct should generally be very small, and it should generally not store references, especially to managed memory.
- An entity's set of component types is called its ***archetype***. Like the cloumns of a relational table, there is no sense of order amongst the component types of an archtype: given component types A, B, and C, then ABC, ACB, BAC, BCA, CAB, and CBA all describe the same archetype.

A ***system*** is a class inheriting from **ComponentSystem**, whose methods like *OnUpdate()* and *OnInitialize()* are called in the system event loop. These system event methods typically access many entities.

A **World** stores an EntityManager instance and a set of ComponentSystem instances. Each EntityManager has its own set of entities, so the entities of one World are separate from any other World, and the systems of a World only accesses entities of that World. A common reason to have more than one world is to separate simulation from presentation, which is particularly useful for networked games because it separates client-only concerns from server concerns. In cases where multiple Worlds need the same ComponentSystem, we give each World its own instance; otherwise, it is usual to have just one instance of a ComponentSystem.

### entity storage

An EntityManager's entities and their components are stored in chunks:

- each chunk is 16KB
- a single chunk only stores entities of the same ***archetype*** (thus adding or removing component types on an entity requires moving it to another chunk)
- a chunk is divided into parallel arrays for each component of the archetype and one array for the entity ids themselves
- these arrays are kept tightly packed: when an entity is removed, everything above is shifted down to fill the gap


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