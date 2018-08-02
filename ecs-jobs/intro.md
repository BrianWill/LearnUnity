Unity version 2018.1 introduces a few major new features for achieving high performance:

- The **[Job System](jobs.md)** farms units of work called 'jobs' out to threads while helping us maintain thread safety.
- The **Burst compiler** optimizes code using [SIMD instructions](https://en.wikipedia.org/wiki/SIMD), which are particularly beneficial for math-heavy code. The Burst compiler is not a general-purpose C# compiler: it only works on job code, which is written in a subset of C# called HPC# (High Performance C#).
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

When a worker thread is available, the job system executes a job waiting on the queue. The execution order of jobs on the queue is left up to the job system and not necessarily the same as the order the jobs were added to the queue. Once started, a job runs on its thread until finished without interuption.

A scheduled job can be 'completed' on the main thread, meaning the main thread will wait for the job to finish executing (if it hasn't finished already) and all references to the job are removed from the job system.

### job input and output

A job is passed a struct as input. This struct cannot contain any references, except it can have NativeContainer types (such as NativeArray or NativeHashMap), which have unsafe pointers to native memory. These NativeContainers are manually allocated and so require manual deallocation (by calling the *Dispose()* method).

A job only produces output by mutating the contents of NativeContainer(s) passed in the input struct. (Mutations to the input struct itself are not visible outside the job because the job gets its own private copy of the struct.) A job cannot touch static fields or methods and cannot do I/O. The purpose of a job is always just to produce output data in one or more NativeContainers passed in *via* the input struct.

### job dependencies

When scheduling a job, we can specify another already scheduled job as its dependency. The job system will not start executing a scheduled job until that job's dependency has finished executing. This is useful when two jobs use the same NativeContainer(s) because we usually want to guarantee that one job finishes using the NativeContainer(s) before the other job starts.

Effectively, scheduled jobs can form chains of dependency. For example, job A depends upon job B which depends upon job C, such that B will wait for C to finish, and A will wait for B to finish.

A job can be the direct dependency of multiple other jobs, and a job can have multiple direct dependencies. Consequently, a chain of dependencies can have branches. A job with multiple dependencies will not start execution until all of its dependencies have finished. A job that is the dependency of multiple other jobs must finish before those other jobs can start executing.

Cycles of dependency are not possible because you can only specify already scheduled jobs as dependencies.

Completing a job transitively completes all of that job's dependencies as well. 

### safety checks

It's generally a mistake to have two or more jobs concurrently use the same NativeContainer, and so, when executing your game inside the editor, Unity throws exceptions when it detects such cases. When two jobs access the same NativeContainer, one of the jobs should be completed before the other is scheduled, or one job should be the dependency (direct or indirect) of the other. Doing either of these things guarantees that one job finishes executing before the other starts. (Which should run first is up to you because it depends upon the particular logic!)

## ECS (Entity Component System) overview
