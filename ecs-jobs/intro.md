Unity version 2018.1 introduces a few major new features for achieving high performance:

- The **[Job System](jobs.md)** farms units of work called 'jobs' out to threads while helping us maintain thread safety.
- The **Burst compiler** optimizes code using [SIMD instructions](https://en.wikipedia.org/wiki/SIMD), which is particularly beneficial for math-heavy code. The Burst compiler is not a general-purpose C# compiler: it only works on job code, which is written in a subset of C# called HPC# (High Performance C#).
- **[ECS (Entity Component System)](ecs.md)** is an architectural pattern in which we lay out data in native (non-garbage collected) memory in the optimal, linear fashion: tightly packed, contiguous, and accessible in sequence. Also, by separating code from data, ECS (arguably) improves code structure over the traditional Object-Oriented approach.

ECS and the Job System can be used separately, but they are [highly complementary](ecs_jobs.md): ECS guarantees data is layed out linearly in memory, which speeds up job code accessing the data and gives the Burst compiler more optimization opportunities.

### Videos

- [the Job System](https://www.youtube.com/watch?v=zkVYbcSlfoE)
- [ECS](https://www.youtube.com/watch?v=kk8RCwQHIy4)
- [using the Job System with ECS](https://www.youtube.com/watch?v=SZGRtQ7-ilo)
- [ECS fixed arrays and shared components](https://youtu.be/oO2yqVQwFUQ)
- [ECS transforms and rendering](https://www.youtube.com/watch?v=QD2DpeuOrS0)

**TIP**: If you find the narration a bit too fast, you can set Youtube video playback to any speed you like in the Javascript console. For example, you can set the playback rate to 92% with `document.getElementsByTagName('video')[0].playbackRate = 0.92`

[next \>\>](jobs.md)

## Job System overview

### job execution

A job can only be scheduled (added to the job queue) from the main thread, but a job usually executes on one of Unity's background worker threads (though in some cases a job executes on the main thread).

When a worker thread is available, the job system executes a job waiting on the queue. The execution order of jobs on the queue is left up to the job system and is not necessarily the same as the order the jobs were added to the queue. Once started, a job runs on its thread without interuption until finished.

A scheduled job can be 'completed' on the main thread, meaning that the main thread will wait for the job to finish executing (if it hasn't finished already) and that all references to the job are removed from the job system.

### job input and output

A job is passed a struct as input. This struct cannot contain any memory references, except it can have NativeContainer types (such as NativeArray or NativeHashMap), which have unsafe pointers to native memory. These NativeContainers are manually allocated and so require manual deallocation (by calling the *Dispose()* method).

A job only produces output by mutating the contents of NativeContainer(s) passed in the input struct. (Mutations to the input struct itself are not visible outside the job because the job gets its own private copy of the struct.) A job cannot touch static fields or methods and cannot do I/O.

The purpose of a job is just to produce output data in one or more NativeContainers passed in *via* the input struct. (The exception to this rule is that jobs can also produce output by mutating ECS entities and components, as discussed later.)

### job dependencies

When scheduling a job, we can specify another already scheduled job as its dependency. The job system will not start executing a scheduled job until that job's dependency has finished executing. This is useful when two jobs use the same NativeContainer(s) because we usually want to guarantee that one job finishes using the NativeContainer(s) before the other job starts.

Effectively, scheduled jobs can form chains of dependency. For example, job A depends upon job B which depends upon job C, such that A will not start until B has finished, and B will not start until C has finished.

A job can be the direct dependency of multiple other jobs, and a job can have multiple direct dependencies. Consequently, a chain of dependencies can have branches. A job with multiple dependencies will not start executing until all of its dependencies have finished. A job that is the dependency of multiple other jobs must finish before those other jobs can start executing.

Cycles of dependency are not possible because we can only specify already scheduled jobs as dependencies (and we cannot change the dependencies of an already scheduled job).

Completing a job transitively completes all of that job's dependencies as well. 

### safety checks

It's generally a mistake to have two or more jobs concurrently use the same NativeContainer, and so, when executing our game inside the editor, Unity throws exceptions when it detects such cases. When two jobs access the same NativeContainer, one of the jobs should be completed before the other is scheduled, or one job should be the dependency (direct or indirect) of the other. Either of these arrangements guarantees that one job finishes executing before the other starts. (Which of the two jobs should run first is up to us because it depends upon the particular logic!)

## ECS (Entity Component System) overview

An **entity** is a piece of data known by a unique ID number and which logically contains any number of **components** (not to be confused with Unity's GameObject Components). These components are struct types which can contain other value types but no memory references, and a single entity cannot have multiple components of the same type. Components can include references to other entities by storing their unique ID numbers.

A **system** is a class whose *Update()* method is called once every frame in the main thread. By default, the order of system updates within a frame is chosen automatically, but we can specify their relative execution order. A typical system update iterates through a selection of component types for all entities which have components of those types. For example, a system might iterate through all components of types A, C, and D for all entities which have components of those types (regardless of what other types of components those entities might have, *e.g.* an entity with types A, B, C, D, and E would be included). The entities and their components are stored in memory in a linear fashion, making these iterations through the entities and their components as optimal as possible.

### using jobs to read and write entities and their components

We can create and schedule jobs which read and mutate the entities and their components, but doing so requires special consideration to avoid conflicting reads/writes between overlapping jobs. Safety checks catch these conflicts, and a special type of system called JobComponentSystem helps us avoid these conflicts.

### hybrid ECS

As of yet, Unity provides very few stock component types and systems, so a game that uses only ECS rather than GameObjects will have to implement most pieces of game functionality for itself. For example, there are no ECS components or systems yet for collision detection. Until these missing pieces are filled in over the next few years, most projects using ECS will want to use the old GameObjects as well. Just be clear that the old GameObjects do not have the performance benefits of ECS's linear memory storage and integration into the job system.
