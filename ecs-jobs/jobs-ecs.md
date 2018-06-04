Unity version 2018.1 introduces a few major new features for achieving high performance:

 - The **Job System** farms units of work called 'jobs' out to threads while helping us avoid race conditions.
 - The **Burst compiler** optimizes code using [SIMD instructions](https://en.wikipedia.org/wiki/SIMD), which are particularly beneficial for math-heavy code. The Burst compiler is not a general-purpose C# compiler: it only works on job code, which is written in a subset of C# called HPC# (High Performance C#).
 - **ECS (Entity Component System)** is an architectural pattern in which we lay out data in native (non-garbage collected) memory in the optimal, linear fashion: tightly packed, contiguous, and accessible in sequence. By separating code from data, ECS not only improves performance, it (arguably) improves code structure over the traditional Object-Oriented approach.

ECS and the Job System can be used separately, but they are highly complementary: ECS guarantees data is layed out linearly in memory, which speeds up job code accessing the data and gives the Burst compiler more optimization opportunities.

 ## Entity Component System

In Unity's traditional programming model, an instance of the GameObject class is a container of instances of the Component class, and the Components have not just data but also methods like *Update()*, which are called in the Unity event loop.

In ECS, an ***entity*** is just a unique ID number, and ***components*** are structs implementing the **IComponentData** interface (which has no required methods): 

- An IComponentData struct can have methods, but Unity itself will not call them.
- A single entity can have any number of associated components but only one component of any particular type. An entity's set of component types is called its ***archetype***. Like the cloumns of a relational table, there is no sense of order amongst the component types of an archetype: given component types A, B, and C, then ABC, ACB, BAC, BCA, CAB, and CBA all describe the same archetype.
- An IComponentData struct should generally be very small (under 100 bytes, let's say), and it should not store references. Large data, like textures and meshes, should not be stored in components.
- Unlike GameObjects, entities cannot have parents or children.

A ***system*** is a class inheriting from **ComponentSystem**, whose methods *OnUpdate()*, *OnCreateManager()*, and *OnDestroyManager()* are called in the system event loop. It's common in a system's *OnUPdate()* to access many hundreds or thousands of entities rather than just one or a few.

A **World** stores an EntityManager instance and a set of ComponentSystem instances. Each EntityManager has its own set of entities, so the entities of one World are separate from any other World, and the systems of a World only access entities of that World. A common reason to have more than one world is to separate simulation from presentation, which is particularly useful for networked games because it separates client-only concerns from server concerns. In cases where multiple Worlds need the same ComponentSystem, we give each World its own instance; a ComponentSystem used by just one World usually has just one instance.

### pure ECS vs. hybrid ECS

The old Component types offer tons of functionality: rendering, collisions, physics, audio, animations, *etc.* As of yet, the ECS package has a few stock components and systems for basic rendering but has very little else. Consequently, making a 'pure' ECS-only game today requires replicating much core engine functionality yourself. As we'll demonstrate later, a 'hybrid' approach uses both ECS and the old GameObjects/Components. (Just be clear that, the more we involve GameObjects, the more we lose the benefits of linear memory layout.) Eventually, the set of stock ECS components and systems should grow to provide all the functionality of the old GameObjects and their Components.

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

As we'll see with the Job System, the native containers have runtime thread-safety checks enabled in the editor which catch improper concurrent access.

When creating a native container, we specify one of three allocators from which to allocate its memory:

- **Allocator.Temp**: fastest allocation. Temp allocations should not live longer than the frame, and so you should generally dispose of a Temp native container in the same method call in which it's created.
- **Allocator.TempJob**: slower allocation. The safety checks throw an exception if a TempJob allocation lives longer than 4 frames.
- **Allocator.Persistent**: slowest allocation (basically just a wrapper for malloc). Lives indefinitely.

Example:

```csharp
// a Temp array of 5 floats
NativeArray<float> result = new NativeArray<float>(5, Allocator.Temp);
```

### entity storage

An EntityManager's entities and their components are stored in chunks:

- Each chunk is 16KB.
- A single chunk only stores entities of the same archetype. (Consequently, adding or removing a component on an entity requires moving it to another chunk!)
- A chunk is divided into parallel arrays: one for each component type of the archetype and one array for the entity id's themselves. (These are not normal C# arrays but rather arrays stored directly in the chunk's native-allocated memory.) The arrays are kept tightly packed: when an entity is removed, everything above its slots is shifted down in the arrays to fill the gaps.

For example, say a chunk stores entities of the archetype made up of component types A, B, and C. The number of entities the chunk can store is approximately:

```csharp
    // 16KB divided by size of each entity
    int maxEntities = 16536 / (sizeof(id) + sizeof(A) + sizeof(B) + sizeof(C));
```

So this chunk is divided into four logical arrays, each *maxEntities* in size: one array for the id's, one for the A components, one for the B components, and one for the C components. The chunk also, of course, stores the offsets to these arrays and the count of entities currently stored. The first entity of the chunk is stored at index 0 of all four of the arrays, the second at index 1, the third at index 2, *etc.* If the chunk has, say, 100 stored entities but we then remove the entity at index 37, the entities at indexes 38 through 99 all get moved down a slot.

![chunk layout](ecs%20slides.png?raw=true)

What this chunk layout allows us to do very efficiently is loop over a set of component types for all entities. For example, to loop over all entities with component types A and B:

```csharp
// pseudocode
for (all chunks of the archetypes that include A and B)
    for (i = 0; i < chunk.count; i++)
        // a and b of the same entity
        var a = chunk.A[i]
        var b = chunk.B[i]
```

This explains why the components are stored in their own arrays: for a chunk with archetype, say, ABCDEFG, we often only want to loop through a subset of the components, like A and B, rather than through all of them. If instead components of a single entity were packed together, looping through a subset of the components would require wastefully accessing the memory of other components.

![chunk traversal](ecs%20slides3.png?raw=true)

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

The EntityData of entity *n* is stored at index *n* in this array, *e.g.* entity 72's EntityData is stored at index 72. So the id's themselves are *implied* in this array but not actually stored.

The Chunk field points to the chunk where the entity and its components are actually stored, and IndexInChunk denotes the index of the entity within the arrays of that chunk.

A pointer to an entity's archetype is stored in its chunk, but the same pointer is also stored in the EntityData so as to avoid one extra lookup in some common operations.

Not all slots in the EntityData array denote living entities because:

1. entities can be destroyed
2. the EntityData array's length exceeds the number of created entities (except in the rare case where the number of created entities *exactly* matches the length of the array)

A free slot is denoted by the Chunk field being null. The EntityManager keeps track of the first free slot in the array, and a free slot's IndexInChunk field is repurposed to store the index of the next free slot. When new entities are created, they are created in the first free slots, which are quickly found by following this chain of indexes.

![entitydata array](ecs%20slides2.png?raw=true)

But what if an entity is destroyed and then its id reused for a subsequently created entity? How do we avoid confusing the new entity for the old? Well in truth, an entity's id is *really* a combination of its index in the EntityData array *and* its Version. The Version fields are all initialized to 1, and when an entity is destroyed, its Version is incremented. To reference an entity, we need not just its index but also its version so as to make sure our referenced entity still exists: if we lookup an entity by index but the version is greater than in our reference, that means the entity we're referencing no longer exists.

When we create more entities than will fit in the EntityData array, the array is expanded by copying everything to a new, larger array. The array is never shrunk.

Because the EntityData array stores the indexes of the entities within the chunks, these indexes must be updated when entities are moved within/between chunks. (Recall that many entities may get shifted down slots when an entity is removed from a chunk, hence removing entities is one of the costlier operations.)

### shared components

For some components whose values frequently reoccur among entities, we'd like to avoid storing those values repeatedly in memory. The ISharedComponentData interface does just that: entities of the same archetype which have equal values of an ISharedComponentData type all share that value in memory rather than each having its own copy.

A chunk stores only one shared component value of a particular type, and so setting a shared component value on an entity usually requires moving the entity to another chunk. Say two entities in a chunk share a FooSharedComponent value: if we set a new FooSharedComponent value on one entity, the other entity still has the old value, and the two values cannot both exist in the same chunk, so the modified entity is moved to a new chunk.

The entity manager hashes shared component values to keep track of which chunks store which shared values. (We wouldn't want multiple chunks to needlessly store the same shared component values and thereby excessively fragment our entities across chunks.)

Unlike regular components, shared components need not be blitable and so can store references into native and managed memory.

Shared components are most appropriate for component types which are mutated infrequently and which have the same values across many entities. For example, a component consisting of a single enum field is a good candidate for a shared component because many entities typically would share the same enum values.

### data modeling guidelines

Our (non-shared) components cannot store arrays or collections of any kind, including native containers. What we *can* store in our components, though, are entity ids and indexes into native containers.

Say in a deathmatch game, the player who killed another the most times is that other player's nemesis. A single player can be the nemesis of multiple other players, so this is a one-to-many relationship. The most obvious way to represent this is to give the player component a Nemesis field to store the entity id of the player's nemesis (with id index -1 denoting that the player has no nemesis).

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
    public MyStruct C;   // must be a struct with only blitable fields
}
```

Because we usually don't give the components methods or properties, it generally doesn't make sense to make any field non-public.

The fields must be [blitable types](https://docs.microsoft.com/en-us/dotnet/framework/interop/blittable-and-non-blittable-types).

#### ISharedComponentData

```csharp
public struct MySharedComponent : ISharedComponentData 
{
    public string A;             // field types needn't be blitable
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

Contradictory orderings, such as A-before-B but B-before-A, trigger runtime errors.

#### EntityManager

```csharp
World world = World.Active;  // the default World
EntityManager em = world.GetOrCreateManager<EntityManager>();

// create an entity with no components (yet)
Entity entity = em.CreateEntity();
// the index and version together form a logical entity id
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

// true if entity exists (false if never existed or if it's been destroyd)
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

[subtractive]
[SetFilter]

[disadvantage to injection? can i get reference to the componentgroup so as to set filters?]

#### EntityCommandBuffer

An EntityCommandBuffer queues up EntityManager add/remove/set operations to be performed later. This is useful because:

1. these operations invalidate ComponentGroup iterators
2. batching these operations can improve performance
3. jobs cannot do these operations directly

A ComponentSystem has an EntityCommandBuffer field called *PostUpdateCommands*. Operations queued on this buffer during *OnUpdate()* will be applied after every update.

[always immediately after? or is it smart about batching buffers from multiple systems?]

EntityCommandBuffer's methods correspond to EntityManager's methods: *CreateEntity()*, *AddComponent()*, *SetComponent()*, *etc.*


### injection

Rather than explicitly create a component group, we can use the Inject attribute to get the same iterators through injection. We can also inject other systems, which is useful for accessing their fields:

```csharp
public MySystem : ComponentSystem

    public struct MyGroup
    {
        EntityArray Entities;
        ComponentDataArray<FooComponent> Foos;
        ComponentDataArray<BarComponent> Bars;
        public int Length;
    }

    [Inject]
    private MyGroup group;   // creates iterators from ComponentGroup implied by struct fields

    [Inject]
    private OtherSystem otherSystem;    // useful for accesing fields of OtherSystem
    
    protected override void OnUpdate()
    {
        // the iterators are created for us before every OnUpdate
        for (int i = 0; i != group.Length; i++) 
        {
            Entity e = group.Entities[i];
            FooComponent f = group.Foos[i];
            BarComponent b = group.Bars[i];
            // ...
        }
    }
```

### hybrid API

We can add IComponentData struct values to GameObjects by making a MonoBehavior wrapper by inheriting from ComponentDataWrapper:

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

[is the entity and gameobject coordinated in any way, or are they just separate representations of the same data? give example]

### [misc. questions]

[is an effort made to avoid fragmentation from too many non-full chunks of a given archetype?]

does EntityManager have static versions of CreateEntity(), AddComponent(), etc.? Examples in docs imply this is case but I see no such methods in the code

how to create worlds and copy entities between?


how much should we lean on native containers?

only blitable types in components, but shouldn't a native container be blitable? it's not managed memory, so...

when is it ok to store entity references? doesn't looking up entities by id in our loop nullify linear memory benefits?




[can't have booleans because they're not blitable, instead use enum of struct of byte?]

ExclusiveEntityTransaction

 MoveEntitiesFrom




## the Job System

Rather than creating our own threads, we can use the Job System, which schedules units of work to run in Unity's own threads. The Job System addresses three problems with writing multi-threaded code:

1. Creating and managing our own threads is bothersome and error-prone.
2. Any threads we create wastefully contend with each other and with Unity's own threads for CPU time.
3. Avoiding and detecting race conditions can be very difficult.

Unity creates a thread for every logical core in your system: one core runs the main thread, another runs the graphics thread, and the rest each run a worker thread. The main thread runs our MonoBehavior, coroutine, and ECS system code. Unity's own jobs and the jobs we create run in the worker threads (and in some cases on the main thread).

Once running, a job cannot be interrupted or moved to another thread, and so a job effectively owns its thread until it completes. For this reason (and others), the Job System is inappropriate for I/O tasks.

#### IJob

IJob is the primary interface for defining a job:

```csharp
public struct MyJob : IJob
{
    // the fields can only be blitable types and native containers
    public float a;
    public NativeArray<float> b;

    // IJob's single required method
    public void Execute()
    {
        // this change WILL be visible outside the job
        b[0] = b[0] + a;

        // this change will NOT be visible outside the job 
        // (explained in text below)
        a = 10.0f;  
    }
}


// ...somewhere in the main thread

// create an instance, set up its data, 
// and schedule the job
MyJob job = new MyJob();
job.a = 5.0f; 
// a native array of one float which must 
// be disposed within 4 frames
job.b = new NativeArray<float>(1, Allocator.TempJob);
job.b[0] = 3.0f;

JobHandle h = job.Schedule();       // add job to the queue (will not execute yet)
JobHandle.ScheduleBatchedJobs();    // begin execution of queued jobs


//...later in the main thread
h.Complete();               // wait for the job to complete
float val = job.b[0];       // 8.0f
val = job.a;                // 5.0f (job.a was unchanged by the job!)
job.b.Dispose();
```

A job should only access its own fields and not touch anything outside the job. Because a job runs outside of C#'s usual context, very bad things can happen if a job accesses globals (either directly or indirectly).

When a job executes, it is accessing a *copy* of the struct, not the very same value we created on the main thread. Consequently, **changes to the fields within the job are *not* accessible outside the job**. However, when a NativeContainer struct is copied, the copy points to the same native allocated memory, and so **changes in a NativeContainer *are* accessible outside the job**. In the above example, we want to produce one piece of data from the job, so we give it a NativeArray of length one.

The iterators we get from ComponentGroups (ComponentDataArray, EntityArray, *et al.*) are also valid job fields, but jobs touching entity components should only be created in the context of *JobComponentSystems* (described later).

A job may finish before *Complete()*, but calling *Complete()* allows the main thread to procede knowing that a job is totally finished and its internal references removed from the Job System. Every job should be completed at some point because failing to do so effectively creates a resource leak and because code logic generally demands a sync point past which a job is guaranteed to be finished.

Only the main thread can schedule and complete jobs (for reasons explained [here](https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/master/Documentation/content/scheduling_a_job_from_a_job.md)). We usually want the main thread to do more business while jobs run on worker threads, so we usually delay calling *Complete()* on a job until we actually need the job completed (which most commonly is at the end of the current frame or at the beginning of the next frame).

To complete multiple jobs but still allow them to execute in parallel, we can use *JobHandle.CompleteAll()*, which takes two or three job handles or a NativeArray\<JobHandle\>:

```csharp
JobHandle a = jobA.Schedule();
JobHandle b = jobB.Schedule();
JobHandle c = jobC.Schedule();
// wait for A, B, and C to complete
// A, B, and C may run in parallel
JobHandle.CompleteAll(a, b, c);
```

If we want job A to complete before job B starts running, we make A a dependency of B by passing job A's handle when scheduling B:

```csharp
// B will not run until after A completes
JobHandle a = jobA.Schedule();
JobHandle b = jobB.Schedule(a);   
```

Calling *Complete()* on a job implicitly first completes its any other jobs upon which it depends (directly or indirectly). For example, if A is a dependency of B which is a dependency of C, calling *Complete()* on C will first complete A and then B.

Though *Schedule()* only takes one handle, a job can wait for multiple other jobs by using *JobHandle.CombineDependencies()* 
(which takes two or three job handles or a NativeArray\<JobHandle\>):

```csharp
// A will not run until after B, C, and D complete
// B, C, and D may run in parallel
JobHandle d = jobD.Schedule();
JobHandle c = jobC.Schedule();
JobHandle b = jobB.Schedule();
JobHandle combined = JobHandle.CombineDependencies(b, c, d);
JobHandle a = jobA.Schedule(combined);  
```

Understand that a queued job is not readied for execution until:

1. *JobHandle.ScheduleBatchedJobs()* is called, which readies *all* queued jobs
2. *Complete()* is called on its handle
3. *Complete()* is called on the handle of another job for which this job is a (direct or indirect) dependency

When the main thread calls *Complete()*, one or more of the jobs to complete may not have started running yet, and those jobs get priority over any other jobs waiting on the queue. Rather than let a core go to waste, the Job System may use the main thread to run one or more of those jobs. (After all, the main thread would otherwise just sit there and wait, so it might as well chip in!)

#### IJobParallelFor

A ParallelFor job is automatically split behind-the-scenes into multiple actual jobs that can run in parallel, each processing its own range of indexes, *e.g.* one job handles indexes 0-99, another handles 100-199, another 200-299, *etc.*

```csharp
public struct MyParallelJob : IJobParallelFor
{
    public float a;
    public NativeArray<float> b;
    
    // 'i' is 0 in first call, then incremented for each subsequent call
    public void Execute(int i)
    {
        b[i] = b[i] + a;
    }
}


// ...somewhere in the main thread

MyParallelJob job = new MyJob();
job.a = 5.0f; 
job.b = new NativeArray<float>(1000, Allocator.TempJob);
for (int i = 0; i < 1000; i++) {
    job.b[i] = i;
}

// add job to the queue (1000 iterations, batch size of 64)
JobHandle h = job.Schedule(1000, 64);
// begin executing queued jobs       
JobHandle.ScheduleBatchedJobs();    


//...later in the main thread
h.Complete();               // wait for the job(s) to complete
float val = job.b[0];         // 5.0f
float val2 = job.b[4];        // 9.0f
job.b.Dispose();
```

Above, the job calls *Execute()* 1000 times, with *i* starting at 0 and incrementing for each other call. The calls are batched into logical jobs that each make 64 calls (except for one batch which makes 40 calls, because 1000 divided by 64 has a remainder of 40). These logical jobs don't have their own handles, but they run as their own units of work and so can run in parallel. (The overhead of 1000 calls is avoided by the compiler inlining the *Execute()* call in each batch loop.)

The larger the batch size, the fewer the logical jobs and so the less overhead, but the fewer opportunities for parallel execution. In general, 32 or 64 is a good batch size when each *Execute()* does very little, and 1 is a good batch size when each *Execute()* does a significant chunk of work.


### safety checks

When one job (or the main thread) writes to a native container, we don't want other jobs (or the main thread) that might run in parallel to read or write the same container, as this likely will create race condition bugs. Given two conflicting jobs, it is our responsibility to ensure that one will complete before the other starts by making one a dependency of the other.

The Job System can't decide for us which of two conflicting jobs should run first because that depends upon the logic of what they do! However, the Job System performs safety checks at editor runtime to help us detect conflicting jobs. The safety checks track which scheduled jobs touch which native containers, and an exception is thrown when jobs conflict. Some example scenarios:

```csharp
// assume jobs A and B reference the same NativeContainer
JobHandle a = jobA.Schedule();
JobHandle b = jobB.Schedule();     // exception!
```

```csharp
// assume jobs A and B reference the same NativeContainer
JobHandle a = jobA.Schedule();
JobHandle.ScheduleBatchedJobs();  

// Even if job A has finished by now, that is just 
// happenstance of scheduling, so we want an error here.
JobHandle b = jobB.Schedule();     // exception!
```

```csharp
// assume jobs A and B reference the same NativeContainer
JobHandle a = jobA.Schedule();
handleA.Complete();  

// no possible conflict, so no exception
JobHandle b = jobB.Schedule();      // OK
```


```csharp
// assume jobs A and B reference the same NativeContainer
JobHandle handleA = jobA.Schedule();

// B will wait for A to complete, so no possible conflict
JobHandle handleB = jobB.Schedule(handleA);    // OK
```

If two jobs have the same container marked as \[ReadOnly\], there is no conflict from their concurrent access:

```csharp
public struct MyJob : IJob
{
    [ReadOnly]
    public NativeArray<float> x;

    public void Execute()
    {...}
}


// ...somewhere in the main thread

MyJob jobA = new MyJob();
jobA.x = new NativeArray<float>(10, Allocator.TempJob);

MyJob jobB = new MyJob();
jobB.x = jobA.x;       // both jobs have the same container

JobHandle a = jobA.Schedule();

// OK because both jobs only read the container
JobHandle b = jobB.Schedule();   
```

While a job is scheduled but not complete, accessing any of its native containers in the main thread also triggers an exception:

```csharp
public struct MyJob : IJob
{
    public NativeArray<float> x;

    public void Execute()
    {...}
}


// ...somewhere in the main thread

MyJob job = new MyJob();
job.x = new NativeArray<float>(10, Allocator.TempJob);
JobHandle h = job.Schedule();

// exception! conflict with the container's use in scheduled job       
job.x[0] = 5.0f;

h.Complete();       

// OK to touch the container after the job's completion
job.x[0] = 5.0f;     
```

#### JobComponentSystem

A **JobComponentSystem** is like a ComponentSystem, but the OnUpdate method receives a job handle and returns a job handle. 


who is completing the returned job?

When ECS and the Job System are fully utilized together, most systems will do most of their work in jobs. The *JobComponentSystem* class not only expresses this pattern more conveniently, it allows us to define job(s)



### integrating ECS with GameObjects/Components

As mentioned, ECS as of yet has few stock components and systems, and so lacks most of the functionality of the old GameObjects and their Components. Thus we may wish to still use GameObjects along with ECS.